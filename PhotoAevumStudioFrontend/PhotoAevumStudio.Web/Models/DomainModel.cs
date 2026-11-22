using System.ComponentModel.DataAnnotations;

namespace PhotoAevumStudio.Web.Models;

// ---------- Unified View Models ----------

public class AdminDashboardViewModel
{
    public IEnumerable<ScheduleViewModel> Schedules { get; set; } = new List<ScheduleViewModel>();
    public IEnumerable<EmployeeViewModel> Employees { get; set; } = new List<EmployeeViewModel>();
    public IEnumerable<ServiceViewModel> Services { get; set; } = new List<ServiceViewModel>();
    public IEnumerable<EquipmentViewModel> EquipmentList { get; set; } = new List<EquipmentViewModel>();
    public DashboardStatsViewModel Stats { get; set; } = new();
}

public class EmployeeViewModel
{
    public int Id { get; set; } // Database Primary Key
    public string EmployeeId { get; set; } = string.Empty; // e.g., PA-001
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? Address { get; set; }
    public DateTime? BirthDate { get; set; }
    private int? _birthYear;
    public int? BirthYear
    {
        get => BirthDate?.Year ?? _birthYear;
        set => _birthYear = value;
    }
    public string? DepartmentId { get; set; }
    private string? _department;
    public string? Department
    {
        get => _department;
        set => _department = value;
    }
    public string? DepartmentName
    {
        get => _department;
        set => _department = value;
    }
    public List<string> Specializations { get; set; } = new();
    public List<string> SelectedDaysOff { get; set; } = new() { "Sunday" };
    public string DaysOffString => SelectedDaysOff.Count > 0 ? string.Join(",", SelectedDaysOff) : "Sunday";
    public string Status { get; set; } = "Active";
    public string DaysOff { get; set; } = "Sunday";
    public string PayBasis { get; set; } = "CommissionPerEvent";
    public bool IsTitleIExempt { get; set; } = true;
    public bool IsOfficeClerk => !IsTitleIExempt
        || PayBasis.Equals("FixedMonthly", StringComparison.OrdinalIgnoreCase)
        || EmployeeId.Equals("EMP-003", StringComparison.OrdinalIgnoreCase)
        || EmployeeId.Equals("EMP-004", StringComparison.OrdinalIgnoreCase)
        || EmployeeId.StartsWith("CLK-", StringComparison.OrdinalIgnoreCase)
        || Specializations.Any(s => s.Contains("Office Clerk", StringComparison.OrdinalIgnoreCase))
        || Role.Contains("Clerk", StringComparison.OrdinalIgnoreCase);
    public bool IsClockedIn { get; set; }
    public DateTime? TodayClockIn { get; set; }
    public DateTime? TodayClockOut { get; set; }
    public double TodayHoursWorked { get; set; }
    public bool IsDayOffToday { get; set; }
    public bool IsOnLeaveToday { get; set; }
    public string? ActiveLeaveType { get; set; }

    // Computed properties to maintain consistency and prevent redundant DB columns
    public bool IsActive => Status.Equals("Active", StringComparison.OrdinalIgnoreCase);
    public string FullName => string.Join(' ', new[] { FirstName, MiddleName, LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));
    public string Initials => $"{FirstName?.FirstOrDefault()}{LastName?.FirstOrDefault()}".ToUpper();
}

public class ScheduleViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
}

public class ServiceViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Duration { get; set; }
}

public class EquipmentViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string AssignedEmployee { get; set; } = string.Empty;
    public string Status { get; set; } = "Available";
}

public class DashboardStatsViewModel
{
    public int AppointmentsToday { get; set; }
    public int ActiveEmployees { get; set; }
    public int OpenSlotsThisWeek { get; set; }
    public int BookedThisWeek { get; set; }
}

public record TimeSlotViewModel(TimeOnly Start, bool IsAvailable);

public class DayAvailabilityViewModel
{
    public DateOnly Date { get; set; }
    public List<TimeSlotViewModel> Slots { get; set; } = new();
    public int OpenCount => Slots.Count(s => s.IsAvailable);
    public int BookedCount => Slots.Count(s => !s.IsAvailable);
}

// ---------- Immutable DTOs & Form Models ----------

public record NewEmployeeRequest(
    string EmployeeId,
    string FirstName,
    string LastName,
    string? MiddleName,
    string Email,
    IReadOnlyList<string> Specializations,
    string Password,
    string DaysOff = "Sunday",
    string? Address = null,
    int? BirthYear = null,
    string? DepartmentId = null,
    DateTime? BirthDate = null);

public record SaveEmployeeResult(
    bool Success,
    string? Error = null,
    bool WelcomeEmailSent = true,
    bool HandbookAttached = true,
    string? EmailWarning = null);

public record RegistrationRequest(
    string FirstName,
    string LastName,
    string? MiddleName,
    string Email,
    string? Cellphone,
    string Password);

public class EmployeePersonalModel
{
    [Required(ErrorMessage = "Enter a first name.")]
    [StringLength(50)]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a last name.")]
    [StringLength(50)]
    public string LastName { get; set; } = string.Empty;

    [StringLength(50)]
    public string? MiddleName { get; set; }

    [Required(ErrorMessage = "Enter the employee's email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the employee's address.")]
    [StringLength(500)]
    public string? Address { get; set; }

    [Required(ErrorMessage = "Select the employee's birth date.")]
    public DateTime? BirthDate { get; set; }

    private int? _birthYear;
    public int? BirthYear
    {
        get => BirthDate?.Year ?? _birthYear;
        set => _birthYear = value;
    }

    public string? DepartmentId { get; set; }
}

public class EmployeeAccountModel
{
    [Required(ErrorMessage = "Assign an employee ID.")]
    [RegularExpression(@"^[A-Za-z0-9\-]{3,20}$", ErrorMessage = "Use 3 20 letters, numbers or hyphens.")]
    public string EmployeeId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Set a starting password.")]
    [MinLength(8, ErrorMessage = "Use at least 8 characters.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Include at least one letter and one number.")]
    public string Password { get; set; } = string.Empty;
}

public class VerificationModel
{
    [Required(ErrorMessage = "Enter the 6-digit code we emailed you.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "The code is 6 digits.")]
    public string Code { get; set; } = string.Empty;
}

public class EmployeeRegistrationModel : IValidatableObject
{
    // Step 1: Personal Information
    [Required(ErrorMessage = "First name is required.")]
    [StringLength(50, ErrorMessage = "First name cannot exceed 50 characters.")]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(50, ErrorMessage = "Last name cannot exceed 50 characters.")]
    [Display(Name = "Last Name")]
    public string LastName { get; set; } = string.Empty;

    [StringLength(50, ErrorMessage = "Middle name cannot exceed 50 characters.")]
    [Display(Name = "Middle Name")]
    public string? MiddleName { get; set; }

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [Display(Name = "Employee Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Home address is required.")]
    [StringLength(500, ErrorMessage = "Address cannot exceed 500 characters.")]
    [Display(Name = "Home Address")]
    public string? Address { get; set; }

    [Required(ErrorMessage = "Birth date is required.")]
    [Display(Name = "Birth Date")]
    public DateTime? BirthDate { get; set; }

    private int? _birthYear;
    [Range(1900, 2026, ErrorMessage = "Please enter a valid birth year.")]
    [Display(Name = "Birth Year")]
    public int? BirthYear
    {
        get => BirthDate?.Year ?? _birthYear;
        set
        {
            _birthYear = value;
            if (value.HasValue && !BirthDate.HasValue && value.Value is >= 1900 and <= 2100)
            {
                BirthDate = new DateTime(value.Value, 1, 1);
            }
        }
    }

    public string? DepartmentId { get; set; }
    private string? _regDeptName;
    public string? DepartmentName
    {
        get => _regDeptName;
        set => _regDeptName = value;
    }
    public string? Department
    {
        get => _regDeptName;
        set => _regDeptName = value;
    }

    // Step 2: Specializations
    public List<string> Specializations { get; set; } = new();
    public List<string> SelectedDaysOff { get; set; } = new() { "Sunday" };
    public string DaysOffString => SelectedDaysOff.Count > 0 ? string.Join(",", SelectedDaysOff) : "Sunday";

    public string FullName => string.Join(" ", new[] { FirstName, MiddleName, LastName }
        .Where(s => !string.IsNullOrWhiteSpace(s)));

    // Step 3: Overview & Credentials
    [Required(ErrorMessage = "Employee ID is required.")]
    [RegularExpression(@"^[A-Za-z0-9\-]{3,20}$", ErrorMessage = "Employee ID must be 3 to 20 letters, numbers, or hyphens.")]
    [Display(Name = "Employee ID")]
    public string EmployeeId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Starting password is required.")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Password must contain at least one letter and one number.")]
    [Display(Name = "Starting Password")]
    public string Password { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Specializations == null || Specializations.Count == 0)
        {
            yield return new ValidationResult(
                "At least one specialization is required.",
                new[] { nameof(Specializations) });
        }
    }
}

public class LeaveItemViewModel
{
    public int Id { get; set; }
    public string LeaveId { get; set; } = string.Empty;
    public string EmployeeStudioId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string LeaveType { get; set; } = "Sick Leave";
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
    public string Status { get; set; } = "Pending";
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class LeaveBalanceItemViewModel
{
    public int LeaveTypeId { get; set; }
    public string LeaveTypeCode { get; set; } = string.Empty;
    public string LeaveTypeName { get; set; } = string.Empty;
    public bool IsPaid { get; set; }
    public bool RequiresCredits { get; set; }
    public bool IsCashConvertible { get; set; }
    public decimal TotalAllocated { get; set; }
    public decimal PendingCredits { get; set; }
    public decimal UsedCredits { get; set; }
    public decimal AvailableCredits { get; set; }
}

public class EmployeeLeaveBalanceSummaryViewModel
{
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public int Year { get; set; } = 2026;
    public string PayBasis { get; set; } = "CommissionPerEvent";
    public bool IsTitleIExempt { get; set; } = true;
    public string DaysOff { get; set; } = "Sunday";
    public decimal TotalAllocatedPaidDays { get; set; }
    public decimal TotalRemainingPaidDays { get; set; }
    public decimal VacationLeaveRemaining { get; set; }
    public decimal SickLeaveRemaining { get; set; }
    public List<LeaveBalanceItemViewModel> Balances { get; set; } = new();
}

public class LeaveConflictEventViewModel
{
    public string EventAppointmentId { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public DateOnly EventDate { get; set; }
    public string EventStatus { get; set; } = string.Empty;
}

public class SuddenAbsenceResultViewModel
{
    public LeaveItemViewModel? Leave { get; set; }
    public List<LeaveConflictEventViewModel> AffectedAppointments { get; set; } = new();
}

public class CountableWorkdaysPreviewViewModel
{
    public string EmployeeId { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsHalfDay { get; set; }
    public string DaysOff { get; set; } = "Sunday";
    public decimal CountableDays { get; set; }
    public int CalendarDays { get; set; }
    public int ExcludedDaysOffCount { get; set; }
    public int ExcludedHolidaysCount { get; set; }
    public bool HasEventConflict { get; set; }
    public int ConflictCount { get; set; }
    public List<LeaveConflictEventViewModel> Conflicts { get; set; } = new();
}

public static class LeaveWorkdayHelper
{
    private static readonly (int Month, int Day)[] FixedPhHolidays =
    [
        (1, 1), (2, 25), (4, 9), (5, 1), (6, 12), (8, 21), (8, 31),
        (11, 1), (11, 2), (11, 30), (12, 8), (12, 24), (12, 25), (12, 30), (12, 31)
    ];

    private static readonly (int Year, int Month, int Day)[] MovablePhHolidays =
    [
        (2026, 2, 17), (2026, 4, 2), (2026, 4, 3), (2026, 4, 4), (2026, 8, 31)
    ];

    public static bool IsPhHoliday(DateOnly date)
        => MovablePhHolidays.Any(m => m.Year == date.Year && m.Month == date.Month && m.Day == date.Day)
        || FixedPhHolidays.Any(f => f.Month == date.Month && f.Day == date.Day);

    public static bool IsOfficeClerkEmployee(string? employeeId, IEnumerable<string>? specializations = null, string? role = null)
    {
        if (!string.IsNullOrWhiteSpace(employeeId))
        {
            var id = employeeId.Trim();
            if (id.Equals("EMP-003", StringComparison.OrdinalIgnoreCase)
                || id.Equals("EMP-004", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("CLK-", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        if (specializations != null && specializations.Any(s => s.Contains("Clerk", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (!string.IsNullOrWhiteSpace(role) && role.Contains("Clerk", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public static CountableWorkdaysPreviewViewModel CalculateCountableWorkdays(
        string employeeId,
        DateOnly startDate,
        DateOnly endDate,
        bool isHalfDay,
        string? employeeDaysOff)
    {
        var daysOff = string.IsNullOrWhiteSpace(employeeDaysOff)
            ? (employeeId.Equals("EMP-004", StringComparison.OrdinalIgnoreCase) ? "Sunday,Monday" : "Sunday")
            : employeeDaysOff;

        if (endDate < startDate)
        {
            return new CountableWorkdaysPreviewViewModel
            {
                EmployeeId = employeeId,
                StartDate = startDate,
                EndDate = endDate,
                IsHalfDay = isHalfDay,
                DaysOff = daysOff
            };
        }

        int calendarDays = 0;
        int excludedDaysOff = 0;
        int excludedHolidays = 0;
        int workdays = 0;

        for (var d = startDate; d <= endDate; d = d.AddDays(1))
        {
            calendarDays++;
            var dayName = d.DayOfWeek.ToString();
            if (daysOff.Contains(dayName, StringComparison.OrdinalIgnoreCase))
            {
                excludedDaysOff++;
                continue;
            }
            if (IsPhHoliday(d))
            {
                excludedHolidays++;
                continue;
            }
            workdays++;
        }

        decimal countable = workdays;
        if (isHalfDay && countable > 0m)
        {
            countable = startDate == endDate ? 0.5m : Math.Max(0.5m, countable - 0.5m);
        }

        return new CountableWorkdaysPreviewViewModel
        {
            EmployeeId = employeeId,
            StartDate = startDate,
            EndDate = endDate,
            IsHalfDay = isHalfDay,
            DaysOff = daysOff,
            CountableDays = countable,
            CalendarDays = calendarDays,
            ExcludedDaysOffCount = excludedDaysOff,
            ExcludedHolidaysCount = excludedHolidays
        };
    }
}

public class ClockStatusViewModel
{
    public string EmployeeStudioId { get; set; } = string.Empty;
    public bool IsClockedIn { get; set; }
    public DateTime? ClockIn { get; set; }
    public DateTime? ClockOut { get; set; }
    public double ElapsedHours { get; set; }
    public bool IsDayOffToday { get; set; }
    public bool IsOnLeaveToday { get; set; }
    public string? ActiveLeaveType { get; set; }
}

public class TodayAttendanceSummaryDto
{
    public string EmployeeStudioId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string DaysOff { get; set; } = "Sunday";
    public string PayBasis { get; set; } = "CommissionPerEvent";
    public bool IsTitleIExempt { get; set; } = true;
    public bool IsClockedIn { get; set; }
    public DateTime? ClockIn { get; set; }
    public DateTime? ClockOut { get; set; }
    public double TotalHours { get; set; }
    public int RegularMinutes { get; set; }
    public int UndertimeMinutes { get; set; }
    public int PendingOvertimeMinutes { get; set; }
    public int ApprovedOvertimeMinutes { get; set; }
    public bool IsOvertimeApproved { get; set; }
    public bool IsDayOffToday { get; set; }
    public bool IsOnLeaveToday { get; set; }
    public string? ActiveLeaveType { get; set; }
}

public class EmployeeTimesheetHistoryDto
{
    public int Id { get; set; }
    public string TimesheetId { get; set; } = string.Empty;
    public string EmployeeStudioId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateOnly WorkDate { get; set; }
    public DateTime ClockIn { get; set; }
    public DateTime? ClockOut { get; set; }
    public double TotalHours { get; set; }
    public int RegularMinutes { get; set; }
    public int UndertimeMinutes { get; set; }
    public int PendingOvertimeMinutes { get; set; }
    public int ApprovedOvertimeMinutes { get; set; }
    public bool IsOvertimeApproved { get; set; }
    public string? OvertimeApprovedBy { get; set; }
    public DateTime? OvertimeApprovedAt { get; set; }
    public string EntryType { get; set; } = "Work";
    public int? LeaveRequestID { get; set; }
    public string Status { get; set; } = "Open";
    public bool IsDayOffWork { get; set; }
    public string? Notes { get; set; }
    public string? AdjustedBy { get; set; }
    public string? AdjustmentReason { get; set; }
    public DateTime CreatedAt { get; set; }
}