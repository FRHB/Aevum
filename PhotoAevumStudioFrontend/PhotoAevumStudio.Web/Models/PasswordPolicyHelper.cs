namespace PhotoAevumStudio.Web.Models;

/// <summary>
/// Single source of truth for password complexity checks shared across registration,
/// first-login password setup, forgot password reset, and profile security updates.
/// </summary>
public static class PasswordPolicyHelper
{
    public const int MinLength = 8;
    public const int MaxLength = 30;
    public const string AllowedSpecialCharacters = "@#$%&";
    public const string PolicyRegexPattern = @"^(?=.*[0-9])(?=.*[@#$%&]).{8,30}$";

    public static bool HasValidLength(string? password) =>
        !string.IsNullOrEmpty(password) &&
        password.Length >= MinLength &&
        password.Length <= MaxLength;

    public static bool HasNumber(string? password) =>
        !string.IsNullOrEmpty(password) &&
        password.Any(char.IsDigit);

    public static bool HasSpecialCharacter(string? password) =>
        !string.IsNullOrEmpty(password) &&
        password.Any(c => AllowedSpecialCharacters.Contains(c));

    public static bool IsCompliant(string? password) =>
        HasValidLength(password) && HasNumber(password) && HasSpecialCharacter(password);

    public static string? GetValidationError(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            return $"Password must be at least {MinLength} characters long.";
        if (password.Length > MaxLength)
            return $"Password must not exceed {MaxLength} characters.";
        if (!HasNumber(password))
            return "Password must contain at least one number (0–9).";
        if (!HasSpecialCharacter(password))
            return "Password must contain at least one special character (@, #, $, %, &).";
        return null;
    }
}
