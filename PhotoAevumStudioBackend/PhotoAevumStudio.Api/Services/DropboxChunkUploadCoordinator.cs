using System.Collections.Concurrent;
using Amazon.S3;
using Microsoft.Data.SqlClient;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.Services;

public sealed class DropboxChunkUploadCoordinator
{
    public const long MaxZipSizeBytes = 524_288_000L; // 500 MB
    public const int DefaultChunkSizeBytes = 4 * 1024 * 1024; // 4 MB

    private readonly ConcurrentDictionary<string, DropboxUploadSession> _sessions = new(StringComparer.Ordinal);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DropboxChunkUploadCoordinator> _logger;

    public DropboxChunkUploadCoordinator(
        IServiceScopeFactory scopeFactory,
        ILogger<DropboxChunkUploadCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public DropboxUploadInitResponse CreateSession(
        string eventAppointmentId,
        string staffId,
        string fileName,
        long fileSizeBytes,
        string? contentType)
    {
        CleanupExpiredSessions();

        if (string.IsNullOrWhiteSpace(eventAppointmentId))
            throw new ArgumentException("Event appointment ID is required.", nameof(eventAppointmentId));
        if (string.IsNullOrWhiteSpace(fileName) ||
            !Path.GetExtension(fileName.Trim()).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .zip files are allowed for Customer Dropbox uploads.");
        if (fileSizeBytes <= 0 || fileSizeBytes > MaxZipSizeBytes)
            throw new InvalidOperationException("ZIP file must be between 1 byte and 500 MB.");

        var sessionId = Guid.NewGuid().ToString("N");
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"aevum-dropbox-chunk-{sessionId}.zip");
        var now = DateTimeOffset.UtcNow;

        var session = new DropboxUploadSession
        {
            SessionId = sessionId,
            EventAppointmentId = eventAppointmentId.Trim(),
            StaffId = staffId,
            FileName = Path.GetFileName(fileName.Trim()),
            TotalSizeBytes = fileSizeBytes,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/zip" : contentType.Trim(),
            TempFilePath = tempFilePath,
            Status = "Uploading",
            CreatedAtUtc = now,
            LastActivityUtc = now
        };

        _sessions[sessionId] = session;

        return new DropboxUploadInitResponse(sessionId, DefaultChunkSizeBytes);
    }

    public async Task<DropboxUploadChunkResponse> AppendChunkAsync(
        string sessionId,
        string eventAppointmentId,
        string staffId,
        int chunkIndex,
        Stream chunkStream,
        long chunkLength,
        CancellationToken ct)
    {
        var session = GetOwnedSession(sessionId, staffId, eventAppointmentId);

        await session.WriteLock.WaitAsync(ct);
        try
        {
            session.LastActivityUtc = DateTimeOffset.UtcNow;

            if (!string.Equals(session.Status, "Uploading", StringComparison.Ordinal))
                throw new InvalidOperationException($"Upload session is in '{session.Status}' state and cannot accept chunks.");

            if (chunkIndex < session.NextChunkIndex)
            {
                return new DropboxUploadChunkResponse(session.SessionId, session.BytesReceived, session.TotalSizeBytes, session.NextChunkIndex);
            }

            if (chunkIndex != session.NextChunkIndex)
                throw new InvalidOperationException($"Unexpected chunk index {chunkIndex}; expected {session.NextChunkIndex}.");

            if (chunkLength <= 0)
                throw new InvalidOperationException("Chunk payload cannot be empty.");

            if (session.BytesReceived + chunkLength > session.TotalSizeBytes)
                throw new InvalidOperationException("Uploaded chunk bytes exceed declared ZIP file size.");

            var buffer = new byte[81920];
            var totalChunkRead = 0L;

            await using var fs = new FileStream(
                session.TempFilePath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            fs.Seek(session.BytesReceived, SeekOrigin.Begin);

            bool checkedZipMagic = chunkIndex > 0;
            int read;
            while ((read = await chunkStream.ReadAsync(buffer, ct)) > 0)
            {
                if (!checkedZipMagic)
                {
                    if (totalChunkRead == 0 && read < 2)
                        throw new InvalidOperationException("The uploaded file is not a valid ZIP archive (missing PK signature).");
                    if (totalChunkRead == 0 && (buffer[0] != 0x50 || buffer[1] != 0x4B))
                        throw new InvalidOperationException("The uploaded file is not a valid ZIP archive (missing PK signature).");
                    checkedZipMagic = true;
                }

                await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                totalChunkRead += read;
                if (session.BytesReceived + totalChunkRead > session.TotalSizeBytes)
                    throw new InvalidOperationException("Uploaded chunk bytes exceed declared ZIP file size.");
            }

            await fs.FlushAsync(ct);
            session.BytesReceived += totalChunkRead;
            session.NextChunkIndex++;

            return new DropboxUploadChunkResponse(session.SessionId, session.BytesReceived, session.TotalSizeBytes, session.NextChunkIndex);
        }
        finally
        {
            session.WriteLock.Release();
        }
    }

    public DropboxUploadStatusResponse StartFinalize(
        string sessionId,
        string eventAppointmentId,
        string staffId)
    {
        var session = GetOwnedSession(sessionId, staffId, eventAppointmentId);
        session.LastActivityUtc = DateTimeOffset.UtcNow;

        if (string.Equals(session.Status, "Finalizing", StringComparison.Ordinal) ||
            string.Equals(session.Status, "Completed", StringComparison.Ordinal))
        {
            return ToStatusResponse(session);
        }

        if (!string.Equals(session.Status, "Uploading", StringComparison.Ordinal))
            throw new InvalidOperationException($"Cannot finalize upload session in '{session.Status}' state.");

        if (session.BytesReceived != session.TotalSizeBytes)
            throw new InvalidOperationException($"Incomplete ZIP upload ({session.BytesReceived} of {session.TotalSizeBytes} bytes received).");

        session.Status = "Finalizing";

        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var dropboxService = scope.ServiceProvider.GetRequiredService<ICustomerDropboxService>();

                await using var fs = new FileStream(
                    session.TempFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    useAsync: true);

                var updated = await dropboxService.UploadZipAsync(
                    session.EventAppointmentId,
                    fs,
                    session.FileName,
                    session.TotalSizeBytes,
                    session.ContentType,
                    session.StaffId,
                    transferred =>
                    {
                        session.R2BytesTransferred = transferred;
                        session.LastActivityUtc = DateTimeOffset.UtcNow;
                    },
                    session.Cts.Token);

                session.R2BytesTransferred = session.TotalSizeBytes;
                session.Result = updated;
                session.Status = "Completed";
                session.LastActivityUtc = DateTimeOffset.UtcNow;

                _logger.LogInformation(
                    "Staff {StaffId} completed chunked Dropbox ZIP upload '{FileName}' ({Size} bytes) for event {EventId}.",
                    session.StaffId, session.FileName, session.TotalSizeBytes, session.EventAppointmentId);
            }
            catch (OperationCanceledException)
            {
                session.Status = "Aborted";
                session.Error = "Upload was canceled.";
                session.LastActivityUtc = DateTimeOffset.UtcNow;
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogError(ex, "Cloudflare R2 upload failed during chunked finalize for event {EventId}.", session.EventAppointmentId);
                session.Status = "Failed";
                session.Error = $"R2 storage rejected the upload ({ex.ErrorCode ?? ex.StatusCode.ToString()}): {ex.Message}";
                session.LastActivityUtc = DateTimeOffset.UtcNow;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Database error saving CustomerDropbox metadata during chunked finalize for event {EventId}.", session.EventAppointmentId);
                session.Status = "Failed";
                session.Error = "Failed to save uploaded ZIP archive metadata.";
                session.LastActivityUtc = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chunked Dropbox upload finalize failed for event {EventId}.", session.EventAppointmentId);
                session.Status = "Failed";
                session.Error = ex.Message;
                session.LastActivityUtc = DateTimeOffset.UtcNow;
            }
            finally
            {
                CleanupTempFile(session.TempFilePath);
            }
        });

        return ToStatusResponse(session);
    }

    public DropboxUploadStatusResponse GetStatus(string sessionId, string staffId)
    {
        CleanupExpiredSessions();
        var session = GetOwnedSession(sessionId, staffId, null);
        session.LastActivityUtc = DateTimeOffset.UtcNow;
        return ToStatusResponse(session);
    }

    public void AbortSession(string sessionId, string staffId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return;

        if (!string.Equals(session.StaffId, staffId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("You do not own this upload session.");

        session.Status = "Aborted";
        try { session.Cts.Cancel(); } catch { }
        CleanupTempFile(session.TempFilePath);
        _sessions.TryRemove(sessionId, out _);
        session.Dispose();
    }

    private DropboxUploadSession GetOwnedSession(string sessionId, string staffId, string? eventAppointmentId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || !_sessions.TryGetValue(sessionId.Trim(), out var session))
            throw new KeyNotFoundException("Upload session was not found or has expired.");

        if (!string.Equals(session.StaffId, staffId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("You do not own this upload session.");

        if (!string.IsNullOrWhiteSpace(eventAppointmentId) &&
            !string.Equals(session.EventAppointmentId, eventAppointmentId.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Upload session does not match the specified event.");

        return session;
    }

    private static DropboxUploadStatusResponse ToStatusResponse(DropboxUploadSession session) =>
        new(
            session.SessionId,
            session.Status,
            session.BytesReceived,
            session.TotalSizeBytes,
            session.R2BytesTransferred,
            session.Result,
            session.Error);

    private void CleanupExpiredSessions()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _sessions)
        {
            var s = kvp.Value;
            var isTerminal = s.Status is "Completed" or "Failed" or "Aborted";
            var ttl = isTerminal ? TimeSpan.FromMinutes(10) : TimeSpan.FromHours(2);
            if (now - s.LastActivityUtc > ttl)
            {
                if (_sessions.TryRemove(kvp.Key, out var removed))
                {
                    try { removed.Cts.Cancel(); } catch { }
                    CleanupTempFile(removed.TempFilePath);
                    removed.Dispose();
                }
            }
        }
    }

    private static void CleanupTempFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private sealed class DropboxUploadSession : IDisposable
    {
        public required string SessionId { get; init; }
        public required string EventAppointmentId { get; init; }
        public required string StaffId { get; init; }
        public required string FileName { get; init; }
        public required long TotalSizeBytes { get; init; }
        public required string ContentType { get; init; }
        public required string TempFilePath { get; init; }
        public required DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset LastActivityUtc { get; set; }
        public string Status { get; set; } = "Uploading";
        public long BytesReceived { get; set; }
        public int NextChunkIndex { get; set; }
        public long R2BytesTransferred { get; set; }
        public CustomerDropboxFolderDto? Result { get; set; }
        public string? Error { get; set; }
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
        public CancellationTokenSource Cts { get; } = new();

        public void Dispose()
        {
            WriteLock.Dispose();
            Cts.Dispose();
        }
    }
}

public sealed record DropboxUploadInitRequest(string FileName, long FileSizeBytes, string? ContentType);
public sealed record DropboxUploadInitResponse(string SessionId, int ChunkSizeBytes);
public sealed record DropboxUploadChunkResponse(string SessionId, long BytesReceived, long TotalSizeBytes, int NextChunkIndex);
public sealed record DropboxUploadStatusResponse(
    string SessionId,
    string Status,
    long BytesReceived,
    long TotalSizeBytes,
    long R2BytesTransferred,
    CustomerDropboxFolderDto? Result,
    string? Error);
