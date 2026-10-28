namespace PhotoAevumStudio.Web.Models;

/// <summary>
/// Dynamic user profile model for Staff ID Card and Account Center.
/// </summary>
public class AppUser
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string JobTitle { get; set; } = "Studio Administrator";
    public string Department { get; set; } = "Administration & Studio Management";
    public string Role { get; set; } = "Administrator";
    public string Status { get; set; } = "Active";
    public string MemberSince { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FullName))
                return !string.IsNullOrWhiteSpace(Id) && Id.Length >= 2 ? Id[..2].ToUpperInvariant() : "U";

            var parts = FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
            return char.ToUpperInvariant(FullName[0]).ToString();
        }
    }
}

/// <summary>
/// Module permission model for Profile permissions management.
/// </summary>
public class Permission
{
    public string Name { get; set; } = string.Empty;
    public string IconClass { get; set; } = "fa-shield-halved";
    public bool IsGranted { get; set; } = true;
    public string? Description { get; set; }
}
