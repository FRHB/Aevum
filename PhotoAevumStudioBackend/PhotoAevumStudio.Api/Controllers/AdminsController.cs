using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Route("api/management/admins")]
[Authorize(Roles = $"{AppRoles.SystemAdmin},{AppRoles.Admin}")]
public class AdminsController : ControllerBase
{
    private readonly IAdminManagementService _adminService;
    private readonly ILogger<AdminsController> _logger;

    public AdminsController(
        IAdminManagementService adminService,
        ILogger<AdminsController> logger)
    {
        _adminService = adminService;
        _logger = logger;
    }

    private (string RequesterId, bool IsSystemAdmin) GetCallerContext()
    {
        string requesterId = User.FindFirstValue("studio_id")
                             ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                             ?? User.FindFirstValue(ClaimTypes.Name)
                             ?? "ADM-001";

        bool isSystemAdmin = User.IsInRole(AppRoles.SystemAdmin)
                             || User.IsInRole("System Admin")
                             || User.IsInRole("Super Admin")
                             || requesterId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase);

        return (requesterId, isSystemAdmin);
    }

    /// <summary>
    /// Fetches administrator accounts based on the requesting user's authorization tier:
    /// - System Admin: Sees all administrators (System Admins + regular Admins).
    /// - Admin: Sees only other Admins (System Admins are strictly omitted).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAdmins(CancellationToken ct)
    {
        var (requesterId, isSystemAdmin) = GetCallerContext();
        var admins = await _adminService.GetAdminsAsync(requesterId, isSystemAdmin, ct);
        return Ok(admins);
    }

    /// <summary>
    /// Retrieves a single administrator by unique ID.
    /// Regular Admins cannot inspect System Admin accounts.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetAdminById(string id, CancellationToken ct)
    {
        var (requesterId, isSystemAdmin) = GetCallerContext();
        var admin = await _adminService.GetAdminByIdAsync(id, ct);
        if (admin is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Admin Not Found",
                Detail = $"Administrator with ID '{id}' was not found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        // Enforce visibility restriction for regular admins
        if (!isSystemAdmin && admin.IsSuperAdmin)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Admin Not Found",
                Detail = $"Administrator with ID '{id}' was not found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(admin);
    }

    /// <summary>
    /// Generates the next sequential custom ID (SADM-### or ADM-###) based on the specified role.
    /// </summary>
    [HttpGet("next-id")]
    public async Task<IActionResult> GetNextId([FromQuery] string role = "Admin", CancellationToken ct = default)
    {
        var nextId = await _adminService.GetNextAdminIdAsync(role, ct);
        return Ok(new NextAdminIdResponse(nextId, role));
    }

    /// <summary>
    /// Creates a new administrator account and dispatches credentials via email.
    /// STRICT RULE: Restricted exclusively to System Admin role callers.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateAdmin([FromBody] CreateAdminDto dto, CancellationToken ct)
    {
        var (requesterId, isSystemAdmin) = GetCallerContext();

        // Server-side check: Role "Admin" cannot add new admins
        if (!isSystemAdmin)
        {
            _logger.LogWarning("Forbidden admin creation attempt by user {RequesterId}", requesterId);
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Forbidden",
                Detail = "Permission Denied: Only a SystemAdmin (SADM-###) is authorized to create new Admin accounts.",
                Status = StatusCodes.Status403Forbidden
            });
        }

        try
        {
            var createdAdmin = await _adminService.CreateAdminAsync(dto, requesterId, isSystemAdmin, ct);
            return Ok(createdAdmin);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Conflict",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Forbidden",
                Detail = ex.Message,
                Status = StatusCodes.Status403Forbidden
            });
        }
    }

    /// <summary>
    /// Updates administrator profile fields.
    /// STRICT MUTATION RULES:
    /// - Role "Admin": Cannot edit ANY administrator (read-only view).
    /// - Role "System Admin": Can update data for "Admin" users. Cannot edit other "System Admin" users.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAdmin(string id, [FromBody] UpdateAdminDto dto, CancellationToken ct)
    {
        var (requesterId, isSystemAdmin) = GetCallerContext();

        // 1. Role "Admin": Cannot edit ANY administrator
        if (!isSystemAdmin)
        {
            _logger.LogWarning("Forbidden update attempt by Admin {RequesterId} on Admin {TargetId}", requesterId, id);
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Forbidden",
                Detail = "Permission Denied: Admins have read-only access and cannot edit administrator records.",
                Status = StatusCodes.Status403Forbidden
            });
        }

        try
        {
            var updatedAdmin = await _adminService.UpdateAdminAsync(id, dto, requesterId, isSystemAdmin, ct);
            return Ok(updatedAdmin);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Forbidden",
                Detail = ex.Message,
                Status = StatusCodes.Status403Forbidden
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    /// <summary>
    /// Removes an administrator account. Restricted exclusively to System Admins, and cannot delete System Admin accounts.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAdmin(string id, CancellationToken ct)
    {
        var (requesterId, isSystemAdmin) = GetCallerContext();

        if (!isSystemAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Forbidden",
                Detail = "Permission Denied: Only System Admins can remove administrators.",
                Status = StatusCodes.Status403Forbidden
            });
        }

        try
        {
            var deleted = await _adminService.DeleteAdminAsync(id, requesterId, isSystemAdmin, ct);
            if (!deleted)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Not Found",
                    Detail = $"Admin '{id}' not found.",
                    Status = StatusCodes.Status404NotFound
                });
            }

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Operation Prohibited",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }
}
