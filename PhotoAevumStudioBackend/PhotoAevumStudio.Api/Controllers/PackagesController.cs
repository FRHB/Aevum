using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "StaffOrAbove")] // Package configuration requires staff or admin access
public class PackagesController : ControllerBase
{
    private readonly IStudioManagementService _service;
    private readonly ILogger<PackagesController> _logger;

    public PackagesController(IStudioManagementService service, ILogger<PackagesController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Generates the next sequential custom Combo Package ID (PKG-####)
    /// strictly via backend logic to prevent concurrent race conditions.
    /// </summary>
    [HttpGet("next-id")]
    [ProducesResponseType(typeof(PackageNextIdDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNextId(CancellationToken ct)
    {
        var nextId = await _service.GetNextComboPackageIdAsync(ct);
        return Ok(new PackageNextIdDto(nextId));
    }

    /// <summary>
    /// Retrieves service categories available for dropdown selection in Wizard Step 1.
    /// </summary>
    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories(CancellationToken ct)
    {
        var categories = await _service.GetCategoriesAsync(ct);
        return Ok(categories);
    }

    /// <summary>
    /// Retrieves available Main Services and Additional Services for many-to-many bundling in Wizard Step 2.
    /// </summary>
    [HttpGet("available-services")]
    [ProducesResponseType(typeof(IReadOnlyList<BundledServiceSelectionItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailableServices(CancellationToken ct)
    {
        var services = await _service.GetAvailableServicesForBundlingAsync(ct);
        return Ok(services);
    }

    /// <summary>
    /// Retrieves a lightweight summary of all combo packages.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPackageSummary(CancellationToken ct)
    {
        var packages = await _service.GetComboPackagesSummaryAsync(ct);
        return Ok(packages);
    }

    /// <summary>
    /// Creates a new Combo Package service, mapping many-to-many services and attaching multipart hero image.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ComboPackageCreatedResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreatePackage([FromForm] CreatePackageApiFormRequest request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { Message = "Package creation payload is required." });

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { Message = "Combo Package Name is required." });

        if (string.IsNullOrWhiteSpace(request.CategoryId))
            return BadRequest(new { Message = "Service Category is required." });

        if (request.Price <= 0)
            return BadRequest(new { Message = "Package price must be greater than zero." });

        // Parse selected services (supports both repeated form values and JSON/comma strings)
        var parsedServiceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (request.SelectedServiceIds != null)
        {
            foreach (var id in request.SelectedServiceIds)
            {
                if (!string.IsNullOrWhiteSpace(id))
                    parsedServiceIds.Add(id.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(request.SelectedServiceIdsRaw))
        {
            // Attempt JSON parse or comma split
            if (request.SelectedServiceIdsRaw.TrimStart().StartsWith("["))
            {
                try
                {
                    var deserialized = System.Text.Json.JsonSerializer.Deserialize<List<string>>(request.SelectedServiceIdsRaw);
                    if (deserialized != null)
                    {
                        foreach (var s in deserialized)
                        {
                            if (!string.IsNullOrWhiteSpace(s))
                                parsedServiceIds.Add(s.Trim());
                        }
                    }
                }
                catch { }
            }
            else
            {
                var split = request.SelectedServiceIdsRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var s in split)
                {
                    if (!string.IsNullOrWhiteSpace(s))
                        parsedServiceIds.Add(s.Trim());
                }
            }
        }

        if (parsedServiceIds.Count == 0)
        {
            return BadRequest(new { Message = "At least one service (Main or Additional) must be selected for the package bundle." });
        }

        // Validate image file if provided
        byte[]? imageBytes = null;
        string? mimeType = null;

        if (request.ImageFile != null && request.ImageFile.Length > 0)
        {
            const long maxFileSize = 5 * 1024 * 1024; // 5 MB
            if (request.ImageFile.Length > maxFileSize)
            {
                return BadRequest(new { Message = "Selected image file exceeds the maximum 5MB size limit." });
            }

            var allowedMimeTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/webp" };
            var fileMime = request.ImageFile.ContentType?.ToLowerInvariant() ?? string.Empty;
            var ext = Path.GetExtension(request.ImageFile.FileName).ToLowerInvariant();

            bool isAllowed = allowedMimeTypes.Contains(fileMime) || ext is ".jpg" or ".jpeg" or ".png" or ".webp";
            if (!isAllowed)
            {
                return BadRequest(new { Message = "Only JPG, PNG, and WEBP image formats are supported." });
            }

            using var ms = new MemoryStream();
            await request.ImageFile.CopyToAsync(ms, ct);
            imageBytes = ms.ToArray();
            mimeType = string.IsNullOrWhiteSpace(fileMime) ? "image/jpeg" : fileMime;
        }

        // Identify acting admin
        string? adminId = User.FindFirstValue("studio_id")
                          ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue(ClaimTypes.Name)
                          ?? "SADM-001";

        try
        {
            var model = new CreateComboPackageModel(
                Name: request.Name.Trim(),
                Description: request.Description?.Trim(),
                CategoryId: request.CategoryId.Trim(),
                Price: request.Price,
                SelectedServiceIds: parsedServiceIds.ToList()
            );

            var result = await _service.CreateComboPackageWithServicesAsync(model, imageBytes, mimeType, adminId, ct);
            _logger.LogInformation("Successfully created Combo Package {PackageId} with {ServiceCount} bundled services.", result.PackageId, result.BundledServicesCount);

            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Combo Package: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = $"An error occurred while creating the package: {ex.Message}" });
        }
    }
}

/// <summary>
/// Form-data request model binding for multipart API submission.
/// </summary>
public class CreatePackageApiFormRequest
{
    [Required(ErrorMessage = "Combo Package Name is required.")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "Package Name must be between 3 and 100 characters.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Service Category is required.")]
    public string CategoryId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Price is required.")]
    [Range(0.01, 1000000, ErrorMessage = "Price must be greater than zero.")]
    public decimal Price { get; set; }

    /// <summary>
    /// Bundled service IDs passed as repeated form keys (e.g., SelectedServiceIds=SRV-001&SelectedServiceIds=ADD-002)
    /// </summary>
    public List<string>? SelectedServiceIds { get; set; }

    /// <summary>
    /// Alternative: Bundled service IDs passed as a single JSON array or comma-delimited string.
    /// </summary>
    public string? SelectedServiceIdsRaw { get; set; }

    /// <summary>
    /// The binary image payload for multipart/form-data upload.
    /// </summary>
    public IFormFile? ImageFile { get; set; }
}
