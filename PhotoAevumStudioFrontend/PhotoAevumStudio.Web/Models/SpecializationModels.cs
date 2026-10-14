using System.ComponentModel.DataAnnotations;

namespace PhotoAevumStudio.Web.Models;

/// <summary>
/// DTO representing an employee and their assigned specializations in the management dashboard table.
/// </summary>
public class EmployeeSpecializationDto
{
    public int Id { get; set; }
    public string EmployeeId { get; set; } = string.Empty; // e.g. "PA-001"
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = "Employee";
    public string Status { get; set; } = "Active";
    public List<string> Specializations { get; set; } = new();

    // Helper computed properties
    public string FullName => string.Join(' ', new[] { FirstName, MiddleName, LastName }
        .Where(p => !string.IsNullOrWhiteSpace(p)));

    public string Initials
    {
        get
        {
            var parts = FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "PA";
            if (parts.Length == 1) return parts[0][..1].ToUpperInvariant();
            return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
        }
    }

    public bool IsActive => Status.Equals("Active", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Master Specialization lookup record. Corresponds to the master lookup table in the database.
/// </summary>
public class SpecializationLookupItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Department { get; set; } = "Photography";
    public string ColorTag { get; set; } = "Blue";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Form model for the "Add New Specialization" wizard.
/// Uses DataAnnotations for validation across the multi-step form.
/// </summary>
public class SpecializationModel
{
    [Required(ErrorMessage = "Specialization name is required.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "Specialization name must be between 2 and 80 characters.")]
    [RegularExpression(@"^[a-zA-Z0-9\s\-/&.,']+$", ErrorMessage = "Specialization name contains invalid characters.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(300, ErrorMessage = "Description cannot exceed 300 characters.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Please select a department category.")]
    public string Department { get; set; } = "Photography";

    [Required(ErrorMessage = "Please select a UI color tag.")]
    public string ColorTag { get; set; } = "Blue";
}

/// <summary>
/// Helper to map ColorTag strings to Tailwind CSS chip classes.
/// </summary>
public static class SpecializationColorHelper
{
    public static (string BadgeClass, string DotClass, string BorderClass) GetClasses(string? colorTag)
    {
        return (colorTag?.ToLowerInvariant()) switch
        {
            "purple" => (
                "bg-purple-50 text-purple-700 border-purple-200 ring-purple-500/10",
                "bg-purple-500",
                "border-purple-500 ring-2 ring-purple-200"
            ),
            "green" or "emerald" => (
                "bg-emerald-50 text-emerald-700 border-emerald-200 ring-emerald-500/10",
                "bg-emerald-500",
                "border-emerald-500 ring-2 ring-emerald-200"
            ),
            "amber" or "yellow" => (
                "bg-amber-50 text-amber-800 border-amber-200 ring-amber-500/10",
                "bg-amber-500",
                "border-amber-500 ring-2 ring-amber-200"
            ),
            "rose" or "red" => (
                "bg-rose-50 text-rose-700 border-rose-200 ring-rose-500/10",
                "bg-rose-500",
                "border-rose-500 ring-2 ring-rose-200"
            ),
            "indigo" => (
                "bg-indigo-50 text-indigo-700 border-indigo-200 ring-indigo-500/10",
                "bg-indigo-500",
                "border-indigo-500 ring-2 ring-indigo-200"
            ),
            "cyan" => (
                "bg-cyan-50 text-cyan-700 border-cyan-200 ring-cyan-500/10",
                "bg-cyan-500",
                "border-cyan-500 ring-2 ring-cyan-200"
            ),
            _ => (
                "bg-blue-50 text-blue-700 border-blue-200 ring-blue-500/10",
                "bg-blue-500",
                "border-blue-500 ring-2 ring-blue-200"
            )
        };
    }
}
