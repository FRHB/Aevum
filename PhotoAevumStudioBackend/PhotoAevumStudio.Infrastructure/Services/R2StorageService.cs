using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
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
    private readonly string _publicBaseUrl;

    public string PortfolioBucket { get; }
    public string ServicesBucket { get; }
    public string ArchiveBucket { get; }

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

        _publicBaseUrl = (configuration["CloudflareR2:PublicUrl"] ?? serviceUrl).TrimEnd('/');

        PortfolioBucket = configuration["CloudflareR2:Buckets:Portfolio"]
                          ?? configuration["CloudflareR2:BucketName"]
                          ?? "portfolio";
        ServicesBucket = configuration["CloudflareR2:Buckets:Services"]
                         ?? "services";
        ArchiveBucket = configuration["CloudflareR2:Buckets:Archive"]
                        ?? "archive";

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

    public async Task CreateBucketIfNotExistsAsync(string bucketName)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
            throw new ArgumentException("Bucket name cannot be null or empty.", nameof(bucketName));

        if (_s3Client == null)
            throw new InvalidOperationException("AmazonS3Client is not initialized.");

        var normalizedBucket = bucketName.Trim();

        try
        {
            var exists = await AmazonS3Util.DoesS3BucketExistV2Async(_s3Client, normalizedBucket);
            if (!exists)
            {
                var putBucketRequest = new PutBucketRequest
                {
                    BucketName = normalizedBucket
                };
                await _s3Client.PutBucketAsync(putBucketRequest);
                _logger.LogInformation("Provisioned Cloudflare R2 bucket '{BucketName}'.", normalizedBucket);
            }
        }
        catch (AmazonS3Exception ex) when (
            string.Equals(ex.ErrorCode, "BucketAlreadyOwnedByYou", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ex.ErrorCode, "BucketAlreadyExists", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Cloudflare R2 bucket '{BucketName}' already exists.", normalizedBucket);
        }
    }

    public async Task<string> UploadFileAsync(string bucketName, string objectKey, Stream fileStream, string contentType)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
            throw new ArgumentException("Bucket name cannot be null or empty.", nameof(bucketName));
        if (string.IsNullOrWhiteSpace(objectKey))
            throw new ArgumentException("Object key cannot be null or empty.", nameof(objectKey));
        if (fileStream == null)
            throw new ArgumentNullException(nameof(fileStream));

        if (_s3Client == null)
            throw new InvalidOperationException("AmazonS3Client is not initialized.");

        var targetBucket = bucketName.Trim();
        var normalizedKey = objectKey.Trim().TrimStart('/');
        var resolvedContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;

        if (fileStream.CanSeek && fileStream.Position > 0)
        {
            fileStream.Position = 0;
        }

        var putRequest = new PutObjectRequest
        {
            BucketName = targetBucket,
            Key = normalizedKey,
            InputStream = fileStream,
            ContentType = resolvedContentType,
            DisablePayloadSigning = true
        };

        var response = await _s3Client.PutObjectAsync(putRequest);
        if (response.HttpStatusCode == HttpStatusCode.OK)
        {
            return $"r2://{targetBucket}/{normalizedKey}";
        }

        _logger.LogWarning("Upload to Cloudflare R2 bucket '{Bucket}' for key '{Key}' returned unexpected HTTP status {StatusCode}.",
            targetBucket, normalizedKey, response.HttpStatusCode);
        return string.Empty;
    }

    public async Task<Stream> DownloadFileAsync(string bucketName, string objectKey)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
            throw new ArgumentException("Bucket name cannot be null or empty.", nameof(bucketName));
        if (string.IsNullOrWhiteSpace(objectKey))
            throw new ArgumentException("Object key cannot be null or empty.", nameof(objectKey));

        if (_s3Client == null)
            throw new InvalidOperationException("AmazonS3Client is not initialized.");

        var getRequest = new GetObjectRequest
        {
            BucketName = bucketName.Trim(),
            Key = objectKey.Trim().TrimStart('/')
        };

        var response = await _s3Client.GetObjectAsync(getRequest);
        return response.ResponseStream;
    }

    public async Task<bool> DeleteFileAsync(string bucketName, string objectKey)
    {
        if (string.IsNullOrWhiteSpace(bucketName) || string.IsNullOrWhiteSpace(objectKey) || _s3Client == null)
            return false;

        try
        {
            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = bucketName.Trim(),
                Key = objectKey.Trim().TrimStart('/')
            };

            var response = await _s3Client.DeleteObjectAsync(deleteRequest);
            return response.HttpStatusCode == HttpStatusCode.NoContent
                   || response.HttpStatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete object '{Key}' from Cloudflare R2 bucket '{Bucket}'.", objectKey, bucketName);
            return false;
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
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    using var stream = file.OpenReadStream();
                    var putRequest = new PutObjectRequest
                    {
                        BucketName = targetBucket,
                        Key = fileName,
                        InputStream = stream,
                        ContentType = file.ContentType,
                        DisablePayloadSigning = true
                    };

                    await _s3Client.PutObjectAsync(putRequest, cts.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed uploading file {FileName} to Cloudflare R2 bucket {Bucket}.", fileName, targetBucket);
                }
            }

            savedFileNames.Add(fileName);
        }

        return savedFileNames;
    }

    public async Task<string> UploadServiceImageAsync(
        byte[] imageBytes,
        string? originalFileName,
        string? contentType,
        string folderPrefix = "services",
        CancellationToken cancellationToken = default,
        string? bucketName = null)
    {
        if (imageBytes == null || imageBytes.Length == 0)
            return string.Empty;

        var prefix = string.IsNullOrWhiteSpace(folderPrefix) ? "services" : folderPrefix.Trim().Trim('/');
        var targetBucket = !string.IsNullOrWhiteSpace(bucketName) ? bucketName.Trim() : ServicesBucket;
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
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));

                using var ms = new MemoryStream(imageBytes);
                var putRequest = new PutObjectRequest
                {
                    BucketName = targetBucket,
                    Key = fileName,
                    InputStream = ms,
                    ContentType = resolvedMime,
                    DisablePayloadSigning = true
                };

                await _s3Client.PutObjectAsync(putRequest, cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed uploading service image {FileName} to Cloudflare R2 bucket {Bucket}.", fileName, targetBucket);
            }
        }

        return fileName;
    }

    public string GetPresignedUrl(string fileName, string? bucketName = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        var cleanKey = fileName.TrimStart('/');
        var targetBucket = !string.IsNullOrWhiteSpace(bucketName)
            ? bucketName.Trim()
            : (cleanKey.StartsWith("services", StringComparison.OrdinalIgnoreCase) ? ServicesBucket : PortfolioBucket);

        if (_isConfigured && _s3Client != null)
        {
            try
            {
                var request = new GetPreSignedUrlRequest
                {
                    BucketName = targetBucket,
                    Key = cleanKey,
                    Expires = DateTime.UtcNow.AddHours(1)
                };
                var presigned = _s3Client.GetPreSignedURL(request);
                if (!string.IsNullOrWhiteSpace(presigned))
                    return presigned;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate presigned URL for {FileName} in bucket {Bucket}", cleanKey, targetBucket);
            }
        }

        return $"{_publicBaseUrl}/{targetBucket}/{cleanKey}";
    }
}
