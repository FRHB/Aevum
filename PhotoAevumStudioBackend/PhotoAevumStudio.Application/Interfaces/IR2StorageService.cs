using Microsoft.AspNetCore.Http;

namespace PhotoAevumStudio.Application.Interfaces;

public interface IR2StorageService
{
    Task<List<string>> UploadImagesAsync(List<IFormFile> files, string folderPrefix);
    string GetPresignedUrl(string fileName);
}
