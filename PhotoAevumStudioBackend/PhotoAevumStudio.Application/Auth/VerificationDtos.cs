using System.Text.RegularExpressions;

namespace PhotoAevumStudio.Application.DTOs;

public record SendVerificationRequest(string Email);

public record CustomerRegisterRequest(
    string FirstName,
    string LastName,
    string? MiddleName,
    string Email,
    string Password,
    string VerificationCode);
