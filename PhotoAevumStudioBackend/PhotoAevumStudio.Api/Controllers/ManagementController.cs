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
    public async Task<IActionResult> GetEmployees([FromQuery] bool includeArchived, CancellationToken ct)
    {
        var employees = await _service.GetEmployeesAsync(includeArchived, ct);
        
        var result = employees.Select(e => new
        {
            e.Id,
            EmployeeId = e.IdentityUser.StudioId,
            e.IdentityUser.FirstName,
            e.IdentityUser.LastName,
            e.IdentityUser.MiddleName,
            e.IdentityUser.Email,
            Role = "Employee",
            e.Address,
            e.BirthDate,
            e.BirthYear,
            DepartmentId = e.DepartmentID,
            Department = e.DepartmentEntity?.DepartmentName,
            DepartmentName = e.DepartmentEntity?.DepartmentName,
            e.Specializations,
            e.Status,
            e.DaysOff
        });
        
        return Ok(result);
    }

    [HttpPut("employees/{employeeId}/days-off")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> UpdateEmployeeDaysOff(string employeeId, [FromBody] UpdateEmployeeDaysOffDto dto, CancellationToken ct)
    {
        if (dto == null)
            return BadRequest(new { Message = "Request payload is required." });

        var adminId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "Admin";
        var updated = await _service.UpdateEmployeeDaysOffAsync(employeeId, dto.DaysOff ?? "None", adminId, ct);
        if (!updated)
            return NotFound(new { Message = $"Employee '{employeeId}' not found." });

        return Ok(new { Success = true, EmployeeId = employeeId, DaysOff = dto.DaysOff });
    }

    [HttpPost("employees/{employeeId}/reset-password")]
    [Authorize(Policy = "AdminOrAbove")]
    public async Task<IActionResult> ResetEmployeePassword(string employeeId, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("studio_id")
                      ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.Identity?.Name
                      ?? "Admin";

        var (success, message) = await _service.ResetEmployeePasswordToInitialAsync(employeeId, adminId, ct);
        if (!success)
            return BadRequest(new { Success = false, Message = message });

        return Ok(new { Success = true, Message = message, EmployeeId = employeeId });
    }

    [HttpPost("employees/{employeeId}/resend-welcome")]
    [Authorize(Policy = "AdminOrAbove")]
    public async Task<IActionResult> ResendEmployeeWelcomeEmail(string employeeId, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("studio_id")
                      ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.Identity?.Name
                      ?? "Admin";

        var (success, emailSent, handbookAttached, message, warning) =
            await _service.ResendEmployeeWelcomeEmailAsync(employeeId, adminId, ct);

        if (!success)
            return BadRequest(new { Success = false, EmailSent = false, HandbookAttached = false, Message = message, Warning = warning });

        return Ok(new
        {
            Success = emailSent,
            EmailSent = emailSent,
            HandbookAttached = handbookAttached,
            Message = message,
            Warning = warning,
            EmployeeId = employeeId
        });
    }

    [HttpGet("departments")]
    public async Task<IActionResult> GetDepartments(CancellationToken ct)
    {
        var depts = await _service.GetDepartmentsAsync(ct);
        return Ok(depts);
    }

    [HttpGet("departments/next-id")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> GetNextDepartmentId(CancellationToken ct)
    {
        var nextId = await _service.GetNextDepartmentIdAsync(ct);
        return Ok(new NextIdResponseDto(nextId));
    }

    [HttpPost("departments")]
    [Authorize(Policy = "AdminOrAbove")]
    public async Task<IActionResult> CreateDepartment([FromBody] CreateDepartmentDto dto, CancellationToken ct)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.DepartmentName))
            return BadRequest(new { Message = "Department name is required." });

        var adminId = User.FindFirst("studio_id")?.Value ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(adminId))
            return Unauthorized();
        try
        {
            var created = await _service.CreateDepartmentAsync(dto, adminId, ct);
            return Ok(created);
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
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

    private static bool TryValidateCatalogImage(byte[]? bytes, string? requestedMime, out string validatedMime, out string error)
    {
        validatedMime = "image/jpeg";
        error = string.Empty;

        if (bytes == null || bytes.Length == 0)
        {
            error = "Image bytes are required.";
            return false;
        }

        if (bytes.Length > 10 * 1024 * 1024)
        {
            error = "Image exceeds maximum allowed size of 10 MB.";
            return false;
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            validatedMime = "image/jpeg";
            return true;
        }

        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            validatedMime = "image/png";
            return true;
        }

        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            validatedMime = "image/webp";
            return true;
        }

        error = "Invalid image content. Only JPEG, PNG, and WebP signatures are allowed.";
        return false;
    }

    /// <summary>
    /// Uploads and attaches an image to a service.
    /// </summary>
    [HttpPost("services/{id}/image")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> UploadServiceImage(string id, [FromBody] UpdateServiceImageRequest request, CancellationToken ct)
    {
        if (!TryValidateCatalogImage(request?.ImageBytes, request?.MimeType, out var validatedMime, out var error))
            return BadRequest(error);

        string? adminId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(adminId)) return Unauthorized();

        var success = await _service.UpdateServiceImageAsync(id, request!.ImageBytes, validatedMime, adminId, ct);
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
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> UploadAdditionalServiceImage(string id, [FromBody] UpdateServiceImageRequest request, CancellationToken ct)
    {
        if (!TryValidateCatalogImage(request?.ImageBytes, request?.MimeType, out var validatedMime, out var error))
            return BadRequest(error);

        string? adminId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(adminId)) return Unauthorized();

        var success = await _service.UpdateAdditionalServiceImageAsync(id, request!.ImageBytes, validatedMime, adminId, ct);
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
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> UploadComboPackageImage(string id, [FromBody] UpdateServiceImageRequest request, CancellationToken ct)
    {
        if (!TryValidateCatalogImage(request?.ImageBytes, request?.MimeType, out var validatedMime, out var error))
            return BadRequest(error);

        string? adminId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(adminId)) return Unauthorized();

        var success = await _service.UpdateComboPackageImageAsync(id, request!.ImageBytes, validatedMime, adminId, ct);
        return Ok(new { Success = success, Message = "Combo package image updated successfully." });
    }

    // ── History Logs (SystemAdmin has full access) ────────────────────────────

    [HttpGet("history-logs")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> GetHistoryLogs([FromQuery] int top = 100, CancellationToken ct = default)
    {
        string? requestingUserId = User.FindFirstValue("studio_id")
                                   ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                                   ?? User.FindFirstValue(ClaimTypes.Name)
                                   ?? string.Empty;

        var logs = await _service.GetHistoryLogsAsync(requestingUserId, top, ct);
        return Ok(logs);
    }
}
