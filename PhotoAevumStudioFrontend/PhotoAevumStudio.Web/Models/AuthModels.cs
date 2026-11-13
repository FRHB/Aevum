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
    [property: Required(ErrorMessage = "User ID is required.")]
    string UserId,

    [property: Required(ErrorMessage = "Current password is required.")]
    string CurrentPassword,

    [property: Required(ErrorMessage = "New password is required.")]
    [property: MinLength(8, ErrorMessage = "Password must be at least 8 characters long.")]
    [property: MaxLength(30, ErrorMessage = "Password must not exceed 30 characters.")]
    [property: RegularExpression(@"^(?=.*[0-9])(?=.*[@#$%&]).{8,30}$",
        ErrorMessage = "Password must be 8–30 characters long and contain at least one number (0–9) and one special character (@, #, $, %, &).")]
    string NewPassword,

    [property: Required(ErrorMessage = "Confirm password is required.")]
    [property: Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
    string ConfirmPassword);

public class ChangePasswordModel
{
    [Required(ErrorMessage = "User ID or Email is required.")]
    public string UserId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Current password is required.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "New password is required.")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters long.")]
    [MaxLength(30, ErrorMessage = "Password must not exceed 30 characters.")]
    [RegularExpression(@"^(?=.*[0-9])(?=.*[@#$%&]).{8,30}$",
        ErrorMessage = "Password must be 8–30 characters long and contain at least one number (0–9) and one special character (@, #, $, %, &).")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm password is required.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

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
    [property: MaxLength(30, ErrorMessage = "Password must not exceed 30 characters.")]
    [property: RegularExpression(@"^(?=.*[0-9])(?=.*[@#$%&]).{8,30}$",
        ErrorMessage = "Password must be 8–30 characters long and contain at least one number (0–9) and one special character (@, #, $, %, &).")]
    string Password,

    [property: Required(ErrorMessage = "Verification code is required.")]
    string VerificationCode,

    [property: Required(ErrorMessage = "Cellphone number is required.")]
    [property: RegularExpression(@"^09\d{9}$",
        ErrorMessage = "Cellphone number must be exactly 11 digits starting with 09.")]
    string PhoneNumber = "");
