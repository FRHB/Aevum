using System.Security.Claims;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public static class AppRoles
{
    public const string SystemAdmin = "SystemAdmin";
    public const string Admin       = "Admin";
    public const string OfficeClerk = "Office Clerk";
    public const string Employee    = "Employee";
    public const string Customer    = "Customer";

    public static readonly IReadOnlyList<string> All = [SystemAdmin, Admin, OfficeClerk, Employee, Customer];
}

public static class EmployeeRoleHelper
{
    public static bool IsSystemAdmin(ClaimsPrincipal? user)
    {
        if (user == null || user.Identity?.IsAuthenticated != true)
            return false;

        if (user.IsInRole("SystemAdmin") || user.IsInRole("System Admin") || user.IsInRole("Super Admin"))
            return true;

        var studioId = user.FindFirst("studio_id")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return !string.IsNullOrEmpty(studioId) && studioId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAdmin(ClaimsPrincipal? user)
    {
        if (user == null || user.Identity?.IsAuthenticated != true)
            return false;

        if (IsSystemAdmin(user))
            return true;

        if (user.IsInRole("Admin"))
            return true;

        var studioId = user.FindFirst("studio_id")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return !string.IsNullOrEmpty(studioId) && studioId.StartsWith("ADM-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsOfficeClerk(ClaimsPrincipal? user, IUserService? userService = null)
    {
        if (user == null || user.Identity?.IsAuthenticated != true)
            return false;

        if (user.IsInRole(AppRoles.OfficeClerk) ||
            user.IsInRole("OfficeClerk") ||
            user.IsInRole("Clerk") ||
            user.IsInRole("office_clerk"))
        {
            return true;
        }

        var studioId = user.FindFirst("studio_id")?.Value 
                       ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrEmpty(studioId) && studioId.StartsWith("CLK-", StringComparison.OrdinalIgnoreCase))
            return true;

        var roleClaim = user.FindFirst("role")?.Value ?? user.FindFirst(ClaimTypes.Role)?.Value;
        if (!string.IsNullOrEmpty(roleClaim) && roleClaim.Contains("clerk", StringComparison.OrdinalIgnoreCase))
            return true;

        var specClaim = user.FindFirst("specialization")?.Value 
                        ?? user.FindFirst("position")?.Value
                        ?? user.FindFirst("job_title")?.Value;
        if (!string.IsNullOrEmpty(specClaim) && specClaim.Contains("clerk", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(studioId) && userService != null)
        {
            try
            {
                var emp = userService.GetEmployeeById(studioId);
                if (emp != null)
                {
                    if (!string.IsNullOrEmpty(emp.Position) && emp.Position.Contains("clerk", StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (!string.IsNullOrEmpty(emp.Department) && (emp.Department.Contains("clerk", StringComparison.OrdinalIgnoreCase) || emp.Department.Contains("office", StringComparison.OrdinalIgnoreCase)))
                        return true;
                }

                var allSpecs = userService.GetEmployeeSpecializations();
                var mySpecs = allSpecs.FirstOrDefault(s => s.EmployeeId.Equals(studioId, StringComparison.OrdinalIgnoreCase));
                if (mySpecs != null && mySpecs.Specializations.Any(s => s.Contains("clerk", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            catch
            {
                // Fallback gracefully on database exceptions
            }
        }

        return false;
    }

    public static bool IsPhotographerOrCreative(ClaimsPrincipal? user, IUserService? userService = null)
    {
        if (user == null || user.Identity?.IsAuthenticated != true)
            return false;

        if (IsAdmin(user) || IsOfficeClerk(user, userService))
            return false;

        if (user.IsInRole("Employee") || user.IsInRole("Staff") || user.IsInRole("Photographer") || user.IsInRole("Editor") || user.IsInRole("Videographer"))
            return true;

        var studioId = user.FindFirst("studio_id")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return !string.IsNullOrEmpty(studioId) && studioId.StartsWith("EMP-", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetCurrentEmployeeId(ClaimsPrincipal? user)
    {
        if (user == null) return string.Empty;
        return user.FindFirst("studio_id")?.Value 
               ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value 
               ?? string.Empty;
    }

    public static string GetCurrentEmployeeName(ClaimsPrincipal? user, IUserService? userService = null)
    {
        if (user == null) return string.Empty;

        var name = user.FindFirst("full_name")?.Value 
                   ?? user.FindFirst(ClaimTypes.Name)?.Value;

        if (!string.IsNullOrWhiteSpace(name) && !name.Contains('@') && !name.StartsWith("EMP-", StringComparison.OrdinalIgnoreCase))
            return name;

        var empId = GetCurrentEmployeeId(user);
        if (!string.IsNullOrWhiteSpace(empId) && userService != null)
        {
            try
            {
                var emp = userService.GetEmployeeById(empId);
                if (emp != null && !string.IsNullOrWhiteSpace(emp.Name))
                    return emp.Name;
            }
            catch
            {
                // Fallback gracefully
            }
        }

        return name ?? empId;
    }

    public static bool IsEventAssignedToEmployee(EventBookingModel booking, string employeeName, string employeeId)
    {
        if (booking == null) return false;

        if (!string.IsNullOrWhiteSpace(employeeName))
        {
            if (string.Equals(booking.AssignedPhotographer, employeeName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (booking.AssignedPhotographer.Contains(employeeName, StringComparison.OrdinalIgnoreCase) ||
                employeeName.Contains(booking.AssignedPhotographer, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(employeeId))
        {
            if (booking.AssignedPhotographer.Contains(employeeId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (booking.Notes.Contains(employeeId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
