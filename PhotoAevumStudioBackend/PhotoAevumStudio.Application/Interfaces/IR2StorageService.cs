using Microsoft.AspNetCore.Http;

namespace PhotoAevumStudio.Application.Interfaces;

public interface IR2StorageService
{
    string PortfolioBucket { get; }
    string ServicesBucket { get; }
    string ArchiveBucket { get; }

    Task CreateBucketIfNotExistsAsync(string bucketName);
    Task<string> UploadFileAsync(string bucketName, string objectKey, Stream fileStream, string contentType);
    Task<Stream> DownloadFileAsync(string bucketName, string objectKey);
    Task<bool> DeleteFileAsync(string bucketName, string objectKey);

    Task<List<string>> UploadImagesAsync(List<IFormFile> files, string folderPrefix, string? bucketName = null);
    Task<string> UploadServiceImageAsync(byte[] imageBytes, string? originalFileName, string? contentType, string folderPrefix = "services", CancellationToken cancellationToken = default, string? bucketName = null);
    string GetPresignedUrl(string fileName, string? bucketName = null);
}
