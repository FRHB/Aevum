using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

/// <summary>
/// Production SQL Server implementation of <see cref="IUserService"/>.
/// Strictly queries and mutates the configured SQL Server database (DefaultConnection).
/// </summary>
public sealed class UserService : IUserService
{
    private const string DefaultLocalConnectionString =
        "Data Source=LAPTOP-VJQ60GP3\\SQLEXPRESS;Initial Catalog=PhotoAevumStudio;Integrated Security=True;Persist Security Info=False;Pooling=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=True;Application Name=\"SQL Server Management Studio\";Command Timeout=0";

    private static readonly Regex ValidAdminIdRegex = new(@"^(SADM|ADM)-\d{3}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ValidEmployeeIdRegex = new(@"^EMP-\d{3}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ValidCustomerIdRegex = new(@"^CUST-\d{4}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _connectionString;
    private readonly IHttpClientFactory? _httpClientFactory;

    public UserService(IConfiguration? configuration = null, IHttpClientFactory? httpClientFactory = null)
    {
        _connectionString = configuration?.GetConnectionString("DefaultConnection")
            ?? DefaultLocalConnectionString;
        _httpClientFactory = httpClientFactory;
    }

    private SqlConnection CreateConnection() => new(_connectionString);

    private static (string FirstName, string? MiddleName, string LastName) SplitFullName(string fullName)
    {
        var parts = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("User", null, "Account"),
            1 => (parts[0], null, parts[0]),
            2 => (parts[0], null, parts[1]),
            _ => (parts[0], string.Join(" ", parts[1..^1]), parts[^1])
        };
    }

    // ── Admins ───────────────────────────────────────────────────────────────

    public IReadOnlyList<Admin> GetAdmins()
    {
        var list = new List<Admin>();
        using var conn = CreateConnection();
        conn.Open();

        bool hasEmailCol = false;
        using (var colCmd = new SqlCommand("SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Admin' AND COLUMN_NAME = 'Email';", conn))
        {
            hasEmailCol = colCmd.ExecuteScalar() != null;
        }

        string sql = hasEmailCol
            ? @"IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL
                BEGIN
                    SELECT AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, Email
                    FROM dbo.Admin
                    ORDER BY AdminID;
                END"
            : @"IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL
                BEGIN
                    SELECT AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, NULL AS Email
                    FROM dbo.Admin
                    ORDER BY AdminID;
                END";

        using var cmd = new SqlCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0).Trim().ToUpperInvariant();
            if (!ValidAdminIdRegex.IsMatch(id))
                continue;

            var role = reader.IsDBNull(1) ? "Admin" : reader.GetString(1);
            var first = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var last = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            var middle = reader.IsDBNull(4) ? null : reader.GetString(4);
            var email = reader.IsDBNull(5) ? $"{id.ToLowerInvariant()}@photoaevum.studio" : reader.GetString(5);
            var fullName = string.Join(" ", new[] { first, middle, last }.Where(s => !string.IsNullOrWhiteSpace(s)));

            list.Add(new Admin
            {
                Id = id,
                Name = fullName,
                Email = string.IsNullOrWhiteSpace(email) ? $"{id.ToLowerInvariant()}@photoaevum.studio" : email,
                Role = id.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase) ? "Super Admin" : role
            });
        }

        return list;
    }

    public Admin? GetAdminById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var trimmed = id.Trim().ToUpperInvariant();

        using var conn = CreateConnection();
        conn.Open();

        bool hasEmailCol = false;
        using (var colCmd = new SqlCommand("SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Admin' AND COLUMN_NAME = 'Email';", conn))
        {
            hasEmailCol = colCmd.ExecuteScalar() != null;
        }

        string sql = hasEmailCol
            ? @"IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL
                BEGIN
                    SELECT AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, Email
                    FROM dbo.Admin
                    WHERE UPPER(AdminID) = @Id;
                END"
            : @"IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL
                BEGIN
                    SELECT AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, NULL AS Email
                    FROM dbo.Admin
                    WHERE UPPER(AdminID) = @Id;
                END";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", trimmed);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var dbId = reader.GetString(0).Trim().ToUpperInvariant();
        if (!ValidAdminIdRegex.IsMatch(dbId)) return null;

        var role = reader.IsDBNull(1) ? "Admin" : reader.GetString(1);
        var first = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
        var last = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
        var middle = reader.IsDBNull(4) ? null : reader.GetString(4);
        var email = reader.IsDBNull(5) ? $"{dbId.ToLowerInvariant()}@photoaevum.studio" : reader.GetString(5);
        var fullName = string.Join(" ", new[] { first, middle, last }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return new Admin
        {
            Id = dbId,
            Name = fullName,
            Email = string.IsNullOrWhiteSpace(email) ? $"{dbId.ToLowerInvariant()}@photoaevum.studio" : email,
            Role = dbId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase) ? "Super Admin" : role
        };
    }

    public void AddAdmin(Admin admin, string? requestingUserId = null)
    {
        ArgumentNullException.ThrowIfNull(admin);

        if (!string.IsNullOrWhiteSpace(requestingUserId) &&
            !requestingUserId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Insufficient privileges. Only a System Administrator (SADM-###) is authorized to create a new Admin account.");
        }

        var (first, middle, last) = SplitFullName(admin.Name);
        using var conn = CreateConnection();
        conn.Open();

        if (GetAdminById(admin.Id) is not null)
            throw new InvalidOperationException($"An admin with ID '{admin.Id}' already exists.");

        bool hasEmailCol = false;
        using (var colCmd = new SqlCommand("SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Admin' AND COLUMN_NAME = 'Email';", conn))
        {
            hasEmailCol = colCmd.ExecuteScalar() != null;
        }

        string emailVal = string.IsNullOrWhiteSpace(admin.Email)
            ? $"{admin.Id.ToLowerInvariant()}@photoaevum.studio"
            : admin.Email.Trim();

        string sql = hasEmailCol
            ? @"INSERT INTO dbo.Admin (AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, Email, AdminPassword, isFirstLogin)
                VALUES (@Id, @Role, @First, @Last, @Middle, @Email, @Password, 1);"
            : @"INSERT INTO dbo.Admin (AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, AdminPassword, isFirstLogin)
                VALUES (@Id, @Role, @First, @Last, @Middle, @Password, 1);";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", admin.Id.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@Role", string.IsNullOrWhiteSpace(admin.Role) ? "Admin" : admin.Role);
        cmd.Parameters.AddWithValue("@First", first);
        cmd.Parameters.AddWithValue("@Last", last);
        cmd.Parameters.AddWithValue("@Middle", (object?)middle ?? DBNull.Value);
        if (hasEmailCol)
        {
            cmd.Parameters.AddWithValue("@Email", emailVal);
        }
        cmd.Parameters.AddWithValue("@Password", "AxiomBit@2026");
        cmd.ExecuteNonQuery();

        // History Log
        try
        {
            const string logSql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedBy, ActionDetails, LoggedAt)
                    VALUES ('Create', 'Admin', @Id, @By, @Details, SYSUTCDATETIME());
                END";
            using var logCmd = new SqlCommand(logSql, conn);
            logCmd.Parameters.AddWithValue("@Id", admin.Id.ToUpperInvariant());
            logCmd.Parameters.AddWithValue("@By", string.IsNullOrWhiteSpace(requestingUserId) ? "SADM-001" : requestingUserId.Trim());
            logCmd.Parameters.AddWithValue("@Details", $"Created admin {admin.Id} ({admin.Name}) with email {emailVal}");
            logCmd.ExecuteNonQuery();
        }
        catch
        {
            // Logging is non-blocking
        }
    }

    public void DeleteAdmin(string targetId, string requestingUserId)
    {
        if (targetId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The Super Admin account (SADM-###) is the root administrator and can never be deleted or modified.");

        if (!requestingUserId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Insufficient privileges. Only the Super Admin (SADM-###) can delete a regular Admin account.");

        var target = GetAdminById(targetId)
            ?? throw new KeyNotFoundException($"Admin with ID '{targetId}' was not found.");

        using var conn = CreateConnection();
        conn.Open();
        using var cmd = new SqlCommand("DELETE FROM dbo.Admin WHERE UPPER(AdminID) = @Id;", conn);
        cmd.Parameters.AddWithValue("@Id", target.Id.ToUpperInvariant());
        cmd.ExecuteNonQuery();

        try
        {
            const string logSql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedBy, ActionDetails, LoggedAt)
                    VALUES ('Delete', 'Admin', @Id, @By, 'Deleted admin account', SYSUTCDATETIME());
                END";
            using var logCmd = new SqlCommand(logSql, conn);
            logCmd.Parameters.AddWithValue("@Id", target.Id.ToUpperInvariant());
            logCmd.Parameters.AddWithValue("@By", requestingUserId.Trim());
            logCmd.ExecuteNonQuery();
        }
        catch
        {
        }
    }

    // ── Employees ────────────────────────────────────────────────────────────

    public IReadOnlyList<Employee> GetEmployees()
    {
        var list = new List<Employee>();
        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            IF OBJECT_ID(N'dbo.Employee', N'U') IS NOT NULL
            BEGIN
                SELECT e.EmployeeID,
                       e.EmployeeFirstName,
                       e.EmployeeLastName,
                       e.EmployeeMiddleName,
                       e.EmployeeEmail,
                       (
                           SELECT TOP 1 COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization)
                           FROM dbo.EmployeeJointSpecialization ejs
                           INNER JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID
                           WHERE ejs.EmployeeID = e.EmployeeID
                       ) AS PrimarySpecialization,
                       (
                           SELECT TOP 1 COALESCE(NULLIF(es.SpecializationDescription, ''), es.Description)
                           FROM dbo.EmployeeJointSpecialization ejs
                           INNER JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID
                           WHERE ejs.EmployeeID = e.EmployeeID
                       ) AS PrimaryDepartment
                FROM dbo.Employee e
                ORDER BY e.EmployeeID;
            END";

        using var cmd = new SqlCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0).Trim().ToUpperInvariant();
            if (!ValidEmployeeIdRegex.IsMatch(id))
                continue;

            var first = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var last = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var middle = reader.IsDBNull(3) ? null : reader.GetString(3);
            var email = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            var position = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
            var department = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
            var fullName = string.Join(" ", new[] { first, middle, last }.Where(s => !string.IsNullOrWhiteSpace(s)));

            list.Add(new Employee
            {
                Id = id,
                Name = fullName,
                Email = email,
                Position = position,
                Department = department
            });
        }

        return list;
    }

    public Employee? GetEmployeeById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var trimmed = id.Trim().ToUpperInvariant();

        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            IF OBJECT_ID(N'dbo.Employee', N'U') IS NOT NULL
            BEGIN
                SELECT e.EmployeeID,
                       e.EmployeeFirstName,
                       e.EmployeeLastName,
                       e.EmployeeMiddleName,
                       e.EmployeeEmail,
                       (
                           SELECT TOP 1 COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization)
                           FROM dbo.EmployeeJointSpecialization ejs
                           INNER JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID
                           WHERE ejs.EmployeeID = e.EmployeeID
                       ) AS PrimarySpecialization,
                       (
                           SELECT TOP 1 COALESCE(NULLIF(es.SpecializationDescription, ''), es.Description)
                           FROM dbo.EmployeeJointSpecialization ejs
                           INNER JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID
                           WHERE ejs.EmployeeID = e.EmployeeID
                       ) AS PrimaryDepartment
                FROM dbo.Employee e
                WHERE UPPER(e.EmployeeID) = @Id;
            END";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", trimmed);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var dbId = reader.GetString(0).Trim().ToUpperInvariant();
        if (!ValidEmployeeIdRegex.IsMatch(dbId)) return null;

        var first = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        var last = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
        var middle = reader.IsDBNull(3) ? null : reader.GetString(3);
        var email = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
        var position = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
        var department = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
        var fullName = string.Join(" ", new[] { first, middle, last }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return new Employee
        {
            Id = dbId,
            Name = fullName,
            Email = email,
            Position = position,
            Department = department
        };
    }

    public void AddEmployee(Employee employee)
    {
        ArgumentNullException.ThrowIfNull(employee);

        if (GetEmployeeById(employee.Id) is not null)
            throw new InvalidOperationException($"An employee with ID '{employee.Id}' already exists.");

        var (first, middle, last) = SplitFullName(employee.Name);
        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            INSERT INTO dbo.Employee (EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeMiddleName, EmployeeEmail, EmployeePassword, EmployeeStatus, isFirstLogin)
            VALUES (@Id, @First, @Last, @Middle, @Email, @Password, 'Active', 1);";

        using (var cmd = new SqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("@Id", employee.Id.ToUpperInvariant());
            cmd.Parameters.AddWithValue("@First", first);
            cmd.Parameters.AddWithValue("@Last", last);
            cmd.Parameters.AddWithValue("@Middle", (object?)middle ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Email", string.IsNullOrWhiteSpace(employee.Email) ? $"{employee.Id.ToLowerInvariant()}@photoaevum.studio" : employee.Email);
            cmd.Parameters.AddWithValue("@Password", "AxiomBit@2026");
            cmd.ExecuteNonQuery();
        }

        if (!string.IsNullOrWhiteSpace(employee.Position) || !string.IsNullOrWhiteSpace(employee.Department))
        {
            var specId = $"SP-{employee.Id.Replace("EMP-", "", StringComparison.OrdinalIgnoreCase)}";
            var jointId = $"EJS-{employee.Id.Replace("EMP-", "", StringComparison.OrdinalIgnoreCase)}";

            const string specSql = @"
                IF NOT EXISTS (SELECT 1 FROM dbo.EmployeeSpecialization WHERE SpecializationID = @SpecId)
                BEGIN
                    INSERT INTO dbo.EmployeeSpecialization (SpecializationID, SpecializationName, SpecializationDescription, Specialization, Description, ColorTag, CreatedAt)
                    VALUES (@SpecId, @Position, @Department, @Position, @Department, 'Blue', SYSUTCDATETIME());
                END
                ELSE
                BEGIN
                    UPDATE dbo.EmployeeSpecialization
                    SET SpecializationName = @Position, SpecializationDescription = @Department, Specialization = @Position, Description = @Department
                    WHERE SpecializationID = @SpecId;
                END

                IF NOT EXISTS (SELECT 1 FROM dbo.EmployeeJointSpecialization WHERE EmployeeJointSpecializationID = @JointId)
                BEGIN
                    INSERT INTO dbo.EmployeeJointSpecialization (EmployeeJointSpecializationID, SpecializationID, EmployeeID, AssignedAt)
                    VALUES (@JointId, @SpecId, @EmpId, SYSUTCDATETIME());
                END";

            using var specCmd = new SqlCommand(specSql, conn);
            specCmd.Parameters.AddWithValue("@SpecId", specId);
            specCmd.Parameters.AddWithValue("@JointId", jointId);
            specCmd.Parameters.AddWithValue("@EmpId", employee.Id.ToUpperInvariant());
            specCmd.Parameters.AddWithValue("@Position", employee.Position ?? string.Empty);
            specCmd.Parameters.AddWithValue("@Department", employee.Department ?? string.Empty);
            specCmd.ExecuteNonQuery();
        }
    }

    public bool DeleteEmployee(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        using var conn = CreateConnection();
        conn.Open();

        var normalizedId = id.Trim().ToUpperInvariant();
        var specId = $"SP-{normalizedId.Replace("EMP-", "", StringComparison.OrdinalIgnoreCase)}";

        const string sql = @"
            DELETE FROM dbo.EmployeeJointSpecialization WHERE UPPER(EmployeeID) = @Id;
            DELETE FROM dbo.EmployeeSpecialization
            WHERE UPPER(SpecializationID) = @SpecId
              AND NOT EXISTS (SELECT 1 FROM dbo.EmployeeJointSpecialization WHERE UPPER(SpecializationID) = @SpecId);
            DELETE FROM dbo.Employee WHERE UPPER(EmployeeID) = @Id;";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", normalizedId);
        cmd.Parameters.AddWithValue("@SpecId", specId);
        return cmd.ExecuteNonQuery() > 0;
    }

    // ── Customers ────────────────────────────────────────────────────────────

    public IReadOnlyList<Customer> GetCustomers()
    {
        var list = new List<Customer>();
        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            IF OBJECT_ID(N'dbo.Customer', N'U') IS NOT NULL
            BEGIN
                SELECT CustomerID, CustomerFirstName, CustomerLastName, CustomerMiddleName, CustomerEmail, CustomerPhoneNumber
                FROM dbo.Customer
                ORDER BY CustomerID;
            END";

        using var cmd = new SqlCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0).Trim().ToUpperInvariant();
            if (!ValidCustomerIdRegex.IsMatch(id))
                continue;

            var first = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var last = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var middle = reader.IsDBNull(3) ? null : reader.GetString(3);
            var email = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            var phone = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
            var fullName = string.Join(" ", new[] { first, middle, last }.Where(s => !string.IsNullOrWhiteSpace(s)));

            list.Add(new Customer
            {
                Id = id,
                Name = fullName,
                Email = email,
                Phone = phone
            });
        }

        return list;
    }

    public Customer? GetCustomerById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var trimmed = id.Trim().ToUpperInvariant();

        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            IF OBJECT_ID(N'dbo.Customer', N'U') IS NOT NULL
            BEGIN
                SELECT CustomerID, CustomerFirstName, CustomerLastName, CustomerMiddleName, CustomerEmail, CustomerPhoneNumber
                FROM dbo.Customer
                WHERE UPPER(CustomerID) = @Id OR UPPER(CustomerEmail) = @Id;
            END";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", trimmed);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var dbId = reader.GetString(0).Trim().ToUpperInvariant();
        if (!ValidCustomerIdRegex.IsMatch(dbId)) return null;

        var first = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        var last = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
        var middle = reader.IsDBNull(3) ? null : reader.GetString(3);
        var email = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
        var phone = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
        var fullName = string.Join(" ", new[] { first, middle, last }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return new Customer
        {
            Id = dbId,
            Name = fullName,
            Email = email,
            Phone = phone
        };
    }

    public void AddCustomer(Customer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);

        if (GetCustomerById(customer.Id) is not null)
            throw new InvalidOperationException($"A customer with ID '{customer.Id}' already exists.");

        var (first, middle, last) = SplitFullName(customer.Name);
        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            INSERT INTO dbo.Customer (CustomerID, CustomerFirstName, CustomerLastName, CustomerMiddleName, CustomerEmail, CustomerPhoneNumber, CustomerPassword)
            VALUES (@Id, @First, @Last, @Middle, @Email, @Phone, @Password);";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", customer.Id.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@First", first);
        cmd.Parameters.AddWithValue("@Last", last);
        cmd.Parameters.AddWithValue("@Middle", (object?)middle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Email", customer.Email ?? string.Empty);
        cmd.Parameters.AddWithValue("@Phone", string.IsNullOrWhiteSpace(customer.Phone) ? DBNull.Value : customer.Phone);
        cmd.Parameters.AddWithValue("@Password", "AxiomBit@2026");
        cmd.ExecuteNonQuery();
    }

    public bool DeleteCustomer(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        using var conn = CreateConnection();
        conn.Open();

        using var cmd = new SqlCommand("DELETE FROM dbo.Customer WHERE UPPER(CustomerID) = @Id;", conn);
        cmd.Parameters.AddWithValue("@Id", id.Trim().ToUpperInvariant());
        return cmd.ExecuteNonQuery() > 0;
    }

    // ── Specializations ──────────────────────────────────────────────────────

    public IReadOnlyList<SpecializationLookupItem> GetSpecializationLookups()
    {
        var list = new List<SpecializationLookupItem>();
        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            IF OBJECT_ID(N'dbo.EmployeeSpecialization', N'U') IS NOT NULL
            BEGIN
                SELECT SpecializationID,
                       COALESCE(NULLIF(SpecializationName, ''), Specialization, SpecializationID) AS Name,
                       COALESCE(NULLIF(SpecializationDescription, ''), Description) AS Description,
                       COALESCE(NULLIF(ColorTag, ''), 'Blue') AS ColorTag,
                       COALESCE(CreatedAt, SYSUTCDATETIME()) AS CreatedAt,
                       CASE
                           WHEN NULLIF(Specialization, '') IS NOT NULL
                                AND Specialization <> COALESCE(NULLIF(SpecializationName, ''), '')
                           THEN Specialization
                           ELSE 'Photography'
                       END AS Department
                FROM dbo.EmployeeSpecialization
                ORDER BY SpecializationID;
            END";

        using var cmd = new SqlCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SpecializationLookupItem
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                Department = reader.IsDBNull(5) ? "Photography" : reader.GetString(5),
                ColorTag = reader.IsDBNull(3) ? "Blue" : reader.GetString(3),
                CreatedAt = reader.IsDBNull(4) ? DateTime.UtcNow : reader.GetDateTime(4)
            });
        }

        return list;
    }

    public IReadOnlyList<EmployeeSpecializationDto> GetEmployeeSpecializations()
    {
        var list = new List<EmployeeSpecializationDto>();
        using var conn = CreateConnection();
        conn.Open();

        var specsByEmp = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        const string specSql = @"
            IF OBJECT_ID(N'dbo.EmployeeJointSpecialization', N'U') IS NOT NULL
               AND OBJECT_ID(N'dbo.EmployeeSpecialization', N'U') IS NOT NULL
            BEGIN
                SELECT ejs.EmployeeID, COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization, es.SpecializationID)
                FROM dbo.EmployeeJointSpecialization ejs
                INNER JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID;
            END";

        using (var specCmd = new SqlCommand(specSql, conn))
        using (var specReader = specCmd.ExecuteReader())
        {
            while (specReader.Read())
            {
                var empId = specReader.GetString(0);
                var specName = specReader.IsDBNull(1) ? string.Empty : specReader.GetString(1);
                if (string.IsNullOrWhiteSpace(specName)) continue;

                if (!specsByEmp.TryGetValue(empId, out var specs))
                {
                    specs = new List<string>();
                    specsByEmp[empId] = specs;
                }
                if (!specs.Contains(specName, StringComparer.OrdinalIgnoreCase))
                    specs.Add(specName);
            }
        }

        const string empSql = @"
            IF OBJECT_ID(N'dbo.Employee', N'U') IS NOT NULL
            BEGIN
                SELECT EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeMiddleName, EmployeeEmail, EmployeeStatus
                FROM dbo.Employee
                ORDER BY EmployeeID;
            END";

        using var empCmd = new SqlCommand(empSql, conn);
        using var reader = empCmd.ExecuteReader();
        int idx = 1;
        while (reader.Read())
        {
            var empId = reader.GetString(0);
            specsByEmp.TryGetValue(empId, out var specs);
            list.Add(new EmployeeSpecializationDto
            {
                Id = idx++,
                EmployeeId = empId,
                FirstName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                LastName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                MiddleName = reader.IsDBNull(3) ? null : reader.GetString(3),
                Email = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Role = "Employee",
                Status = reader.IsDBNull(5) ? "Active" : reader.GetString(5),
                Specializations = specs ?? new List<string>()
            });
        }

        return list;
    }

    public SpecializationLookupItem AddSpecialization(SpecializationModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var trimmedName = model.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new ArgumentException("Specialization name is required.", nameof(model));

        var trimmedDesc = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
        var department = string.IsNullOrWhiteSpace(model.Department) ? "Photography" : model.Department.Trim();
        var colorTag = string.IsNullOrWhiteSpace(model.ColorTag) ? "Blue" : model.ColorTag.Trim();

        using var conn = CreateConnection();
        conn.Open();

        // If a specialization with the same name already exists, update its metadata and return it
        const string findExistingSql = @"
            SELECT TOP 1 SpecializationID
            FROM dbo.EmployeeSpecialization
            WHERE UPPER(LTRIM(RTRIM(COALESCE(NULLIF(SpecializationName, ''), Specialization)))) = UPPER(@Name)
            ORDER BY SpecializationID;";

        string? existingId = null;
        using (var findCmd = new SqlCommand(findExistingSql, conn))
        {
            findCmd.Parameters.AddWithValue("@Name", trimmedName);
            existingId = findCmd.ExecuteScalar() as string;
        }

        if (!string.IsNullOrWhiteSpace(existingId))
        {
            const string updateSql = @"
                UPDATE dbo.EmployeeSpecialization
                SET SpecializationName = @Name,
                    SpecializationDescription = @Desc,
                    Specialization = @Dept,
                    Description = @Desc,
                    ColorTag = @Color
                WHERE SpecializationID = @Id;";

            using var updateCmd = new SqlCommand(updateSql, conn);
            updateCmd.Parameters.AddWithValue("@Id", existingId);
            updateCmd.Parameters.AddWithValue("@Name", trimmedName);
            updateCmd.Parameters.AddWithValue("@Dept", department);
            updateCmd.Parameters.AddWithValue("@Desc", (object?)trimmedDesc ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("@Color", colorTag);
            updateCmd.ExecuteNonQuery();

            return new SpecializationLookupItem
            {
                Id = existingId,
                Name = trimmedName,
                Description = trimmedDesc,
                Department = department,
                ColorTag = colorTag,
                CreatedAt = DateTime.UtcNow
            };
        }

        // Compute collision-free next SP-### ID
        const string maxNumSql = @"
            SELECT ISNULL(MAX(TRY_CAST(SUBSTRING(SpecializationID, 4, 20) AS INT)), 0)
            FROM dbo.EmployeeSpecialization
            WHERE SpecializationID LIKE 'SP-[0-9]%'
              AND TRY_CAST(SUBSTRING(SpecializationID, 4, 20) AS INT) < 900;";

        int nextNumber = 1;
        using (var maxCmd = new SqlCommand(maxNumSql, conn))
        {
            var scalar = maxCmd.ExecuteScalar();
            if (scalar != null && scalar != DBNull.Value)
            {
                nextNumber = Convert.ToInt32(scalar) + 1;
            }
        }

        var nextId = $"SP-{nextNumber:D3}";

        const string sql = @"
            INSERT INTO dbo.EmployeeSpecialization (SpecializationID, SpecializationName, SpecializationDescription, Specialization, Description, ColorTag, CreatedAt)
            VALUES (@Id, @Name, @Desc, @Dept, @Desc, @Color, SYSUTCDATETIME());";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", nextId);
        cmd.Parameters.AddWithValue("@Name", trimmedName);
        cmd.Parameters.AddWithValue("@Dept", department);
        cmd.Parameters.AddWithValue("@Desc", (object?)trimmedDesc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Color", colorTag);
        cmd.ExecuteNonQuery();

        return new SpecializationLookupItem
        {
            Id = nextId,
            Name = trimmedName,
            Description = trimmedDesc,
            Department = department,
            ColorTag = colorTag,
            CreatedAt = DateTime.UtcNow
        };
    }

    // ── Studio Services & Packages ───────────────────────────────────────────

    public IReadOnlyList<MainServiceItem> GetMainServices()
    {
        var list = new List<MainServiceItem>();
        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL
            BEGIN
                SELECT ServicesID, ServiceName, ServicesDescription
                FROM dbo.Services
                ORDER BY ServicesID;
            END";

        using var cmd = new SqlCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var code = reader.GetString(0);
            var title = reader.GetString(1);
            var desc = reader.IsDBNull(2) ? null : reader.GetString(2);

            list.Add(new MainServiceItem
            {
                Code = code,
                Title = title,
                Category = "Studio",
                CategorySpec = "Photography",
                Deliverables = new List<(string Label, string ColorTag)>(),
                Duration = "1 hr",
                BasePrice = 0m,
                Description = desc,
                Status = "Active"
            });
        }

        return list;
    }

    public IReadOnlyList<MainServiceItem> GetMainServicesSummary()
    {
        var list = new List<MainServiceItem>();
        try
        {
            using var conn = CreateConnection();
            conn.Open();

            const string sql = @"
                IF OBJECT_ID(N'dbo.sp_GetServicesSummary', N'P') IS NOT NULL
                BEGIN
                    EXEC dbo.sp_GetServicesSummary;
                END
                ELSE IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL
                BEGIN
                    SELECT ServicesID, ServiceName, ServicesDescription
                    FROM dbo.Services
                    ORDER BY ServicesID;
                END";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var code = reader.GetString(0);
                var title = reader.GetString(1);
                var desc = reader.IsDBNull(2) ? null : reader.GetString(2);

                list.Add(new MainServiceItem
                {
                    Code = code,
                    Title = title,
                    Category = "Studio",
                    CategorySpec = "Photography",
                    Deliverables = new List<(string Label, string ColorTag)>(),
                    Duration = "1 hr",
                    BasePrice = 0m,
                    Description = desc,
                    Status = "Active"
                });
            }
        }
        catch
        {
            return GetMainServices();
        }

        return list;
    }

    public (byte[]? Image, string? MimeType) GetServiceImage(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return (null, null);
        try
        {
            using var conn = CreateConnection();
            conn.Open();

            const string sql = @"
                IF OBJECT_ID(N'dbo.sp_GetServiceDetailWithImage', N'P') IS NOT NULL
                BEGIN
                    EXEC dbo.sp_GetServiceDetailWithImage @ServicesID = @Id;
                END
                ELSE IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL
                BEGIN
                    SELECT ServiceImage, ServiceImageMimeType
                    FROM dbo.Services
                    WHERE UPPER(ServicesID) = UPPER(@Id);
                END";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", code.Trim());
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                if (reader.FieldCount >= 5)
                {
                    var bytes = reader.IsDBNull(3) ? null : (byte[])reader[3];
                    var mime = reader.IsDBNull(4) ? "image/jpeg" : reader.GetString(4);
                    return (bytes, mime);
                }
                else
                {
                    var bytes = reader.IsDBNull(0) ? null : (byte[])reader[0];
                    var mime = reader.IsDBNull(1) ? "image/jpeg" : reader.GetString(1);
                    return (bytes, mime);
                }
            }
        }
        catch
        {
        }
        return (null, null);
    }

    public void UpdateServiceImage(string code, byte[] imageBytes, string mimeType, string? requestingAdminId = null)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            IF OBJECT_ID(N'dbo.sp_UpdateServiceImage', N'P') IS NOT NULL
            BEGIN
                EXEC dbo.sp_UpdateServiceImage @ServicesID = @Id, @ServiceImage = @Img, @ServiceImageMimeType = @Mime, @UpdatedBy = @By;
            END
            ELSE IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL
            BEGIN
                UPDATE dbo.Services
                SET ServiceImage = @Img,
                    ServiceImageMimeType = @Mime
                WHERE UPPER(ServicesID) = UPPER(@Id);
            END";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", code.Trim());
        cmd.Parameters.AddWithValue("@Img", imageBytes);
        cmd.Parameters.AddWithValue("@Mime", string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType);
        cmd.Parameters.AddWithValue("@By", (object?)requestingAdminId ?? "Admin");
        cmd.ExecuteNonQuery();
    }

    public MainServiceItem AddMainService(MainServiceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        using var conn = CreateConnection();
        conn.Open();

        var existingCount = GetMainServices().Count;
        var code = string.IsNullOrWhiteSpace(model.Code) ? $"SRV-{existingCount + 1:D3}" : model.Code.Trim();

        const string sql = @"
            INSERT INTO dbo.Services (ServicesID, ServiceName, ServicesDescription)
            VALUES (@Id, @Name, @Desc);";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", code);
        cmd.Parameters.AddWithValue("@Name", model.Name.Trim());
        cmd.Parameters.AddWithValue("@Desc", string.IsNullOrWhiteSpace(model.Description) ? model.Category : model.Description.Trim());
        cmd.ExecuteNonQuery();

        return new MainServiceItem
        {
            Code = code,
            Title = model.Name.Trim(),
            Category = string.IsNullOrWhiteSpace(model.Category) ? "Studio" : model.Category,
            CategorySpec = string.IsNullOrWhiteSpace(model.CategorySpec) ? "Photography" : model.CategorySpec,
            Deliverables = model.Deliverables ?? new(),
            Duration = $"{model.DurationHours:0.#} hrs",
            BasePrice = model.BasePrice,
            Description = model.Description,
            Status = model.Status
        };
    }

    public IReadOnlyList<PackageServiceItem> GetPackageServices()
    {
        var list = new List<PackageServiceItem>();
        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL
            BEGIN
                SELECT ComboPackageID, ComboPackageName, ComboPackageDescription
                FROM dbo.ComboPackage
                ORDER BY ComboPackageID;
            END";

        using var cmd = new SqlCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new PackageServiceItem
            {
                Code = reader.GetString(0),
                Title = reader.GetString(1),
                BundleCategory = "Package",
                Inclusions = new List<(string Label, string ColorTag)>(),
                Price = "$0",
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                Status = "Active"
            });
        }

        return list;
    }

    public IReadOnlyList<PackageServiceItem> GetPackageServicesSummary()
    {
        var list = new List<PackageServiceItem>();
        try
        {
            using var conn = CreateConnection();
            conn.Open();

            const string sql = @"
                IF OBJECT_ID(N'dbo.sp_GetComboPackagesSummary', N'P') IS NOT NULL
                BEGIN
                    EXEC dbo.sp_GetComboPackagesSummary;
                END
                ELSE IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL
                BEGIN
                    SELECT ComboPackageID, ComboPackageName, ComboPackageDescription
                    FROM dbo.ComboPackage
                    ORDER BY ComboPackageID;
                END";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new PackageServiceItem
                {
                    Code = reader.GetString(0),
                    Title = reader.GetString(1),
                    BundleCategory = "Package",
                    Inclusions = new List<(string Label, string ColorTag)>(),
                    Price = "$0",
                    Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Status = "Active"
                });
            }
        }
        catch
        {
            return GetPackageServices();
        }

        return list;
    }

    public (byte[]? Image, string? MimeType) GetComboPackageImage(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return (null, null);
        try
        {
            using var conn = CreateConnection();
            conn.Open();

            const string sql = @"
                IF OBJECT_ID(N'dbo.sp_GetComboPackageDetailWithImage', N'P') IS NOT NULL
                BEGIN
                    EXEC dbo.sp_GetComboPackageDetailWithImage @ComboPackageID = @Id;
                END
                ELSE IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL
                BEGIN
                    SELECT ComboPackageImage, ComboPackageImageMimeType
                    FROM dbo.ComboPackage
                    WHERE UPPER(ComboPackageID) = UPPER(@Id);
                END";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", code.Trim());
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                if (reader.FieldCount >= 5)
                {
                    var bytes = reader.IsDBNull(3) ? null : (byte[])reader[3];
                    var mime = reader.IsDBNull(4) ? "image/jpeg" : reader.GetString(4);
                    return (bytes, mime);
                }
                else
                {
                    var bytes = reader.IsDBNull(0) ? null : (byte[])reader[0];
                    var mime = reader.IsDBNull(1) ? "image/jpeg" : reader.GetString(1);
                    return (bytes, mime);
                }
            }
        }
        catch
        {
        }
        return (null, null);
    }

    public void UpdateComboPackageImage(string code, byte[] imageBytes, string mimeType, string? requestingAdminId = null)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            IF OBJECT_ID(N'dbo.sp_UpdateComboPackageImage', N'P') IS NOT NULL
            BEGIN
                EXEC dbo.sp_UpdateComboPackageImage @ComboPackageID = @Id, @ComboPackageImage = @Img, @ComboPackageImageMimeType = @Mime, @UpdatedBy = @By;
            END
            ELSE IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL
            BEGIN
                UPDATE dbo.ComboPackage
                SET ComboPackageImage = @Img,
                    ComboPackageImageMimeType = @Mime
                WHERE UPPER(ComboPackageID) = UPPER(@Id);
            END";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", code.Trim());
        cmd.Parameters.AddWithValue("@Img", imageBytes);
        cmd.Parameters.AddWithValue("@Mime", string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType);
        cmd.Parameters.AddWithValue("@By", (object?)requestingAdminId ?? "Admin");
        cmd.ExecuteNonQuery();
    }

    public PackageServiceItem AddPackageService(PackageServiceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        using var conn = CreateConnection();
        conn.Open();

        var existingCount = GetPackageServices().Count;
        var code = string.IsNullOrWhiteSpace(model.Code) ? $"PKG-{existingCount + 1:D3}" : model.Code.Trim();

        const string sql = @"
            INSERT INTO dbo.ComboPackage (ComboPackageID, ComboPackageName, ComboPackageDescription)
            VALUES (@Id, @Name, @Desc);";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", code);
        cmd.Parameters.AddWithValue("@Name", model.Name.Trim());
        cmd.Parameters.AddWithValue("@Desc", string.IsNullOrWhiteSpace(model.Description) ? model.TargetCategory : model.Description.Trim());
        cmd.ExecuteNonQuery();

        return new PackageServiceItem
        {
            Code = code,
            Title = model.Name.Trim(),
            BundleCategory = string.IsNullOrWhiteSpace(model.TargetCategory) ? "Package" : model.TargetCategory,
            Inclusions = model.Inclusions ?? new(),
            Price = $"${model.Price:N0}",
            SavingsNote = model.SavingsNote,
            Description = model.Description,
            Status = model.Status
        };
    }

    // ── 4-Step Wizard API Client & Service Bundle Integration ─────────────────

    public async Task<string> GetNextComboPackageIdAsync(CancellationToken ct = default)
    {
        // 1. Attempt API Endpoint Call
        if (_httpClientFactory != null)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("AevumApi.Authorized");
                var response = await client.GetAsync("api/packages/next-id", ct);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: ct);
                    if (result.TryGetProperty("packageId", out var pkgId) || result.TryGetProperty("nextId", out pkgId))
                    {
                        var val = pkgId.GetString();
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }
                }
            }
            catch
            {
                // API unavailable; fall through to database query
            }
        }

        // 2. Database Fallback (Atomic SQL Server Sequence or Stored Procedure)
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            await using var cmd = new SqlCommand("dbo.sp_GetNextComboPackageId", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            var outParam = new SqlParameter("@NextPackageID", SqlDbType.NVarChar, 30)
            {
                Direction = ParameterDirection.Output
            };
            cmd.Parameters.Add(outParam);
            await cmd.ExecuteNonQueryAsync(ct);

            var nextId = outParam.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(nextId)) return nextId;
        }
        catch
        {
            // Direct query fallback
        }

        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            const string fallbackSql = @"
                IF EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_ComboPackageID')
                BEGIN
                    DECLARE @CurVal SQL_VARIANT;
                    SELECT @CurVal = current_value FROM sys.sequences WHERE name = 'Seq_ComboPackageID';
                    DECLARE @NextNum INT = 1;
                    IF @CurVal IS NOT NULL
                    BEGIN
                        DECLARE @ExistingCount INT = 0;
                        SELECT @ExistingCount = COUNT(*) FROM dbo.ComboPackage WHERE ComboPackageID LIKE 'PKG-%';
                        IF @ExistingCount > 0 OR CAST(@CurVal AS INT) > 1
                            SET @NextNum = CAST(@CurVal AS INT) + 1;
                    END
                    SELECT 'PKG-' + RIGHT('0000' + CAST(@NextNum AS VARCHAR(10)), 4);
                END
                ELSE
                BEGIN
                    DECLARE @MaxVal INT = 0;
                    IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL
                    BEGIN
                        SELECT @MaxVal = ISNULL(MAX(TRY_CAST(SUBSTRING(ComboPackageID, 5, 20) AS INT)), 0)
                        FROM dbo.ComboPackage WITH (NOLOCK)
                        WHERE ComboPackageID LIKE 'PKG-%';
                    END
                    SELECT 'PKG-' + RIGHT('0000' + CAST(@MaxVal + 1 AS VARCHAR(10)), 4);
                END";

            await using var cmd = new SqlCommand(fallbackSql, conn);
            var scalar = await cmd.ExecuteScalarAsync(ct);
            return scalar?.ToString() ?? "PKG-0001";
        }
        catch
        {
            var fallbackCount = GetPackageServices().Count + 1;
            return $"PKG-{fallbackCount:D4}";
        }
    }

    public async Task<IReadOnlyList<CategoryLookupModel>> GetServiceCategoriesLookupAsync(CancellationToken ct = default)
    {
        // 1. Attempt API Endpoint Call
        if (_httpClientFactory != null)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("AevumApi.Authorized");
                var response = await client.GetAsync("api/packages/categories", ct);
                if (response.IsSuccessStatusCode)
                {
                    var apiList = await response.Content.ReadFromJsonAsync<List<CategoryLookupModel>>(cancellationToken: ct);
                    if (apiList != null && apiList.Count > 0)
                        return apiList;
                }
            }
            catch { }
        }

        // 2. Database Fallback
        var list = new List<CategoryLookupModel>();
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            const string catSql = @"
                IF OBJECT_ID(N'dbo.ServiceCategory', N'U') IS NOT NULL
                BEGIN
                    SELECT ServiceCategoryID, ServiceCategoryName, ServiceCategoryDescription
                    FROM dbo.ServiceCategory
                    ORDER BY ServiceCategoryName;
                END";

            await using var cmd = new SqlCommand(catSql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new CategoryLookupModel
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? null : reader.GetString(2)
                });
            }
        }
        catch { }

        // If database table is empty or unpopulated, supply standard studio service categories
        if (list.Count == 0)
        {
            list.AddRange(new[]
            {
                new CategoryLookupModel { Id = "CAT-PORT", Name = "Portrait Photography", Description = "Studio headshots, family portraits, and graduation" },
                new CategoryLookupModel { Id = "CAT-WEDD", Name = "Wedding & Reception", Description = "Pre-nuptial, ceremony, and all-day wedding coverage" },
                new CategoryLookupModel { Id = "CAT-COMM", Name = "Commercial & Editorial", Description = "Corporate branding, product showcases, and fashion" },
                new CategoryLookupModel { Id = "CAT-EVNT", Name = "Event Photography", Description = "Birthdays, corporate events, and live celebrations" }
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<BundleServiceOption>> GetAvailableServicesForBundlingAsync(CancellationToken ct = default)
    {
        // 1. Attempt API Endpoint Call
        if (_httpClientFactory != null)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("AevumApi.Authorized");
                var response = await client.GetAsync("api/packages/available-services", ct);
                if (response.IsSuccessStatusCode)
                {
                    var apiList = await response.Content.ReadFromJsonAsync<List<BundleServiceOption>>(cancellationToken: ct);
                    if (apiList != null && apiList.Count > 0)
                        return apiList;
                }
            }
            catch { }
        }

        // 2. Database Fallback (Stored Procedure or Direct Query)
        var list = new List<BundleServiceOption>();
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            const string svcSql = @"
                IF OBJECT_ID(N'dbo.sp_GetAvailableServicesForBundling', N'P') IS NOT NULL
                BEGIN
                    EXEC dbo.sp_GetAvailableServicesForBundling;
                END
                ELSE
                BEGIN
                    IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL
                    BEGIN
                        SELECT ServicesID, ServiceName, ServicesDescription, ServicePrice, 'Main' AS ServiceType,
                               CAST(CASE WHEN ServiceImage IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImage
                        FROM dbo.Services
                    END
                    UNION ALL
                    IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NOT NULL
                    BEGIN
                        SELECT AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription, 0.00 AS ServicePrice, 'Additional' AS ServiceType,
                               CAST(CASE WHEN AdditionalServiceImage IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImage
                        FROM dbo.AdditionalServices
                    END
                    ORDER BY ServiceType DESC, ServiceName ASC;
                END";

            await using var cmd = new SqlCommand(svcSql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new BundleServiceOption
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price = reader.IsDBNull(3) ? 0m : reader.GetDecimal(3),
                    ServiceType = reader.GetString(4),
                    HasImage = !reader.IsDBNull(5) && reader.GetBoolean(5)
                });
            }
        }
        catch { }

        // If database is empty, provide default options
        if (list.Count == 0)
        {
            foreach (var m in GetMainServices())
            {
                list.Add(new BundleServiceOption
                {
                    Id = m.Code,
                    Name = m.Title,
                    Description = m.Description,
                    Price = m.BasePrice,
                    ServiceType = "Main",
                    HasImage = false
                });
            }

            foreach (var a in GetAdditionalServices())
            {
                list.Add(new BundleServiceOption
                {
                    Id = a.Code,
                    Name = a.Title,
                    Description = a.Description,
                    Price = 50.00m,
                    ServiceType = "Additional",
                    HasImage = false
                });
            }
        }

        return list;
    }

    public async Task<PackageServiceItem> CreateComboPackageViaApiAsync(
        PackageWizardFormModel formModel,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(formModel);

        // 1. Attempt Secure Multipart/Form-Data API Submission
        if (_httpClientFactory != null)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("AevumApi.Authorized");

                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(formModel.Name.Trim()), "Name");
                form.Add(new StringContent(formModel.Description?.Trim() ?? string.Empty), "Description");
                form.Add(new StringContent(formModel.CategoryId.Trim()), "CategoryId");
                form.Add(new StringContent(formModel.Price.ToString(System.Globalization.CultureInfo.InvariantCulture)), "Price");

                if (formModel.SelectedServiceIds != null)
                {
                    foreach (var sId in formModel.SelectedServiceIds)
                    {
                        form.Add(new StringContent(sId), "SelectedServiceIds");
                    }
                    var json = System.Text.Json.JsonSerializer.Serialize(formModel.SelectedServiceIds);
                    form.Add(new StringContent(json), "SelectedServiceIdsRaw");
                }

                if (formModel.ImageBytes != null && formModel.ImageBytes.Length > 0)
                {
                    var fileContent = new ByteArrayContent(formModel.ImageBytes);
                    var mime = string.IsNullOrWhiteSpace(formModel.ImageMimeType) ? "image/jpeg" : formModel.ImageMimeType;
                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
                    form.Add(fileContent, "ImageFile", formModel.ImageFileName ?? "package-hero.jpg");
                }

                var response = await client.PostAsync("api/packages", form, ct);
                if (response.IsSuccessStatusCode)
                {
                    var created = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: ct);
                    var packageId = created.TryGetProperty("packageId", out var pid) ? pid.GetString() ?? formModel.GeneratedPackageId : formModel.GeneratedPackageId;
                    var title = created.TryGetProperty("name", out var ptitle) ? ptitle.GetString() ?? formModel.Name : formModel.Name;

                    return new PackageServiceItem
                    {
                        Code = packageId,
                        Title = title,
                        BundleCategory = formModel.CategoryName,
                        Description = formModel.Description,
                        Price = $"${formModel.Price:N0}",
                        Status = "Active"
                    };
                }
                else
                {
                    var errBody = await response.Content.ReadAsStringAsync(ct);
                    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        throw new InvalidOperationException($"API Validation Failed: {errBody}");
                    }
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                // Fall through to database transactional save
            }
        }

        // 2. Direct Database Transactional Save (Anti-Race Condition via Sequence / Stored Procedure)
        var packageCode = string.IsNullOrWhiteSpace(formModel.GeneratedPackageId) 
            ? await GetNextComboPackageIdAsync(ct) 
            : formModel.GeneratedPackageId;

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        const string prepSql = @"
            IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.ComboPackage', N'ComboPackagePrice') IS NULL
                    ALTER TABLE dbo.ComboPackage ADD ComboPackagePrice decimal(18,2) NOT NULL CONSTRAINT DF_ComboPackage_Price DEFAULT 0.00;
            END;
            IF OBJECT_ID(N'dbo.ComboPackageJoin', N'U') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.ComboPackageJoin ALTER COLUMN ServicesID nvarchar(30) NULL;
            END;";

        await using (var prepCmd = new SqlCommand(prepSql, conn))
        {
            await prepCmd.ExecuteNonQueryAsync(ct);
        }

        await using var trans = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            // Acquire transaction lock
            const string lockSql = @"
                DECLARE @res INT;
                EXEC @res = sp_getapplock @Resource = 'ComboPackage_IdGen_Lock', @LockMode = 'Exclusive', @LockOwner = 'Transaction';
                SELECT @res;";
            await using (var lockCmd = new SqlCommand(lockSql, conn, trans))
            {
                await lockCmd.ExecuteScalarAsync(ct);
            }

            // Insert into dbo.ComboPackage
            const string insertSql = @"
                INSERT INTO dbo.ComboPackage (
                    ComboPackageID, ComboPackageName, ComboPackageDescription, ComboPackagePrice, ComboPackageImage, ComboPackageImageMimeType
                )
                VALUES (@Id, @Name, @Desc, @Price, @Img, @Mime);";

            await using (var cmd = new SqlCommand(insertSql, conn, trans))
            {
                cmd.Parameters.AddWithValue("@Id", packageCode);
                cmd.Parameters.AddWithValue("@Name", formModel.Name.Trim());
                cmd.Parameters.AddWithValue("@Desc", formModel.Description?.Trim() ?? string.Empty);
                cmd.Parameters.AddWithValue("@Price", formModel.Price);
                cmd.Parameters.Add("@Img", SqlDbType.VarBinary, -1).Value = (object?)formModel.ImageBytes ?? DBNull.Value;
                cmd.Parameters.AddWithValue("@Mime", string.IsNullOrWhiteSpace(formModel.ImageMimeType) ? "image/jpeg" : formModel.ImageMimeType.Trim());
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Insert Category mapping into dbo.ServiceCategoryJoins
            if (!string.IsNullOrWhiteSpace(formModel.CategoryId))
            {
                const string catJoinSql = @"
                    IF NOT EXISTS (SELECT 1 FROM dbo.ServiceCategory WHERE ServiceCategoryID = @CatId)
                    BEGIN
                        INSERT INTO dbo.ServiceCategory (ServiceCategoryID, ServiceCategoryName, ServiceCategoryDescription)
                        VALUES (@CatId, @CatId, @CatId);
                    END;
                    INSERT INTO dbo.ServiceCategoryJoins (ServiceCategoryJoinID, ServiceCategoryID, ServicesID, AdditionalServicesID, ComboPackageID)
                    VALUES ('SCJ-' + SUBSTRING(REPLACE(CAST(NEWID() AS nvarchar(36)), '-', ''), 1, 10), @CatId, NULL, NULL, @PkgId);";

                await using var catCmd = new SqlCommand(catJoinSql, conn, trans);
                catCmd.Parameters.AddWithValue("@CatId", formModel.CategoryId.Trim());
                catCmd.Parameters.AddWithValue("@PkgId", packageCode);
                await catCmd.ExecuteNonQueryAsync(ct);
            }

            // Insert Many-to-Many mappings into dbo.ComboPackageJoin
            if (formModel.SelectedServiceIds != null && formModel.SelectedServiceIds.Count > 0)
            {
                foreach (var svcId in formModel.SelectedServiceIds)
                {
                    if (string.IsNullOrWhiteSpace(svcId)) continue;

                    const string joinSql = @"
                        DECLARE @JID nvarchar(30) = 'CPJ-' + SUBSTRING(REPLACE(CAST(NEWID() AS nvarchar(36)), '-', ''), 1, 10);
                        IF EXISTS (SELECT 1 FROM dbo.Services WHERE ServicesID = @SvcId)
                        BEGIN
                            INSERT INTO dbo.ComboPackageJoin (ComboPackageJoinID, ComboPackageID, ServicesID, AdditionalServicesID)
                            VALUES (@JID, @PkgId, @SvcId, NULL);
                        END
                        ELSE IF EXISTS (SELECT 1 FROM dbo.AdditionalServices WHERE AdditionalServicesID = @SvcId)
                        BEGIN
                            INSERT INTO dbo.ComboPackageJoin (ComboPackageJoinID, ComboPackageID, ServicesID, AdditionalServicesID)
                            VALUES (@JID, @PkgId, NULL, @SvcId);
                        END
                        ELSE
                        BEGIN
                            INSERT INTO dbo.ComboPackageJoin (ComboPackageJoinID, ComboPackageID, ServicesID, AdditionalServicesID)
                            VALUES (@JID, @PkgId, @SvcId, NULL);
                        END;";

                    await using var joinCmd = new SqlCommand(joinSql, conn, trans);
                    joinCmd.Parameters.AddWithValue("@PkgId", packageCode);
                    joinCmd.Parameters.AddWithValue("@SvcId", svcId.Trim());
                    await joinCmd.ExecuteNonQueryAsync(ct);
                }
            }

            await trans.CommitAsync(ct);

            return new PackageServiceItem
            {
                Code = packageCode,
                Title = formModel.Name.Trim(),
                BundleCategory = formModel.CategoryName,
                Description = formModel.Description,
                Price = $"${formModel.Price:N0}",
                Status = "Active"
            };
        }
        catch
        {
            await trans.RollbackAsync(ct);
            throw;
        }
    }

    public IReadOnlyList<AddOnServiceItem> GetAdditionalServices()
    {
        var list = new List<AddOnServiceItem>();
        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NOT NULL
            BEGIN
                SELECT AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription
                FROM dbo.AdditionalServices
                ORDER BY AdditionalServicesID;
            END";

        using var cmd = new SqlCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new AddOnServiceItem
            {
                Code = reader.GetString(0),
                Title = reader.GetString(1),
                TypeCategory = "Add-On",
                FilterCategory = "Add-On",
                PriceText = "$0",
                CompatibleServices = new List<(string Label, string ColorTag)>(),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                Status = "Active"
            });
        }

        return list;
    }

    public IReadOnlyList<AddOnServiceItem> GetAdditionalServicesSummary()
    {
        var list = new List<AddOnServiceItem>();
        try
        {
            using var conn = CreateConnection();
            conn.Open();

            const string sql = @"
                IF OBJECT_ID(N'dbo.sp_GetAdditionalServicesSummary', N'P') IS NOT NULL
                BEGIN
                    EXEC dbo.sp_GetAdditionalServicesSummary;
                END
                ELSE IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NOT NULL
                BEGIN
                    SELECT AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription
                    FROM dbo.AdditionalServices
                    ORDER BY AdditionalServicesID;
                END";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new AddOnServiceItem
                {
                    Code = reader.GetString(0),
                    Title = reader.GetString(1),
                    TypeCategory = "Add-On",
                    FilterCategory = "Add-On",
                    PriceText = "$0",
                    CompatibleServices = new List<(string Label, string ColorTag)>(),
                    Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Status = "Active"
                });
            }
        }
        catch
        {
            return GetAdditionalServices();
        }

        return list;
    }

    public (byte[]? Image, string? MimeType) GetAdditionalServiceImage(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return (null, null);
        try
        {
            using var conn = CreateConnection();
            conn.Open();

            const string sql = @"
                IF OBJECT_ID(N'dbo.sp_GetAdditionalServiceDetailWithImage', N'P') IS NOT NULL
                BEGIN
                    EXEC dbo.sp_GetAdditionalServiceDetailWithImage @AdditionalServicesID = @Id;
                END
                ELSE IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NOT NULL
                BEGIN
                    SELECT AdditionalServiceImage, AdditionalServiceImageMimeType
                    FROM dbo.AdditionalServices
                    WHERE UPPER(AdditionalServicesID) = UPPER(@Id);
                END";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", code.Trim());
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                if (reader.FieldCount >= 5)
                {
                    var bytes = reader.IsDBNull(3) ? null : (byte[])reader[3];
                    var mime = reader.IsDBNull(4) ? "image/jpeg" : reader.GetString(4);
                    return (bytes, mime);
                }
                else
                {
                    var bytes = reader.IsDBNull(0) ? null : (byte[])reader[0];
                    var mime = reader.IsDBNull(1) ? "image/jpeg" : reader.GetString(1);
                    return (bytes, mime);
                }
            }
        }
        catch
        {
        }
        return (null, null);
    }

    public void UpdateAdditionalServiceImage(string code, byte[] imageBytes, string mimeType, string? requestingAdminId = null)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            IF OBJECT_ID(N'dbo.sp_UpdateAdditionalServiceImage', N'P') IS NOT NULL
            BEGIN
                EXEC dbo.sp_UpdateAdditionalServiceImage @AdditionalServicesID = @Id, @AdditionalServiceImage = @Img, @AdditionalServiceImageMimeType = @Mime, @UpdatedBy = @By;
            END
            ELSE IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NOT NULL
            BEGIN
                UPDATE dbo.AdditionalServices
                SET AdditionalServiceImage = @Img,
                    AdditionalServiceImageMimeType = @Mime
                WHERE UPPER(AdditionalServicesID) = UPPER(@Id);
            END";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", code.Trim());
        cmd.Parameters.AddWithValue("@Img", imageBytes);
        cmd.Parameters.AddWithValue("@Mime", string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType);
        cmd.Parameters.AddWithValue("@By", (object?)requestingAdminId ?? "Admin");
        cmd.ExecuteNonQuery();
    }

    public AddOnServiceItem AddAdditionalService(AddOnServiceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        using var conn = CreateConnection();
        conn.Open();

        var existingCount = GetAdditionalServices().Count;
        var code = string.IsNullOrWhiteSpace(model.Code) ? $"ADD-{existingCount + 1:D3}" : model.Code.Trim();

        const string sql = @"
            INSERT INTO dbo.AdditionalServices (AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription)
            VALUES (@Id, @Name, @Desc);";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", code);
        cmd.Parameters.AddWithValue("@Name", model.Title.Trim());
        cmd.Parameters.AddWithValue("@Desc", string.IsNullOrWhiteSpace(model.Description) ? model.TypeCategory : model.Description.Trim());
        cmd.ExecuteNonQuery();

        return new AddOnServiceItem
        {
            Code = code,
            Title = model.Title.Trim(),
            TypeCategory = string.IsNullOrWhiteSpace(model.TypeCategory) ? "Add-On" : model.TypeCategory,
            FilterCategory = string.IsNullOrWhiteSpace(model.FilterCategory) ? "Add-On" : model.FilterCategory,
            PriceText = $"${model.Rate:N0} {model.RateUnit}",
            CompatibleServices = model.CompatibleServices ?? new(),
            Description = model.Description,
            Status = model.Status
        };
    }

    // ── Equipment ────────────────────────────────────────────────────────────

    public List<EquipmentRowDto> GetEquipmentRows()
    {
        var list = new List<EquipmentRowDto>();
        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            IF OBJECT_ID(N'dbo.Equipment', N'U') IS NOT NULL
            BEGIN
                SELECT eq.EquipmentID,
                       eq.EquipmentName,
                       eq.EquipmentTypeID,
                       COALESCE(et.EquipmentTypeName, eq.EquipmentTypeID) AS TypeName,
                       COALESCE(eq.SerialNo, eq.EquipmentID) AS SerialNo,
                       eq.EquipmentStatus,
                       eq.EquipmentImage
                FROM dbo.Equipment eq
                LEFT JOIN dbo.EquipmentType et ON et.EquipmentTypeID = eq.EquipmentTypeID
                ORDER BY eq.EquipmentID;
            END";

        using var cmd = new SqlCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new EquipmentRowDto(
                Id: reader.GetString(0),
                Name: reader.GetString(1),
                TypeId: reader.GetString(2),
                TypeName: reader.IsDBNull(3) ? "General" : reader.GetString(3),
                SerialNumber: reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Status: reader.IsDBNull(5) ? "Available" : reader.GetString(5),
                Image: reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return list;
    }

    public List<EquipmentTypeRowDto> GetEquipmentTypes()
    {
        var list = new List<EquipmentTypeRowDto>();
        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            IF OBJECT_ID(N'dbo.EquipmentType', N'U') IS NOT NULL
            BEGIN
                SELECT et.EquipmentTypeID,
                       et.EquipmentTypeName,
                       COUNT(eq.EquipmentID) AS TotalUnits,
                       SUM(CASE WHEN eq.EquipmentStatus = 'Available' THEN 1 ELSE 0 END) AS AvailableUnits
                FROM dbo.EquipmentType et
                LEFT JOIN dbo.Equipment eq ON eq.EquipmentTypeID = et.EquipmentTypeID
                GROUP BY et.EquipmentTypeID, et.EquipmentTypeName
                ORDER BY et.EquipmentTypeID;
            END";

        using var cmd = new SqlCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new EquipmentTypeRowDto(
                Id: reader.GetString(0),
                Name: reader.GetString(1),
                TotalUnits: reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                AvailableUnits: reader.IsDBNull(3) ? 0 : reader.GetInt32(3)));
        }

        return list;
    }

    public EquipmentRowDto AddEquipment(string name, string typeName, string serialNo, string status = "Available")
    {
        using var conn = CreateConnection();
        conn.Open();

        var cleanType = string.IsNullOrWhiteSpace(typeName) ? "Camera" : typeName.Trim();
        var typeId = $"ET-{cleanType.ToUpperInvariant()[..Math.Min(3, cleanType.Length)]}";

        const string ensureTypeSql = @"
            IF NOT EXISTS (SELECT 1 FROM dbo.EquipmentType WHERE EquipmentTypeID = @TypeId)
            BEGIN
                INSERT INTO dbo.EquipmentType (EquipmentTypeID, EquipmentTypeName)
                VALUES (@TypeId, @TypeName);
            END";

        using (var typeCmd = new SqlCommand(ensureTypeSql, conn))
        {
            typeCmd.Parameters.AddWithValue("@TypeId", typeId);
            typeCmd.Parameters.AddWithValue("@TypeName", cleanType);
            typeCmd.ExecuteNonQuery();
        }

        var existingCount = GetEquipmentRows().Count;
        var eqId = $"EQ-{existingCount + 1:D3}";
        var cleanSerial = string.IsNullOrWhiteSpace(serialNo) ? eqId : serialNo.Trim();

        const string insertSql = @"
            INSERT INTO dbo.Equipment (EquipmentID, EquipmentName, EquipmentTypeID, EquipmentStatus, SerialNo)
            VALUES (@Id, @Name, @TypeId, @Status, @SerialNo);";

        using (var cmd = new SqlCommand(insertSql, conn))
        {
            cmd.Parameters.AddWithValue("@Id", eqId);
            cmd.Parameters.AddWithValue("@Name", name.Trim());
            cmd.Parameters.AddWithValue("@TypeId", typeId);
            cmd.Parameters.AddWithValue("@Status", string.IsNullOrWhiteSpace(status) ? "Available" : status);
            cmd.Parameters.AddWithValue("@SerialNo", cleanSerial);
            cmd.ExecuteNonQuery();
        }

        return new EquipmentRowDto(eqId, name.Trim(), typeId, cleanType, cleanSerial, status, null);
    }

    // ── Bookings, Photos & Audit Logs ────────────────────────────────────────

    public List<BookingRowDto> GetBookings(string? customerId = null)
    {
        var list = new List<BookingRowDto>();
        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
            BEGIN
                SELECT ea.EventAppointmentID,
                       ea.EventName,
                       ea.EventDescription,
                       ea.CustomerID,
                       COALESCE(c.CustomerFirstName + ' ' + c.CustomerLastName, ea.CustomerID) AS CustomerName,
                       ea.EventDate,
                       ea.EventTime,
                       ea.EventStatus,
                       ea.CreatedAt
                FROM dbo.EventAppointment ea
                LEFT JOIN dbo.Customer c ON c.CustomerID = ea.CustomerID
                WHERE (@CustomerId IS NULL OR UPPER(ea.CustomerID) = UPPER(@CustomerId) OR UPPER(c.CustomerEmail) = UPPER(@CustomerId))
                ORDER BY ea.EventDate DESC, ea.EventTime DESC;
            END";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CustomerId", (object?)customerId ?? DBNull.Value);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new BookingRowDto(
                Id: reader.GetString(0),
                EventName: reader.GetString(1),
                EventDescription: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                CustomerId: reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                CustomerName: reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                EventDate: reader.GetDateTime(5),
                EventTime: reader.GetFieldValue<TimeSpan>(6),
                Status: reader.IsDBNull(7) ? "Scheduled" : reader.GetString(7),
                CreatedAt: reader.IsDBNull(8) ? DateTime.UtcNow : reader.GetDateTime(8)));
        }

        return list;
    }

    public void UpdateBookingStatus(string eventAppointmentId, string newStatus)
    {
        if (string.IsNullOrWhiteSpace(eventAppointmentId) || string.IsNullOrWhiteSpace(newStatus))
            return;

        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            UPDATE dbo.EventAppointment
            SET EventStatus = @Status
            WHERE EventAppointmentID = @Id;";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", eventAppointmentId.Trim());
        cmd.Parameters.AddWithValue("@Status", newStatus.Trim());
        cmd.ExecuteNonQuery();
    }

    public List<PhotoRowDto> GetPhotos(string? bookingId = null)
    {
        var list = new List<PhotoRowDto>();
        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            IF OBJECT_ID(N'dbo.Photos', N'U') IS NOT NULL
            BEGIN
                SELECT Id, BookingId, FileName, StoragePath, UploadedById, UploadedAt, IsWatermarked, IsFinalDelivery
                FROM dbo.Photos
                WHERE (@BookingId IS NULL OR BookingId = @BookingId)
                ORDER BY UploadedAt DESC;
            END";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@BookingId", (object?)bookingId ?? DBNull.Value);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new PhotoRowDto(
                Id: reader.GetInt32(0),
                BookingId: reader.GetString(1),
                FileName: reader.GetString(2),
                StoragePath: reader.GetString(3),
                UploadedById: reader.GetString(4),
                UploadedAt: reader.GetDateTime(5),
                IsWatermarked: reader.GetBoolean(6),
                IsFinalDelivery: reader.GetBoolean(7)));
        }

        return list;
    }

    public List<AuditLogRowDto> GetAuditLogs(string? userId = null)
    {
        var list = new List<AuditLogRowDto>();
        try
        {
            using var conn = CreateConnection();
            conn.Open();

            const string sql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    SELECT TOP 20 HistoryLogID, ActionType, EntityName, EntityID, ChangedBy, ActionDetails, LoggedAt
                    FROM dbo.HistoryLog
                    WHERE (@UserId IS NULL OR UPPER(ChangedBy) = UPPER(@UserId))
                    ORDER BY LoggedAt DESC;
                END
                ELSE IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL
                BEGIN
                    SELECT TOP 20 Id, Action, EntityType, EntityId, UserId, IpAddress, Timestamp
                    FROM dbo.AuditLogs
                    WHERE (@UserId IS NULL OR UPPER(UserId) = UPPER(@UserId))
                    ORDER BY Timestamp DESC;
                END";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UserId", (object?)userId ?? DBNull.Value);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new AuditLogRowDto(
                    Id: reader.GetInt64(0),
                    Action: reader.IsDBNull(1) ? "Action" : reader.GetString(1),
                    EntityType: reader.IsDBNull(2) ? "General" : reader.GetString(2),
                    EntityId: reader.IsDBNull(3) ? null : reader.GetString(3),
                    UserId: reader.IsDBNull(4) ? null : reader.GetString(4),
                    IpAddress: reader.IsDBNull(5) ? null : reader.GetString(5),
                    Timestamp: reader.IsDBNull(6) ? DateTime.UtcNow : reader.GetDateTime(6)));
            }
        }
        catch
        {
            // Defensive handling ensures Profile.razor will never throw an unhandled exception
        }

        return list;
    }

    public IReadOnlyList<AuditLogRowDto> GetHistoryLogs(string requestingUserId, int topCount = 100)
    {
        var list = new List<AuditLogRowDto>();
        try
        {
            using var conn = CreateConnection();
            conn.Open();

            bool isSuperAdmin = !string.IsNullOrWhiteSpace(requestingUserId) &&
                requestingUserId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase);

            string sql = isSuperAdmin
                ? @"IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                    BEGIN
                        SELECT TOP (@TopCount) HistoryLogID, ActionType, EntityName, EntityID, ChangedBy, ActionDetails, LoggedAt
                        FROM dbo.HistoryLog
                        ORDER BY LoggedAt DESC;
                    END"
                : @"IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                    BEGIN
                        SELECT TOP (@TopCount) HistoryLogID, ActionType, EntityName, EntityID, ChangedBy, ActionDetails, LoggedAt
                        FROM dbo.HistoryLog
                        WHERE UPPER(ChangedBy) = UPPER(@RequestingUserId)
                        ORDER BY LoggedAt DESC;
                    END";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@TopCount", topCount);
            if (!isSuperAdmin)
            {
                cmd.Parameters.AddWithValue("@RequestingUserId", requestingUserId.Trim());
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new AuditLogRowDto(
                    Id: reader.GetInt64(0),
                    Action: reader.IsDBNull(1) ? "Action" : reader.GetString(1),
                    EntityType: reader.IsDBNull(2) ? "General" : reader.GetString(2),
                    EntityId: reader.IsDBNull(3) ? null : reader.GetString(3),
                    UserId: reader.IsDBNull(4) ? null : reader.GetString(4),
                    IpAddress: reader.IsDBNull(5) ? null : reader.GetString(5),
                    Timestamp: reader.IsDBNull(6) ? DateTime.UtcNow : reader.GetDateTime(6)));
            }
        }
        catch
        {
        }

        return list;
    }
}
