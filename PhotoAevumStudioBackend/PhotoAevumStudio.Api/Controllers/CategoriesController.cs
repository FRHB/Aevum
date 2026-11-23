using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.Controllers;

public class CreateCategoryRequest
{
    [Required(ErrorMessage = "Service category name is required.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Category name must be between 2 and 100 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Service category description is required.")]
    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Optional specific service IDs to associate with this category upon creation.
    /// If omitted or empty, relevant or default associations are automatically assigned.
    /// </summary>
    public List<string>? AssociatedServiceIds { get; set; }

    /// <summary>
    /// Optional specific additional service / add-on IDs to associate.
    /// </summary>
    public List<string>? AssociatedAdditionalServiceIds { get; set; }

    /// <summary>
    /// Optional specific combo package IDs to associate.
    /// </summary>
    public List<string>? AssociatedComboPackageIds { get; set; }

    /// <summary>
    /// General association names or IDs to resolve and associate.
    /// </summary>
    public List<string>? Associations { get; set; }

    /// <summary>
    /// Foreign key RateTypeID referencing dbo.RateType(RateTypeID) (e.g. RT-0001). Default is RT-0001.
    /// </summary>
    public string RateTypeId { get; set; } = "RT-0001";

    /// <summary>
    /// Rate type display name (e.g. Per Hour, Per Picture). Default is 'Per Hour'.
    /// </summary>
    public string? RateTypeName { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly IStudioManagementService _service;
    private readonly ILogger<CategoriesController> _logger;
    private readonly IOutputCacheStore? _outputCache;

    public CategoriesController(IStudioManagementService service, ILogger<CategoriesController> logger, IOutputCacheStore? outputCache = null)
    {
        _service = service;
        _logger = logger;
        _outputCache = outputCache;
    }

    /// <summary>
    /// Returns the next sequential custom Service Category ID (format: CAT-####).
    /// </summary>
    [HttpGet("next-id")]
    [AllowAnonymous]
    public async Task<IActionResult> GetNextId(CancellationToken ct)
    {
        try
        {
            var nextId = await _service.GetNextCategoryIdAsync(ct);
            return Ok(new GeneratedIdResponseDto(nextId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate next category ID.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "Failed to generate next category ID." });
        }
    }

    /// <summary>
    /// Retrieves all service categories (used for dropdown mapping and category listings).
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [OutputCache(PolicyName = "CatalogCache")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        try
        {
            var categories = await _service.GetCategoriesAsync(ct);
            return Ok(categories);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch categories.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "Failed to fetch categories." });
        }
    }

    /// <summary>
    /// Retrieves a single service category by ID including its associated services.
    /// </summary>
    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        try
        {
            var category = await _service.GetCategoryByIdAsync(id, ct);
            if (category == null)
                return NotFound(new { Message = $"Service Category '{id}' was not found." });

            return Ok(category);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch category {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "Failed to fetch category." });
        }
    }

    /// <summary>
    /// Creates a new Service Category with auto-generated custom ID and automatic associations.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "StaffOrAbove")]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        string? adminId = User.FindFirstValue("studio_id") 
                          ?? User.FindFirstValue(ClaimTypes.NameIdentifier) 
                          ?? "Admin";

        var dto = new CreateCategoryDto(
            request.Name.Trim(), 
            request.Description.Trim(),
            request.AssociatedServiceIds,
            request.AssociatedAdditionalServiceIds,
            request.AssociatedComboPackageIds,
            request.Associations,
            request.RateTypeId,
            request.RateTypeName);

        try
        {
            var result = await _service.CreateCategoryAsync(dto, adminId, ct);
            if (_outputCache != null) await _outputCache.EvictByTagAsync("catalog", ct);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating service category {CategoryName}", request.Name);
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = ex.Message });
        }
    }

    [HttpPost("{id}/archive")]
    [Authorize(Roles = "Admin,SystemAdmin")]
    public async Task<IActionResult> ArchiveCategory(string id, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.Name);
        var ok = await _service.SetCatalogItemArchivedAsync("category", id, true, adminId, ct);
        if (!ok) return NotFound(new { Message = $"Category '{id}' was not found." });
        if (_outputCache != null) await _outputCache.EvictByTagAsync("catalog", ct);
        return Ok(new { Archived = true, Id = id });
    }

    [HttpPost("{id}/restore")]
    [Authorize(Roles = "Admin,SystemAdmin")]
    public async Task<IActionResult> RestoreCategory(string id, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.Name);
        var ok = await _service.SetCatalogItemArchivedAsync("category", id, false, adminId, ct);
        if (!ok) return NotFound(new { Message = $"Category '{id}' was not found." });
        if (_outputCache != null) await _outputCache.EvictByTagAsync("catalog", ct);
        return Ok(new { Archived = false, Id = id });
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,SystemAdmin")]
    public async Task<IActionResult> DeleteCategory(string id, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.Name);
        var (ok, message) = await _service.DeleteCatalogItemWithSnapshotAsync("category", id, adminId, ct);
        if (!ok) return BadRequest(new { Message = message });
        if (_outputCache != null) await _outputCache.EvictByTagAsync("catalog", ct);
        return Ok(new { Deleted = true, Id = id, Message = message });
    }
}
