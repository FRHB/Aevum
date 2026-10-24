using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PhotoAevumStudio.Application.Utils;

/// <summary>
/// Utility methods for generating formatted Admin IDs and cryptographically secure temporary passwords.
/// </summary>
public static class AdminCredentialGenerator
{
    private static readonly Regex SadmRegex = new(@"^SADM-(\d{3})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AdmRegex = new(@"^ADM-(\d{3})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Determines if a role name represents a System Admin.
    /// </summary>
    public static bool IsSystemAdminRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;
        var r = role.Trim();
        return r.Equals("SystemAdmin", StringComparison.OrdinalIgnoreCase)
            || r.Equals("System Admin", StringComparison.OrdinalIgnoreCase)
            || r.Equals("Super Admin", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Automatically generates the next unique custom ID formatted as SADM-### (for System Admins) or ADM-### (for Admins).
    /// </summary>
    /// <param name="role">Role name: System Admin or Admin.</param>
    /// <param name="existingIds">Collection of existing admin IDs currently in the system.</param>
    /// <returns>A formatted 3-digit custom ID, e.g. "SADM-002" or "ADM-005".</returns>
    public static string GenerateAdminId(string role, IEnumerable<string> existingIds)
    {
        bool isSysAdmin = IsSystemAdminRole(role);
        string prefix = isSysAdmin ? "SADM" : "ADM";
        var regex = isSysAdmin ? SadmRegex : AdmRegex;

        var usedNumbers = new HashSet<int>();
        if (existingIds != null)
        {
            foreach (var id in existingIds)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                var match = regex.Match(id.Trim());
                if (match.Success && int.TryParse(match.Groups[1].Value, out int number))
                {
                    usedNumbers.Add(number);
                }
            }
        }

        // Find the lowest available number starting from 1 up to 999
        int candidate = 1;
        while (usedNumbers.Contains(candidate))
        {
            candidate++;
            if (candidate > 999)
            {
                throw new InvalidOperationException($"Exceeded maximum allowable ID allocations (999) for prefix '{prefix}'.");
            }
        }

        return $"{prefix}-{candidate:D3}";
    }

    /// <summary>
    /// Generates a cryptographically secure, random temporary password that strictly meets studio security policies:
    /// - At least 8 characters (default 12)
    /// - Contains uppercase letters
    /// - Contains lowercase letters
    /// - Contains numbers
    /// - Contains special symbols
    /// - Excludes ambiguous glyphs (O, 0, I, l) to facilitate manual transcription.
    /// </summary>
    public static string GenerateTemporaryPassword(int length = 12)
    {
        if (length < 8) length = 8;

        const string upperChars   = "ABCDEFGHJKLMNPQRSTUVWXYZ";  // No I, O
        const string lowerChars   = "abcdefghijkmnopqrstuvwxyz";  // No l
        const string digitChars   = "23456789";                   // No 0, 1
        const string specialChars = "!@#$%^&*?";

        var passwordChars = new char[length];

        // Guarantee at least one character from each policy bucket
        passwordChars[0] = upperChars[RandomNumberGenerator.GetInt32(upperChars.Length)];
        passwordChars[1] = lowerChars[RandomNumberGenerator.GetInt32(lowerChars.Length)];
        passwordChars[2] = digitChars[RandomNumberGenerator.GetInt32(digitChars.Length)];
        passwordChars[3] = specialChars[RandomNumberGenerator.GetInt32(specialChars.Length)];

        var combinedPool = upperChars + lowerChars + digitChars + specialChars;
        for (int i = 4; i < length; i++)
        {
            passwordChars[i] = combinedPool[RandomNumberGenerator.GetInt32(combinedPool.Length)];
        }

        // Cryptographically shuffle the character array (Fisher-Yates)
        for (int i = passwordChars.Length - 1; i > 0; i--)
        {
            int swapIndex = RandomNumberGenerator.GetInt32(i + 1);
            (passwordChars[i], passwordChars[swapIndex]) = (passwordChars[swapIndex], passwordChars[i]);
        }

        return new string(passwordChars);
    }
}
