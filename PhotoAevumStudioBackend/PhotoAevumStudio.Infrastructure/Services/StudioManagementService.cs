using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Infrastructure.Services;

public class StudioManagementService : IStudioManagementService
{
    private readonly ApplicationDbContext _db;

    public StudioManagementService(ApplicationDbContext db)
    {
        _db = db;
    }

    private string GetConnectionString() =>
        _db.Database.GetConnectionString()
        ?? throw new InvalidOperationException("Database connection string is not configured.");

    public async Task<IReadOnlyList<Employee>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        var specializationsByEmployee = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        const string specJoinSql = @"
            IF OBJECT_ID(N'dbo.EmployeeJointSpecialization', N'U') IS NOT NULL
               AND OBJECT_ID(N'dbo.EmployeeSpecialization', N'U') IS NOT NULL
            BEGIN
                SELECT ejs.EmployeeID, COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization, es.SpecializationID) AS SpecName
                FROM dbo.EmployeeJointSpecialization ejs
                INNER JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID;
            END";

        await using (var specCmd = new SqlCommand(specJoinSql, conn))
        await using (var specReader = await specCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await specReader.ReadAsync(cancellationToken))
            {
                var empId = specReader.GetString(0);
                var specName = specReader.IsDBNull(1) ? string.Empty : specReader.GetString(1);
                if (string.IsNullOrWhiteSpace(specName)) continue;

                if (!specializationsByEmployee.TryGetValue(empId, out var list))
                {
                    list = new List<string>();
                    specializationsByEmployee[empId] = list;
                }
                if (!list.Contains(specName, StringComparer.OrdinalIgnoreCase))
                {
                    list.Add(specName);
                }
            }
        }

        var employees = new List<Employee>();
        const string empSql = @"
            SELECT EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeMiddleName, EmployeeEmail, EmployeeStatus
            FROM dbo.Employee
            ORDER BY EmployeeID;";

        await using var cmd = new SqlCommand(empSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        int idx = 1;
        while (await reader.ReadAsync(cancellationToken))
        {
            var empId = reader.GetString(0);
            var firstName = reader.GetString(1);
            var lastName = reader.GetString(2);
            var middleName = reader.IsDBNull(3) ? null : reader.GetString(3);
            var email = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            var status = reader.IsDBNull(5) ? "Active" : reader.GetString(5);

            specializationsByEmployee.TryGetValue(empId, out var specs);

            employees.Add(new Employee
            {
                Id = idx++,
                IdentityUserId = empId,
                IdentityUser = new ApplicationUser
                {
                    Id = empId,
                    StudioId = empId,
                    FirstName = firstName,
                    LastName = lastName,
                    MiddleName = middleName,
                    Email = email,
                    UserName = email
                },
                Status = status,
                Specializations = specs ?? new List<string>()
            });
        }

        return employees;
    }

    public async Task<IReadOnlyList<string>> GetSpecializationsAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        var specs = new List<string>();
        const string sql = @"
            IF OBJECT_ID(N'dbo.EmployeeSpecialization', N'U') IS NOT NULL
            BEGIN
                SELECT DISTINCT COALESCE(NULLIF(SpecializationName, ''), Specialization) AS SpecName
                FROM dbo.EmployeeSpecialization
                WHERE COALESCE(NULLIF(SpecializationName, ''), Specialization) IS NOT NULL
                ORDER BY SpecName;
            END";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                var name = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(name))
                    specs.Add(name);
            }
        }

        return specs;
    }

    public async Task<IReadOnlyList<StudioService>> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        var services = new List<StudioService>();
        const string sql = @"
            IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL
            BEGIN
                SELECT ServicesID, ServiceName, ServicesDescription
                FROM dbo.Services
                ORDER BY ServicesID;
            END";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        int idx = 1;
        while (await reader.ReadAsync(cancellationToken))
        {
            services.Add(new StudioService
            {
                Id = idx++,
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                Price = 0m,
                Duration = 60,
                IsActive = true
            });
        }

        return services;
    }

    public async Task<IReadOnlyList<Equipment>> GetEquipmentAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        var equipmentList = new List<Equipment>();
        const string sql = @"
            IF OBJECT_ID(N'dbo.Equipment', N'U') IS NOT NULL
            BEGIN
                SELECT EquipmentID, EquipmentName, COALESCE(SerialNo, EquipmentID) AS SerialNo, EquipmentStatus
                FROM dbo.Equipment
                ORDER BY EquipmentID;
            END";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        int idx = 1;
        while (await reader.ReadAsync(cancellationToken))
        {
            equipmentList.Add(new Equipment
            {
                Id = idx++,
                Name = reader.GetString(1),
                SerialNumber = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Status = reader.IsDBNull(3) ? "Available" : reader.GetString(3)
            });
        }

        return equipmentList;
    }
}
