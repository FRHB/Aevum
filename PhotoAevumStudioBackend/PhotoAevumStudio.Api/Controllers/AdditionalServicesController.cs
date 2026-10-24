using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.Controllers;

/// <summary>
/// Form request payload for creating an Additional Service Add-On via multipart/form-data.
/// </summary>
public class CreateAdditionalServiceApiRequest
{
    [Required(ErrorMessage = "Service category name (Add-On name) is required.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 100 characters.")]
    public string ServiceName { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? ServiceCategoryDescription { get; set; }

    [Required(ErrorMessage = "Service category is required.")]
    public string ServiceCategory { get; set; } = string.Empty;

    [Range(0.00, 100000.00, ErrorMessage = "Price must be a valid non-negative decimal value.")]
    public decimal ServicePrice { get; set; } = 0.00m;

    public IFormFile? ImageFile { get; set; }
}

[ApiController]
[Route("api/[controller]")]
[Route("api/management/additional-services")]
public class AdditionalServicesController : ControllerBase
{
    private readonly IStudioManagementService _service;
    private readonly ILogger<AdditionalServicesController> _logger;

    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp"
    };

    public AdditionalServicesController(
        IStudioManagementService service,
        ILogger<AdditionalServicesController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Returns the read-only next available custom sequential ID (e.g. ADD-0001).
    /// Used by Step 3 (Overview) of the wizard modal to preview the system-generated ID.
    /// </summary>
    [HttpGet("next-id")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(NextIdResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNextId(CancellationToken ct)
    {
        var nextId = await _service.GetNextAdditionalServiceIdAsync(ct);
        return Ok(new NextIdResponseDto(nextId));
    }

    /// <summary>
    /// Returns active service categories for populating the wizard's category dropdown.
    /// </summary>
    [HttpGet("categories")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories(CancellationToken ct)
    {
        try
        {
            var dbCategories = await _service.GetCategoriesAsync(ct);
            if (dbCategories.Count > 0)
            {
                return Ok(dbCategories.Select(c => c.Name).Distinct());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load dynamic categories from DB. Using standard catalog categories.");
        }

        var defaultCategories = new[]
        {
            "Portrait Photography",
            "Event & Gala Coverage",
            "Commercial & Branding",
            "Wedding & Nuptials",
            "Aerial & Drone Operations",
            "Post-Processing & Retouching",
            "Custom Framing & Prints",
            "Equipment & Studio Props"
        };
        return Ok(defaultCategories);
    }

    /// <summary>
    /// Gets all Additional Services (Add-Ons) summary items.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var list = await _service.GetAdditionalServicesSummaryAsync(ct);
        return Ok(list);
    }

    /// <summary>
    /// Gets specific Additional Service details including image availability.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ServiceDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var detail = await _service.GetAdditionalServiceDetailWithImageAsync(id, ct);
        if (detail is null)
            return NotFound(new ProblemDetails { Title = "Not Found", Detail = $"Additional service '{id}' was not found." });

        return Ok(detail);
    }

    /// <summary>
    /// Streams the raw image binary for an Additional Service.
    /// </summary>
    [HttpGet("{id}/image")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImage(string id, CancellationToken ct)
    {
        var detail = await _service.GetAdditionalServiceDetailWithImageAsync(id, ct);
        if (detail?.Image is null || detail.Image.Length == 0)
            return NotFound(new ProblemDetails { Title = "Not Found", Detail = $"No image found for additional service '{id}'." });

        return File(detail.Image, detail.MimeType ?? "image/jpeg");
    }

    /// <summary>
    /// Saves a newly created Additional Service (Add-On) submitted from Wizard Page 3.
    /// Securely processes the multipart/form-data upload stream and triggers atomic ADD-#### generation.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(AdditionalServiceResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromForm] CreateAdditionalServiceApiRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        byte[]? imageBytes = null;
        string? mimeType = null;

        // 1. Process and validate the uploaded image stream if present
        if (request.ImageFile != null && request.ImageFile.Length > 0)
        {
            const long maxFileSizeBytes = 10 * 1024 * 1024; // 10 MB limit
            if (request.ImageFile.Length > maxFileSizeBytes)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "File Size Exceeded",
                    Detail = "The uploaded image exceeds the 10 MB maximum allowed file size."
                });
            }

            mimeType = request.ImageFile.ContentType?.ToLowerInvariant() ?? "image/jpeg";
            if (!AllowedMimeTypes.Contains(mimeType))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Media Format",
                    Detail = "Only image formats (.jpg, .jpeg, .png, .webp) are supported."
                });
            }

            // Stream reading using async buffer copy
            try
            {
                await using var stream = request.ImageFile.OpenReadStream();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct);
                imageBytes = ms.ToArray();

                // Validate magic number / file signature
                if (!ValidateImageSignature(imageBytes, mimeType))
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Invalid File Content",
                        Detail = "The file payload does not contain a valid image header signature."
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while reading the uploaded image file stream.");
                return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
                {
                    Title = "Stream Processing Error",
                    Detail = "An unexpected error occurred while reading the uploaded image."
                });
            }
        }

        // 2. Resolve requesting identity
        string? requestingAdminId = User.FindFirstValue("studio_id")
                                   ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                                   ?? User.FindFirstValue(ClaimTypes.Name)
                                   ?? "SADM-001";

        // 3. Atomically persist via backend Stored Procedure & SQL Sequence
        try
        {
            var dto = new CreateAdditionalServiceDto(
                ServiceName: request.ServiceName.Trim(),
                ServiceDescription: request.ServiceCategoryDescription?.Trim() ?? string.Empty,
                ServiceCategory: request.ServiceCategory.Trim(),
                ServicePrice: request.ServicePrice,
                ImageBytes: imageBytes,
                ImageMimeType: mimeType ?? "image/jpeg"
            );

            var result = await _service.CreateAdditionalServiceAsync(dto, requestingAdminId, ct);

            _logger.LogInformation("Successfully created Additional Service Add-On '{Id}' ({Name}) with price {Price:C}.",
                result.Id, result.Name, result.Price);

            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Additional Service Add-On '{Name}'.", request.ServiceName);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Persistence Failure",
                Detail = $"Failed to create Add-On service: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Validates file magic headers to ensure the uploaded stream is genuinely an image.
    /// </summary>
    private static bool ValidateImageSignature(byte[] bytes, string mimeType)
    {
        if (bytes.Length < 4) return false;

        // JPEG: FF D8 FF
        if (mimeType.Contains("jpeg") || mimeType.Contains("jpg"))
            return bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

        // PNG: 89 50 4E 47
        if (mimeType.Contains("png"))
            return bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;

        // WebP: RIFF (first 4 bytes: 52 49 46 46), bytes 8..11: WEBP (57 45 42 50)
        if (mimeType.Contains("webp"))
        {
            if (bytes.Length < 12) return false;
            return bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50;
        }

        return true;
    }
}
