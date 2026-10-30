namespace PhotoAevumStudio.Application.Entities;

public class ArchiveIndex
{
    public int ArchiveId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string BucketName { get; set; } = "archive";
    public string R2Key { get; set; } = string.Empty;
    public string FileUri { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public DateTime? DateRangeFrom { get; set; }
    public DateTime? DateRangeTo { get; set; }
    public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;
}
