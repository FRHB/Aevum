using System.ComponentModel.DataAnnotations;

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

public record CustomerRegisterRequest(
    [property: Required(ErrorMessage = "First name is required.")]
    [property: StringLength(50, ErrorMessage = "Maximum 50 characters.")]
    string FirstName,

    [property: Required(ErrorMessage = "Last name is required.")]
    [property: StringLength(50, ErrorMessage = "Maximum 50 characters.")]
    string LastName,

    [property: StringLength(50, ErrorMessage = "Maximum 50 characters.")]
    string? MiddleName,

    [property: Required(ErrorMessage = "Email is required.")]
    [property: EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [property: StringLength(256)]
    string Email,

    [property: Required(ErrorMessage = "Password is required.")]
    [property: MinLength(8, ErrorMessage = "Password must be at least 8 characters long.")]
    [property: RegularExpression(@"^(?=.*[0-9])(?=.*[^a-zA-Z0-9]).{8,}$",
        ErrorMessage = "Password must be at least 8 characters long and contain at least one number (0–9) and one special character (e.g., @, #, $, !, %, etc.).")]
    string Password,

    [property: Required(ErrorMessage = "Verification code is required.")]
    string VerificationCode,

    [property: Required(ErrorMessage = "Cellphone number is required.")]
    [property: RegularExpression(@"^09\d{9}$",
        ErrorMessage = "Cellphone number must be exactly 11 digits starting with 09.")]
    string PhoneNumber = "");
