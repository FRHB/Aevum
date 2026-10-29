using Microsoft.AspNetCore.Http;

namespace PhotoAevumStudio.Application.Interfaces;

public interface IR2StorageService
{
    string PortfolioBucket { get; }
    string ServicesBucket { get; }
    Task<List<string>> UploadImagesAsync(List<IFormFile> files, string folderPrefix, string? bucketName = null);
    Task<string> UploadServiceImageAsync(byte[] imageBytes, string? originalFileName, string? contentType, string folderPrefix = "services", CancellationToken cancellationToken = default);
    string GetPresignedUrl(string fileName, string? bucketName = null);
}
