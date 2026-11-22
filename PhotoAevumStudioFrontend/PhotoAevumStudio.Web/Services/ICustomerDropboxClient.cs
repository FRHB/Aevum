using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public interface ICustomerDropboxClient
{
    Task<ApiResult<IReadOnlyList<CustomerDropboxFolderItem>>> GetMyDropboxesAsync(CancellationToken ct = default);

    Task<ApiResult<DropboxDownloadUrlResponse>> GetDownloadUrlAsync(string dropboxId, CancellationToken ct = default);

    Task<ApiResult<DropboxDownloadUrlResponse>> GetDownloadUrlAsync(string dropboxId, string? dropboxFileId, CancellationToken ct = default)
        => GetDownloadUrlAsync(dropboxId, ct);

    Task<ApiResult<IReadOnlyList<CustomerDropboxFolderItem>>> GetAdminEventContainersAsync(CancellationToken ct = default);

    Task<ApiResult<CustomerDropboxFolderItem>> UploadEventZipAsync(
        string eventAppointmentId,
        Stream zipStream,
        string fileName,
        string? contentType,
        CancellationToken ct = default);

    Task<ApiResult<CustomerDropboxFolderItem>> DeleteDropboxFileAsync(
        string eventAppointmentId,
        string dropboxFileId,
        CancellationToken ct = default)
        => Task.FromResult(ApiResult<CustomerDropboxFolderItem>.Fail(ApiErrorKind.Server, "Not implemented."));
}
