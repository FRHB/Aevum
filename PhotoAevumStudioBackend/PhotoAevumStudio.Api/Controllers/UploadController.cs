using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UploadController : ControllerBase
{
    private readonly IR2StorageService _r2Service;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<UploadController> _logger;

    private static readonly Regex SafePortfolioIdRegex = new(@"^[A-Za-z0-9_-]{1,30}$", RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif", "image/avif"
    };

    public UploadController(
        IR2StorageService r2Service,
        ApplicationDbContext db,
        ILogger<UploadController> logger)
    {
        _r2Service = r2Service;
        _db = db;
        _logger = logger;
    }

    [HttpPost]
    [Authorize(Policy = "ClerkOrAdmin")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50MB batch limit
    public async Task<IActionResult> UploadImages(
        [FromForm] List<IFormFile> files,
        [FromForm] string? portfolioId,
        CancellationToken cancellationToken)
    {
        if (files == null || files.Count == 0)
            return BadRequest(new ProblemDetails { Title = "No files uploaded", Detail = "Please select at least one image.", Status = 400 });

        string? sanitizedPortfolioId = null;
        if (!string.IsNullOrWhiteSpace(portfolioId))
        {
            var trimmedPortfolioId = portfolioId.Trim();
            if (!SafePortfolioIdRegex.IsMatch(trimmedPortfolioId))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid portfolioId",
                    Detail = "portfolioId may only contain letters, digits, hyphens, and underscores.",
                    Status = 400
                });
            }
            sanitizedPortfolioId = trimmedPortfolioId;
        }

        // Validate extension, size, and magic bytes for every uploaded file
        var validatedFiles = new List<(IFormFile File, byte[] Bytes, string ContentType)>();
        foreach (var file in files)
        {
            if (file.Length <= 0 || file.Length > 15 * 1024 * 1024)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid file size",
                    Detail = $"File '{Path.GetFileName(file.FileName)}' must be between 1 byte and 15 MB.",
                    Status = 400
                });
            }

            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid file type",
                    Detail = $"File '{Path.GetFileName(file.FileName)}' has an unsupported extension. Allowed: jpg, jpeg, png, webp, gif, avif.",
                    Status = 400
                });
            }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, cancellationToken);
            var bytes = ms.ToArray();

            if (!TryDetectImageMimeType(bytes, ext, out var detectedMime))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid image signature",
                    Detail = $"File '{Path.GetFileName(file.FileName)}' does not match a valid image header signature.",
                    Status = 400
                });
            }

            validatedFiles.Add((file, bytes, detectedMime));
        }

        var folderPrefix = string.IsNullOrWhiteSpace(sanitizedPortfolioId) ? "portfolio" : $"portfolio/{sanitizedPortfolioId}";
        var savedFileNames = await _r2Service.UploadImagesAsync(files, folderPrefix, _r2Service.PortfolioBucket);

        var savedImages = new List<PortfolioImage>();
        var effectivePortfolioId = sanitizedPortfolioId ?? "TEMP";

        for (var i = 0; i < savedFileNames.Count; i++)
        {
            var fileName = savedFileNames[i];
            var (file, imageBytes, contentType) = i < validatedFiles.Count
                ? validatedFiles[i]
                : (null!, Array.Empty<byte>(), "image/jpeg");

            var r2Url = _r2Service.GetPresignedUrl(fileName, _r2Service.PortfolioBucket);

            var img = new PortfolioImage
            {
                PortfolioId = effectivePortfolioId,
                FileName = fileName,
                ImageUrl = r2Url,
                ContentType = contentType,
                ImageData = imageBytes,
                UploadedAt = DateTime.UtcNow
            };
            _db.PortfolioImages.Add(img);
            savedImages.Add(img);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var result = savedImages.Select(img => new
        {
            img.Id,
            img.PortfolioId,
            img.FileName,
            Url = !string.IsNullOrWhiteSpace(img.ImageUrl) ? img.ImageUrl : _r2Service.GetPresignedUrl(img.FileName, _r2Service.PortfolioBucket)
        });

        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPresignedUrl(int id, CancellationToken cancellationToken)
    {
        var image = await _db.PortfolioImages.FindAsync([id], cancellationToken);
        if (image == null)
            return NotFound(new ProblemDetails { Title = "Image not found", Detail = $"No image found with ID {id}.", Status = 404 });

        var url = !string.IsNullOrWhiteSpace(image.ImageUrl)
            ? image.ImageUrl
            : _r2Service.GetPresignedUrl(image.FileName, _r2Service.PortfolioBucket);
        return Ok(new { ImageId = image.Id, FileName = image.FileName, Url = url });
    }

    [HttpGet("{id:int}/image")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPortfolioImageBinary(int id, CancellationToken cancellationToken)
    {
        var image = await _db.PortfolioImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (image?.ImageData == null || image.ImageData.Length == 0)
            return NotFound(new ProblemDetails { Title = "Image binary not found", Detail = $"No binary image data found in SQL Server for ID {id}.", Status = 404 });

        var safeMime = !string.IsNullOrWhiteSpace(image.ContentType) && AllowedContentTypes.Contains(image.ContentType)
            ? image.ContentType
            : "image/jpeg";

        return File(image.ImageData, safeMime);
    }

    private static bool TryDetectImageMimeType(byte[] bytes, string ext, out string mimeType)
    {
        mimeType = string.Empty;
        if (bytes.Length < 4) return false;

        if ((ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)) &&
            bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            mimeType = "image/jpeg";
            return true;
        }

        if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            mimeType = "image/png";
            return true;
        }

        if (ext.Equals(".gif", StringComparison.OrdinalIgnoreCase) &&
            bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
        {
            mimeType = "image/gif";
            return true;
        }

        if (ext.Equals(".webp", StringComparison.OrdinalIgnoreCase) &&
            bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            mimeType = "image/webp";
            return true;
        }

        if (ext.Equals(".avif", StringComparison.OrdinalIgnoreCase) &&
            bytes.Length >= 12 && bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70)
        {
            mimeType = "image/avif";
            return true;
        }

        return false;
    }
}
