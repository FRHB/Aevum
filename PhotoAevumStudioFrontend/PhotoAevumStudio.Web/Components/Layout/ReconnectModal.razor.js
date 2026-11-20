// Set up event handlers
const reconnectModal = document.getElementById("components-reconnect-modal");
const retryButton = document.getElementById("components-reconnect-button");
const resumeButton = document.getElementById("components-resume-button");

if (reconnectModal) {
    reconnectModal.addEventListener("components-reconnect-state-changed", handleReconnectStateChanged);
}
if (retryButton) {
    retryButton.addEventListener("click", retry);
}
if (resumeButton) {
    resumeButton.addEventListener("click", resume);
}

function recoverOrReload() {
    if (window.__aevumNav && typeof window.__aevumNav.recoverNavigation === "function") {
        window.__aevumNav.recoverNavigation();
    } else {
        location.reload();
    }
}

function handleReconnectStateChanged(event) {
    const state = event?.detail?.state;
    if (state === "show") {
        window.__aevumNav?.markCircuitBroken?.(true);
        reconnectModal?.showModal();
    } else if (state === "retrying" || state === "paused") {
        window.__aevumNav?.markCircuitBroken?.(true);
    } else if (state === "hide") {
        window.__aevumNav?.markCircuitBroken?.(false);
        reconnectModal?.close();
    } else if (state === "failed") {
        window.__aevumNav?.markCircuitBroken?.(true);
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    } else if (state === "rejected") {
        window.__aevumNav?.markCircuitBroken?.(true);
        recoverOrReload();
    }
}

async function retry() {
    document.removeEventListener("visibilitychange", retryWhenDocumentBecomesVisible);

    try {
        // Reconnect will asynchronously return:
        // - true to mean success
        // - false to mean we reached the server, but it rejected the connection (e.g., unknown circuit ID)
        // - exception to mean we didn't reach the server (this can be sync or async)
        const successful = await Blazor.reconnect();
        if (!successful) {
            // We have been able to reach the server, but the circuit is no longer available.
            // Recover to the intended target page (if navigating) or reload cleanly.
            const resumeSuccessful = await Blazor.resumeCircuit();
            if (!resumeSuccessful) {
                recoverOrReload();
            } else {
                window.__aevumNav?.markCircuitBroken?.(false);
                reconnectModal?.close();
            }
        }
    } catch (err) {
        // We got an exception, server is currently unavailable
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    }
}

async function resume() {
    try {
        const successful = await Blazor.resumeCircuit();
        if (!successful) {
            recoverOrReload();
        } else {
            window.__aevumNav?.markCircuitBroken?.(false);
            reconnectModal?.close();
        }
    } catch {
        reconnectModal?.classList.replace("components-reconnect-paused", "components-reconnect-resume-failed");
    }
}

async function retryWhenDocumentBecomesVisible() {
    if (document.visibilityState === "visible") {
        await retry();
    }
}
