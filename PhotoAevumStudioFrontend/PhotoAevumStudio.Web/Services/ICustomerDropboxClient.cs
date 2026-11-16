using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public interface ICustomerDropboxClient
{
    Task<ApiResult<IReadOnlyList<CustomerDropboxFolderItem>>> GetMyDropboxesAsync(CancellationToken ct = default);

    Task<ApiResult<DropboxDownloadUrlResponse>> GetDownloadUrlAsync(string dropboxId, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<CustomerDropboxFolderItem>>> GetAdminEventContainersAsync(CancellationToken ct = default);

    Task<ApiResult<CustomerDropboxFolderItem>> UploadEventZipAsync(
        string eventAppointmentId,
        Stream zipStream,
        string fileName,
        string? contentType,
        CancellationToken ct = default);
}
