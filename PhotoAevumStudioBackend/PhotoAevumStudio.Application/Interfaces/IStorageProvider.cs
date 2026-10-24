<<<<<<< HEAD
namespace PhotoAevumStudio.Application.Interfaces;

public interface IStorageProvider
{
    /// <summary>
    /// Uploads a file stream and returns the secure URL or identifier.
    /// </summary>
    Task<string> UploadFileAsync(string fileName, Stream stream, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file from storage by its identifier.
    /// </summary>
    Task DeleteFileAsync(string fileIdentifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a secure, potentially time-limited download URL for a private photo.
    /// </summary>
    Task<string> GetSecureUrlAsync(string fileIdentifier, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
}
=======
namespace PhotoAevumStudio.Application.Interfaces;

public interface IStorageProvider
{
    /// <summary>
    /// Uploads a file stream and returns the secure URL or identifier.
    /// </summary>
    Task<string> UploadFileAsync(string fileName, Stream stream, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file from storage by its identifier.
    /// </summary>
    Task DeleteFileAsync(string fileIdentifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a secure, potentially time-limited download URL for a private photo.
    /// </summary>
    Task<string> GetSecureUrlAsync(string fileIdentifier, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
}
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
