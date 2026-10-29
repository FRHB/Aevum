using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Infrastructure.Services;

public class R2StorageService : IR2StorageService
{
    private readonly AmazonS3Client? _s3Client;
    private readonly ILogger<R2StorageService> _logger;
    private readonly bool _isConfigured;

    public string PortfolioBucket { get; }
    public string ServicesBucket { get; }

    public R2StorageService(IConfiguration configuration, ILogger<R2StorageService> logger)
    {
        _logger = logger;
        var accessKey = configuration["CloudflareR2:AccessKey"]
                        ?? configuration["R2_ACCESS_KEY"]
                        ?? "PLACEHOLDER";
        var secretKey = configuration["CloudflareR2:SecretKey"]
                        ?? configuration["R2_SECRET_KEY"]
                        ?? "PLACEHOLDER";
        var accountId = configuration["CloudflareR2:AccountId"]
                        ?? configuration["R2_ACCOUNT_ID"];
        var serviceUrl = configuration["CloudflareR2:ServiceUrl"]
                         ?? configuration["R2_SERVICE_URL"];

        if (string.IsNullOrWhiteSpace(serviceUrl))
        {
            serviceUrl = !string.IsNullOrWhiteSpace(accountId)
                ? $"https://{accountId.Trim()}.r2.cloudflarestorage.com"
                : "https://f6757b400d51c23671cedb8b0efc4b32.r2.cloudflarestorage.com";
        }

        PortfolioBucket = configuration["CloudflareR2:Buckets:Portfolio"]
                          ?? configuration["CloudflareR2:BucketName"]
                          ?? "portfolio";
        ServicesBucket = configuration["CloudflareR2:Buckets:Services"]
                         ?? "services";

        _isConfigured = !string.IsNullOrWhiteSpace(accessKey) 
                        && !string.IsNullOrWhiteSpace(secretKey) 
                        && accessKey != "PLACEHOLDER" 
                        && secretKey != "PLACEHOLDER";

        try
        {
            var credentials = new BasicAWSCredentials(accessKey, secretKey);
            var s3Config = new AmazonS3Config
            {
                ServiceURL = serviceUrl,
                AuthenticationRegion = "auto"
            };
            _s3Client = new AmazonS3Client(credentials, s3Config);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize AmazonS3Client for Cloudflare R2.");
        }
    }

    public async Task<List<string>> UploadImagesAsync(List<IFormFile> files, string folderPrefix, string? bucketName = null)
    {
        var savedFileNames = new List<string>();

        if (files == null || files.Count == 0)
            return savedFileNames;

        var prefix = string.IsNullOrWhiteSpace(folderPrefix) ? "portfolio" : folderPrefix.Trim().TrimEnd('/');
        var targetBucket = !string.IsNullOrWhiteSpace(bucketName)
            ? bucketName.Trim()
            : (prefix.StartsWith("services", StringComparison.OrdinalIgnoreCase) ? ServicesBucket : PortfolioBucket);

        foreach (var file in files)
        {
            if (file.Length == 0) continue;

            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            var fileName = $"{prefix}/{Guid.NewGuid():N}{ext}";

            if (_isConfigured && _s3Client != null)
            {
                try
                {
                    using var stream = file.OpenReadStream();
                    var putRequest = new PutObjectRequest
                    {
                        BucketName = targetBucket,
                        Key = fileName,
                        InputStream = stream,
                        ContentType = file.ContentType,
                        DisablePayloadSigning = true
                    };

                    await _s3Client.PutObjectAsync(putRequest);
                    savedFileNames.Add(fileName);
                    continue;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed uploading file {FileName} to Cloudflare R2 bucket {Bucket}. Falling back to local storage.", fileName, targetBucket);
                }
            }

            // Fallback for placeholder credentials: save locally in wwwroot/uploads
            try
            {
                var localUploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", prefix);
                Directory.CreateDirectory(localUploadDir);
                var localFilePath = Path.Combine(localUploadDir, Path.GetFileName(fileName));
                using (var localStream = new FileStream(localFilePath, FileMode.Create))
                {
                    await file.CopyToAsync(localStream);
                }
                savedFileNames.Add(fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed fallback saving of image {FileName}", fileName);
                savedFileNames.Add(fileName);
            }
        }

        return savedFileNames;
    }

    public async Task<string> UploadServiceImageAsync(
        byte[] imageBytes,
        string? originalFileName,
        string? contentType,
        string folderPrefix = "services",
        CancellationToken cancellationToken = default)
    {
        if (imageBytes == null || imageBytes.Length == 0)
            return string.Empty;

        var prefix = string.IsNullOrWhiteSpace(folderPrefix) ? "services" : folderPrefix.Trim().Trim('/');
        var ext = !string.IsNullOrWhiteSpace(originalFileName) ? Path.GetExtension(originalFileName) : null;
        if (string.IsNullOrWhiteSpace(ext))
        {
            ext = (contentType?.ToLowerInvariant()) switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                _ => ".jpg"
            };
        }

        var fileName = $"{prefix}/{Guid.NewGuid():N}{ext}";
        var resolvedMime = string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType;

        if (_isConfigured && _s3Client != null)
        {
            try
            {
                using var ms = new MemoryStream(imageBytes);
                var putRequest = new PutObjectRequest
                {
                    BucketName = ServicesBucket,
                    Key = fileName,
                    InputStream = ms,
                    ContentType = resolvedMime,
                    DisablePayloadSigning = true
                };

                await _s3Client.PutObjectAsync(putRequest, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed uploading service image {FileName} to Cloudflare R2 bucket {Bucket}. Saving to local fallback.", fileName, ServicesBucket);
            }
        }

        // Persist local fallback copy under wwwroot/uploads/{prefix}
        try
        {
            var localUploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", prefix.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(localUploadDir);
            var localFilePath = Path.Combine(localUploadDir, Path.GetFileName(fileName));
            await File.WriteAllBytesAsync(localFilePath, imageBytes, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist local fallback copy for service image {FileName}", fileName);
        }

        return fileName;
    }

    public string GetPresignedUrl(string fileName, string? bucketName = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        var targetBucket = !string.IsNullOrWhiteSpace(bucketName)
            ? bucketName.Trim()
            : (fileName.StartsWith("services", StringComparison.OrdinalIgnoreCase) ? ServicesBucket : PortfolioBucket);

        if (_isConfigured && _s3Client != null)
        {
            try
            {
                var request = new GetPreSignedUrlRequest
                {
                    BucketName = targetBucket,
                    Key = fileName,
                    Expires = DateTime.UtcNow.AddHours(1)
                };
                return _s3Client.GetPreSignedURL(request);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate presigned URL for {FileName} in bucket {Bucket}", fileName, targetBucket);
            }
        }

        // Return fallback path for local wwwroot or relative URL
        return $"/uploads/{fileName.TrimStart('/')}";
    }
}
