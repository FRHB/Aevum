using System.Collections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;

namespace PhotoAevumStudio.Infrastructure.Services;

/// <summary>
/// Resolves protected onboarding documents (such as the Employee Handbook & Operations Manual PDF)
/// from configurable local storage or private Cloudflare R2 storage.
/// </summary>
public class OnboardingDocumentService : IOnboardingDocumentService
{
    private const string DefaultStoragePath = "storage/documents/Employee_Handbook_and_Manual_PHOTO_AEVUM_STUDIO.pdf";
    private const string DefaultFileName = "Employee_Handbook_and_Manual_PHOTO_AEVUM_STUDIO.pdf";

    private readonly OnboardingDocumentsSettings _settings;
    private readonly IR2StorageService? _r2StorageService;
    private readonly IHostEnvironment? _hostEnvironment;
    private readonly ILogger<OnboardingDocumentService> _logger;

    [ActivatorUtilitiesConstructor]
    public OnboardingDocumentService(
        IOptions<OnboardingDocumentsSettings> options,
        ILogger<OnboardingDocumentService> logger,
        IR2StorageService? r2StorageService = null,
        IHostEnvironment? hostEnvironment = null)
    {
        _logger = logger ?? NullLogger<OnboardingDocumentService>.Instance;
        _r2StorageService = r2StorageService;
        _hostEnvironment = hostEnvironment;

        var src = options?.Value ?? new OnboardingDocumentsSettings();
        _settings = new OnboardingDocumentsSettings
        {
            StoragePath = string.IsNullOrWhiteSpace(src.StoragePath) ? DefaultStoragePath : src.StoragePath.Trim(),
            HandbookFileName = string.IsNullOrWhiteSpace(src.HandbookFileName) ? DefaultFileName : src.HandbookFileName.Trim(),
            R2Bucket = string.IsNullOrWhiteSpace(src.R2Bucket) ? "documents" : src.R2Bucket.Trim(),
            R2ObjectKey = string.IsNullOrWhiteSpace(src.R2ObjectKey) ? $"onboarding/{DefaultFileName}" : src.R2ObjectKey.Trim(),
            PreferR2 = src.PreferR2,
            MaxAttachmentBytes = src.MaxAttachmentBytes > 0 ? src.MaxAttachmentBytes : 10 * 1024 * 1024,
            PresignedUrlExpiryMinutes = src.PresignedUrlExpiryMinutes > 0 ? src.PresignedUrlExpiryMinutes : 7 * 24 * 60
        };

        ApplyEnvironmentOverrides(_settings);
    }

    private static string? GetEnv(params string[] names)
    {
        foreach (var name in names)
        {
            var direct = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(direct))
                return direct.Trim();

            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (string.Equals(entry.Key?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    var val = entry.Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(val))
                        return val.Trim();
                }
            }
        }
        return null;
    }

    private static void ApplyEnvironmentOverrides(OnboardingDocumentsSettings settings)
    {
        var path = GetEnv("ONBOARDING_DOCUMENTS__STORAGE_PATH", "OnboardingDocuments__StoragePath", "EMPLOYEE_HANDBOOK_PATH");
        if (!string.IsNullOrWhiteSpace(path))
            settings.StoragePath = path;

        var fileName = GetEnv("ONBOARDING_DOCUMENTS__HANDBOOK_FILE_NAME", "OnboardingDocuments__HandbookFileName");
        if (!string.IsNullOrWhiteSpace(fileName))
            settings.HandbookFileName = fileName;

        var bucket = GetEnv("ONBOARDING_DOCUMENTS__R2_BUCKET", "OnboardingDocuments__R2Bucket");
        if (!string.IsNullOrWhiteSpace(bucket))
            settings.R2Bucket = bucket;

        var objectKey = GetEnv("ONBOARDING_DOCUMENTS__R2_OBJECT_KEY", "OnboardingDocuments__R2ObjectKey");
        if (!string.IsNullOrWhiteSpace(objectKey))
            settings.R2ObjectKey = objectKey;

        var preferR2 = GetEnv("ONBOARDING_DOCUMENTS__PREFER_R2", "OnboardingDocuments__PreferR2");
        if (bool.TryParse(preferR2, out var parsedPrefer))
            settings.PreferR2 = parsedPrefer;

        var maxBytes = GetEnv("ONBOARDING_DOCUMENTS__MAX_ATTACHMENT_BYTES", "OnboardingDocuments__MaxAttachmentBytes");
        if (int.TryParse(maxBytes, out var parsedMax) && parsedMax > 0)
            settings.MaxAttachmentBytes = parsedMax;

        var expiryMin = GetEnv("ONBOARDING_DOCUMENTS__PRESIGNED_URL_EXPIRY_MINUTES", "OnboardingDocuments__PresignedUrlExpiryMinutes");
        if (int.TryParse(expiryMin, out var parsedExpiry) && parsedExpiry > 0)
            settings.PresignedUrlExpiryMinutes = parsedExpiry;
    }

    public async Task<OnboardingDocumentResult> GetEmployeeHandbookAsync(CancellationToken cancellationToken = default)
    {
        var safeFileName = Path.GetFileName(_settings.HandbookFileName);
        if (string.IsNullOrWhiteSpace(safeFileName) ||
            !safeFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            safeFileName = DefaultFileName;
        }

        if (_settings.PreferR2 && _r2StorageService?.IsConfigured == true)
        {
            var r2First = await TryLoadFromR2Async(safeFileName, cancellationToken);
            if (r2First.Found)
                return r2First;
        }

        var localResult = await TryLoadFromLocalStorageAsync(safeFileName, cancellationToken);
        if (localResult.Found)
            return localResult;

        if (!_settings.PreferR2 && _r2StorageService?.IsConfigured == true)
        {
            var r2Fallback = await TryLoadFromR2Async(safeFileName, cancellationToken);
            if (r2Fallback.Found)
                return r2Fallback;
        }

        return localResult;
    }

    private async Task<OnboardingDocumentResult> TryLoadFromLocalStorageAsync(string safeFileName, CancellationToken ct)
    {
        try
        {
            var configuredPath = _settings.StoragePath;
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                const string msg = "Onboarding document storage path is not configured.";
                _logger.LogError(msg);
                return new OnboardingDocumentResult(false, null, null, msg);
            }

            if (configuredPath.Contains("..", StringComparison.Ordinal))
            {
                _logger.LogWarning("Rejected unsafe onboarding document path containing parent traversal: {Path}", configuredPath);
                return new OnboardingDocumentResult(false, null, null, "Configured onboarding document path is invalid.");
            }

            string? resolvedFilePath = null;
            if (Path.IsPathRooted(configuredPath))
            {
                var full = Path.GetFullPath(configuredPath);
                if (File.Exists(full))
                    resolvedFilePath = full;
            }
            else
            {
                var normalizedRelative = configuredPath.TrimStart('/', '\\')
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);

                var candidateRoots = new[]
                {
                    _hostEnvironment?.ContentRootPath,
                    AppContext.BaseDirectory,
                    Directory.GetCurrentDirectory()
                }
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase);

                foreach (var root in candidateRoots)
                {
                    var fullRoot = Path.GetFullPath(root!);
                    if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
                        fullRoot += Path.DirectorySeparatorChar;

                    var candidate = Path.GetFullPath(Path.Combine(fullRoot, normalizedRelative));
                    if (!candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (File.Exists(candidate))
                    {
                        resolvedFilePath = candidate;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(resolvedFilePath) || !File.Exists(resolvedFilePath))
            {
                var missingMsg = $"Employee Handbook PDF could not be located at configured storage path '{configuredPath}'.";
                _logger.LogWarning(missingMsg);
                return new OnboardingDocumentResult(false, null, null, missingMsg);
            }

            if (!resolvedFilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                const string extMsg = "Configured onboarding handbook file must be a PDF document.";
                _logger.LogWarning("{Message} Resolved path: {Path}", extMsg, resolvedFilePath);
                return new OnboardingDocumentResult(false, null, null, extMsg);
            }

            var bytes = await File.ReadAllBytesAsync(resolvedFilePath, ct);
            return BuildValidatedResult(bytes, safeFileName, resolvedFilePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve Employee Handbook PDF from local storage path '{Path}'.", _settings.StoragePath);
            return new OnboardingDocumentResult(false, null, null, "Could not read Employee Handbook PDF from configured storage.");
        }
    }

    private async Task<OnboardingDocumentResult> TryLoadFromR2Async(string safeFileName, CancellationToken ct)
    {
        if (_r2StorageService == null || !_r2StorageService.IsConfigured)
            return new OnboardingDocumentResult(false, null, null, "R2 storage is not configured.");

        var bucket = string.IsNullOrWhiteSpace(_settings.R2Bucket) ? _r2StorageService.DocumentsBucket : _settings.R2Bucket.Trim();
        var key = string.IsNullOrWhiteSpace(_settings.R2ObjectKey) ? $"onboarding/{safeFileName}" : _settings.R2ObjectKey.Trim().TrimStart('/');

        try
        {
            await using var stream = await _r2StorageService.DownloadFileAsync(bucket, key);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();

            if (bytes.Length > _settings.MaxAttachmentBytes)
            {
                var presignedUrl = _r2StorageService.GetPresignedDownloadUrl(
                    bucket,
                    key,
                    safeFileName,
                    TimeSpan.FromMinutes(_settings.PresignedUrlExpiryMinutes));

                if (!string.IsNullOrWhiteSpace(presignedUrl))
                {
                    _logger.LogInformation("Employee Handbook PDF ({Length} bytes) exceeds attachment limit ({Max} bytes); using time-limited R2 link.",
                        bytes.Length, _settings.MaxAttachmentBytes);
                    return new OnboardingDocumentResult(true, null, presignedUrl, null);
                }
            }

            return BuildValidatedResult(bytes, safeFileName, $"r2://{bucket}/{key}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve Employee Handbook PDF from R2 bucket '{Bucket}' key '{Key}'.", bucket, key);
            return new OnboardingDocumentResult(false, null, null, $"Could not retrieve Employee Handbook PDF from R2 storage ({bucket}/{key}).");
        }
    }

    private OnboardingDocumentResult BuildValidatedResult(byte[] bytes, string safeFileName, string sourceLabel)
    {
        if (bytes == null || bytes.Length == 0)
        {
            var emptyMsg = $"Employee Handbook PDF at '{sourceLabel}' is empty.";
            _logger.LogWarning(emptyMsg);
            return new OnboardingDocumentResult(false, null, null, emptyMsg);
        }

        // Verify PDF magic header (%PDF-)
        if (bytes.Length < 5 || bytes[0] != 0x25 || bytes[1] != 0x50 || bytes[2] != 0x44 || bytes[3] != 0x46 || bytes[4] != 0x2D)
        {
            var invalidMsg = $"Onboarding document at '{sourceLabel}' is not a valid PDF file.";
            _logger.LogWarning(invalidMsg);
            return new OnboardingDocumentResult(false, null, null, invalidMsg);
        }

        if (bytes.Length > _settings.MaxAttachmentBytes)
        {
            if (_r2StorageService?.IsConfigured == true)
            {
                var bucket = string.IsNullOrWhiteSpace(_settings.R2Bucket) ? _r2StorageService.DocumentsBucket : _settings.R2Bucket.Trim();
                var key = string.IsNullOrWhiteSpace(_settings.R2ObjectKey) ? $"onboarding/{safeFileName}" : _settings.R2ObjectKey.Trim().TrimStart('/');
                var url = _r2StorageService.GetPresignedDownloadUrl(bucket, key, safeFileName, TimeSpan.FromMinutes(_settings.PresignedUrlExpiryMinutes));
                if (!string.IsNullOrWhiteSpace(url))
                {
                    return new OnboardingDocumentResult(true, null, url, null);
                }
            }

            var sizeMsg = $"Employee Handbook PDF ({bytes.Length} bytes) exceeds maximum email attachment size ({_settings.MaxAttachmentBytes} bytes).";
            _logger.LogWarning(sizeMsg);
            return new OnboardingDocumentResult(false, null, null, sizeMsg);
        }

        return new OnboardingDocumentResult(
            Found: true,
            Attachment: new EmailAttachment(safeFileName, bytes, "application/pdf"),
            DownloadUrl: null,
            ErrorMessage: null);
    }
}
