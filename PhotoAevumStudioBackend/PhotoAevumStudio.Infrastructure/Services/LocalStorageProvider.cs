using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Infrastructure.Services;

public class LocalStorageProvider : IStorageProvider
{
    private readonly string _storageDirectory;
    private readonly string _baseUrl;

    public LocalStorageProvider()
    {
        // Store private photos outside wwwroot so they cannot be served anonymously as static files
        _storageDirectory = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "private-photos");
        if (!Directory.Exists(_storageDirectory))
        {
            Directory.CreateDirectory(_storageDirectory);
        }

        _baseUrl = "/api/photos/download";
    }

    private string ResolveSafePath(string fileNameOrIdentifier)
    {
        var safeFileName = Path.GetFileName(fileNameOrIdentifier?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(safeFileName) ||
            safeFileName.Contains("..", StringComparison.Ordinal) ||
            safeFileName != fileNameOrIdentifier?.Trim())
        {
            throw new ArgumentException("Invalid file identifier.", nameof(fileNameOrIdentifier));
        }

        var fullRoot = Path.GetFullPath(_storageDirectory);
        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
            fullRoot += Path.DirectorySeparatorChar;

        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, safeFileName));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path traversal detected.");

        return fullPath;
    }

    public async Task<string> UploadFileAsync(string fileName, Stream stream, string contentType, CancellationToken cancellationToken = default)
    {
        var cleanOriginal = Path.GetFileName(fileName?.Trim() ?? "upload.bin");
        if (string.IsNullOrWhiteSpace(cleanOriginal))
            cleanOriginal = "upload.bin";
        var uniqueFileName = $"{Guid.NewGuid()}_{cleanOriginal}";
        var filePath = ResolveSafePath(uniqueFileName);

        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        await stream.CopyToAsync(fileStream, cancellationToken);

        return uniqueFileName; // We store the identifier, not the full URL in DB for security
    }

    public Task DeleteFileAsync(string fileIdentifier, CancellationToken cancellationToken = default)
    {
        var filePath = ResolveSafePath(fileIdentifier);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
        return Task.CompletedTask;
    }

    public Task<string> GetSecureUrlAsync(string fileIdentifier, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var safeIdentifier = Path.GetFileName(fileIdentifier?.Trim() ?? string.Empty);
        return Task.FromResult($"{_baseUrl}/{Uri.EscapeDataString(safeIdentifier)}");
    }
}
