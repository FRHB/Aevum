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

    Task<CustomerDropboxFolderDto> UploadZipAsync(
        string eventAppointmentId,
        Stream zipStream,
        string originalFileName,
        long fileSizeBytes,
        string? contentType,
        string uploadedByEmployeeId,
        Action<long>? onR2BytesTransferred,
        CancellationToken ct = default)
        => UploadZipAsync(eventAppointmentId, zipStream, originalFileName, fileSizeBytes, contentType, uploadedByEmployeeId, ct);

    Task ValidateUploadTargetAsync(string eventAppointmentId, CancellationToken ct = default)
        => Task.CompletedTask;

    Task<(string FileName, string ContentType, Stream FileStream, string PresignedUrl)> GetCustomerDownloadAsync(
        string dropboxId,
        string callerCustomerId,
        CancellationToken ct = default);

    Task<(string FileName, string ContentType, Stream FileStream, string PresignedUrl)> GetCustomerDownloadAsync(
        string dropboxId,
        string callerCustomerId,
        string? dropboxFileId,
        CancellationToken ct = default)
        => GetCustomerDownloadAsync(dropboxId, callerCustomerId, ct);

    Task<DropboxDownloadUrlDto> GetCustomerDownloadInfoAsync(
        string dropboxId,
        string callerCustomerId,
        string apiBaseUrl,
        CancellationToken ct = default);

    Task<DropboxDownloadUrlDto> GetCustomerDownloadInfoAsync(
        string dropboxId,
        string callerCustomerId,
        string apiBaseUrl,
        string? dropboxFileId,
        CancellationToken ct = default)
        => GetCustomerDownloadInfoAsync(dropboxId, callerCustomerId, apiBaseUrl, ct);

    Task<CustomerDropboxFolderDto> DeleteDropboxFileAsync(
        string eventAppointmentId,
        string dropboxFileId,
        string deletedByEmployeeId,
        CancellationToken ct = default)
        => throw new NotSupportedException("Deleting individual Dropbox ZIP files is not supported by this implementation.");

    Task<DropboxPurgeResultDto> PurgeExpiredDropboxesAsync(CancellationToken ct = default);
}
