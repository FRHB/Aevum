namespace PhotoAevumStudio.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;

[ApiController]
[Route("api/[controller]")]
[Route("api/equipment-types")]
[Authorize(Policy = "StaffOrAbove")]
public class EquipmentTypesController(
    IEquipmentTypeService service, 
    ILogger<EquipmentTypesController> logger) : ControllerBase
{
    /// <summary>
    /// Generates the next sequential Equipment Type ID (ETYP-####) atomically without race conditions.
    /// </summary>
    [HttpGet("next-id")]
    [ProducesResponseType(typeof(GeneratedEquipmentTypeIdResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNextId(CancellationToken ct)
    {
        try
        {
            var nextId = await service.GetNextEquipmentTypeIdAsync(ct);
            return Ok(new GeneratedEquipmentTypeIdResponse(nextId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate sequential Equipment Type ID.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "Could not generate custom ID." });
        }
    }

    /// <summary>
    /// Retrieves all equipment types registered in dbo.EquipmentType.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<EquipmentTypeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var types = await service.GetAllEquipmentTypesAsync(ct);
        return Ok(types);
    }

    /// <summary>
    /// Creates a new Equipment Type using the verified details and generated ID.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(EquipmentTypeResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateEquipmentTypeRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        string? adminId = User.FindFirstValue("studio_id")
                          ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? "ADM-001";

        try
        {
            var created = await service.CreateEquipmentTypeAsync(request, adminId, ct);
            return StatusCode(StatusCodes.Status201Created, created);
        }
        catch (SqlException ex) when (ex.Number == 51001) // Custom throw: Duplicate Name
        {
            return Conflict(new { Message = ex.Message });
        }
        catch (SqlException ex) when (ex.Number == 51002 || ex.Number == 2627) // Duplicate Key
        {
            return Conflict(new { Message = "Equipment Type ID collision detected. Please refresh." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating Equipment Type: {Name}", request.EquipmentTypeName);
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An unexpected error occurred while saving." });
        }
    }
}
