using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PhotoAevumStudio.Application.Utils;

/// <summary>
/// Utility methods for generating formatted Employee IDs and cryptographically secure initial passwords.
/// </summary>
public static class EmployeeCredentialGenerator
{
    private static readonly Regex EmpRegex = new(@"^EMP-(\d{3})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Automatically generates the next unique custom ID formatted as EMP-###.
    /// Fills gaps sequentially starting from EMP-001 up to EMP-999.
    /// </summary>
    public static string GenerateEmployeeId(IEnumerable<string>? existingIds)
    {
        var usedNumbers = new HashSet<int>();
        if (existingIds != null)
        {
            foreach (var id in existingIds)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                var match = EmpRegex.Match(id.Trim());
                if (match.Success && int.TryParse(match.Groups[1].Value, out int number))
                {
                    usedNumbers.Add(number);
                }
            }
        }

        int candidate = 1;
        while (usedNumbers.Contains(candidate))
        {
            candidate++;
            if (candidate > 999)
            {
                throw new InvalidOperationException("Exceeded maximum allowable ID allocations (999) for prefix 'EMP'.");
            }
        }

        return $"EMP-{candidate:D3}";
    }

    /// <summary>
    /// Generates an initial employee/admin password using the format: LastName + BirthYear + CompactID (e.g. Baclig2006E001, Baclig2006A001, Baclig2006S101).
    /// </summary>
    public static string GenerateInitialPassword(string? lastName, int? birthYear, string? employeeId)
    {
        var cleanLastName = FormatLastName(lastName);
        int year = birthYear is >= 1900 and <= 2100 ? birthYear.Value : DateTime.UtcNow.Year;
        var compactId = FormatCompactId(employeeId);
        return $"{cleanLastName}{year:D4}{compactId}";
    }

    private static string FormatLastName(string? lastName)
    {
        if (string.IsNullOrWhiteSpace(lastName)) return "Staff";
        var parts = Regex.Split(lastName.Trim(), @"\s+")
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p[1..] : string.Empty));
        var joined = string.Concat(parts);
        return string.IsNullOrWhiteSpace(joined) ? "Staff" : joined;
    }

    private static string FormatCompactId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "E001";
        var trimmed = id.Trim().ToUpperInvariant();
        var letter = trimmed.FirstOrDefault(char.IsLetter);
        char prefixLetter = letter != '\0' ? letter : 'E';

        string idNum = "001";
        var match = Regex.Match(trimmed, @"(\d{3,})$");
        if (match.Success)
        {
            idNum = match.Groups[1].Value;
        }
        else
        {
            var digits = new string(trimmed.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(digits))
            {
                idNum = digits.Length >= 3 ? digits[^3..] : digits.PadLeft(3, '0');
            }
        }

        return $"{prefixLetter}{idNum}";
    }

    public static string GenerateInitialPassword(string? lastName, DateTime? birthDate, string? employeeId) =>
        GenerateInitialPassword(lastName, birthDate?.Year, employeeId);

    /// <summary>
    /// Generates a cryptographically secure, random initial password that meets complexity policies:
    /// - At least 8 characters (default 12)
    /// - Contains uppercase, lowercase, digit, and symbol
    /// - Excludes ambiguous glyphs (O, 0, I, l) to facilitate manual transcription.
    /// </summary>
    public static string GenerateInitialPassword(int length = 12)
    {
        return AdminCredentialGenerator.GenerateTemporaryPassword(length);
    }
}
