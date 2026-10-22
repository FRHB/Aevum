namespace PhotoAevumStudio.Application.DTOs;

public record SendVerificationRequest(string Email);

public record VerifyCodeRequest(string Email, string VerificationCode);

public record CustomerRegisterRequest(
    string FirstName,
    string LastName,
    string? MiddleName,
    string Email,
    string Password,
    string VerificationCode);

public record ResetPasswordRequest(
    string Email,
    string VerificationCode,
    string NewPassword);
