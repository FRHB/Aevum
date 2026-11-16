namespace PhotoAevumStudio.Application.Entities;

public class CustomerDropbox
{
    public string DropboxId { get; set; } = string.Empty;
    public string EventAppointmentId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string BucketName { get; set; } = "dropbox";
    public string? R2ObjectKey { get; set; }
    public string? OriginalFileName { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? ContentType { get; set; }
    public string DropboxStatus { get; set; } = "Waiting";
    public DateTime EventCompletedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UploadedAt { get; set; }
    public string? UploadedByEmployeeId { get; set; }
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(30);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
