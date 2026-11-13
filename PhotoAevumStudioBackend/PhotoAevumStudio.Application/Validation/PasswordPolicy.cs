using System.Text.RegularExpressions;

namespace PhotoAevumStudio.Application.Validation;

/// <summary>
/// Shared password policy helper enforcing 8–30 characters, at least one digit,
/// and at least one allowed special character (@, #, $, %, &amp;).
/// </summary>
public static partial class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 30;
    public const string AllowedSpecialCharacters = "@#$%&";

    [GeneratedRegex("[0-9]")]
    private static partial Regex DigitRegex();

    [GeneratedRegex("[@#$%&]")]
    private static partial Regex AllowedSpecialRegex();

    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return "Password is required.";

        if (password.Length < MinLength)
            return "Password must be at least 8 characters long.";

        if (password.Length > MaxLength)
            return "Password must not exceed 30 characters.";

        if (!DigitRegex().IsMatch(password))
            return "Password must contain at least one number (0–9).";

        if (!AllowedSpecialRegex().IsMatch(password))
            return "Password must contain at least one special character (@, #, $, %, &).";

        return null;
    }
}
