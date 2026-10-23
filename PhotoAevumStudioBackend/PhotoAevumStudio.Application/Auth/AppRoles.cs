namespace PhotoAevumStudio.Application.Auth;

/// <summary>Roles that can be assigned to ApplicationUser accounts.</summary>
public static class AppRoles
{
    public const string SystemAdmin = "SystemAdmin";
    public const string Admin       = "Admin";
    public const string Employee    = "Employee";
    public const string Customer    = "Customer";

    public static readonly IReadOnlyList<string> All = [SystemAdmin, Admin, Employee, Customer];
}
