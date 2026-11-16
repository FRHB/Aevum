namespace PhotoAevumStudio.Web.Models;

public class CustomerDropboxFolderItem
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
    public string DropboxStatus { get; set; } = "Waiting";
    public string? OriginalFileName { get; set; }
    public long? FileSizeBytes { get; set; }
    public DateTime EventCompletedAt { get; set; }
    public DateTime? UploadedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int DaysRemaining { get; set; }
    public string CountdownLabel { get; set; } = string.Empty;

    public bool HasFile => string.Equals(DropboxStatus, "Uploaded", StringComparison.OrdinalIgnoreCase)
                           && !string.IsNullOrWhiteSpace(OriginalFileName);

    public string EffectiveCountdownLabel => !string.IsNullOrWhiteSpace(CountdownLabel)
        ? CountdownLabel
        : (DaysRemaining == 1 ? "1 Day Left" : $"{Math.Clamp(DaysRemaining, 0, 30)} Days Left");

    public int CountdownPercent => (int)Math.Round(Math.Clamp(DaysRemaining, 0, 30) / 30.0 * 100.0);

    public string FormattedFileSize
    {
        get
        {
            var bytes = FileSizeBytes ?? 0;
            if (bytes <= 0) return "0 KB";
            string[] units = ["B", "KB", "MB", "GB"];
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return $"{size:0.##} {units[unit]}";
        }
    }
}

public class DropboxDownloadUrlResponse
{
    public string DropboxId { get; set; } = string.Empty;
    public string EventAppointmentId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
