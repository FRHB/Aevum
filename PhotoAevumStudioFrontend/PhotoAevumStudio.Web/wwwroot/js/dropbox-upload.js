(function () {
    const activeUploads = new Map();
    const stagedFiles = new Map();
    let stagedCounter = 0;
    let hostRef = null;
    let beforeUnloadBound = false;

    function updateBeforeUnload() {
        const hasTransferring = Array.from(activeUploads.values()).some(u => u.phase === "uploading");
        if (hasTransferring && !beforeUnloadBound) {
            window.addEventListener("beforeunload", onBeforeUnload);
            beforeUnloadBound = true;
        } else if (!hasTransferring && beforeUnloadBound) {
            window.removeEventListener("beforeunload", onBeforeUnload);
            beforeUnloadBound = false;
        }
    }

    function onBeforeUnload(e) {
        e.preventDefault();
        e.returnValue = "A ZIP archive upload is still transferring. Leaving or reloading will interrupt it.";
        return e.returnValue;
    }

    function notify(method, ...args) {
        if (!hostRef) return;
        try {
            hostRef.invokeMethodAsync(method, ...args).catch(() => { });
        } catch { }
    }

    function extractErrorMessage(xhrOrText, fallback) {
        try {
            const text = typeof xhrOrText === "string" ? xhrOrText : (xhrOrText && xhrOrText.responseText) || "";
            if (!text) return fallback;
            const json = JSON.parse(text);
            return json.detail || json.message || json.title || fallback;
        } catch {
            return fallback;
        }
    }

    async function postJson(url, body, signal) {
        const resp = await fetch(url, {
            method: "POST",
            headers: body ? { "Content-Type": "application/json" } : undefined,
            body: body ? JSON.stringify(body) : undefined,
            signal
        });
        const text = await resp.text();
        if (!resp.ok) {
            throw new Error(extractErrorMessage(text, `HTTP ${resp.status}`));
        }
        return text ? JSON.parse(text) : null;
    }

    function sendChunkXhr(url, blob, state, onProgress) {
        return new Promise((resolve, reject) => {
            if (state.canceled) {
                reject(new Error("Upload canceled."));
                return;
            }

            const xhr = new XMLHttpRequest();
            state.currentXhr = xhr;
            xhr.open("POST", url, true);
            xhr.timeout = 0; // No arbitrary 100s timeout; chunk is max 4 MB

            xhr.upload.onprogress = function (evt) {
                if (evt.lengthComputable && onProgress) {
                    onProgress(evt.loaded);
                }
            };

            xhr.onload = function () {
                state.currentXhr = null;
                if (xhr.status >= 200 && xhr.status < 300) {
                    try {
                        resolve(xhr.responseText ? JSON.parse(xhr.responseText) : null);
                    } catch {
                        resolve(null);
                    }
                } else {
                    const err = new Error(extractErrorMessage(xhr, `Chunk upload failed (HTTP ${xhr.status}).`));
                    err.status = xhr.status;
                    reject(err);
                }
            };

            xhr.onerror = function () {
                state.currentXhr = null;
                reject(new Error("Network error while uploading ZIP chunk."));
            };

            xhr.onabort = function () {
                state.currentXhr = null;
                reject(new Error("Upload canceled."));
            };

            const form = new FormData();
            form.append("chunk", blob, "chunk.bin");
            xhr.send(form);
        });
    }

    async function runUpload(jobId, ticketId, file, totalBytes) {
        const state = activeUploads.get(jobId);
        if (!state) return;

        try {
            const base = `/bff/dropbox/upload/${encodeURIComponent(ticketId)}`;
            const init = await postJson(`${base}/init`, null, state.abortController.signal);
            const sessionId = init && init.sessionId;
            const chunkSize = (init && init.chunkSizeBytes) || (4 * 1024 * 1024);
            if (!sessionId) {
                throw new Error("Could not initialize upload session.");
            }
            state.sessionId = sessionId;

            const size = file.size || totalBytes;
            const totalChunks = Math.max(1, Math.ceil(size / chunkSize));
            let lastEmit = 0;

            for (let i = 0; i < totalChunks; i++) {
                if (state.canceled) throw new Error("Upload canceled.");

                const start = i * chunkSize;
                const end = Math.min(size, start + chunkSize);
                const slice = file.slice(start, end);
                const chunkUrl = `${base}/chunk?sessionId=${encodeURIComponent(sessionId)}&chunkIndex=${i}`;

                let attempt = 0;
                while (true) {
                    try {
                        await sendChunkXhr(chunkUrl, slice, state, function (chunkLoaded) {
                            const now = Date.now();
                            if (now - lastEmit < 120) return;
                            lastEmit = now;

                            const sent = Math.min(size, start + chunkLoaded);
                            const pct = size > 0 ? Math.min(90, Math.floor((sent * 90) / size)) : 0;
                            notify("OnUploadProgress", jobId, sent, pct, false);
                        });
                        break;
                    } catch (err) {
                        if (state.canceled || (err && err.status >= 400 && err.status < 500)) {
                            throw err;
                        }
                        attempt++;
                        if (attempt >= 3) throw err;
                        await new Promise(r => setTimeout(r, 1000 * attempt));
                    }
                }

                const sentAfterChunk = end;
                const pctAfterChunk = size > 0 ? Math.min(90, Math.floor((sentAfterChunk * 90) / size)) : 90;
                notify("OnUploadProgress", jobId, sentAfterChunk, pctAfterChunk, false);
            }

            if (state.canceled) throw new Error("Upload canceled.");

            state.phase = "finalizing";
            updateBeforeUnload();
            notify("OnUploadProgress", jobId, size, 91, true);

            await postJson(`${base}/complete?sessionId=${encodeURIComponent(sessionId)}`, null, state.abortController.signal);

            while (!state.canceled) {
                await new Promise(r => setTimeout(r, 750));
                if (state.canceled) break;

                const statusResp = await fetch(`${base}/status?sessionId=${encodeURIComponent(sessionId)}`, {
                    method: "GET",
                    signal: state.abortController.signal
                });
                const statusText = await statusResp.text();
                if (!statusResp.ok) {
                    throw new Error(extractErrorMessage(statusText, `Status check failed (HTTP ${statusResp.status}).`));
                }

                const statusData = statusText ? JSON.parse(statusText) : null;
                if (!statusData) continue;

                const st = (statusData.status || "").toLowerCase();
                if (st === "completed") {
                    activeUploads.delete(jobId);
                    updateBeforeUnload();
                    notify("OnUploadCompleted", jobId, JSON.stringify(statusData.result || null));
                    return;
                }
                if (st === "failed" || st === "aborted") {
                    throw new Error(statusData.error || "Cloudflare R2 upload failed.");
                }

                const r2Bytes = statusData.r2BytesTransferred || 0;
                const r2Pct = size > 0 ? Math.min(99, 91 + Math.floor((r2Bytes * 8) / size)) : 95;
                notify("OnUploadProgress", jobId, size, r2Pct, true);
            }
        } catch (err) {
            const wasCanceled = state.canceled;
            activeUploads.delete(jobId);
            updateBeforeUnload();
            if (!wasCanceled) {
                notify("OnUploadFailed", jobId, (err && err.message) || "ZIP upload failed.");
            }
        }
    }

    window.aevumDropboxUpload = {
        registerHost: function (dotNetRef) {
            hostRef = dotNetRef;
        },

        captureInputFiles: function (inputElementId) {
            const input = document.getElementById(inputElementId);
            if (!input || !input.files || input.files.length === 0) {
                return [];
            }
            const tokens = [];
            for (let i = 0; i < input.files.length; i++) {
                const token = `f_${++stagedCounter}_${Date.now().toString(36)}`;
                stagedFiles.set(token, input.files[i]);
                tokens.push(token);
            }
            return tokens;
        },

        releaseStagedFile: function (fileToken) {
            if (fileToken) {
                stagedFiles.delete(fileToken);
            }
        },

        start: function (jobId, ticketId, inputElementId, totalBytes, fileToken, fileIndex) {
            let file = null;
            if (fileToken && stagedFiles.has(fileToken)) {
                file = stagedFiles.get(fileToken);
                stagedFiles.delete(fileToken);
            }
            if (!file) {
                const input = document.getElementById(inputElementId);
                const idx = typeof fileIndex === "number" && fileIndex >= 0 ? fileIndex : 0;
                file = input && input.files && input.files[idx];
            }
            if (!file) return false;

            const state = {
                jobId,
                ticketId,
                sessionId: null,
                phase: "uploading",
                canceled: false,
                currentXhr: null,
                abortController: new AbortController()
            };

            activeUploads.set(jobId, state);
            updateBeforeUnload();
            runUpload(jobId, ticketId, file, totalBytes || file.size);
            return true;
        },

        cancel: function (jobId) {
            const state = activeUploads.get(jobId);
            if (!state) return;

            state.canceled = true;
            if (state.currentXhr) {
                try { state.currentXhr.abort(); } catch { }
            }
            try { state.abortController.abort(); } catch { }

            const base = `/bff/dropbox/upload/${encodeURIComponent(state.ticketId)}`;
            const url = state.sessionId ? `${base}?sessionId=${encodeURIComponent(state.sessionId)}` : base;
            fetch(url, { method: "DELETE" }).catch(() => { });

            activeUploads.delete(jobId);
            updateBeforeUnload();
        }
    };
})();
