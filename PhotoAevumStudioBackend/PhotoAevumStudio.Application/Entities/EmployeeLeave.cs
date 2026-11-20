namespace PhotoAevumStudio.Application.Entities;

public static class LeaveTypes
{
    public const string SickLeave           = "Sick Leave";
    public const string VacationLeave       = "Vacation Leave";
    public const string LeaveWithoutPay     = "Leave Without Pay";
    public const string ShootUnavailability = "Shoot Unavailability / Time Off";
    public const string MaternityLeave      = "Maternity Leave";
    public const string PaternityLeave      = "Paternity Leave";
    public const string SoloParentLeave     = "Solo Parent Leave";
    public const string VawcLeave           = "VAWC Leave";
    public const string EmergencySick       = "Emergency / Sudden Sick";

    public const string CodeVacationLeave       = "VL";
    public const string CodeSickLeave           = "SL";
    public const string CodeLeaveWithoutPay     = "LWOP";
    public const string CodeShootUnavailability = "UNAVAIL";
    public const string CodeMaternityLeave      = "MAT";
    public const string CodePaternityLeave      = "PAT";
    public const string CodeSoloParentLeave     = "SPL";
    public const string CodeVawcLeave           = "VAWC";
    public const string CodeEmergencySick       = "EMERG";

    public static readonly IReadOnlyList<string> All =
    [
        SickLeave,
        VacationLeave,
        LeaveWithoutPay,
        ShootUnavailability,
        MaternityLeave,
        PaternityLeave,
        SoloParentLeave,
        VawcLeave,
        EmergencySick
    ];

    public static readonly IReadOnlyList<LeaveTypeCatalogEntry> Catalog =
    [
        new(1, CodeVacationLeave,       VacationLeave,       IsPaid: true,  RequiresCredits: true,  DefaultAnnualCredits: 5.0m,   IsCashConvertible: true,  AppliesToFieldExempt: false, RequiresAttachment: false, GenderRestriction: null),
        new(2, CodeSickLeave,           SickLeave,           IsPaid: true,  RequiresCredits: true,  DefaultAnnualCredits: 5.0m,   IsCashConvertible: false, AppliesToFieldExempt: false, RequiresAttachment: true,  GenderRestriction: null),
        new(3, CodeLeaveWithoutPay,     LeaveWithoutPay,     IsPaid: false, RequiresCredits: false, DefaultAnnualCredits: 0.0m,   IsCashConvertible: false, AppliesToFieldExempt: false, RequiresAttachment: false, GenderRestriction: null),
        new(4, CodeShootUnavailability, ShootUnavailability, IsPaid: false, RequiresCredits: false, DefaultAnnualCredits: 0.0m,   IsCashConvertible: false, AppliesToFieldExempt: true,  RequiresAttachment: false, GenderRestriction: null),
        new(5, CodeMaternityLeave,      MaternityLeave,      IsPaid: true,  RequiresCredits: true,  DefaultAnnualCredits: 105.0m, IsCashConvertible: false, AppliesToFieldExempt: true,  RequiresAttachment: false, GenderRestriction: "Female"),
        new(6, CodePaternityLeave,      PaternityLeave,      IsPaid: true,  RequiresCredits: true,  DefaultAnnualCredits: 7.0m,   IsCashConvertible: false, AppliesToFieldExempt: true,  RequiresAttachment: false, GenderRestriction: "Male"),
        new(7, CodeSoloParentLeave,     SoloParentLeave,     IsPaid: true,  RequiresCredits: true,  DefaultAnnualCredits: 7.0m,   IsCashConvertible: false, AppliesToFieldExempt: true,  RequiresAttachment: false, GenderRestriction: null),
        new(8, CodeVawcLeave,           VawcLeave,           IsPaid: true,  RequiresCredits: true,  DefaultAnnualCredits: 10.0m,  IsCashConvertible: false, AppliesToFieldExempt: true,  RequiresAttachment: false, GenderRestriction: "Female"),
        new(9, CodeEmergencySick,       EmergencySick,       IsPaid: false, RequiresCredits: false, DefaultAnnualCredits: 0.0m,   IsCashConvertible: false, AppliesToFieldExempt: true,  RequiresAttachment: false, GenderRestriction: null),
    ];

    public static LeaveTypeCatalogEntry? Resolve(string? nameOrCode)
    {
        if (string.IsNullOrWhiteSpace(nameOrCode))
            return null;
        var trimmed = nameOrCode.Trim();
        return Catalog.FirstOrDefault(c =>
            c.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
            c.Code.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
            (trimmed.Contains("Unavailability", StringComparison.OrdinalIgnoreCase) && c.Code == CodeShootUnavailability));
    }
}

public sealed record LeaveTypeCatalogEntry(
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

public static class LeaveStatuses
{
    public const string Pending   = "Pending";
    public const string Approved  = "Approved";
    public const string Rejected  = "Rejected";
    public const string Cancelled = "Cancelled";
}

public class EmployeeLeave
{
    public int Id { get; set; }
    public string LeaveId { get; set; } = string.Empty;
    public string EmployeeStudioId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string LeaveType { get; set; } = LeaveTypes.SickLeave;
    public string? LeaveTypeCode { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal? CountableDays { get; set; }
    public bool IsHalfDay { get; set; }
    public int? CreditYear { get; set; }
    public bool CreditsReserved { get; set; }
    public bool CreditsConsumed { get; set; }
    public int ConflictCount { get; set; }
    public string? AttachmentName { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = LeaveStatuses.Pending;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class LeaveTypeDefinition
{
    public int LeaveTypeID { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsPaid { get; set; } = true;
    public bool RequiresCredits { get; set; } = true;
    public decimal DefaultAnnualCredits { get; set; }
    public bool IsCashConvertible { get; set; }
    public bool AppliesToFieldExempt { get; set; }
    public bool RequiresAttachment { get; set; }
    public string? GenderRestriction { get; set; }
}

public class LeaveBalance
{
    public int LeaveBalanceID { get; set; }
    public string EmployeeID { get; set; } = string.Empty;
    public int LeaveTypeID { get; set; }
    public int Year { get; set; } = 2026;
    public decimal TotalAllocated { get; set; }
    public decimal PendingCredits { get; set; }
    public decimal UsedCredits { get; set; }
    public decimal AvailableCredits => TotalAllocated - PendingCredits - UsedCredits;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class PhilippineHolidays
{
    // Standard Philippine Regular & Special Non-Working Holidays (month, day, name)
    private static readonly (int Month, int Day, string Name)[] FixedHolidays =
    [
        (1,  1,  "New Year's Day"),
        (2,  25, "EDSA People Power Revolution Anniversary"),
        (4,  9,  "Araw ng Kagitingan"),
        (5,  1,  "Labor Day"),
        (6,  12, "Independence Day"),
        (8,  21, "Ninoy Aquino Day"),
        (8,  31, "National Heroes Day"),
        (11, 1,  "All Saints' Day"),
        (11, 2,  "All Souls' Day"),
        (11, 30, "Bonifacio Day"),
        (12, 8,  "Feast of the Immaculate Conception"),
        (12, 24, "Christmas Eve"),
        (12, 25, "Christmas Day"),
        (12, 30, "Rizal Day"),
        (12, 31, "Last Day of the Year"),
    ];

    // 2026 Movable Regular / Special Holidays (Proclamation for 2026)
    private static readonly (int Year, int Month, int Day, string Name)[] MovableHolidays =
    [
        (2026, 2, 17, "Chinese New Year"),
        (2026, 4, 2,  "Maundy Thursday"),
        (2026, 4, 3,  "Good Friday"),
        (2026, 4, 4,  "Black Saturday"),
        (2026, 8, 31, "National Heroes Day"),
    ];

    public static bool IsHoliday(DateOnly date, out string? holidayName)
    {
        foreach (var m in MovableHolidays)
        {
            if (m.Year == date.Year && m.Month == date.Month && m.Day == date.Day)
            {
                holidayName = m.Name;
                return true;
            }
        }
        foreach (var f in FixedHolidays)
        {
            if (f.Month == date.Month && f.Day == date.Day)
            {
                holidayName = f.Name;
                return true;
            }
        }
        holidayName = null;
        return false;
    }

    public static CountableWorkdaysResult CalculateCountableWorkdays(
        DateOnly startDate,
        DateOnly endDate,
        bool isHalfDay,
        string? employeeDaysOff)
    {
        if (endDate < startDate)
            return new CountableWorkdaysResult(0m, 0, 0, 0, []);

        var daysOffRaw = string.IsNullOrWhiteSpace(employeeDaysOff)
            ? "Sunday"
            : employeeDaysOff;

        int calendarDays = 0;
        int excludedDaysOff = 0;
        int excludedHolidays = 0;
        var workdates = new List<DateOnly>();

        for (var d = startDate; d <= endDate; d = d.AddDays(1))
        {
            calendarDays++;
            var dayName = d.DayOfWeek.ToString();
            bool isDayOff = daysOffRaw.Contains(dayName, StringComparison.OrdinalIgnoreCase);
            if (isDayOff)
            {
                excludedDaysOff++;
                continue;
            }
            if (IsHoliday(d, out _))
            {
                excludedHolidays++;
                continue;
            }
            workdates.Add(d);
        }

        decimal countable = workdates.Count;
        if (isHalfDay && countable > 0)
        {
            countable = startDate == endDate ? 0.5m : Math.Max(0.5m, countable - 0.5m);
        }

        return new CountableWorkdaysResult(countable, calendarDays, excludedDaysOff, excludedHolidays, workdates);
    }
}

public sealed record CountableWorkdaysResult(
    decimal CountableDays,
    int CalendarDays,
    int ExcludedDaysOffCount,
    int ExcludedHolidaysCount,
    IReadOnlyList<DateOnly> CountableWorkdates);
