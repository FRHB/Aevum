using System.ComponentModel.DataAnnotations;

namespace PhotoAevumStudio.Application.DTOs;

public record SendVerificationRequest(string Email);

public record VerifyCodeRequest(string Email, string VerificationCode);

public record CustomerRegisterRequest(
    string FirstName,
    string LastName,
    string? MiddleName,
    string Email,
    [property: Required(ErrorMessage = "Password is required.")]
    [property: MinLength(8, ErrorMessage = "Password must be at least 8 characters long.")]
    [property: RegularExpression(@"^(?=.*[0-9])(?=.*[^a-zA-Z0-9]).{8,}$",
        ErrorMessage = "Password must be at least 8 characters long and contain at least one number (0–9) and one special character (e.g., @, #, $, !, %, etc.).")]
    string Password,
    string VerificationCode,
    [property: Required(ErrorMessage = "Cellphone number is required.")]
    [property: RegularExpression(@"^09\d{9}$", ErrorMessage = "Cellphone number must be exactly 11 digits starting with 09.")]
    string PhoneNumber = "");

public record ResetPasswordRequest(
    string Email,
    string VerificationCode,
    string NewPassword);
