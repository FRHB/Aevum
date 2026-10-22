using System.Text.RegularExpressions;

namespace PhotoAevumStudio.Web.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  Admin  (ID: "SADM-###"  or  "ADM-###")
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Represents an administrator account.
/// </summary>
/// <remarks>
/// Valid ID formats:
/// <list type="bullet">
///   <item><term>SADM-###</term><description>Super Admin (root, cannot be deleted)</description></item>
///   <item><term>ADM-###</term><description>Regular Admin (can only be deleted by an SADM)</description></item>
/// </list>
/// ### must be exactly three digits (e.g. SADM-001, ADM-042).
/// </remarks>
public sealed class Admin
{
    // Accepts SADM-### or ADM-### where ### = exactly 3 digits
    private static readonly Regex IdPattern =
        new(@"^(SADM|ADM)-\d{3}$", RegexOptions.Compiled);

    private string _id = string.Empty;

    /// <summary>Gets or sets the admin's unique identifier.</summary>
    /// <exception cref="ArgumentException">Thrown when the value does not match SADM-### or ADM-### format.</exception>
    public string Id
    {
        get => _id;
        set
        {
            if (!IdPattern.IsMatch(value))
                throw new ArgumentException(
                    $"Invalid Admin ID '{value}'. Must be 'SADM-###' or 'ADM-###' (3 digits).",
                    nameof(Id));
            _id = value;
        }
    }

    public string Name  { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>Role description, e.g. "Super Admin" or "Admin".</summary>
    public string Role  { get; set; } = string.Empty;

    /// <summary>True when this account holds the root Super Admin privilege.</summary>
    public bool IsSuperAdmin => _id.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase);
}

// ─────────────────────────────────────────────────────────────────────────────
//  Employee  (ID: "EMP-###")
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Represents a studio employee.
/// ID format: EMP-### (exactly 3 digits, e.g. EMP-007).
/// </summary>
public sealed class Employee
{
    private static readonly Regex IdPattern =
        new(@"^EMP-\d{3}$", RegexOptions.Compiled);

    private string _id = string.Empty;

    /// <summary>Gets or sets the employee's unique identifier.</summary>
    /// <exception cref="ArgumentException">Thrown when the value does not match EMP-### format.</exception>
    public string Id
    {
        get => _id;
        set
        {
            if (!IdPattern.IsMatch(value))
                throw new ArgumentException(
                    $"Invalid Employee ID '{value}'. Must be 'EMP-###' (3 digits).",
                    nameof(Id));
            _id = value;
        }
    }

    public string Name       { get; set; } = string.Empty;
    public string Email      { get; set; } = string.Empty;
    public string Position   { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
}

// ─────────────────────────────────────────────────────────────────────────────
//  Customer  (ID: "CUST-####")
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Represents a studio customer/client.
/// ID format: CUST-#### (exactly 4 digits, e.g. CUST-0042).
/// </summary>
public sealed class Customer
{
    private static readonly Regex IdPattern =
        new(@"^CUST-\d{4}$", RegexOptions.Compiled);

    private string _id = string.Empty;

    /// <summary>Gets or sets the customer's unique identifier.</summary>
    /// <exception cref="ArgumentException">Thrown when the value does not match CUST-#### format.</exception>
    public string Id
    {
        get => _id;
        set
        {
            if (!IdPattern.IsMatch(value))
                throw new ArgumentException(
                    $"Invalid Customer ID '{value}'. Must be 'CUST-####' (4 digits).",
                    nameof(Id));
            _id = value;
        }
    }

    public string   Name     { get; set; } = string.Empty;
    public string   Email    { get; set; } = string.Empty;
    public string   Phone    { get; set; } = string.Empty;
    public DateTime JoinDate { get; set; } = DateTime.UtcNow;
}
