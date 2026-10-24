using System.ComponentModel.DataAnnotations;

namespace PhotoAevumStudio.Web.Models;

/// <summary>
/// Display view model for an Administrator account.
/// </summary>
public class AdminViewModel
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = "Admin";
    public bool IsSuperAdmin { get; set; }
    public string Status { get; set; } = "Active";

    public string FullName =>
        string.Join(" ", new[] { FirstName, MiddleName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string Initials
    {
        get
        {
            var first = string.IsNullOrWhiteSpace(FirstName) ? "?" : FirstName[..1].ToUpperInvariant();
            var last = string.IsNullOrWhiteSpace(LastName) ? "" : LastName[..1].ToUpperInvariant();
            return $"{first}{last}";
        }
    }
}

/// <summary>
/// Model for the wizard modal adding a new administrator.
/// </summary>
public class AddAdminWizardModel
{
    [Required(ErrorMessage = "First Name is required.")]
    [StringLength(100, ErrorMessage = "First Name cannot exceed 100 characters.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last Name is required.")]
    [StringLength(100, ErrorMessage = "Last Name cannot exceed 100 characters.")]
    public string LastName { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Middle Name cannot exceed 100 characters.")]
    public string? MiddleName { get; set; }

    [Required(ErrorMessage = "Email Address is required.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [StringLength(255, ErrorMessage = "Email cannot exceed 255 characters.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Role selection is required.")]
    public string Role { get; set; } = "Admin";

    /// <summary>
    /// System-assigned read-only ID (e.g. SADM-### or ADM-###).
    /// strictly read-only and assigned by the system, not editable by user.
    /// </summary>
    public string GeneratedId { get; set; } = string.Empty;

    /// <summary>
    /// System-assigned read-only temporary password.
    /// </summary>
    public string TemporaryPassword { get; set; } = string.Empty;

    public string FullName =>
        string.Join(" ", new[] { FirstName, MiddleName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public bool IsSystemAdmin =>
        Role.Equals("SystemAdmin", StringComparison.OrdinalIgnoreCase)
        || Role.Equals("System Admin", StringComparison.OrdinalIgnoreCase)
        || Role.Equals("Super Admin", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Model for updating an existing administrator's details.
/// </summary>
public class EditAdminModel
{
    public string Id { get; set; } = string.Empty;
    public string Role { get; set; } = "Admin";
    public bool IsSuperAdmin { get; set; }

    [Required(ErrorMessage = "First Name is required.")]
    [StringLength(100, ErrorMessage = "First Name cannot exceed 100 characters.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last Name is required.")]
    [StringLength(100, ErrorMessage = "Last Name cannot exceed 100 characters.")]
    public string LastName { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Middle Name cannot exceed 100 characters.")]
    public string? MiddleName { get; set; }

    [Required(ErrorMessage = "Email Address is required.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [StringLength(255, ErrorMessage = "Email cannot exceed 255 characters.")]
    public string Email { get; set; } = string.Empty;

    public string FullName =>
        string.Join(" ", new[] { FirstName, MiddleName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public record SaveAdminResult(bool Success, string? ErrorMessage = null, AdminViewModel? Admin = null);
