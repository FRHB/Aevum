using System.Text.Json;
using Microsoft.JSInterop;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public enum DropboxUploadJobStatus
{
    Uploading,
    Finalizing,
    Completed,
    Failed,
    Canceled
}

public sealed class DropboxUploadJob
{
    public string JobId { get; init; } = string.Empty;
    public string TicketId { get; init; } = string.Empty;
    public string EventAppointmentId { get; init; } = string.Empty;
    public string EventName { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long TotalBytes { get; init; }
    public long BytesSent { get; set; }
    public int Percent { get; set; }
    public DropboxUploadJobStatus Status { get; set; } = DropboxUploadJobStatus.Uploading;
    public string? ErrorMessage { get; set; }
    public CustomerDropboxFolderItem? CompletedItem { get; set; }
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public bool IsActive => Status is DropboxUploadJobStatus.Uploading or DropboxUploadJobStatus.Finalizing;
    public bool IsFinalizing => Status == DropboxUploadJobStatus.Finalizing;
}

/// <summary>
/// Circuit-scoped coordinator that tracks background ZIP uploads across modal closes
/// and page navigations while delegating chunked HTTP transfer to browser JS.
/// </summary>
public sealed class DropboxBackgroundUploadService
{
    private readonly ITokenStore _tokenStore;
    private readonly DropboxUploadTicketStore _ticketStore;
    private readonly List<DropboxUploadJob> _jobs = new();
    private readonly object _lock = new();

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

    public DropboxUploadJob? GetActiveJobForEvent(string eventAppointmentId)
    {
        if (string.IsNullOrWhiteSpace(eventAppointmentId)) return null;
        lock (_lock)
        {
            return _jobs.LastOrDefault(j =>
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
        if (GetActiveJobForEvent(container.EventAppointmentId) is { } existing)
            return (false, "An upload is already running in the background for this event.", existing);

        var jwt = await _tokenStore.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(jwt))
            return (false, "Your session has expired. Please log in again before uploading.", null);

        var ticket = _ticketStore.CreateTicket(
            jwt,
            container.EventAppointmentId,
            fileName,
            fileSizeBytes,
            contentType);

        var job = new DropboxUploadJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            TicketId = ticket.TicketId,
            EventAppointmentId = container.EventAppointmentId,
            EventName = container.EventName,
            CustomerName = string.IsNullOrWhiteSpace(container.CustomerName) ? container.CustomerId : container.CustomerName,
            CustomerId = container.CustomerId,
            FileName = fileName,
            TotalBytes = fileSizeBytes,
            BytesSent = 0,
            Percent = 0,
            Status = DropboxUploadJobStatus.Uploading
        };

        lock (_lock)
        {
            // Replace any prior finished/failed job for the same event so list stays clean
            _jobs.RemoveAll(j =>
                !j.IsActive &&
                string.Equals(j.EventAppointmentId, container.EventAppointmentId, StringComparison.OrdinalIgnoreCase));
            _jobs.Add(job);
        }

        NotifyChanged();

        try
        {
            var started = await js.InvokeAsync<bool>(
                "aevumDropboxUpload.start",
                job.JobId,
                ticket.TicketId,
                inputElementId,
                fileSizeBytes);

            if (!started)
            {
                _ticketStore.RemoveTicket(ticket.TicketId);
                MarkJobFailed(job.JobId, "Could not access the selected ZIP file in the browser. Please re-select it and try again.");
                return (false, "Could not access the selected ZIP file in the browser. Please re-select it and try again.", job);
            }

            return (true, null, job);
        }
        catch (Exception ex)
        {
            _ticketStore.RemoveTicket(ticket.TicketId);
            MarkJobFailed(job.JobId, ex.Message);
            return (false, ex.Message, job);
        }
    }

    public void ReportProgress(string jobId, long bytesSent, int percent, bool isFinalizing)
    {
        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.JobId == jobId);
            if (job == null || !job.IsActive) return;

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
            _ticketStore.RemoveTicket(job.TicketId);
        }

        NotifyChanged();
    }

    public void MarkJobFailed(string jobId, string? error)
    {
        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.JobId == jobId);
            if (job == null) return;

            job.Status = DropboxUploadJobStatus.Failed;
            job.ErrorMessage = string.IsNullOrWhiteSpace(error) ? "ZIP upload failed." : error;
            _ticketStore.RemoveTicket(job.TicketId);
        }

        NotifyChanged();
    }

    public async Task CancelJobAsync(string jobId, IJSRuntime js)
    {
        string? ticketId = null;
        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.JobId == jobId);
            if (job == null) return;
            ticketId = job.TicketId;
            job.Status = DropboxUploadJobStatus.Canceled;
            job.ErrorMessage = "Upload canceled.";
        }

        NotifyChanged();

        try
        {
            await js.InvokeVoidAsync("aevumDropboxUpload.cancel", jobId);
        }
        catch
        {
        }

        if (!string.IsNullOrWhiteSpace(ticketId))
            _ticketStore.RemoveTicket(ticketId);
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
