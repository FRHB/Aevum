namespace PhotoAevumStudio.Application;

public static class DropboxStatuses
{
    public const string Waiting = "Waiting";
    public const string Uploaded = "Uploaded";
    public const string Expired = "Expired";
}

public static class DropboxCountdownHelper
{
    public const int RetentionDays = 30;

    public static int ComputeDaysRemaining(DateTime expiresAtUtc, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var remaining = expiresAtUtc - now;
        if (remaining <= TimeSpan.Zero)
            return 0;

        // Immediately after completion (e.g., 29 days 23 hours left), Floor yields 29 ("29 Days Left").
        return Math.Clamp((int)Math.Floor(remaining.TotalDays), 0, RetentionDays);
    }

    public static string FormatCountdownLabel(DateTime expiresAtUtc, DateTime? nowUtc = null)
    {
        var days = ComputeDaysRemaining(expiresAtUtc, nowUtc);
        return days == 1 ? "1 Day Left" : $"{days} Days Left";
    }
}

public class CustomerDropboxFolderDto
{
    public string DropboxId { get; set; } = string.Empty;
    public string EventAppointmentId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string EventDescription { get; set; } = string.Empty;
    public DateOnly EventDate { get; set; }
    public TimeOnly EventTime { get; set; }
    public string EventStatus { get; set; } = string.Empty;
    public string DropboxStatus { get; set; } = DropboxStatuses.Waiting;
    public string? OriginalFileName { get; set; }
    public long? FileSizeBytes { get; set; }
    public DateTime EventCompletedAt { get; set; }
    public DateTime? UploadedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int DaysRemaining { get; set; }
    public string CountdownLabel { get; set; } = string.Empty;
    public bool HasFile => string.Equals(DropboxStatus, DropboxStatuses.Uploaded, StringComparison.OrdinalIgnoreCase)
                           && !string.IsNullOrWhiteSpace(OriginalFileName);
}

public class DropboxDownloadUrlDto
{
    public string DropboxId { get; set; } = string.Empty;
    public string EventAppointmentId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public class DropboxPurgeResultDto
{
    public int PurgedCount { get; set; }
    public List<string> PurgedDropboxIds { get; set; } = new();
    public List<string> DeletedR2Keys { get; set; } = new();
}
