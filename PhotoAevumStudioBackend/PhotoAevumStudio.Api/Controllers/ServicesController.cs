using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.Controllers;

public class CreateServiceFormRequest
{
    [Required(ErrorMessage = "Service name is required.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Service name must be between 2 and 100 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Service description is required.")]
    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Service price is required.")]
    [Range(0.01, 1000000.00, ErrorMessage = "Price must be a positive value.")]
    public decimal Price { get; set; }

    [Required(ErrorMessage = "Service category is required.")]
    public string CategoryId { get; set; } = string.Empty;

    public string RateType { get; set; } = "per hour";

    public IFormFile? Image { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class ServicesController : ControllerBase
{
    private readonly IStudioManagementService _service;
    private readonly ILogger<ServicesController> _logger;
    private readonly IOutputCacheStore? _outputCache;

    public ServicesController(IStudioManagementService service, ILogger<ServicesController> logger, IOutputCacheStore? outputCache = null)
    {
        _service = service;
        _logger = logger;
        _outputCache = outputCache;
    }

    /// <summary>
    /// Returns the next sequential custom Service ID (format: SRV-####).
    /// </summary>
    [HttpGet("next-id")]
    [AllowAnonymous]
    public async Task<IActionResult> GetNextId(CancellationToken ct)
    {
        try
        {
            var nextId = await _service.GetNextServiceIdAsync(ct);
            return Ok(new GeneratedIdResponseDto(nextId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate next service ID.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "Failed to generate next service ID." });
        }
    }

    /// <summary>
    /// Retrieves all service catalog summaries.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [OutputCache(PolicyName = "CatalogCache")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var services = await _service.GetServicesSummaryAsync(ct);
        return Ok(services);
    }

    /// <summary>
    /// Handles multipart/form-data upload to create a new Main Service with an image.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "ClerkOrAdmin")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Create([FromForm] CreateServiceFormRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        byte[]? imageBytes = null;
        string? mimeType = null;

        if (request.Image != null && request.Image.Length > 0)
        {
            if (request.Image.Length > 5 * 1024 * 1024)
            {
                return BadRequest(new { Message = "Image size exceeds the 5MB maximum limit." });
            }

            using var memoryStream = new MemoryStream();
            await request.Image.CopyToAsync(memoryStream, ct);
            imageBytes = memoryStream.ToArray();
            if (!TryDetectImageContentType(imageBytes, out var verifiedMimeType))
            {
                return BadRequest(new { Message = "Unsupported or invalid image signature. Allowed: JPG, PNG, WEBP." });
            }
            mimeType = verifiedMimeType;
        }

        string? adminId = User.FindFirstValue("studio_id")
                          ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(adminId))
            return Unauthorized();

        var dto = new CreateServiceDto(
            Name: request.Name.Trim(),
            Description: request.Description.Trim(),
            Price: request.Price,
            CategoryId: request.CategoryId.Trim(),
            ImageBytes: imageBytes,
            ImageMimeType: mimeType,
            ImageFileName: request.Image?.FileName,
            RateType: string.IsNullOrWhiteSpace(request.RateType) ? "per hour" : request.RateType.Trim()
        );

        try
        {
            var result = await _service.CreateServiceAsync(dto, adminId, ct);
            if (_outputCache != null) await _outputCache.EvictByTagAsync("catalog", ct);
            return CreatedAtAction(nameof(GetNextId), new { id = result.Id }, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating main service {ServiceName}", request.Name);
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An error occurred while creating the service." });
        }
    }

    /// <summary>
    /// Retrieves all available rate types (per hour, per picture, per single shot, multi-shot).
    /// </summary>
    [HttpGet("rate-types")]
    [AllowAnonymous]
    [OutputCache(PolicyName = "CatalogCache")]
    public async Task<IActionResult> GetRateTypes(CancellationToken ct)
    {
        var list = await _service.GetRateTypesAsync(ct);
        return Ok(list);
    }

    /// <summary>
    /// Retrieves all configured rates for a specific service.
    /// </summary>
    [HttpGet("{serviceId}/rates")]
    [AllowAnonymous]
    public async Task<IActionResult> GetServiceRates(string serviceId, CancellationToken ct)
    {
        var list = await _service.GetServiceRatesAsync(serviceId, ct);
        return Ok(list);
    }

    /// <summary>
    /// Adds or updates a rate configuration for a service.
    /// </summary>
    [HttpPost("{serviceId}/rates")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> SetServiceRate(string serviceId, [FromBody] SetServiceRateDto dto, CancellationToken ct)
    {
        var success = await _service.AddOrUpdateServiceRateAsync(serviceId, dto.RateTypeId, dto.RateAmount, ct);
        if (success && _outputCache != null) await _outputCache.EvictByTagAsync("catalog", ct);
        return success
            ? Ok(new { Message = "Rate updated successfully." })
            : BadRequest(new { Message = "Failed to update service rate." });
    }

    [HttpPost("{id}/archive")]
    [Authorize(Roles = "Admin,SystemAdmin")]
    public async Task<IActionResult> ArchiveService(string id, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.Name);
        var ok = await _service.SetCatalogItemArchivedAsync("service", id, true, adminId, ct);
        if (!ok) return NotFound(new { Message = $"Service '{id}' was not found." });
        if (_outputCache != null) await _outputCache.EvictByTagAsync("catalog", ct);
        return Ok(new { Archived = true, Id = id });
    }

    [HttpPost("{id}/restore")]
    [Authorize(Roles = "Admin,SystemAdmin")]
    public async Task<IActionResult> RestoreService(string id, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.Name);
        var ok = await _service.SetCatalogItemArchivedAsync("service", id, false, adminId, ct);
        if (!ok) return NotFound(new { Message = $"Service '{id}' was not found." });
        if (_outputCache != null) await _outputCache.EvictByTagAsync("catalog", ct);
        return Ok(new { Archived = false, Id = id });
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,SystemAdmin")]
    public async Task<IActionResult> DeleteService(string id, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.Name);
        var (ok, message) = await _service.DeleteCatalogItemWithSnapshotAsync("service", id, adminId, ct);
        if (!ok) return BadRequest(new { Message = message });
        if (_outputCache != null) await _outputCache.EvictByTagAsync("catalog", ct);
        return Ok(new { Deleted = true, Id = id, Message = message });
    }

    private static bool TryDetectImageContentType(byte[] bytes, out string contentType)
    {
        contentType = string.Empty;
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            contentType = "image/png";
            return true;
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            contentType = "image/jpeg";
            return true;
        }

        if (bytes.Length >= 12 &&
            bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
            bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
        {
            contentType = "image/webp";
            return true;
        }

        return false;
    }
}
