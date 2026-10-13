namespace PhotoAevumStudio.Application.Auth;

// ─── Request DTOs ────────────────────────────────────────────────────────────

/// <summary>Payload sent by the Blazor login form to POST /api/auth/login.</summary>
public record LoginRequest(string UserId, string Password);

/// <summary>
/// Payload sent by an Admin to POST /api/auth/register to create a new
/// Employee or Customer account.
/// </summary>
public record RegisterRequest(
    string FirstName,
    string LastName,
    string? MiddleName,
    string Email,
    string Password,
    string Role,
    string? StudioId = null);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmPassword);

public record InitialPasswordChangeRequest(
    string UserId,
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword);

// ─── Response DTOs ───────────────────────────────────────────────────────────

/// <summary>
/// Successful login response. The Blazor frontend stores the Token and uses it
/// as a Bearer header on all subsequent API calls.
/// When RequiresPasswordChange is true, no Token is issued.
/// </summary>
public record LoginResponse(
    string? Token,
    string? Role,
    string? FullName,
    DateTime? ExpiresAt,
    bool MustChangePassword = false,
    bool RequiresPasswordChange = false,
    string? UserId = null);