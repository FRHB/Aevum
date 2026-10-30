using PhotoAevumStudio.Application.Entities;

namespace PhotoAevumStudio.Application.Interfaces;

public interface IDataArchiveService
{
    /// <summary>
    /// Queries inactive and historical records (older than 1 year by default) across
    /// Employees, Admin, Customer, HistoryLog, AuditLogs, EventAppointment, and Bookings,
    /// serializes them to JSON, uploads to the Cloudflare R2 "archive" bucket, records
    /// metadata in dbo.ArchiveIndex, and only then hard-deletes the source records.
    /// Returns the uploaded archive URI(s).
    /// </summary>
    Task<string> ArchiveHistoricalDataAsync(DateTime? cutoffDate = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all archive index entries from dbo.ArchiveIndex.
    /// </summary>
    Task<IReadOnlyList<ArchiveIndex>> GetArchiveIndicesAsync(string? entityName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the archived JSON stream for a given ArchiveId from the Cloudflare R2 archive bucket.
    /// </summary>
    Task<Stream> DownloadArchivedDataAsync(int archiveId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly deletes an archived JSON object from Cloudflare R2 and removes its dbo.ArchiveIndex entry.
    /// </summary>
    Task<bool> DeleteArchivedDataAsync(int archiveId, CancellationToken cancellationToken = default);
}
