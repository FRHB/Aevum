using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.DTOs;
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
                SELECT ServicesID, ServiceName, ServicesDescription, ServicePrice
                FROM dbo.Services
                ORDER BY ServicesID;
            END";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        int idx = 1;
        while (await reader.ReadAsync(cancellationToken))
        {
            var price = reader.FieldCount > 3 && !reader.IsDBNull(3) ? reader.GetDecimal(3) : 0m;
            services.Add(new StudioService
            {
                Id = idx++,
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                Price = price,
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

    // ── Service Images & Stored Procedures ────────────────────────────────────

    public async Task<IReadOnlyList<ServiceSummaryDto>> GetServicesSummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        var list = new List<ServiceSummaryDto>();
        await using var cmd = new SqlCommand("dbo.sp_GetServicesSummary", conn)
        {
            CommandType = CommandType.StoredProcedure
        };

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new ServiceSummaryDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: reader.IsDBNull(3) ? 0m : reader.GetDecimal(3),
                    HasImage: !reader.IsDBNull(4) && reader.GetBoolean(4)
                ));
            }
        }
        catch (SqlException)
        {
            // Direct query fallback
            const string fallbackSql = @"
                SELECT ServicesID, ServiceName, ServicesDescription, ServicePrice,
                       CAST(CASE WHEN ServiceImage IS NOT NULL THEN 1 ELSE 0 END AS bit)
                FROM dbo.Services ORDER BY ServicesID;";
            await using var fbCmd = new SqlCommand(fallbackSql, conn);
            await using var reader = await fbCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new ServiceSummaryDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: reader.IsDBNull(3) ? 0m : reader.GetDecimal(3),
                    HasImage: !reader.IsDBNull(4) && reader.GetBoolean(4)
                ));
            }
        }

        return list;
    }

    public async Task<ServiceDetailDto?> GetServiceDetailWithImageAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new SqlCommand("dbo.sp_GetServiceDetailWithImage", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@ServicesID", serviceId.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            var name = reader.GetString(1);
            var desc = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var price = reader.IsDBNull(3) ? 0m : reader.GetDecimal(3);
            byte[]? imageBytes = reader.IsDBNull(4) ? null : (byte[])reader[4];
            var mime = reader.IsDBNull(5) ? "image/jpeg" : reader.GetString(5);
            var hasImage = !reader.IsDBNull(6) && reader.GetBoolean(6);

            return new ServiceDetailDto(id, name, desc, price, imageBytes, mime, hasImage);
        }

        return null;
    }

    public async Task<bool> UpdateServiceImageAsync(string serviceId, byte[] imageBytes, string mimeType, string? requestingAdminId, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new SqlCommand("dbo.sp_UpdateServiceImage", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@ServicesID", serviceId.Trim());
        cmd.Parameters.Add("@ServiceImage", SqlDbType.VarBinary, -1).Value = imageBytes;
        cmd.Parameters.AddWithValue("@ServiceImageMimeType", string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType);
        cmd.Parameters.AddWithValue("@UpdatedByAdminID", (object?)requestingAdminId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    // ── Additional Services ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<ServiceSummaryDto>> GetAdditionalServicesSummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        var list = new List<ServiceSummaryDto>();
        await using var cmd = new SqlCommand("dbo.sp_GetAdditionalServicesSummary", conn)
        {
            CommandType = CommandType.StoredProcedure
        };

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new ServiceSummaryDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: 0m,
                    HasImage: !reader.IsDBNull(3) && reader.GetBoolean(3)
                ));
            }
        }
        catch (SqlException)
        {
            const string fallbackSql = @"
                SELECT AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription,
                       CAST(CASE WHEN AdditionalServiceImage IS NOT NULL THEN 1 ELSE 0 END AS bit)
                FROM dbo.AdditionalServices ORDER BY AdditionalServicesID;";
            await using var fbCmd = new SqlCommand(fallbackSql, conn);
            await using var reader = await fbCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new ServiceSummaryDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: 0m,
                    HasImage: !reader.IsDBNull(3) && reader.GetBoolean(3)
                ));
            }
        }

        return list;
    }

    public async Task<ServiceDetailDto?> GetAdditionalServiceDetailWithImageAsync(string addOnId, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new SqlCommand("dbo.sp_GetAdditionalServiceDetailWithImage", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@AdditionalServicesID", addOnId.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new ServiceDetailDto(
                Id: reader.GetString(0),
                Name: reader.GetString(1),
                Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Price: 0m,
                Image: reader.IsDBNull(3) ? null : (byte[])reader[3],
                MimeType: reader.IsDBNull(4) ? "image/jpeg" : reader.GetString(4),
                HasImage: !reader.IsDBNull(5) && reader.GetBoolean(5)
            );
        }

        return null;
    }

    public async Task<bool> UpdateAdditionalServiceImageAsync(string addOnId, byte[] imageBytes, string mimeType, string? requestingAdminId, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new SqlCommand("dbo.sp_UpdateAdditionalServiceImage", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@AdditionalServicesID", addOnId.Trim());
        cmd.Parameters.Add("@AdditionalServiceImage", SqlDbType.VarBinary, -1).Value = imageBytes;
        cmd.Parameters.AddWithValue("@AdditionalServiceImageMimeType", string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType);
        cmd.Parameters.AddWithValue("@UpdatedByAdminID", (object?)requestingAdminId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    // ── Combo Packages ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ServiceSummaryDto>> GetComboPackagesSummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        var list = new List<ServiceSummaryDto>();
        await using var cmd = new SqlCommand("dbo.sp_GetComboPackagesSummary", conn)
        {
            CommandType = CommandType.StoredProcedure
        };

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new ServiceSummaryDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: 0m,
                    HasImage: !reader.IsDBNull(3) && reader.GetBoolean(3)
                ));
            }
        }
        catch (SqlException)
        {
            const string fallbackSql = @"
                SELECT ComboPackageID, ComboPackageName, ComboPackageDescription,
                       CAST(CASE WHEN ComboPackageImage IS NOT NULL THEN 1 ELSE 0 END AS bit)
                FROM dbo.ComboPackage ORDER BY ComboPackageID;";
            await using var fbCmd = new SqlCommand(fallbackSql, conn);
            await using var reader = await fbCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new ServiceSummaryDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: 0m,
                    HasImage: !reader.IsDBNull(3) && reader.GetBoolean(3)
                ));
            }
        }

        return list;
    }

    public async Task<ServiceDetailDto?> GetComboPackageDetailWithImageAsync(string packageId, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new SqlCommand("dbo.sp_GetComboPackageDetailWithImage", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@ComboPackageID", packageId.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new ServiceDetailDto(
                Id: reader.GetString(0),
                Name: reader.GetString(1),
                Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Price: 0m,
                Image: reader.IsDBNull(3) ? null : (byte[])reader[3],
                MimeType: reader.IsDBNull(4) ? "image/jpeg" : reader.GetString(4),
                HasImage: !reader.IsDBNull(5) && reader.GetBoolean(5)
            );
        }

        return null;
    }

    public async Task<bool> UpdateComboPackageImageAsync(string packageId, byte[] imageBytes, string mimeType, string? requestingAdminId, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new SqlCommand("dbo.sp_UpdateComboPackageImage", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@ComboPackageID", packageId.Trim());
        cmd.Parameters.Add("@ComboPackageImage", SqlDbType.VarBinary, -1).Value = imageBytes;
        cmd.Parameters.AddWithValue("@ComboPackageImageMimeType", string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType);
        cmd.Parameters.AddWithValue("@UpdatedByAdminID", (object?)requestingAdminId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    // ── History Logs (SystemAdmin full access) ────────────────────────────────

    public async Task<IReadOnlyList<HistoryLogDto>> GetHistoryLogsAsync(string requestingUserId, int topCount = 100, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        var list = new List<HistoryLogDto>();
        await using var cmd = new SqlCommand("dbo.sp_GetHistoryLogs", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@RequestingUserID", requestingUserId.Trim());
        cmd.Parameters.AddWithValue("@TopCount", topCount);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new HistoryLogDto(
                    Id: reader.GetInt64(reader.GetOrdinal("HistoryLogID")),
                    ActionType: reader.GetString(reader.GetOrdinal("ActionType")),
                    EntityName: reader.GetString(reader.GetOrdinal("EntityName")),
                    EntityId: reader.IsDBNull(reader.GetOrdinal("EntityID")) ? null : reader.GetString(reader.GetOrdinal("EntityID")),
                    ActionDetails: reader.IsDBNull(reader.GetOrdinal("ActionDetails")) ? null : reader.GetString(reader.GetOrdinal("ActionDetails")),
                    EventAppointmentId: reader.IsDBNull(reader.GetOrdinal("EventAppointmentID")) ? null : reader.GetString(reader.GetOrdinal("EventAppointmentID")),
                    CustomerId: reader.IsDBNull(reader.GetOrdinal("CustomerID")) ? null : reader.GetString(reader.GetOrdinal("CustomerID")),
                    EmployeeId: reader.IsDBNull(reader.GetOrdinal("EmployeeID")) ? null : reader.GetString(reader.GetOrdinal("EmployeeID")),
                    ChangedByType: reader.GetString(reader.GetOrdinal("ChangedByType")),
                    ChangedBy: reader.IsDBNull(reader.GetOrdinal("ChangedBy")) ? null : reader.GetString(reader.GetOrdinal("ChangedBy")),
                    LoggedAt: reader.GetDateTime(reader.GetOrdinal("LoggedAt"))
                ));
            }
        }
        catch (SqlException)
        {
            // Direct query fallback
            const string fallbackSql = @"
                SELECT TOP (@TopCount) HistoryLogID, ActionType, EntityName, EntityID, ActionDetails,
                       EventAppointmentID, CustomerID, EmployeeID, ChangedByType, ChangedBy, LoggedAt
                FROM dbo.HistoryLog
                ORDER BY LoggedAt DESC;";
            await using var fbCmd = new SqlCommand(fallbackSql, conn);
            fbCmd.Parameters.AddWithValue("@TopCount", topCount);
            await using var reader = await fbCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new HistoryLogDto(
                    Id: reader.GetInt64(0),
                    ActionType: reader.GetString(1),
                    EntityName: reader.GetString(2),
                    EntityId: reader.IsDBNull(3) ? null : reader.GetString(3),
                    ActionDetails: reader.IsDBNull(4) ? null : reader.GetString(4),
                    EventAppointmentId: reader.IsDBNull(5) ? null : reader.GetString(5),
                    CustomerId: reader.IsDBNull(6) ? null : reader.GetString(6),
                    EmployeeId: reader.IsDBNull(7) ? null : reader.GetString(7),
                    ChangedByType: reader.GetString(8),
                    ChangedBy: reader.IsDBNull(9) ? null : reader.GetString(9),
                    LoggedAt: reader.GetDateTime(10)
                ));
            }
        }

        return list;
    }
}
