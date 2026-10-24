<<<<<<< HEAD
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
    public List<string> Specializations { get; set; } = new();
    public string Status { get; set; } = "Active";

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
    string Password);

public record SaveEmployeeResult(bool Success, string? Error = null);

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

    // Step 2: Specializations
    public List<string> Specializations { get; set; } = new();

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
=======
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
    public List<string> Specializations { get; set; } = new();
    public string Status { get; set; } = "Active";

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
    string Password);

public record SaveEmployeeResult(bool Success, string? Error = null);

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

    // Step 2: Specializations
    public List<string> Specializations { get; set; } = new();

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
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
