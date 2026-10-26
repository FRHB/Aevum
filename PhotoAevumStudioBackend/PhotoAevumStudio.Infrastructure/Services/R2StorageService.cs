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
    private readonly string _bucketName;
    private readonly ILogger<R2StorageService> _logger;
    private readonly bool _isConfigured;

    public R2StorageService(IConfiguration configuration, ILogger<R2StorageService> logger)
    {
        _logger = logger;
        var accessKey = configuration["CloudflareR2:AccessKey"] ?? "PLACEHOLDER";
        var secretKey = configuration["CloudflareR2:SecretKey"] ?? "PLACEHOLDER";
        _bucketName = configuration["CloudflareR2:BucketName"] ?? "photoaevum-portfolio";

        _isConfigured = !string.IsNullOrWhiteSpace(accessKey) 
                        && !string.IsNullOrWhiteSpace(secretKey) 
                        && accessKey != "PLACEHOLDER" 
                        && secretKey != "PLACEHOLDER";

        try
        {
            var credentials = new BasicAWSCredentials(accessKey, secretKey);
            var s3Config = new AmazonS3Config
            {
                ServiceURL = "https://f6757b400d51c23671cedb8b0efc4b32.r2.cloudflarestorage.com",
                AuthenticationRegion = "auto"
            };
            _s3Client = new AmazonS3Client(credentials, s3Config);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize AmazonS3Client for Cloudflare R2.");
        }
    }

    public async Task<List<string>> UploadImagesAsync(List<IFormFile> files, string folderPrefix)
    {
        var savedFileNames = new List<string>();

        if (files == null || files.Count == 0)
            return savedFileNames;

        var prefix = string.IsNullOrWhiteSpace(folderPrefix) ? "portfolio" : folderPrefix.Trim().TrimEnd('/');

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
                        BucketName = _bucketName,
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
                    _logger.LogError(ex, "Failed uploading file {FileName} to Cloudflare R2. Falling back to local storage.", fileName);
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

    public string GetPresignedUrl(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        if (_isConfigured && _s3Client != null)
        {
            try
            {
                var request = new GetPreSignedUrlRequest
                {
                    BucketName = _bucketName,
                    Key = fileName,
                    Expires = DateTime.UtcNow.AddHours(1)
                };
                return _s3Client.GetPreSignedURL(request);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate presigned URL for {FileName}", fileName);
            }
        }

        // Return fallback path for local wwwroot or relative URL
        return $"/uploads/{fileName.TrimStart('/')}";
    }
}
