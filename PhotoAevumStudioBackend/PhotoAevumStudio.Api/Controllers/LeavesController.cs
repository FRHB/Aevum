using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LeavesController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public LeavesController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpPost("request")]
    public async Task<IActionResult> RequestLeave([FromBody] SubmitLeaveRequestDto dto, CancellationToken ct)
    {
        if (!LeaveTypes.All.Contains(dto.LeaveType))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid leave type",
                Detail = $"LeaveType must be one of: {string.Join(", ", LeaveTypes.All)}"
            });
        }

        if (dto.EndDate < dto.StartDate)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid date range",
                Detail = "EndDate must be greater than or equal to StartDate."
            });
        }

        var studioId = dto.EmployeeStudioId;
        var employeeName = dto.EmployeeName;

        if (string.IsNullOrWhiteSpace(studioId))
        {
            studioId = User.FindFirstValue("studio_id")
                       ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? "EMP-001";
        }

        if (string.IsNullOrWhiteSpace(employeeName))
        {
            employeeName = User.FindFirstValue("full_name")
                           ?? User.FindFirstValue(ClaimTypes.Name)
                           ?? studioId;
        }

        var leave = new EmployeeLeave
        {
            LeaveId = "LV-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            EmployeeStudioId = studioId,
            EmployeeName = employeeName,
            LeaveType = dto.LeaveType,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            Reason = dto.Reason,
            Status = LeaveStatuses.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _db.EmployeeLeaves.Add(leave);
        await _db.SaveChangesAsync(ct);

        return StatusCode(StatusCodes.Status201Created, leave);
    }

    [HttpGet("pending")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> GetPendingLeaves(CancellationToken ct)
    {
        var pending = await _db.EmployeeLeaves
            .Where(l => l.Status == LeaveStatuses.Pending)
            .OrderBy(l => l.StartDate)
            .ToListAsync(ct);

        return Ok(pending);
    }

    [HttpGet("my-requests")]
    public async Task<IActionResult> GetMyLeaves([FromQuery] string? employeeId, CancellationToken ct)
    {
        var targetId = employeeId;
        if (string.IsNullOrWhiteSpace(targetId))
        {
            targetId = User.FindFirstValue("studio_id")
                       ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        }

        var leaves = await _db.EmployeeLeaves
            .Where(l => l.EmployeeStudioId == targetId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        return Ok(leaves);
    }

    [HttpGet]
    [Authorize(Policy = "StaffOrAbove")]
    public async Task<IActionResult> GetAllLeaves([FromQuery] string? status, [FromQuery] string? employeeId, CancellationToken ct)
    {
        var query = _db.EmployeeLeaves.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(l => l.Status == status);

        if (!string.IsNullOrWhiteSpace(employeeId))
            query = query.Where(l => l.EmployeeStudioId == employeeId);

        var list = await query.OrderByDescending(l => l.CreatedAt).ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost("{id}/approve")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> ApproveLeave(int id, CancellationToken ct)
    {
        var leave = await _db.EmployeeLeaves.FindAsync([id], ct);
        if (leave == null) return NotFound(new { Message = $"Leave #{id} not found." });

        var reviewer = User.FindFirstValue("studio_id")
                       ?? User.FindFirstValue(ClaimTypes.Name)
                       ?? "OfficeClerk";

        leave.Status = LeaveStatuses.Approved;
        leave.ApprovedBy = reviewer;
        leave.ApprovedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(leave);
    }

    [HttpPost("{id}/reject")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> RejectLeave(int id, [FromBody] RejectLeaveDto? dto, CancellationToken ct)
    {
        var leave = await _db.EmployeeLeaves.FindAsync([id], ct);
        if (leave == null) return NotFound(new { Message = $"Leave #{id} not found." });

        var reviewer = User.FindFirstValue("studio_id")
                       ?? User.FindFirstValue(ClaimTypes.Name)
                       ?? "OfficeClerk";

        leave.Status = LeaveStatuses.Rejected;
        leave.ApprovedBy = reviewer;
        leave.ApprovedAt = DateTime.UtcNow;
        leave.RejectionReason = dto?.Reason ?? "Declined by management.";

        await _db.SaveChangesAsync(ct);
        return Ok(leave);
    }
}

public record SubmitLeaveRequestDto(
    string LeaveType,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Reason,
    string? EmployeeStudioId = null,
    string? EmployeeName = null);

public record RejectLeaveDto(string? Reason);
