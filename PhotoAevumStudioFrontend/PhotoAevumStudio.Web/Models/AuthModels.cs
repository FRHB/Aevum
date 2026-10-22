namespace PhotoAevumStudio.Web.Models;

public record AuthResult(
    bool Success,
    string? Token = null,
    string? Error = null,
    bool RequiresPasswordChange = false,
    string? UserId = null,
    string? Role = null,
    bool IsFirstLogin = false);

public record LoginRequest(string UserId, string Password);

public record LoginResponse(
    string? Token = null,
    string? Role = null,
    string? FullName = null,
    DateTime? ExpiresAt = null,
    bool MustChangePassword = false,
    bool RequiresPasswordChange = false,
    string? UserId = null,
    bool IsFirstLogin = false);

public record InitialPasswordChangeRequest(
    string UserId,
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword);
