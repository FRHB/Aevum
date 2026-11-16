namespace PhotoAevumStudio.Application.Interfaces;

public interface ICustomerDropboxService
{
    Task EnsureSchemaAsync(CancellationToken ct = default);

    Task EnsureFolderForCompletedEventAsync(string eventAppointmentId, CancellationToken ct = default);

    Task<IReadOnlyList<CustomerDropboxFolderDto>> GetCustomerDropboxesAsync(string customerId, CancellationToken ct = default);

    Task<IReadOnlyList<CustomerDropboxFolderDto>> GetAdminEventContainersAsync(CancellationToken ct = default);

    Task<CustomerDropboxFolderDto> UploadZipAsync(
        string eventAppointmentId,
        Stream zipStream,
        string originalFileName,
        long fileSizeBytes,
        string? contentType,
        string uploadedByEmployeeId,
        CancellationToken ct = default);

    Task<(string FileName, string ContentType, Stream FileStream, string PresignedUrl)> GetCustomerDownloadAsync(
        string dropboxId,
        string callerCustomerId,
        CancellationToken ct = default);

    Task<DropboxDownloadUrlDto> GetCustomerDownloadInfoAsync(
        string dropboxId,
        string callerCustomerId,
        string apiBaseUrl,
        CancellationToken ct = default);

    Task<DropboxPurgeResultDto> PurgeExpiredDropboxesAsync(CancellationToken ct = default);
}
