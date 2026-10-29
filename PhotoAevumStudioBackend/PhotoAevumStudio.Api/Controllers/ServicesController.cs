using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public ServicesController(IStudioManagementService service, ILogger<ServicesController> logger)
    {
        _service = service;
        _logger = logger;
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
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var services = await _service.GetServicesSummaryAsync(ct);
        return Ok(services);
    }

    /// <summary>
    /// Handles multipart/form-data upload to create a new Main Service with an image.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "StaffOrAbove")]
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

            var allowedMimeTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/webp" };
            var contentType = request.Image.ContentType.ToLowerInvariant();
            if (!allowedMimeTypes.Contains(contentType))
            {
                return BadRequest(new { Message = "Unsupported image format. Allowed: JPG, PNG, WEBP." });
            }

            using var memoryStream = new MemoryStream();
            await request.Image.CopyToAsync(memoryStream, ct);
            imageBytes = memoryStream.ToArray();
            mimeType = request.Image.ContentType;
        }

        string? adminId = User.FindFirstValue("studio_id") 
                          ?? User.FindFirstValue(ClaimTypes.NameIdentifier) 
                          ?? "Admin";

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
            return CreatedAtAction(nameof(GetNextId), new { id = result.Id }, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating main service {ServiceName}", request.Name);
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Retrieves all available rate types (per hour, per picture, per single shot, multi-shot).
    /// </summary>
    [HttpGet("rate-types")]
    [AllowAnonymous]
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
    [Authorize(Policy = "StaffOrAbove")]
    public async Task<IActionResult> SetServiceRate(string serviceId, [FromBody] SetServiceRateDto dto, CancellationToken ct)
    {
        var success = await _service.AddOrUpdateServiceRateAsync(serviceId, dto.RateTypeId, dto.RateAmount, ct);
        return success
            ? Ok(new { Message = "Rate updated successfully." })
            : BadRequest(new { Message = "Failed to update service rate." });
    }
}
