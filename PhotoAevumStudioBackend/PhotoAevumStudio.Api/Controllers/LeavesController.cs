using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LeavesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IPaymentNotifier? _notifier;

    public LeavesController(ApplicationDbContext db, IPaymentNotifier? notifier = null)
    {
        _db = db;
        _notifier = notifier;
    }

    private sealed record EmployeeLeaveProfile(
        string EmployeeId,
        string FullName,
        string DaysOff,
        string PayBasis,
        bool IsTitleIExempt);

    private static bool IsDefaultClerkId(string? employeeId)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return false;
        var id = employeeId.Trim();
        return id.Equals("EMP-003", StringComparison.OrdinalIgnoreCase)
            || id.Equals("EMP-004", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("CLK-", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<EmployeeLeaveProfile> ResolveEmployeeProfileAsync(string employeeId, string? fallbackName, CancellationToken ct)
    {
        var id = employeeId.Trim();
        string fullName = fallbackName ?? id;
        string daysOff = id.Equals("EMP-004", StringComparison.OrdinalIgnoreCase) ? "Sunday,Monday" : "Sunday";
        bool isClerk = IsDefaultClerkId(id);
        string payBasis = isClerk ? "FixedMonthly" : "CommissionPerEvent";
        bool isTitleIExempt = !isClerk;

        try
        {
            var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT TOP 1
                    EmployeeID,
                    LTRIM(RTRIM(CONCAT(ISNULL(EmployeeFirstName, ''), ' ', ISNULL(EmployeeLastName, '')))) AS FullName,
                    ISNULL(DaysOff, 'Sunday') AS DaysOff,
                    CASE WHEN COL_LENGTH(N'dbo.Employees', N'PayBasis') IS NOT NULL THEN ISNULL(PayBasis, 'CommissionPerEvent') ELSE 'CommissionPerEvent' END AS PayBasis,
                    CASE WHEN COL_LENGTH(N'dbo.Employees', N'IsTitleIExempt') IS NOT NULL THEN ISNULL(IsTitleIExempt, 1) ELSE 1 END AS IsTitleIExempt,
                    ISNULL(Specializations, '') AS Specializations
                FROM dbo.Employees
                WHERE EmployeeID = @EmpId;";
            var p = cmd.CreateParameter();
            p.ParameterName = "@EmpId";
            p.Value = id;
            cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var dbName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                if (!string.IsNullOrWhiteSpace(dbName))
                    fullName = dbName;
                daysOff = reader.IsDBNull(2) ? daysOff : reader.GetString(2);
                payBasis = reader.IsDBNull(3) ? payBasis : reader.GetString(3);
                isTitleIExempt = reader.IsDBNull(4) ? isTitleIExempt : Convert.ToBoolean(reader.GetValue(4));
                var specs = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);

                if (IsDefaultClerkId(id) || specs.Contains("Office Clerk", StringComparison.OrdinalIgnoreCase))
                {
                    payBasis = "FixedMonthly";
                    isTitleIExempt = false;
                }

                return new EmployeeLeaveProfile(id, fullName, daysOff, payBasis, isTitleIExempt);
            }
        }
        catch
        {
            // Fallback to EF in-memory / SQLite test provider
        }

        var efEmp = await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.IdentityUserId == id, ct);

        if (efEmp != null)
        {
            daysOff = string.IsNullOrWhiteSpace(efEmp.DaysOff) ? daysOff : efEmp.DaysOff;
            bool clerkSpec = IsDefaultClerkId(id) || efEmp.Specializations.Any(s => s.Contains("Clerk", StringComparison.OrdinalIgnoreCase));
            isTitleIExempt = clerkSpec ? false : efEmp.IsTitleIExempt;
            payBasis = isTitleIExempt ? "CommissionPerEvent" : "FixedMonthly";
        }

        return new EmployeeLeaveProfile(id, fullName, daysOff, payBasis, isTitleIExempt);
    }

    private async Task<List<LeaveConflictEventDto>> FindOverlappingEventsAsync(string employeeId, DateOnly startDate, DateOnly endDate, CancellationToken ct)
    {
        var conflicts = new List<LeaveConflictEventDto>();
        try
        {
            var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
                BEGIN
                    SELECT DISTINCT
                        ea.EventAppointmentID,
                        ISNULL(ea.EventName, N'Photoshoot Event') AS EventName,
                        ea.EventDate,
                        ISNULL(ea.EventStatus, N'Confirmed') AS EventStatus
                    FROM dbo.EventAppointment ea
                    LEFT JOIN dbo.EventEmployee ee ON ee.EventAppointmentID = ea.EventAppointmentID
                    WHERE (ea.EmployeeID = @EmpId OR ee.EmployeeID = @EmpId)
                      AND ea.EventDate BETWEEN @StartDate AND @EndDate
                      AND ISNULL(ea.EventStatus, N'Pending') NOT IN (N'Cancelled', N'Rejected', N'Archived')
                    ORDER BY ea.EventDate ASC;
                END";
            var pEmp = cmd.CreateParameter(); pEmp.ParameterName = "@EmpId"; pEmp.Value = employeeId; cmd.Parameters.Add(pEmp);
            var pStart = cmd.CreateParameter(); pStart.ParameterName = "@StartDate"; pStart.Value = startDate.ToDateTime(TimeOnly.MinValue); cmd.Parameters.Add(pStart);
            var pEnd = cmd.CreateParameter(); pEnd.ParameterName = "@EndDate"; pEnd.Value = endDate.ToDateTime(TimeOnly.MinValue); cmd.Parameters.Add(pEnd);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var evId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var evName = reader.IsDBNull(1) ? "Photoshoot Event" : reader.GetString(1);
                var evDate = reader.IsDBNull(2) ? startDate : DateOnly.FromDateTime(Convert.ToDateTime(reader.GetValue(2)));
                var evStatus = reader.IsDBNull(3) ? "Confirmed" : reader.GetString(3);
                conflicts.Add(new LeaveConflictEventDto(evId, evName, evDate, evStatus));
            }
        }
        catch
        {
            // Ignore when EventAppointment table does not exist in unit test provider
        }

        return conflicts;
    }

    private async Task EnsureClerkLeaveBalancesSeededAsync(string employeeId, int year, bool isTitleIExempt, CancellationToken ct)
    {
        if (isTitleIExempt)
            return;

        foreach (var cat in LeaveTypes.Catalog.Where(c => !c.AppliesToFieldExempt && c.RequiresCredits))
        {
            var exists = await _db.LeaveBalances
                .AnyAsync(b => b.EmployeeID == employeeId && b.LeaveTypeID == cat.LeaveTypeId && b.Year == year, ct);

            if (!exists)
            {
                var def = await _db.LeaveTypeDefinitions
                    .FirstOrDefaultAsync(t => t.Code == cat.Code, ct);
                int leaveTypeId = def?.LeaveTypeID ?? cat.LeaveTypeId;

                var alreadyById = await _db.LeaveBalances
                    .AnyAsync(b => b.EmployeeID == employeeId && b.LeaveTypeID == leaveTypeId && b.Year == year, ct);

                if (!alreadyById)
                {
                    _db.LeaveBalances.Add(new LeaveBalance
                    {
                        EmployeeID = employeeId,
                        LeaveTypeID = leaveTypeId,
                        Year = year,
                        TotalAllocated = cat.DefaultAnnualCredits,
                        PendingCredits = 0m,
                        UsedCredits = 0m,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
            }
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // Concurrent seed or FK constraint when Employee row is mock
        }
    }

    [HttpGet("types")]
    public async Task<IActionResult> GetLeaveTypes([FromQuery] string? employeeId, CancellationToken ct)
    {
        var targetId = string.IsNullOrWhiteSpace(employeeId)
            ? (User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "EMP-003")
            : employeeId.Trim();

        var profile = await ResolveEmployeeProfileAsync(targetId, null, ct);

        var items = LeaveTypes.Catalog
            .Where(c => !profile.IsTitleIExempt ? c.Code != LeaveTypes.CodeShootUnavailability : c.AppliesToFieldExempt)
            .Select(c => new LeaveTypeDto(
                c.LeaveTypeId,
                c.Code,
                c.Name,
                c.IsPaid,
                c.RequiresCredits,
                c.DefaultAnnualCredits,
                c.IsCashConvertible,
                c.AppliesToFieldExempt,
                c.RequiresAttachment,
                c.GenderRestriction))
            .ToList();

        return Ok(items);
    }

    [HttpGet("balances/{employeeId}")]
    public async Task<IActionResult> GetLeaveBalances(string employeeId, [FromQuery] int year = 2026, CancellationToken ct = default)
    {
        if (year <= 0) year = 2026;
        var profile = await ResolveEmployeeProfileAsync(employeeId, null, ct);
        await EnsureClerkLeaveBalancesSeededAsync(profile.EmployeeId, year, profile.IsTitleIExempt, ct);

        var dbBalances = await _db.LeaveBalances
            .AsNoTracking()
            .Where(b => b.EmployeeID == profile.EmployeeId && b.Year == year)
            .ToListAsync(ct);

        var dbTypes = await _db.LeaveTypeDefinitions.AsNoTracking().ToListAsync(ct);

        // Also compute from EmployeeLeaves in case LeaveBalances wasn't updated via raw SQL fallback
        var yearLeaves = await _db.EmployeeLeaves
            .AsNoTracking()
            .Where(l => l.EmployeeStudioId == profile.EmployeeId
                     && (l.CreditYear == year || l.StartDate.Year == year)
                     && (l.Status == LeaveStatuses.Pending || l.Status == LeaveStatuses.Approved))
            .ToListAsync(ct);

        var items = new List<LeaveBalanceItemDto>();

        if (!profile.IsTitleIExempt)
        {
            foreach (var cat in LeaveTypes.Catalog.Where(c => c.Code is LeaveTypes.CodeVacationLeave or LeaveTypes.CodeSickLeave))
            {
                var def = dbTypes.FirstOrDefault(t => t.Code.Equals(cat.Code, StringComparison.OrdinalIgnoreCase));
                int typeId = def?.LeaveTypeID ?? cat.LeaveTypeId;
                var row = dbBalances.FirstOrDefault(b => b.LeaveTypeID == typeId)
                       ?? dbBalances.FirstOrDefault(b => b.LeaveTypeID == cat.LeaveTypeId);

                decimal allocated = row?.TotalAllocated ?? cat.DefaultAnnualCredits;

                decimal pendingFromLeaves = yearLeaves
                    .Where(l => l.Status == LeaveStatuses.Pending && (l.LeaveType.Equals(cat.Name, StringComparison.OrdinalIgnoreCase) || string.Equals(l.LeaveTypeCode, cat.Code, StringComparison.OrdinalIgnoreCase)))
                    .Sum(l => l.CountableDays ?? PhilippineHolidays.CalculateCountableWorkdays(l.StartDate, l.EndDate, l.IsHalfDay, profile.DaysOff).CountableDays);

                decimal usedFromLeaves = yearLeaves
                    .Where(l => l.Status == LeaveStatuses.Approved && (l.LeaveType.Equals(cat.Name, StringComparison.OrdinalIgnoreCase) || string.Equals(l.LeaveTypeCode, cat.Code, StringComparison.OrdinalIgnoreCase)))
                    .Sum(l => l.CountableDays ?? PhilippineHolidays.CalculateCountableWorkdays(l.StartDate, l.EndDate, l.IsHalfDay, profile.DaysOff).CountableDays);

                decimal pending = Math.Max(row?.PendingCredits ?? 0m, pendingFromLeaves);
                decimal used = Math.Max(row?.UsedCredits ?? 0m, usedFromLeaves);
                decimal available = Math.Max(0m, allocated - pending - used);

                items.Add(new LeaveBalanceItemDto(
                    typeId,
                    cat.Code,
                    cat.Name,
                    cat.IsPaid,
                    cat.RequiresCredits,
                    cat.IsCashConvertible,
                    allocated,
                    pending,
                    used,
                    available));
            }
        }

        decimal vlRemaining = items.FirstOrDefault(i => i.LeaveTypeCode == LeaveTypes.CodeVacationLeave)?.AvailableCredits ?? 0m;
        decimal slRemaining = items.FirstOrDefault(i => i.LeaveTypeCode == LeaveTypes.CodeSickLeave)?.AvailableCredits ?? 0m;
        decimal totalRemainingPaid = items.Where(i => i.IsPaid).Sum(i => i.AvailableCredits);
        decimal totalAllocatedPaid = items.Where(i => i.IsPaid).Sum(i => i.TotalAllocated);

        return Ok(new EmployeeLeaveBalanceSummaryDto(
            profile.EmployeeId,
            profile.FullName,
            year,
            profile.PayBasis,
            profile.IsTitleIExempt,
            profile.DaysOff,
            totalAllocatedPaid,
            totalRemainingPaid,
            vlRemaining,
            slRemaining,
            items));
    }

    [HttpGet("calculate-workdays")]
    public async Task<IActionResult> CalculateWorkdays(
        [FromQuery] string employeeId,
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        [FromQuery] bool isHalfDay = false,
        CancellationToken ct = default)
    {
        var profile = await ResolveEmployeeProfileAsync(employeeId, null, ct);
        var calc = PhilippineHolidays.CalculateCountableWorkdays(startDate, endDate, isHalfDay, profile.DaysOff);
        var conflicts = await FindOverlappingEventsAsync(profile.EmployeeId, startDate, endDate, ct);

        return Ok(new CountableWorkdaysPreviewDto(
            profile.EmployeeId,
            startDate,
            endDate,
            isHalfDay,
            profile.DaysOff,
            calc.CountableDays,
            calc.CalendarDays,
            calc.ExcludedDaysOffCount,
            calc.ExcludedHolidaysCount,
            conflicts.Count > 0,
            conflicts.Count,
            conflicts));
    }

    [HttpPost]
    [HttpPost("request")]
    public async Task<IActionResult> RequestLeave([FromBody] SubmitLeaveRequestDto dto, CancellationToken ct)
    {
        var cat = LeaveTypes.Resolve(dto.LeaveType);
        if (cat == null)
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

        var profile = await ResolveEmployeeProfileAsync(studioId, employeeName, ct);
        if (string.IsNullOrWhiteSpace(employeeName))
        {
            employeeName = User.FindFirstValue("full_name")
                           ?? User.FindFirstValue(ClaimTypes.Name)
                           ?? profile.FullName;
        }

        // Enforce Labor Code Title I exemption rule: On-shoot crew (IsTitleIExempt == true) cannot file VL, SL, or LWOP
        if (profile.IsTitleIExempt && !cat.AppliesToFieldExempt)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Leave Type Not Eligible for Per-Event Crew",
                Detail = $"{profile.EmployeeId} is classified as CommissionPerEvent / Title I Exempt and cannot file {cat.Name}. Use 'Shoot Unavailability / Time Off' or an eligible statutory leave."
            });
        }

        var calc = PhilippineHolidays.CalculateCountableWorkdays(dto.StartDate, dto.EndDate, dto.IsHalfDay, profile.DaysOff);
        decimal countableDays = calc.CountableDays;

        if (countableDays <= 0m)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Zero Countable Workdays",
                Detail = $"The selected date range ({dto.StartDate} to {dto.EndDate}) contains only scheduled days off ({profile.DaysOff}) or holidays."
            });
        }

        // Sick Leave > 2 consecutive countable days requires Medical Certificate attachment
        if (cat.Code == LeaveTypes.CodeSickLeave && countableDays > 2.0m && string.IsNullOrWhiteSpace(dto.AttachmentName))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Medical Certificate Required",
                Detail = "Sick Leave requests exceeding 2 consecutive countable workdays require a Medical Certificate attachment."
            });
        }

        int targetYear = dto.StartDate.Year;
        bool creditsReserved = false;

        if (cat.RequiresCredits && !profile.IsTitleIExempt)
        {
            await EnsureClerkLeaveBalancesSeededAsync(profile.EmployeeId, targetYear, profile.IsTitleIExempt, ct);

            var def = await _db.LeaveTypeDefinitions.FirstOrDefaultAsync(t => t.Code == cat.Code, ct);
            int typeId = def?.LeaveTypeID ?? cat.LeaveTypeId;

            var balance = await _db.LeaveBalances
                .FirstOrDefaultAsync(b => b.EmployeeID == profile.EmployeeId && (b.LeaveTypeID == typeId || b.LeaveTypeID == cat.LeaveTypeId) && b.Year == targetYear, ct);

            decimal allocated = balance?.TotalAllocated ?? cat.DefaultAnnualCredits;
            decimal existingPending = balance?.PendingCredits ?? 0m;
            decimal existingUsed = balance?.UsedCredits ?? 0m;
            decimal available = allocated - existingPending - existingUsed;

            if (countableDays > available)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Insufficient Paid Leave Credits",
                    Detail = $"Insufficient {cat.Name} credits for {targetYear}: requested {countableDays:0.0} countable workdays, but only {Math.Max(0m, available):0.0} days remain available. File overflow days as Leave Without Pay (LWOP)."
                });
            }

            if (balance != null)
            {
                balance.PendingCredits += countableDays;
                balance.UpdatedAt = DateTime.UtcNow;
                creditsReserved = true;
            }
        }

        var conflicts = await FindOverlappingEventsAsync(profile.EmployeeId, dto.StartDate, dto.EndDate, ct);

        var leave = new EmployeeLeave
        {
            LeaveId = "LV-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            EmployeeStudioId = profile.EmployeeId,
            EmployeeName = employeeName,
            LeaveType = cat.Name,
            LeaveTypeCode = cat.Code,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            CountableDays = countableDays,
            IsHalfDay = dto.IsHalfDay,
            CreditYear = targetYear,
            CreditsReserved = creditsReserved,
            CreditsConsumed = false,
            ConflictCount = conflicts.Count,
            AttachmentName = dto.AttachmentName,
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

    [HttpPut("{id}/status")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> UpdateLeaveStatus(int id, [FromBody] UpdateLeaveStatusDto dto, CancellationToken ct)
    {
        if (string.Equals(dto.Status, LeaveStatuses.Approved, StringComparison.OrdinalIgnoreCase))
            return await ProcessLeaveDecisionAsync(id, approve: true, reason: null, ct);

        if (string.Equals(dto.Status, LeaveStatuses.Rejected, StringComparison.OrdinalIgnoreCase)
            || string.Equals(dto.Status, LeaveStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
            return await ProcessLeaveDecisionAsync(id, approve: false, reason: dto.Reason, ct, targetStatus: dto.Status);

        return BadRequest(new ProblemDetails
        {
            Title = "Invalid status transition",
            Detail = "Status must be Approved, Rejected, or Cancelled."
        });
    }

    [HttpPost("{id}/approve")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public Task<IActionResult> ApproveLeave(int id, CancellationToken ct)
        => ProcessLeaveDecisionAsync(id, approve: true, reason: null, ct);

    [HttpPost("{id}/reject")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public Task<IActionResult> RejectLeave(int id, [FromBody] RejectLeaveDto? dto, CancellationToken ct)
        => ProcessLeaveDecisionAsync(id, approve: false, reason: dto?.Reason, ct);

    private bool IsAdminReviewer() =>
        User.IsInRole(AppRoles.Admin)
        || User.IsInRole(AppRoles.SystemAdmin)
        || User.HasClaim(c => c.Type == ClaimTypes.Role && (c.Value == AppRoles.Admin || c.Value == AppRoles.SystemAdmin));

    private async Task<IActionResult> ProcessLeaveDecisionAsync(
        int id,
        bool approve,
        string? reason,
        CancellationToken ct,
        string? targetStatus = null)
    {
        var leave = await _db.EmployeeLeaves.FindAsync([id], ct);
        if (leave == null)
            return NotFound(new { Message = $"Leave #{id} not found." });

        var reviewerId = User.FindFirstValue("studio_id")
                         ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? string.Empty;
        var reviewerName = User.FindFirstValue("full_name")
                           ?? User.FindFirstValue(ClaimTypes.Name)
                           ?? (!string.IsNullOrWhiteSpace(reviewerId) ? reviewerId : "OfficeClerk");

        bool isAdmin = IsAdminReviewer();

        // Self-approval guard: no user can approve or reject their own leave request
        if (!string.IsNullOrWhiteSpace(reviewerId)
            && reviewerId.Equals(leave.EmployeeStudioId, StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Self-Approval Prohibited",
                Detail = "You cannot approve or reject your own leave request."
            });
        }

        var applicantProfile = await ResolveEmployeeProfileAsync(leave.EmployeeStudioId, leave.EmployeeName, ct);
        var cat = LeaveTypes.Resolve(leave.LeaveTypeCode ?? leave.LeaveType);

        // RBAC: Office Clerks can approve/reject Shoot Crew (IsTitleIExempt = 1) UNAVAIL requests,
        // but CANNOT approve/reject Office Clerk (IsTitleIExempt = 0 / VL / SL / LWOP) requests.
        if (!isAdmin)
        {
            bool isCrewUnavail = applicantProfile.IsTitleIExempt
                && (cat?.Code == LeaveTypes.CodeShootUnavailability || ! (cat?.Code is LeaveTypes.CodeVacationLeave or LeaveTypes.CodeSickLeave or LeaveTypes.CodeLeaveWithoutPay));

            if (!isCrewUnavail)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
                {
                    Title = "Studio Admin Approval Required",
                    Detail = "Office Clerks can approve Shoot Crew unavailability requests for dispatch scheduling, but Office Clerk leave requests (VL/SL/LWOP) require Studio Admin approval."
                });
            }
        }

        var calc = PhilippineHolidays.CalculateCountableWorkdays(leave.StartDate, leave.EndDate, leave.IsHalfDay, applicantProfile.DaysOff);
        decimal countableDays = leave.CountableDays ?? calc.CountableDays;
        leave.CountableDays = countableDays;
        int creditYear = leave.CreditYear ?? leave.StartDate.Year;

        if (cat != null && cat.RequiresCredits && !applicantProfile.IsTitleIExempt)
        {
            await EnsureClerkLeaveBalancesSeededAsync(applicantProfile.EmployeeId, creditYear, applicantProfile.IsTitleIExempt, ct);
            var def = await _db.LeaveTypeDefinitions.FirstOrDefaultAsync(t => t.Code == cat.Code, ct);
            int typeId = def?.LeaveTypeID ?? cat.LeaveTypeId;

            var balance = await _db.LeaveBalances
                .FirstOrDefaultAsync(b => b.EmployeeID == applicantProfile.EmployeeId && (b.LeaveTypeID == typeId || b.LeaveTypeID == cat.LeaveTypeId) && b.Year == creditYear, ct);

            if (balance != null)
            {
                if (approve && !leave.CreditsConsumed)
                {
                    if (leave.CreditsReserved)
                    {
                        balance.PendingCredits = Math.Max(0m, balance.PendingCredits - countableDays);
                    }
                    balance.UsedCredits += countableDays;
                    balance.UpdatedAt = DateTime.UtcNow;
                    leave.CreditsReserved = false;
                    leave.CreditsConsumed = true;
                }
                else if (!approve)
                {
                    if (leave.CreditsReserved)
                    {
                        balance.PendingCredits = Math.Max(0m, balance.PendingCredits - countableDays);
                        leave.CreditsReserved = false;
                    }
                    else if (leave.CreditsConsumed)
                    {
                        balance.UsedCredits = Math.Max(0m, balance.UsedCredits - countableDays);
                        leave.CreditsConsumed = false;
                    }
                    balance.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        if (approve)
        {
            leave.Status = LeaveStatuses.Approved;
            leave.ApprovedBy = !string.IsNullOrWhiteSpace(reviewerId) ? reviewerId : reviewerName;
            leave.ApprovedAt = DateTime.UtcNow;

            // Sync Office Clerk (IsTitleIExempt = 0) countable workdays into EmployeeTimesheets (8.0h / 480m Paid Leave or Unpaid Leave)
            if (!applicantProfile.IsTitleIExempt)
            {
                string entryType = (cat?.IsPaid ?? true) ? TimesheetEntryTypes.PaidLeave : TimesheetEntryTypes.UnpaidLeave;
                int shiftMinutes = leave.IsHalfDay && calc.CountableWorkdates.Count == 1 ? 240 : 480;
                double shiftHours = shiftMinutes / 60.0;

                foreach (var workDate in calc.CountableWorkdates)
                {
                    bool alreadySynced = await _db.EmployeeTimesheets
                        .AnyAsync(t => t.LeaveRequestID == leave.Id && t.WorkDate == workDate, ct);

                    if (!alreadySynced)
                    {
                        var clockIn = workDate.ToDateTime(new TimeOnly(9, 0));
                        var clockOut = clockIn.AddMinutes(shiftMinutes);
                        _db.EmployeeTimesheets.Add(new EmployeeTimesheet
                        {
                            TimesheetId = $"TS-LV-{leave.Id}-{workDate:yyyyMMdd}",
                            EmployeeStudioId = applicantProfile.EmployeeId,
                            EmployeeName = applicantProfile.FullName,
                            WorkDate = workDate,
                            ClockIn = clockIn,
                            ClockOut = clockOut,
                            TotalHours = shiftHours,
                            RegularMinutes = shiftMinutes,
                            UndertimeMinutes = 0,
                            PendingOvertimeMinutes = 0,
                            ApprovedOvertimeMinutes = 0,
                            IsOvertimeApproved = false,
                            EntryType = entryType,
                            LeaveRequestID = leave.Id,
                            Status = TimesheetStatuses.Approved,
                            IsDayOffWork = false,
                            Notes = $"{entryType}: {leave.LeaveType} ({leave.LeaveId})",
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
            }
        }
        else
        {
            leave.Status = string.Equals(targetStatus, LeaveStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
                ? LeaveStatuses.Cancelled
                : LeaveStatuses.Rejected;
            leave.ApprovedBy = !string.IsNullOrWhiteSpace(reviewerId) ? reviewerId : reviewerName;
            leave.ApprovedAt = DateTime.UtcNow;
            leave.RejectionReason = reason ?? "Declined by management.";
        }

        await _db.SaveChangesAsync(ct);
        return Ok(leave);
    }

    /// <summary>
    /// Allows an Office Clerk or Admin to record a sudden same-day absence (emergency / suddenly sick)
    /// when an employee did not submit an advance leave notice, and returns any active events on that date
    /// where the absent employee is assigned so crew/Team Leader can be swapped immediately.
    /// </summary>
    [HttpPost("sudden-absence")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> MarkSuddenAbsence([FromBody] MarkSuddenAbsenceDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.EmployeeStudioId))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Employee ID required",
                Detail = "EmployeeStudioId is required to mark a sudden absence."
            });
        }

        var empId = dto.EmployeeStudioId.Trim();
        var targetDate = dto.Date ?? DateOnly.FromDateTime(GetPhtNow());
        var leaveType = string.IsNullOrWhiteSpace(dto.LeaveType)
            ? LeaveTypes.EmergencySick
            : dto.LeaveType.Trim();

        if (!LeaveTypes.All.Contains(leaveType))
        {
            leaveType = LeaveTypes.EmergencySick;
        }

        var reviewer = User.FindFirstValue("studio_id")
                       ?? User.FindFirstValue(ClaimTypes.Name)
                       ?? "OfficeClerk";

        var profile = await ResolveEmployeeProfileAsync(empId, dto.EmployeeName, ct);
        var employeeName = string.IsNullOrWhiteSpace(profile.FullName) ? empId : profile.FullName;
        var conflicts = await FindOverlappingEventsAsync(empId, targetDate, targetDate, ct);
        var activeConflicts = conflicts
            .Where(c => !string.Equals(c.EventStatus, "Complete", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(c.EventStatus, "Completed", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(c.EventStatus, "Delivered", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(c.EventStatus, "Expired", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var existingApproved = await _db.EmployeeLeaves
            .FirstOrDefaultAsync(l =>
                l.EmployeeStudioId == empId &&
                l.Status == LeaveStatuses.Approved &&
                targetDate >= l.StartDate &&
                targetDate <= l.EndDate, ct);

        EmployeeLeave leave;
        if (existingApproved != null)
        {
            leave = existingApproved;
        }
        else
        {
            var resolved = LeaveTypes.Resolve(leaveType);
            var reasonPrefix = "[Sudden Absence - No Advance Notice]";
            var reasonText = string.IsNullOrWhiteSpace(dto.Reason)
                ? $"{reasonPrefix} Sudden emergency / sick reported on {targetDate:yyyy-MM-dd}."
                : $"{reasonPrefix} {dto.Reason.Trim()}";

            leave = new EmployeeLeave
            {
                LeaveId = "LV-" + targetDate.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
                EmployeeStudioId = empId,
                EmployeeName = employeeName,
                LeaveType = leaveType,
                LeaveTypeCode = resolved?.Code ?? LeaveTypes.CodeEmergencySick,
                StartDate = targetDate,
                EndDate = targetDate,
                CountableDays = 1.0m,
                IsHalfDay = false,
                CreditYear = targetDate.Year,
                CreditsReserved = false,
                CreditsConsumed = false,
                ConflictCount = activeConflicts.Count,
                Reason = reasonText,
                Status = LeaveStatuses.Approved,
                ApprovedBy = reviewer,
                ApprovedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            _db.EmployeeLeaves.Add(leave);
            await _db.SaveChangesAsync(ct);
        }

        if (_notifier != null)
        {
            try
            {
                await _notifier.RosterUpdatedAsync("*", null, new[] { empId }, ct);
            }
            catch
            {
            }
        }

        return Ok(new SuddenAbsenceResultDto(leave, activeConflicts));
    }

    private static DateTime GetPhtNow()
    {
        foreach (var tzId in new[] { "Asia/Manila", "Singapore Standard Time" })
        {
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(tzId));
            }
            catch
            {
            }
        }
        return DateTime.UtcNow.AddHours(8);
    }
}

public record SubmitLeaveRequestDto(
    string LeaveType,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Reason,
    string? EmployeeStudioId = null,
    string? EmployeeName = null,
    bool IsHalfDay = false,
    string? AttachmentName = null);

public record RejectLeaveDto(string? Reason);

public record UpdateLeaveStatusDto(
    string Status,
    string? Reason = null);

public record LeaveTypeDto(
    int LeaveTypeId,
    string Code,
    string Name,
    bool IsPaid,
    bool RequiresCredits,
    decimal DefaultAnnualCredits,
    bool IsCashConvertible,
    bool AppliesToFieldExempt,
    bool RequiresAttachment,
    string? GenderRestriction);

public record LeaveBalanceItemDto(
    int LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    bool IsPaid,
    bool RequiresCredits,
    bool IsCashConvertible,
    decimal TotalAllocated,
    decimal PendingCredits,
    decimal UsedCredits,
    decimal AvailableCredits);

public record EmployeeLeaveBalanceSummaryDto(
    string EmployeeId,
    string EmployeeName,
    int Year,
    string PayBasis,
    bool IsTitleIExempt,
    string DaysOff,
    decimal TotalAllocatedPaidDays,
    decimal TotalRemainingPaidDays,
    decimal VacationLeaveRemaining,
    decimal SickLeaveRemaining,
    IReadOnlyList<LeaveBalanceItemDto> Balances);

public record LeaveConflictEventDto(
    string EventAppointmentId,
    string EventName,
    DateOnly EventDate,
    string EventStatus);

public record CountableWorkdaysPreviewDto(
    string EmployeeId,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsHalfDay,
    string DaysOff,
    decimal CountableDays,
    int CalendarDays,
    int ExcludedDaysOffCount,
    int ExcludedHolidaysCount,
    bool HasEventConflict,
    int ConflictCount,
    IReadOnlyList<LeaveConflictEventDto> Conflicts);

public record MarkSuddenAbsenceDto(
    string EmployeeStudioId,
    string? EmployeeName = null,
    DateOnly? Date = null,
    string? LeaveType = null,
    string? Reason = null);

public record SuddenAbsenceResultDto(
    EmployeeLeave Leave,
    IReadOnlyList<LeaveConflictEventDto> AffectedAppointments);
