using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif"
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
    [Authorize]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50MB batch limit
    public async Task<IActionResult> UploadImages(
        [FromForm] List<IFormFile> files,
        [FromForm] string? portfolioId,
        CancellationToken cancellationToken)
    {
        if (files == null || files.Count == 0)
            return BadRequest(new ProblemDetails { Title = "No files uploaded", Detail = "Please select at least one image.", Status = 400 });

        // Validate that all files are valid images
        foreach (var file in files)
        {
            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid file type",
                    Detail = $"File '{file.FileName}' has an unsupported extension. Allowed: jpg, jpeg, png, webp, gif, avif.",
                    Status = 400
                });
            }
        }

        var folderPrefix = string.IsNullOrWhiteSpace(portfolioId) ? "portfolio" : $"portfolio/{portfolioId}";
        var savedFileNames = await _r2Service.UploadImagesAsync(files, folderPrefix, _r2Service.PortfolioBucket);

        var savedImages = new List<PortfolioImage>();
        var effectivePortfolioId = portfolioId ?? "TEMP";

        for (var i = 0; i < savedFileNames.Count; i++)
        {
            var fileName = savedFileNames[i];
            var file = i < files.Count ? files[i] : null;
            byte[]? imageBytes = null;
            string contentType = file?.ContentType ?? "image/jpeg";

            if (file != null && file.Length > 0)
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, cancellationToken);
                imageBytes = ms.ToArray();
            }

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

        return File(image.ImageData, string.IsNullOrWhiteSpace(image.ContentType) ? "image/jpeg" : image.ContentType);
    }
}
