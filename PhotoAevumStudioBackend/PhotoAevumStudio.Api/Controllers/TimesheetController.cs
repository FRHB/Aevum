using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TimesheetController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public TimesheetController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus([FromQuery] string? employeeId, CancellationToken ct)
    {
        var targetId = ResolveTargetId(employeeId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var activeShift = await _db.EmployeeTimesheets
            .Where(t => t.EmployeeStudioId == targetId && t.ClockOut == null)
            .OrderByDescending(t => t.ClockIn)
            .FirstOrDefaultAsync(ct);

        var todayShift = activeShift ?? await _db.EmployeeTimesheets
            .Where(t => t.EmployeeStudioId == targetId && t.WorkDate == today)
            .OrderByDescending(t => t.ClockIn)
            .FirstOrDefaultAsync(ct);

        // Check if on leave today
        var activeLeave = await _db.EmployeeLeaves
            .Where(l => l.EmployeeStudioId == targetId && l.Status == LeaveStatuses.Approved && today >= l.StartDate && today <= l.EndDate)
            .FirstOrDefaultAsync(ct);

        // Check day off
        var emp = await _db.Employees
            .Include(e => e.IdentityUser)
            .FirstOrDefaultAsync(e => e.IdentityUser.StudioId == targetId || e.IdentityUserId == targetId, ct);

        var dayName = DateTime.UtcNow.DayOfWeek.ToString();
        bool isDayOff = emp != null && (emp.DaysOff ?? "Sunday").Contains(dayName, StringComparison.OrdinalIgnoreCase);

        var isClockedIn = activeShift != null;
        double elapsedHours = 0;
        if (isClockedIn && activeShift != null)
        {
            elapsedHours = Math.Round((DateTime.UtcNow - activeShift.ClockIn).TotalHours, 2);
        }
        else if (todayShift?.ClockOut != null)
        {
            elapsedHours = todayShift.TotalHours;
        }

        return Ok(new
        {
            EmployeeStudioId = targetId,
            IsClockedIn = isClockedIn,
            ClockIn = todayShift?.ClockIn,
            ClockOut = todayShift?.ClockOut,
            ElapsedHours = elapsedHours,
            IsDayOffToday = isDayOff,
            IsOnLeaveToday = activeLeave != null,
            ActiveLeaveType = activeLeave?.LeaveType
        });
    }

    [HttpPost("clock-in")]
    public async Task<IActionResult> ClockIn([FromBody] ClockInOutRequestDto dto, CancellationToken ct)
    {
        var targetId = ResolveTargetId(dto.EmployeeStudioId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // 1. Check if already clocked in
        var existingOpen = await _db.EmployeeTimesheets
            .FirstOrDefaultAsync(t => t.EmployeeStudioId == targetId && t.ClockOut == null, ct);

        if (existingOpen != null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Already Clocked In",
                Detail = $"Employee {targetId} is already clocked in since {existingOpen.ClockIn:hh:mm tt} UTC."
            });
        }

        // 2. Check if on approved leave today
        var activeLeave = await _db.EmployeeLeaves
            .FirstOrDefaultAsync(l => l.EmployeeStudioId == targetId && l.Status == LeaveStatuses.Approved && today >= l.StartDate && today <= l.EndDate, ct);

        if (activeLeave != null)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Employee On Approved Leave",
                Detail = $"Cannot clock in: Employee {targetId} is on approved {activeLeave.LeaveType} (from {activeLeave.StartDate} to {activeLeave.EndDate})."
            });
        }

        // 3. Check day off
        var emp = await _db.Employees
            .Include(e => e.IdentityUser)
            .FirstOrDefaultAsync(e => e.IdentityUser.StudioId == targetId || e.IdentityUserId == targetId, ct);

        var dayName = DateTime.UtcNow.DayOfWeek.ToString();
        bool isDayOff = emp != null && (emp.DaysOff ?? "Sunday").Contains(dayName, StringComparison.OrdinalIgnoreCase);

        var employeeName = dto.EmployeeName;
        if (string.IsNullOrWhiteSpace(employeeName))
        {
            employeeName = emp?.IdentityUser != null
                ? $"{emp.IdentityUser.FirstName} {emp.IdentityUser.LastName}".Trim()
                : (User.FindFirstValue("full_name") ?? User.FindFirstValue(ClaimTypes.Name) ?? targetId);
        }

        var timesheet = new EmployeeTimesheet
        {
            TimesheetId = "TS-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            EmployeeStudioId = targetId,
            EmployeeName = employeeName,
            WorkDate = today,
            ClockIn = DateTime.UtcNow,
            Status = TimesheetStatuses.Open,
            IsDayOffWork = isDayOff,
            Notes = dto.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _db.EmployeeTimesheets.Add(timesheet);
        await _db.SaveChangesAsync(ct);

        return Ok(timesheet);
    }

    [HttpPost("clock-out")]
    public async Task<IActionResult> ClockOut([FromBody] ClockInOutRequestDto dto, CancellationToken ct)
    {
        var targetId = ResolveTargetId(dto.EmployeeStudioId);

        var openRecord = await _db.EmployeeTimesheets
            .Where(t => t.EmployeeStudioId == targetId && t.ClockOut == null)
            .OrderByDescending(t => t.ClockIn)
            .FirstOrDefaultAsync(ct);

        if (openRecord == null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Not Clocked In",
                Detail = $"No active clocked-in session found for employee {targetId}."
            });
        }

        openRecord.ClockOut = DateTime.UtcNow;
        openRecord.TotalHours = Math.Round((openRecord.ClockOut.Value - openRecord.ClockIn).TotalHours, 2);
        openRecord.Status = TimesheetStatuses.Completed;
        if (!string.IsNullOrWhiteSpace(dto.Notes))
        {
            openRecord.Notes = string.IsNullOrWhiteSpace(openRecord.Notes)
                ? dto.Notes
                : $"{openRecord.Notes} | {dto.Notes}";
        }

        await _db.SaveChangesAsync(ct);
        return Ok(openRecord);
    }

    [HttpGet("summary-today")]
    [Authorize(Policy = "StaffOrAbove")]
    public async Task<IActionResult> GetTodaySummary(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dayName = DateTime.UtcNow.DayOfWeek.ToString();

        var timesheets = await _db.EmployeeTimesheets
            .Where(t => t.WorkDate == today || t.ClockOut == null)
            .ToListAsync(ct);

        var activeLeaves = await _db.EmployeeLeaves
            .Where(l => l.Status == LeaveStatuses.Approved && today >= l.StartDate && today <= l.EndDate)
            .ToListAsync(ct);

        var employees = await _db.Employees
            .Include(e => e.IdentityUser)
            .ToListAsync(ct);

        var list = new List<object>();

        foreach (var emp in employees)
        {
            var empId = emp.IdentityUser?.StudioId ?? emp.IdentityUserId;
            var punch = timesheets
                .Where(t => t.EmployeeStudioId == empId)
                .OrderByDescending(t => t.ClockIn)
                .FirstOrDefault();

            var leave = activeLeaves.FirstOrDefault(l => l.EmployeeStudioId == empId);
            bool isDayOff = (emp.DaysOff ?? "Sunday").Contains(dayName, StringComparison.OrdinalIgnoreCase);

            list.Add(new
            {
                EmployeeStudioId = empId,
                EmployeeName = emp.IdentityUser != null ? $"{emp.IdentityUser.FirstName} {emp.IdentityUser.LastName}".Trim() : empId,
                DaysOff = emp.DaysOff ?? "Sunday",
                IsClockedIn = punch != null && punch.ClockOut == null,
                ClockIn = punch?.ClockIn,
                ClockOut = punch?.ClockOut,
                TotalHours = punch != null ? (punch.ClockOut == null ? Math.Round((DateTime.UtcNow - punch.ClockIn).TotalHours, 2) : punch.TotalHours) : 0,
                IsDayOffToday = isDayOff,
                IsOnLeaveToday = leave != null,
                ActiveLeaveType = leave?.LeaveType
            });
        }

        return Ok(list);
    }

    [HttpGet("my-history")]
    public async Task<IActionResult> GetMyHistory([FromQuery] string? employeeId, CancellationToken ct)
    {
        var targetId = ResolveTargetId(employeeId);
        var history = await _db.EmployeeTimesheets
            .Where(t => t.EmployeeStudioId == targetId)
            .OrderByDescending(t => t.ClockIn)
            .Take(50)
            .ToListAsync(ct);

        return Ok(history);
    }

    [HttpPost("{id}/adjust")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> AdjustPunch(int id, [FromBody] AdjustPunchDto dto, CancellationToken ct)
    {
        var record = await _db.EmployeeTimesheets.FindAsync([id], ct);
        if (record == null) return NotFound(new { Message = $"Timesheet #{id} not found." });

        var reviewer = User.FindFirstValue("studio_id")
                       ?? User.FindFirstValue(ClaimTypes.Name)
                       ?? "OfficeClerk";

        if (dto.ClockIn.HasValue) record.ClockIn = dto.ClockIn.Value;
        if (dto.ClockOut.HasValue) record.ClockOut = dto.ClockOut.Value;

        if (record.ClockOut.HasValue)
        {
            record.TotalHours = Math.Round((record.ClockOut.Value - record.ClockIn).TotalHours, 2);
            record.Status = TimesheetStatuses.Adjusted;
        }

        record.AdjustedBy = reviewer;
        record.AdjustmentReason = dto.AdjustmentReason ?? "Adjusted by management";

        await _db.SaveChangesAsync(ct);
        return Ok(record);
    }

    private string ResolveTargetId(string? candidateId)
    {
        if (!string.IsNullOrWhiteSpace(candidateId)) return candidateId;
        return User.FindFirstValue("studio_id")
               ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? "EMP-001";
    }
}

public record ClockInOutRequestDto(
    string? EmployeeStudioId = null,
    string? EmployeeName = null,
    string? Notes = null);

public record AdjustPunchDto(
    DateTime? ClockIn,
    DateTime? ClockOut,
    string? AdjustmentReason);
