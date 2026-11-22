using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "StaffOrAbove")]
public class TimesheetController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ISqlAuthService _sqlAuthService;

    public TimesheetController(ApplicationDbContext db, ISqlAuthService sqlAuthService)
    {
        _db = db;
        _sqlAuthService = sqlAuthService;
    }

    private static DateTime GetPhtNow()
    {
        try
        {
            var phtZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phtZone);
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                var phtZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phtZone);
            }
            catch
            {
                return DateTime.UtcNow.AddHours(8);
            }
        }
    }

    private static bool IsDefaultClerkId(string? employeeId)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return false;
        var id = employeeId.Trim();
        return id.Equals("EMP-003", StringComparison.OrdinalIgnoreCase)
            || id.Equals("EMP-004", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("CLK-", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record EmployeeInfo(string Id, string Name, string DaysOff, string PayBasis, bool IsTitleIExempt);

    private async Task<List<EmployeeInfo>> GetEmployeesListAsync(CancellationToken ct)
    {
        var list = new List<EmployeeInfo>();
        try
        {
            var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    EmployeeID,
                    LTRIM(RTRIM(CONCAT(ISNULL(EmployeeFirstName, ''), ' ', ISNULL(EmployeeLastName, '')))) AS FullName,
                    ISNULL(DaysOff, 'Sunday') AS DaysOff,
                    CASE WHEN COL_LENGTH(N'dbo.Employees', N'PayBasis') IS NOT NULL THEN ISNULL(PayBasis, 'CommissionPerEvent') ELSE 'CommissionPerEvent' END AS PayBasis,
                    CASE WHEN COL_LENGTH(N'dbo.Employees', N'IsTitleIExempt') IS NOT NULL THEN ISNULL(IsTitleIExempt, 1) ELSE 1 END AS IsTitleIExempt,
                    ISNULL(Specializations, '') AS Specializations
                FROM dbo.Employees
                WHERE EmployeeStatus = 'Active' OR Status = 'Active' OR EmployeeStatus IS NULL
                ORDER BY EmployeeID ASC;
            ";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var empId = reader.GetString(0);
                var name = reader.IsDBNull(1) || string.IsNullOrWhiteSpace(reader.GetString(1)) ? empId : reader.GetString(1);
                var daysOff = reader.IsDBNull(2) ? "Sunday" : reader.GetString(2);
                var payBasis = reader.IsDBNull(3) ? "CommissionPerEvent" : reader.GetString(3);
                var isExempt = reader.IsDBNull(4) ? true : Convert.ToBoolean(reader.GetValue(4));
                var specs = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);

                if (IsDefaultClerkId(empId) || specs.Contains("Office Clerk", StringComparison.OrdinalIgnoreCase))
                {
                    payBasis = "FixedMonthly";
                    isExempt = false;
                }

                list.Add(new EmployeeInfo(empId, name, daysOff, payBasis, isExempt));
            }
        }
        catch
        {
            var emps = await _db.Employees.AsNoTracking().ToListAsync(ct);
            foreach (var e in emps)
            {
                var id = e.IdentityUserId ?? e.Id.ToString();
                bool isClerk = IsDefaultClerkId(id) || e.Specializations.Any(s => s.Contains("Clerk", StringComparison.OrdinalIgnoreCase));
                bool isExempt = isClerk ? false : e.IsTitleIExempt;
                string payBasis = isExempt ? "CommissionPerEvent" : "FixedMonthly";
                list.Add(new EmployeeInfo(id, id, e.DaysOff ?? "Sunday", payBasis, isExempt));
            }
        }
        return list;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus([FromQuery] string? employeeId, CancellationToken ct)
    {
        var targetId = ResolveTargetId(employeeId);
        if (string.IsNullOrWhiteSpace(targetId))
            return Unauthorized();
        var phtNow = GetPhtNow();
        var today = DateOnly.FromDateTime(phtNow);

        var activeShift = await _db.EmployeeTimesheets
            .Where(t => t.EmployeeStudioId == targetId && t.ClockOut == null)
            .OrderByDescending(t => t.ClockIn)
            .FirstOrDefaultAsync(ct);

        var todayShift = activeShift ?? await _db.EmployeeTimesheets
            .Where(t => t.EmployeeStudioId == targetId && t.WorkDate == today)
            .OrderByDescending(t => t.ClockIn)
            .FirstOrDefaultAsync(ct);

        var activeLeave = await _db.EmployeeLeaves
            .Where(l => l.EmployeeStudioId == targetId && l.Status == LeaveStatuses.Approved && today >= l.StartDate && today <= l.EndDate)
            .FirstOrDefaultAsync(ct);

        var employees = await GetEmployeesListAsync(ct);
        var emp = employees.FirstOrDefault(e => e.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));

        var dayName = phtNow.DayOfWeek.ToString();
        bool isDayOff = emp != null && (emp.DaysOff ?? "Sunday").Contains(dayName, StringComparison.OrdinalIgnoreCase);

        var isClockedIn = activeShift != null;
        double elapsedHours = 0;
        if (isClockedIn && activeShift != null)
        {
            elapsedHours = Math.Round((phtNow - activeShift.ClockIn).TotalHours, 2);
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
        if (string.IsNullOrWhiteSpace(targetId))
            return Unauthorized();
        var phtNow = GetPhtNow();
        var today = DateOnly.FromDateTime(phtNow);

        var existingOpen = await _db.EmployeeTimesheets
            .FirstOrDefaultAsync(t => t.EmployeeStudioId == targetId && t.ClockOut == null, ct);

        if (existingOpen != null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Already Clocked In",
                Detail = $"Employee {targetId} is already clocked in since {existingOpen.ClockIn:hh:mm tt} PHT."
            });
        }

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

        var employees = await GetEmployeesListAsync(ct);
        var emp = employees.FirstOrDefault(e => e.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));

        var dayName = phtNow.DayOfWeek.ToString();
        bool isDayOff = emp != null && (emp.DaysOff ?? "Sunday").Contains(dayName, StringComparison.OrdinalIgnoreCase);

        var employeeName = dto.EmployeeName;
        if (string.IsNullOrWhiteSpace(employeeName))
        {
            employeeName = emp != null && !string.IsNullOrWhiteSpace(emp.Name)
                ? emp.Name
                : (User.FindFirstValue("full_name") ?? User.FindFirstValue(ClaimTypes.Name) ?? targetId);
        }

        var timesheet = new EmployeeTimesheet
        {
            TimesheetId = "TS-" + phtNow.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            EmployeeStudioId = targetId,
            EmployeeName = employeeName,
            WorkDate = today,
            ClockIn = phtNow,
            Status = TimesheetStatuses.Open,
            EntryType = TimesheetEntryTypes.Work,
            IsDayOffWork = isDayOff,
            Notes = dto.Notes,
            CreatedAt = phtNow
        };

        _db.EmployeeTimesheets.Add(timesheet);
        await _db.SaveChangesAsync(ct);

        await _sqlAuthService.LogActivityAsync(
            "CLOCK_IN",
            "EmployeeTimesheet",
            timesheet.TimesheetId,
            $"Employee {targetId} clocked in at {phtNow:yyyy-MM-dd hh:mm:ss tt} PHT",
            "Employee",
            targetId,
            ct);

        return Ok(timesheet);
    }

    [HttpPost("clock-out")]
    public async Task<IActionResult> ClockOut([FromBody] ClockInOutRequestDto dto, CancellationToken ct)
    {
        var targetId = ResolveTargetId(dto.EmployeeStudioId);
        if (string.IsNullOrWhiteSpace(targetId))
            return Unauthorized();
        var phtNow = GetPhtNow();

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

        var employees = await GetEmployeesListAsync(ct);
        var emp = employees.FirstOrDefault(e => e.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));
        bool isTitleIExempt = emp?.IsTitleIExempt ?? !IsDefaultClerkId(targetId);

        openRecord.ClockOut = phtNow;
        openRecord.ApplyDoleShiftMinutes(isTitleIExempt);
        openRecord.Status = TimesheetStatuses.Completed;
        if (!string.IsNullOrWhiteSpace(dto.Notes))
        {
            openRecord.Notes = string.IsNullOrWhiteSpace(openRecord.Notes)
                ? dto.Notes
                : $"{openRecord.Notes} | {dto.Notes}";
        }

        await _db.SaveChangesAsync(ct);

        await _sqlAuthService.LogActivityAsync(
            "CLOCK_OUT",
            "EmployeeTimesheet",
            openRecord.TimesheetId,
            $"Employee {targetId} clocked out at {phtNow:yyyy-MM-dd hh:mm:ss tt} PHT (Duration: {openRecord.TotalHours}h, Reg: {openRecord.RegularMinutes}m, UT: {openRecord.UndertimeMinutes}m, PendingOT: {openRecord.PendingOvertimeMinutes}m)",
            "Employee",
            targetId,
            ct);

        return Ok(openRecord);
    }

    [HttpGet("summary-today")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> GetTodaySummary(CancellationToken ct)
    {
        var phtNow = GetPhtNow();
        var today = DateOnly.FromDateTime(phtNow);
        var dayName = phtNow.DayOfWeek.ToString();

        var timesheets = await _db.EmployeeTimesheets
            .Where(t => t.WorkDate == today || t.ClockOut == null)
            .ToListAsync(ct);

        var activeLeaves = await _db.EmployeeLeaves
            .Where(l => l.Status == LeaveStatuses.Approved && today >= l.StartDate && today <= l.EndDate)
            .ToListAsync(ct);

        var employees = await GetEmployeesListAsync(ct);

        var list = new List<object>();

        foreach (var emp in employees)
        {
            var punch = timesheets
                .Where(t => t.EmployeeStudioId == emp.Id)
                .OrderByDescending(t => t.ClockIn)
                .FirstOrDefault();

            var leave = activeLeaves.FirstOrDefault(l => l.EmployeeStudioId == emp.Id);
            bool isDayOff = (emp.DaysOff ?? "Sunday").Contains(dayName, StringComparison.OrdinalIgnoreCase);

            list.Add(new
            {
                EmployeeStudioId = emp.Id,
                EmployeeName = emp.Name,
                DaysOff = emp.DaysOff ?? "Sunday",
                PayBasis = emp.PayBasis,
                IsTitleIExempt = emp.IsTitleIExempt,
                IsClockedIn = punch != null && punch.ClockOut == null,
                ClockIn = punch?.ClockIn,
                ClockOut = punch?.ClockOut,
                TotalHours = punch != null ? (punch.ClockOut == null ? Math.Round((phtNow - punch.ClockIn).TotalHours, 2) : punch.TotalHours) : 0,
                RegularMinutes = punch?.RegularMinutes ?? 0,
                UndertimeMinutes = punch?.UndertimeMinutes ?? 0,
                PendingOvertimeMinutes = punch?.PendingOvertimeMinutes ?? 0,
                ApprovedOvertimeMinutes = punch?.ApprovedOvertimeMinutes ?? 0,
                IsOvertimeApproved = punch?.IsOvertimeApproved ?? false,
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
        var isManager = IsClerkOrAdmin();

        if (string.IsNullOrWhiteSpace(employeeId) && isManager)
        {
            var allHistory = await _db.EmployeeTimesheets
                .OrderByDescending(t => t.ClockIn)
                .Take(100)
                .ToListAsync(ct);

            return Ok(allHistory);
        }

        var targetId = ResolveTargetId(employeeId);
        if (string.IsNullOrWhiteSpace(targetId))
            return Unauthorized();
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
            var employees = await GetEmployeesListAsync(ct);
            var emp = employees.FirstOrDefault(e => e.Id.Equals(record.EmployeeStudioId, StringComparison.OrdinalIgnoreCase));
            bool isTitleIExempt = emp?.IsTitleIExempt ?? !IsDefaultClerkId(record.EmployeeStudioId);

            record.ApplyDoleShiftMinutes(isTitleIExempt, dto.ExplicitUndertimeMinutes);
            record.Status = TimesheetStatuses.Adjusted;
        }

        record.AdjustedBy = reviewer;
        record.AdjustmentReason = dto.AdjustmentReason ?? "Adjusted by management";

        await _db.SaveChangesAsync(ct);

        await _sqlAuthService.LogActivityAsync(
            "ADJUST_PUNCH",
            "EmployeeTimesheet",
            record.TimesheetId,
            $"Timesheet #{id} ({record.EmployeeStudioId}) punch adjusted by {reviewer}: In={record.ClockIn:yyyy-MM-dd HH:mm}, Out={record.ClockOut:yyyy-MM-dd HH:mm}, Reg={record.RegularMinutes}m, UT={record.UndertimeMinutes}m, PendingOT={record.PendingOvertimeMinutes}m. Reason: {record.AdjustmentReason}",
            "OfficeClerk",
            reviewer,
            ct);

        return Ok(record);
    }

    [HttpPost("{id}/approve-ot")]
    [Authorize(Policy = "AdminOrAbove")]
    public async Task<IActionResult> ApproveOvertime(int id, [FromBody] ApproveOvertimeDto? dto, CancellationToken ct)
    {
        var record = await _db.EmployeeTimesheets.FindAsync([id], ct);
        if (record == null) return NotFound(new { Message = $"Timesheet #{id} not found." });

        var employees = await GetEmployeesListAsync(ct);
        var emp = employees.FirstOrDefault(e => e.Id.Equals(record.EmployeeStudioId, StringComparison.OrdinalIgnoreCase));
        bool isTitleIExempt = emp?.IsTitleIExempt ?? !IsDefaultClerkId(record.EmployeeStudioId);

        if (isTitleIExempt)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Title I Exempt Staff",
                Detail = "Per-event shoot crew are exempt from Labor Code Title I hourly overtime rules."
            });
        }

        if (record.PendingOvertimeMinutes <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "No Pending Overtime",
                Detail = $"Timesheet #{id} has 0 pending overtime minutes."
            });
        }

        var approver = User.FindFirstValue("studio_id")
                       ?? User.FindFirstValue(ClaimTypes.Name)
                       ?? "Admin";

        // DOLE Art. 88: Never subtract UndertimeMinutes from ApprovedOvertimeMinutes.
        int approvedMinutes = dto?.ApprovedOvertimeMinutes.HasValue == true && dto.ApprovedOvertimeMinutes.Value > 0
            ? Math.Min(dto.ApprovedOvertimeMinutes.Value, record.PendingOvertimeMinutes)
            : record.PendingOvertimeMinutes;

        record.ApprovedOvertimeMinutes = approvedMinutes;
        record.IsOvertimeApproved = true;
        record.OvertimeApprovedBy = approver;
        record.OvertimeApprovedAt = GetPhtNow();
        if (record.Status == TimesheetStatuses.Completed)
            record.Status = TimesheetStatuses.Approved;

        await _db.SaveChangesAsync(ct);

        await _sqlAuthService.LogActivityAsync(
            "APPROVE_OVERTIME",
            "EmployeeTimesheet",
            record.TimesheetId,
            $"Timesheet #{id} ({record.EmployeeStudioId}) overtime approved by {approver}: {record.ApprovedOvertimeMinutes} min at 1.25x rate (Undertime preserved at {record.UndertimeMinutes} min per DOLE Art. 88).",
            "Admin",
            approver,
            ct);

        return Ok(record);
    }

    private bool IsClerkOrAdmin() =>
        User.IsInRole("Admin")
        || User.IsInRole("SystemAdmin")
        || User.IsInRole("Office Clerk")
        || User.IsInRole("OfficeClerk")
        || User.HasClaim(c => c.Type == ClaimTypes.Role && (c.Value == "Admin" || c.Value == "SystemAdmin" || c.Value.Contains("Clerk", StringComparison.OrdinalIgnoreCase)));

    private string? ResolveTargetId(string? candidateId)
    {
        if (IsClerkOrAdmin() && !string.IsNullOrWhiteSpace(candidateId))
            return candidateId.Trim();

        return User.FindFirstValue("studio_id")
               ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");
    }
}

public record ClockInOutRequestDto(
    string? EmployeeStudioId = null,
    string? EmployeeName = null,
    string? Notes = null);

public record AdjustPunchDto(
    DateTime? ClockIn,
    DateTime? ClockOut,
    string? AdjustmentReason,
    int? ExplicitUndertimeMinutes = null);

public record ApproveOvertimeDto(
    int? ApprovedOvertimeMinutes = null,
    string? Notes = null);
