using Microsoft.Extensions.Configuration;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Infrastructure.Services;

public class LocalStorageProvider : IStorageProvider
{
    private readonly string _storageDirectory;
    private readonly string _baseUrl;

    public LocalStorageProvider(IConfiguration? configuration = null)
    {
        // Store photo uploads strictly outside wwwroot to prevent data loss upon deployment
        var configuredPath = configuration?["Storage:PhotosPath"];
        _storageDirectory = !string.IsNullOrWhiteSpace(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.Combine(Directory.GetCurrentDirectory(), "Storage", "Photos");

        if (!Directory.Exists(_storageDirectory))
        {
            Directory.CreateDirectory(_storageDirectory);
        }

        _baseUrl = "/api/photos/download";
    }

    public async Task<string> UploadFileAsync(string fileName, Stream stream, string contentType, CancellationToken cancellationToken = default)
    {
        var safeOriginalName = Path.GetFileName(fileName);
        var uniqueFileName = $"{Guid.NewGuid()}_{safeOriginalName}";
        var filePath = Path.Combine(_storageDirectory, uniqueFileName);

        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        await stream.CopyToAsync(fileStream, cancellationToken);

        return uniqueFileName;
    }

    public Task DeleteFileAsync(string fileIdentifier, CancellationToken cancellationToken = default)
    {
        var safeIdentifier = Path.GetFileName(fileIdentifier);
        var filePath = Path.Combine(_storageDirectory, safeIdentifier);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
        return Task.CompletedTask;
    }

    public Task<string> GetSecureUrlAsync(string fileIdentifier, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var safeIdentifier = Path.GetFileName(fileIdentifier);
        return Task.FromResult($"{_baseUrl}/{safeIdentifier}");
    }

    public Task<(Stream Stream, string ContentType)?> GetFileAsync(string fileIdentifier, CancellationToken cancellationToken = default)
    {
        var safeIdentifier = Path.GetFileName(fileIdentifier);
        var filePath = Path.Combine(_storageDirectory, safeIdentifier);

        if (!File.Exists(filePath))
        {
            return Task.FromResult<(Stream Stream, string ContentType)?>(null);
        }

        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var contentType = GetContentType(safeIdentifier);

        return Task.FromResult<(Stream Stream, string ContentType)?>((stream, contentType));
    }

    private static string GetContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".cr2" => "image/x-canon-cr2",
            ".cr3" => "image/x-canon-cr3",
            ".nef" => "image/x-nikon-nef",
            ".arw" => "image/x-sony-arw",
            _ => "application/octet-stream"
        };
    }
}
