using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
}

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly IStudioManagementService _service;
    private readonly ILogger<CategoriesController> _logger;

    public CategoriesController(IStudioManagementService service, ILogger<CategoriesController> logger)
    {
        _service = service;
        _logger = logger;
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
    /// Creates a new Service Category with auto-generated custom ID.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        string? adminId = User.FindFirstValue("studio_id") 
                          ?? User.FindFirstValue(ClaimTypes.NameIdentifier) 
                          ?? "Admin";

        var dto = new CreateCategoryDto(request.Name.Trim(), request.Description.Trim());

        try
        {
            var result = await _service.CreateCategoryAsync(dto, adminId, ct);
            return CreatedAtAction(nameof(GetNextId), new { id = result.Id }, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating service category {CategoryName}", request.Name);
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = ex.Message });
        }
    }
}
