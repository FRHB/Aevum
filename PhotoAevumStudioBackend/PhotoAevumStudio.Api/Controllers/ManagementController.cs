using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "StaffOrAbove")] // All management data requires authentication
public class ManagementController : ControllerBase
{
    private readonly IStudioManagementService _service;

    public ManagementController(IStudioManagementService service)
    {
        _service = service;
    }

    [HttpGet("employees")]
    public async Task<IActionResult> GetEmployees(CancellationToken ct)
    {
        var employees = await _service.GetEmployeesAsync(ct);
        
        var result = employees.Select(e => new
        {
            e.Id,
            EmployeeId = e.IdentityUser.StudioId,
            e.IdentityUser.FirstName,
            e.IdentityUser.LastName,
            e.IdentityUser.MiddleName,
            e.IdentityUser.Email,
            Role = "Employee",
            e.Specializations,
            e.Status
        });
        
        return Ok(result);
    }

    [HttpGet("specializations")]
    public async Task<IActionResult> GetSpecializations(CancellationToken ct)
    {
        var specs = await _service.GetSpecializationsAsync(ct);
        return Ok(specs);
    }

    [HttpGet("services")]
    public async Task<IActionResult> GetServices(CancellationToken ct)
    {
        var services = await _service.GetServicesAsync(ct);
        return Ok(services);
    }

    [HttpGet("equipment")]
    public async Task<IActionResult> GetEquipment(CancellationToken ct)
    {
        var equipment = await _service.GetEquipmentAsync(ct);
        var result = equipment.Select(eq => new
        {
            eq.Id,
            eq.Name,
            eq.SerialNumber,
            eq.Status,
            AssignedEmployee = eq.AssignedEmployee?.IdentityUser.FullName ?? string.Empty
        });
        return Ok(result);
    }

    // ── Stored Procedures: Service Images (Summary vs Detailed View) ──────────

    /// <summary>
    /// Lightweight summary view of Services without heavy binary image payloads.
    /// </summary>
    [HttpGet("services/summary")]
    public async Task<IActionResult> GetServicesSummary(CancellationToken ct)
    {
        var summary = await _service.GetServicesSummaryAsync(ct);
        return Ok(summary);
    }

    /// <summary>
    /// Detailed view of a specific service including image binary attributes.
    /// </summary>
    [HttpGet("services/{id}/details")]
    public async Task<IActionResult> GetServiceDetails(string id, CancellationToken ct)
    {
        var detail = await _service.GetServiceDetailWithImageAsync(id, ct);
        if (detail is null) return NotFound(new { Message = $"Service '{id}' not found." });
        return Ok(detail);
    }

    /// <summary>
    /// Directly streams the image binary for a specific service.
    /// </summary>
    [HttpGet("services/{id}/image")]
    [AllowAnonymous]
    public async Task<IActionResult> GetServiceImage(string id, CancellationToken ct)
    {
        var detail = await _service.GetServiceDetailWithImageAsync(id, ct);
        if (detail?.Image is null || detail.Image.Length == 0)
            return NotFound(new { Message = "No image found for this service." });

        return File(detail.Image, detail.MimeType ?? "image/jpeg");
    }

    /// <summary>
    /// Uploads and attaches an image to a service.
    /// </summary>
    [HttpPost("services/{id}/image")]
    public async Task<IActionResult> UploadServiceImage(string id, [FromBody] UpdateServiceImageRequest request, CancellationToken ct)
    {
        if (request?.ImageBytes is null || request.ImageBytes.Length == 0)
            return BadRequest("Image bytes are required.");

        string? adminId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        var success = await _service.UpdateServiceImageAsync(id, request.ImageBytes, request.MimeType, adminId, ct);
        return Ok(new { Success = success, Message = "Service image updated successfully." });
    }

    // ── Additional Services ──────────────────────────────────────────────────

    [HttpGet("additional-services/summary")]
    public async Task<IActionResult> GetAdditionalServicesSummary(CancellationToken ct)
    {
        var summary = await _service.GetAdditionalServicesSummaryAsync(ct);
        return Ok(summary);
    }

    [HttpGet("additional-services/{id}/details")]
    public async Task<IActionResult> GetAdditionalServiceDetails(string id, CancellationToken ct)
    {
        var detail = await _service.GetAdditionalServiceDetailWithImageAsync(id, ct);
        if (detail is null) return NotFound(new { Message = $"Additional service '{id}' not found." });
        return Ok(detail);
    }

    [HttpGet("additional-services/{id}/image")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAdditionalServiceImage(string id, CancellationToken ct)
    {
        var detail = await _service.GetAdditionalServiceDetailWithImageAsync(id, ct);
        if (detail?.Image is null || detail.Image.Length == 0)
            return NotFound(new { Message = "No image found for this additional service." });

        return File(detail.Image, detail.MimeType ?? "image/jpeg");
    }

    [HttpPost("additional-services/{id}/image")]
    public async Task<IActionResult> UploadAdditionalServiceImage(string id, [FromBody] UpdateServiceImageRequest request, CancellationToken ct)
    {
        if (request?.ImageBytes is null || request.ImageBytes.Length == 0)
            return BadRequest("Image bytes are required.");

        string? adminId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        var success = await _service.UpdateAdditionalServiceImageAsync(id, request.ImageBytes, request.MimeType, adminId, ct);
        return Ok(new { Success = success, Message = "Additional service image updated successfully." });
    }

    // ── Combo Packages ───────────────────────────────────────────────────────

    [HttpGet("combo-packages/summary")]
    public async Task<IActionResult> GetComboPackagesSummary(CancellationToken ct)
    {
        var summary = await _service.GetComboPackagesSummaryAsync(ct);
        return Ok(summary);
    }

    [HttpGet("combo-packages/{id}/details")]
    public async Task<IActionResult> GetComboPackageDetails(string id, CancellationToken ct)
    {
        var detail = await _service.GetComboPackageDetailWithImageAsync(id, ct);
        if (detail is null) return NotFound(new { Message = $"Combo package '{id}' not found." });
        return Ok(detail);
    }

    [HttpGet("combo-packages/{id}/image")]
    [AllowAnonymous]
    public async Task<IActionResult> GetComboPackageImage(string id, CancellationToken ct)
    {
        var detail = await _service.GetComboPackageDetailWithImageAsync(id, ct);
        if (detail?.Image is null || detail.Image.Length == 0)
            return NotFound(new { Message = "No image found for this package." });

        return File(detail.Image, detail.MimeType ?? "image/jpeg");
    }

    [HttpPost("combo-packages/{id}/image")]
    public async Task<IActionResult> UploadComboPackageImage(string id, [FromBody] UpdateServiceImageRequest request, CancellationToken ct)
    {
        if (request?.ImageBytes is null || request.ImageBytes.Length == 0)
            return BadRequest("Image bytes are required.");

        string? adminId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        var success = await _service.UpdateComboPackageImageAsync(id, request.ImageBytes, request.MimeType, adminId, ct);
        return Ok(new { Success = success, Message = "Combo package image updated successfully." });
    }

    // ── History Logs (SystemAdmin has full access) ────────────────────────────

    [HttpGet("history-logs")]
    public async Task<IActionResult> GetHistoryLogs([FromQuery] int top = 100, CancellationToken ct = default)
    {
        string? requestingUserId = User.FindFirstValue("studio_id")
                                   ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                                   ?? User.FindFirstValue(ClaimTypes.Name)
                                   ?? "SADM-001";

        var logs = await _service.GetHistoryLogsAsync(requestingUserId, top, ct);
        return Ok(logs);
    }
}
