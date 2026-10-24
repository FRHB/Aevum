using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace PhotoAevumStudio.Application.Entities;

/// <summary>
/// Extends ASP.NET Core Identity's IdentityUser with studio-specific profile fields.
/// The roles Admin, Employee, and Customer are seeded separately in Infrastructure.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }

    /// <summary>
    /// Human-readable studio ID, e.g. "PA-001". Required for Employee accounts.
    /// Null for Customer accounts.
    /// </summary>
    public string? StudioId { get; set; }

    /// <summary>Computed display name for UI rendering.</summary>
    public string FullName =>
        string.Join(' ',
            new[] { FirstName, MiddleName, LastName }
            .Where(p => !string.IsNullOrWhiteSpace(p)));

    public bool MustChangePassword { get; set; } = true;
    public DateTime? PasswordChangedAt { get; set; }

    [NotMapped]
    public bool isFirstLogin
    {
        get => MustChangePassword;
        set => MustChangePassword = value;
    }

    [NotMapped]
    public bool IsFirstLogin
    {
        get => MustChangePassword;
        set => MustChangePassword = value;
    }
}
