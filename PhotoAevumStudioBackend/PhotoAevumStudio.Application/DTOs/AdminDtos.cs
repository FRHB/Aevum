namespace PhotoAevumStudio.Application.DTOs;

/// <summary>
/// DTO representing an administrator record returned to clients.
/// </summary>
public record AdminDto(
    string Id,
    string FirstName,
    string LastName,
    string? MiddleName,
    string FullName,
    string Email,
    string Role,
    bool IsSuperAdmin,
    bool IsFirstLogin,
    string Status = "Active",
    bool WelcomeEmailSent = true,
    bool HandbookAttached = true,
    string? EmailWarning = null);

/// <summary>
/// DTO for creating a new administrator via the wizard workflow.
/// </summary>
public record CreateAdminDto(
    string FirstName,
    string LastName,
    string? MiddleName,
    string Email,
    string Role,
    string? CustomId = null,
    string? TemporaryPassword = null);

/// <summary>
/// DTO for updating an existing administrator's profile.
/// </summary>
public record UpdateAdminDto(
    string FirstName,
    string LastName,
    string? MiddleName,
    string Email);

/// <summary>
/// Response containing the next auto-generated Admin ID.
/// </summary>
public record NextAdminIdResponse(string NextId, string Role);

/// <summary>
/// Request to update an employee's scheduled days off.
/// </summary>
public record UpdateEmployeeDaysOffDto(string? DaysOff);
