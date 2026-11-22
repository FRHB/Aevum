using System.Text.Json;
using Microsoft.JSInterop;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public enum DropboxUploadJobStatus
{
    Queued,
    Uploading,
    Finalizing,
    Completed,
    Failed,
    Canceled
}

public sealed record DropboxQueuedFileRequest(
    string FileName,
    long FileSizeBytes,
    string? ContentType,
    string? JsFileToken = null,
    int InputFileIndex = 0);

public sealed class DropboxUploadJob
{
    public string JobId { get; init; } = string.Empty;
    public string TicketId { get; set; } = string.Empty;
    public string EventAppointmentId { get; init; } = string.Empty;
    public string EventName { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long TotalBytes { get; init; }
    public string? ContentType { get; init; }
    public string InputElementId { get; init; } = "zipUploadInput";
    public string? JsFileToken { get; init; }
    public int InputFileIndex { get; init; }
    internal string? FallbackJwt { get; set; }
    public long BytesSent { get; set; }
    public int Percent { get; set; }
    public DropboxUploadJobStatus Status { get; set; } = DropboxUploadJobStatus.Queued;
    public string? ErrorMessage { get; set; }
    public CustomerDropboxFolderItem? CompletedItem { get; set; }
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public bool IsQueued => Status == DropboxUploadJobStatus.Queued;
    public bool IsActive => Status is DropboxUploadJobStatus.Queued or DropboxUploadJobStatus.Uploading or DropboxUploadJobStatus.Finalizing;
    public bool IsTransferring => Status is DropboxUploadJobStatus.Uploading or DropboxUploadJobStatus.Finalizing;
    public bool IsFinalizing => Status == DropboxUploadJobStatus.Finalizing;
}

/// <summary>
/// Circuit-scoped coordinator that tracks sequential background ZIP upload queues
/// across modal closes and page navigations while delegating chunked HTTP transfer to browser JS.
/// </summary>
public sealed class DropboxBackgroundUploadService
{
    public const long MaxZipSizeBytes = 500L * 1024 * 1024; // 500 MB per individual ZIP file

    private readonly ITokenStore _tokenStore;
    private readonly DropboxUploadTicketStore _ticketStore;
    private readonly List<DropboxUploadJob> _jobs = new();
    private readonly object _lock = new();
    private readonly SemaphoreSlim _dispatchLock = new(1, 1);
    private IJSRuntime? _jsRuntime;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DropboxBackgroundUploadService(
        ITokenStore tokenStore,
        DropboxUploadTicketStore ticketStore)
    {
        _tokenStore = tokenStore;
        _ticketStore = ticketStore;
    }

    public event Action? OnJobsChanged;

    public IReadOnlyList<DropboxUploadJob> Jobs
    {
        get
        {
            lock (_lock)
            {
                return _jobs.ToList();
            }
        }
    }

    public bool HasActiveUploads
    {
        get
        {
            lock (_lock)
            {
                return _jobs.Any(j => j.IsActive);
            }
        }
    }

    public IReadOnlyList<DropboxUploadJob> GetJobsForEvent(string eventAppointmentId)
    {
        if (string.IsNullOrWhiteSpace(eventAppointmentId)) return Array.Empty<DropboxUploadJob>();
        lock (_lock)
        {
            return _jobs
                .Where(j => string.Equals(j.EventAppointmentId, eventAppointmentId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public DropboxUploadJob? GetActiveJobForEvent(string eventAppointmentId)
    {
        if (string.IsNullOrWhiteSpace(eventAppointmentId)) return null;
        lock (_lock)
        {
            return _jobs.FirstOrDefault(j =>
                       j.IsTransferring &&
                       string.Equals(j.EventAppointmentId, eventAppointmentId, StringComparison.OrdinalIgnoreCase))
                   ?? _jobs.FirstOrDefault(j =>
                       j.IsActive &&
                       string.Equals(j.EventAppointmentId, eventAppointmentId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public DropboxUploadJob? GetLatestJobForEvent(string eventAppointmentId)
    {
        if (string.IsNullOrWhiteSpace(eventAppointmentId)) return null;
        lock (_lock)
        {
            return _jobs.LastOrDefault(j =>
                string.Equals(j.EventAppointmentId, eventAppointmentId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public async Task<(bool Started, string? Error, DropboxUploadJob? Job)> StartBackgroundUploadAsync(
        CustomerDropboxFolderItem container,
        string fileName,
        long fileSizeBytes,
        string? contentType,
        string inputElementId,
        IJSRuntime js)
    {
        var (started, error, jobs) = await StartBackgroundBatchUploadAsync(
            container,
            new[] { new DropboxQueuedFileRequest(fileName, fileSizeBytes, contentType) },
            inputElementId,
            js);

        return (started, error, jobs.FirstOrDefault());
    }

    public async Task<(bool Started, string? Error, IReadOnlyList<DropboxUploadJob> Jobs)> StartBackgroundBatchUploadAsync(
        CustomerDropboxFolderItem container,
        IReadOnlyList<DropboxQueuedFileRequest> files,
        string inputElementId,
        IJSRuntime js)
    {
        if (files == null || files.Count == 0)
            return (false, "Please select at least one .zip file.", Array.Empty<DropboxUploadJob>());

        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file.FileName) ||
                !Path.GetExtension(file.FileName.Trim()).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"File '{file.FileName}' is not a valid .zip archive.", Array.Empty<DropboxUploadJob>());
            }

            if (file.FileSizeBytes <= 0 || file.FileSizeBytes > MaxZipSizeBytes)
            {
                return (false, $"ZIP file '{file.FileName}' must be between 1 byte and 500 MB.", Array.Empty<DropboxUploadJob>());
            }
        }

        var jwt = await _tokenStore.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(jwt))
            return (false, "Your session has expired. Please log in again before uploading.", Array.Empty<DropboxUploadJob>());

        _jsRuntime = js;

        var newJobs = new List<DropboxUploadJob>(files.Count);
        foreach (var file in files)
        {
            newJobs.Add(new DropboxUploadJob
            {
                JobId = Guid.NewGuid().ToString("N"),
                EventAppointmentId = container.EventAppointmentId,
                EventName = container.EventName,
                CustomerName = string.IsNullOrWhiteSpace(container.CustomerName) ? container.CustomerId : container.CustomerName,
                CustomerId = container.CustomerId,
                FileName = Path.GetFileName(file.FileName.Trim()),
                TotalBytes = file.FileSizeBytes,
                ContentType = file.ContentType,
                InputElementId = inputElementId,
                JsFileToken = file.JsFileToken,
                InputFileIndex = file.InputFileIndex,
                FallbackJwt = jwt,
                BytesSent = 0,
                Percent = 0,
                Status = DropboxUploadJobStatus.Queued
            });
        }

        lock (_lock)
        {
            // Clear prior finished/failed jobs for this event when a new batch is queued
            _jobs.RemoveAll(j =>
                !j.IsActive &&
                string.Equals(j.EventAppointmentId, container.EventAppointmentId, StringComparison.OrdinalIgnoreCase));
            _jobs.AddRange(newJobs);
        }

        NotifyChanged();

        var firstStartError = await TryStartNextQueuedJobAsync(js);
        var firstJob = newJobs[0];
        if (firstJob.Status == DropboxUploadJobStatus.Failed && newJobs.All(j => j.Status == DropboxUploadJobStatus.Failed))
        {
            return (false, firstStartError ?? firstJob.ErrorMessage ?? "Could not start ZIP upload.", newJobs);
        }

        return (true, null, newJobs);
    }

    public async Task<string?> TryStartNextQueuedJobAsync(IJSRuntime? jsOverride = null)
    {
        var js = jsOverride ?? _jsRuntime;
        if (js == null) return null;

        await _dispatchLock.WaitAsync();
        try
        {
            while (true)
            {
                DropboxUploadJob? nextJob;
                lock (_lock)
                {
                    if (_jobs.Any(j => j.IsTransferring))
                        return null;

                    nextJob = _jobs.FirstOrDefault(j => j.Status == DropboxUploadJobStatus.Queued);
                    if (nextJob == null)
                        return null;
                }

                var jwt = await _tokenStore.GetTokenAsync();
                if (string.IsNullOrWhiteSpace(jwt))
                    jwt = nextJob.FallbackJwt;

                if (string.IsNullOrWhiteSpace(jwt))
                {
                    MarkJobFailedInternal(nextJob.JobId, "Your session has expired. Please log in again before uploading.");
                    return "Your session has expired. Please log in again before uploading.";
                }

                var ticket = _ticketStore.CreateTicket(
                    jwt,
                    nextJob.EventAppointmentId,
                    nextJob.FileName,
                    nextJob.TotalBytes,
                    nextJob.ContentType);

                lock (_lock)
                {
                    if (nextJob.Status != DropboxUploadJobStatus.Queued)
                    {
                        _ticketStore.RemoveTicket(ticket.TicketId);
                        continue;
                    }

                    nextJob.TicketId = ticket.TicketId;
                    nextJob.Status = DropboxUploadJobStatus.Uploading;
                }

                NotifyChanged();

                try
                {
                    var started = await js.InvokeAsync<bool>(
                        "aevumDropboxUpload.start",
                        nextJob.JobId,
                        ticket.TicketId,
                        nextJob.InputElementId,
                        nextJob.TotalBytes,
                        nextJob.JsFileToken,
                        nextJob.InputFileIndex);

                    if (!started)
                    {
                        const string msg = "Could not access the selected ZIP file in the browser. Please re-select it and try again.";
                        _ticketStore.RemoveTicket(ticket.TicketId);
                        MarkJobFailedInternal(nextJob.JobId, msg);
                        continue;
                    }

                    return null;
                }
                catch (Exception ex)
                {
                    _ticketStore.RemoveTicket(ticket.TicketId);
                    MarkJobFailedInternal(nextJob.JobId, ex.Message);
                    return ex.Message;
                }
            }
        }
        finally
        {
            _dispatchLock.Release();
        }
    }

    public void ReportProgress(string jobId, long bytesSent, int percent, bool isFinalizing)
    {
        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.JobId == jobId);
            if (job == null || !job.IsTransferring) return;

            job.BytesSent = Math.Clamp(bytesSent, 0, job.TotalBytes > 0 ? job.TotalBytes : bytesSent);
            job.Percent = Math.Clamp(percent, 0, 99);
            job.Status = isFinalizing ? DropboxUploadJobStatus.Finalizing : DropboxUploadJobStatus.Uploading;
        }

        NotifyChanged();
    }

    public void ReportCompletedFromJson(string jobId, string? resultJson)
    {
        CustomerDropboxFolderItem? item = null;
        if (!string.IsNullOrWhiteSpace(resultJson))
        {
            try
            {
                item = JsonSerializer.Deserialize<CustomerDropboxFolderItem>(resultJson, JsonOptions);
            }
            catch
            {
            }
        }

        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.JobId == jobId);
            if (job == null) return;

            job.BytesSent = job.TotalBytes;
            job.Percent = 100;
            job.Status = DropboxUploadJobStatus.Completed;
            job.CompletedItem = item;
            job.ErrorMessage = null;
            job.FallbackJwt = null;
            if (!string.IsNullOrWhiteSpace(job.TicketId))
                _ticketStore.RemoveTicket(job.TicketId);
        }

        NotifyChanged();
        _ = TryStartNextQueuedJobAsync();
    }

    public void MarkJobFailed(string jobId, string? error)
    {
        MarkJobFailedInternal(jobId, error);
        _ = TryStartNextQueuedJobAsync();
    }

    private void MarkJobFailedInternal(string jobId, string? error)
    {
        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.JobId == jobId);
            if (job == null) return;

            job.Status = DropboxUploadJobStatus.Failed;
            job.ErrorMessage = string.IsNullOrWhiteSpace(error) ? "ZIP upload failed." : error;
            job.FallbackJwt = null;
            if (!string.IsNullOrWhiteSpace(job.TicketId))
                _ticketStore.RemoveTicket(job.TicketId);
        }

        NotifyChanged();
    }

    public async Task CancelJobAsync(string jobId, IJSRuntime js)
    {
        _jsRuntime = js;
        string? ticketId = null;
        string? jsFileToken = null;
        bool wasTransferring = false;

        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.JobId == jobId);
            if (job == null) return;
            ticketId = job.TicketId;
            jsFileToken = job.JsFileToken;
            wasTransferring = job.IsTransferring;
            job.Status = DropboxUploadJobStatus.Canceled;
            job.ErrorMessage = "Upload canceled.";
            job.FallbackJwt = null;
        }

        NotifyChanged();

        if (wasTransferring)
        {
            try
            {
                await js.InvokeVoidAsync("aevumDropboxUpload.cancel", jobId);
            }
            catch
            {
            }
        }
        else if (!string.IsNullOrWhiteSpace(jsFileToken))
        {
            try
            {
                await js.InvokeVoidAsync("aevumDropboxUpload.releaseStagedFile", jsFileToken);
            }
            catch
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(ticketId))
            _ticketStore.RemoveTicket(ticketId);

        await TryStartNextQueuedJobAsync(js);
    }

    public void DismissJob(string jobId)
    {
        lock (_lock)
        {
            _jobs.RemoveAll(j => j.JobId == jobId && !j.IsActive);
        }
        NotifyChanged();
    }

    private void NotifyChanged() => OnJobsChanged?.Invoke();
}
