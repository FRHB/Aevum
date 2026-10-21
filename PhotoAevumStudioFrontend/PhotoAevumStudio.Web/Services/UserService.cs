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

    public UserService(IConfiguration? configuration = null)
    {
        _connectionString = configuration?.GetConnectionString("DefaultConnection")
            ?? DefaultLocalConnectionString;
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

        const string sql = @"
            IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL
            BEGIN
                SELECT AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName
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
            var fullName = string.Join(" ", new[] { first, middle, last }.Where(s => !string.IsNullOrWhiteSpace(s)));

            list.Add(new Admin
            {
                Id = id,
                Name = fullName,
                Email = $"{id.ToLowerInvariant()}@photoaevum.studio",
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

        const string sql = @"
            IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL
            BEGIN
                SELECT AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName
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
        var fullName = string.Join(" ", new[] { first, middle, last }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return new Admin
        {
            Id = dbId,
            Name = fullName,
            Email = $"{dbId.ToLowerInvariant()}@photoaevum.studio",
            Role = dbId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase) ? "Super Admin" : role
        };
    }

    public void AddAdmin(Admin admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var (first, middle, last) = SplitFullName(admin.Name);
        using var conn = CreateConnection();
        conn.Open();

        if (GetAdminById(admin.Id) is not null)
            throw new InvalidOperationException($"An admin with ID '{admin.Id}' already exists.");

        const string sql = @"
            INSERT INTO dbo.Admin (AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, AdminPassword, isFirstLogin)
            VALUES (@Id, @Role, @First, @Last, @Middle, @Password, 1);";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", admin.Id.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@Role", string.IsNullOrWhiteSpace(admin.Role) ? "Admin" : admin.Role);
        cmd.Parameters.AddWithValue("@First", first);
        cmd.Parameters.AddWithValue("@Last", last);
        cmd.Parameters.AddWithValue("@Middle", (object?)middle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Password", "AxiomBit@2026");
        cmd.ExecuteNonQuery();
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

        const string sql = @"
            DELETE FROM dbo.EmployeeJointSpecialization WHERE UPPER(EmployeeID) = @Id;
            DELETE FROM dbo.Employee WHERE UPPER(EmployeeID) = @Id;";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id.Trim().ToUpperInvariant());
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
                       COALESCE(CreatedAt, SYSUTCDATETIME()) AS CreatedAt
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
                Department = "Photography",
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
        using var conn = CreateConnection();
        conn.Open();

        var existingCount = GetSpecializationLookups().Count;
        var nextId = $"SP-{existingCount + 1:D3}";

        const string sql = @"
            INSERT INTO dbo.EmployeeSpecialization (SpecializationID, SpecializationName, SpecializationDescription, Specialization, Description, ColorTag, CreatedAt)
            VALUES (@Id, @Name, @Desc, @Name, @Desc, @Color, SYSUTCDATETIME());";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", nextId);
        cmd.Parameters.AddWithValue("@Name", model.Name.Trim());
        cmd.Parameters.AddWithValue("@Desc", (object?)model.Description?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Color", string.IsNullOrWhiteSpace(model.ColorTag) ? "Blue" : model.ColorTag);
        cmd.ExecuteNonQuery();

        return new SpecializationLookupItem
        {
            Id = nextId,
            Name = model.Name.Trim(),
            Description = model.Description?.Trim(),
            Department = model.Department,
            ColorTag = model.ColorTag,
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
        using var conn = CreateConnection();
        conn.Open();

        const string sql = @"
            IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL
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
                Action: reader.GetString(1),
                EntityType: reader.GetString(2),
                EntityId: reader.IsDBNull(3) ? null : reader.GetString(3),
                UserId: reader.IsDBNull(4) ? null : reader.GetString(4),
                IpAddress: reader.IsDBNull(5) ? null : reader.GetString(5),
                Timestamp: reader.GetDateTime(6)));
        }

        return list;
    }
}
