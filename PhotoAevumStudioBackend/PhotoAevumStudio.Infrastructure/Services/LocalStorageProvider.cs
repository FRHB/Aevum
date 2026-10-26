using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Infrastructure.Services;

public class LocalStorageProvider : IStorageProvider
{
    private readonly string _storageDirectory;
    private readonly string _baseUrl;

    public LocalStorageProvider()
    {
        // For phase 4 we use local disk
        _storageDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "private-photos");
        if (!Directory.Exists(_storageDirectory))
        {
            Directory.CreateDirectory(_storageDirectory);
        }
        
        // This should normally come from configuration
        _baseUrl = "/api/photos/download";
    }

    public async Task<string> UploadFileAsync(string fileName, Stream stream, string contentType, CancellationToken cancellationToken = default)
    {
        var uniqueFileName = $"{Guid.NewGuid()}_{fileName}";
        var filePath = Path.Combine(_storageDirectory, uniqueFileName);

        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        await stream.CopyToAsync(fileStream, cancellationToken);

        return uniqueFileName; // We store the identifier, not the full URL in DB for security
    }

    public Task DeleteFileAsync(string fileIdentifier, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_storageDirectory, fileIdentifier);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
        return Task.CompletedTask;
    }

    public Task<string> GetSecureUrlAsync(string fileIdentifier, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        // For a true private gallery, we return a URL to a controller endpoint that validates the user's JWT
        return Task.FromResult($"{_baseUrl}/{fileIdentifier}");
    }
}
