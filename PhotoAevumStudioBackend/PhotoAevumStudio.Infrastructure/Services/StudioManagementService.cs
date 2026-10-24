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
                var price = reader.FieldCount > 3 && !reader.IsDBNull(3) ? reader.GetDecimal(3) : 0m;
                var hasImg = reader.FieldCount > 5 ? (!reader.IsDBNull(5) && reader.GetBoolean(5)) : (!reader.IsDBNull(3) && reader.GetBoolean(3));
                list.Add(new ServiceSummaryDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: price,
                    HasImage: hasImg
                ));
            }
        }
        catch (SqlException)
        {
            const string fallbackSql = @"
                SELECT AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription,
                       ISNULL(ServicePrice, 0.00),
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
                    Price: reader.IsDBNull(3) ? 0m : reader.GetDecimal(3),
                    HasImage: !reader.IsDBNull(4) && reader.GetBoolean(4)
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

    // ── Sequential Custom ID Generation & Wizard Workflows ───────────────────

    public async Task<string> GetNextServiceIdAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        try
        {
            await using var cmd = new SqlCommand("dbo.sp_GetNextServiceID", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            var outParam = new SqlParameter("@NextServiceID", SqlDbType.NVarChar, 30)
            {
                Direction = ParameterDirection.Output
            };
            cmd.Parameters.Add(outParam);

            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            if (outParam.Value is string idVal && !string.IsNullOrWhiteSpace(idVal))
                return idVal;
            if (result is string scalarVal && !string.IsNullOrWhiteSpace(scalarVal))
                return scalarVal;
        }
        catch (SqlException)
        {
            // Direct query fallback
        }

        const string fallbackSql = @"
            SELECT 'SRV-' + RIGHT('0000' + CAST(
                ISNULL(MAX(TRY_CAST(SUBSTRING(ServicesID, CHARINDEX('-', ServicesID) + 1, 20) AS INT)), 0) + 1
            AS VARCHAR(10)), 4)
            FROM dbo.Services
            WHERE ServicesID LIKE 'SRV-%';";

        await using var fbCmd = new SqlCommand(fallbackSql, conn);
        var fallbackVal = await fbCmd.ExecuteScalarAsync(cancellationToken);
        return fallbackVal?.ToString() ?? "SRV-0001";
    }

    public async Task<string> GetNextCategoryIdAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        try
        {
            await using var cmd = new SqlCommand("dbo.sp_GetNextCategoryID", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            var outParam = new SqlParameter("@NextCategoryID", SqlDbType.NVarChar, 30)
            {
                Direction = ParameterDirection.Output
            };
            cmd.Parameters.Add(outParam);

            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            if (outParam.Value is string idVal && !string.IsNullOrWhiteSpace(idVal))
                return idVal;
            if (result is string scalarVal && !string.IsNullOrWhiteSpace(scalarVal))
                return scalarVal;
        }
        catch (SqlException)
        {
            // Direct query fallback
        }

        const string fallbackSql = @"
            SELECT 'CAT-' + RIGHT('0000' + CAST(
                ISNULL(MAX(TRY_CAST(SUBSTRING(ServiceCategoryID, CHARINDEX('-', ServiceCategoryID) + 1, 20) AS INT)), 0) + 1
            AS VARCHAR(10)), 4)
            FROM dbo.ServiceCategory
            WHERE ServiceCategoryID LIKE 'CAT-%';";

        await using var fbCmd = new SqlCommand(fallbackSql, conn);
        var fallbackVal = await fbCmd.ExecuteScalarAsync(cancellationToken);
        return fallbackVal?.ToString() ?? "CAT-0001";
    }

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        var list = new List<CategoryDto>();
        try
        {
            await using var cmd = new SqlCommand("dbo.sp_GetServiceCategories", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new CategoryDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
                ));
            }
            if (list.Count > 0) return list;
        }
        catch (SqlException)
        {
            // Direct query fallback
        }

        const string fallbackSql = @"
            SELECT ServiceCategoryID, ServiceCategoryName, ServiceCategoryDescription
            FROM dbo.ServiceCategory
            ORDER BY ServiceCategoryName;";

        await using var fbCmd = new SqlCommand(fallbackSql, conn);
        await using var readerFb = await fbCmd.ExecuteReaderAsync(cancellationToken);
        while (await readerFb.ReadAsync(cancellationToken))
        {
            list.Add(new CategoryDto(
                Id: readerFb.GetString(0),
                Name: readerFb.GetString(1),
                Description: readerFb.IsDBNull(2) ? string.Empty : readerFb.GetString(2)
            ));
        }

        return list;
    }

    public async Task<ServiceCreatedResponseDto> CreateServiceAsync(CreateServiceDto dto, string? requestingAdminId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Service name is required.", nameof(dto));

        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        try
        {
            await using var cmd = new SqlCommand("dbo.sp_CreateService", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@ServiceName", dto.Name.Trim());
            cmd.Parameters.AddWithValue("@ServicesDescription", dto.Description.Trim());
            cmd.Parameters.AddWithValue("@ServicePrice", dto.Price);
            cmd.Parameters.AddWithValue("@ServiceCategoryID", (object?)dto.CategoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ServiceImage", (object?)dto.ImageBytes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ServiceImageMimeType", (object?)dto.ImageMimeType ?? "image/jpeg");
            cmd.Parameters.AddWithValue("@CreatedByAdminID", (object?)requestingAdminId ?? DBNull.Value);

            var outId = new SqlParameter("@CreatedServiceID", SqlDbType.NVarChar, 30)
            {
                Direction = ParameterDirection.Output
            };
            cmd.Parameters.Add(outId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0);
                var name = reader.GetString(1);
                var desc = reader.GetString(2);
                var price = reader.GetDecimal(3);
                var catId = reader.IsDBNull(4) ? dto.CategoryId : reader.GetString(4);
                var catName = reader.IsDBNull(5) ? null : reader.GetString(5);
                var hasImg = !reader.IsDBNull(6) && reader.GetBoolean(6);

                return new ServiceCreatedResponseDto(id, name, desc, price, catId ?? string.Empty, catName, hasImg);
            }
        }
        catch (SqlException)
        {
            // Fall back to transactional inline execution
        }

        await using var trans = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            const string nextSql = @"
                SELECT 'SRV-' + RIGHT('0000' + CAST(
                    ISNULL(MAX(TRY_CAST(SUBSTRING(ServicesID, CHARINDEX('-', ServicesID) + 1, 20) AS INT)), 0) + 1
                AS VARCHAR(10)), 4)
                FROM dbo.Services WITH (UPDLOCK, HOLDLOCK)
                WHERE ServicesID LIKE 'SRV-%';";

            await using var idCmd = new SqlCommand(nextSql, conn, trans);
            var nextId = (await idCmd.ExecuteScalarAsync(cancellationToken))?.ToString() ?? "SRV-0001";

            const string insertSql = @"
                INSERT INTO dbo.Services (
                    ServicesID, ServiceName, ServicesDescription, ServicePrice, ServiceImage, ServiceImageMimeType
                )
                VALUES (
                    @Id, @Name, @Desc, @Price, @Img, @Mime
                );";

            await using var insertCmd = new SqlCommand(insertSql, conn, trans);
            insertCmd.Parameters.AddWithValue("@Id", nextId);
            insertCmd.Parameters.AddWithValue("@Name", dto.Name.Trim());
            insertCmd.Parameters.AddWithValue("@Desc", dto.Description.Trim());
            insertCmd.Parameters.AddWithValue("@Price", dto.Price);
            insertCmd.Parameters.AddWithValue("@Img", (object?)dto.ImageBytes ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@Mime", (object?)dto.ImageMimeType ?? "image/jpeg");
            await insertCmd.ExecuteNonQueryAsync(cancellationToken);

            string? resolvedCategoryName = null;
            if (!string.IsNullOrWhiteSpace(dto.CategoryId))
            {
                const string checkCatSql = "SELECT ServiceCategoryName FROM dbo.ServiceCategory WHERE ServiceCategoryID = @CatId;";
                await using var catCmd = new SqlCommand(checkCatSql, conn, trans);
                catCmd.Parameters.AddWithValue("@CatId", dto.CategoryId);
                var catVal = await catCmd.ExecuteScalarAsync(cancellationToken);
                if (catVal != null)
                {
                    resolvedCategoryName = catVal.ToString();
                    var joinId = "SCJ-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
                    const string joinSql = @"
                        INSERT INTO dbo.ServiceCategoryJoins (ServiceCategoryJoinID, ServiceCategoryID, ServicesID)
                        VALUES (@JoinId, @CatId, @SrvId);";
                    await using var joinCmd = new SqlCommand(joinSql, conn, trans);
                    joinCmd.Parameters.AddWithValue("@JoinId", joinId);
                    joinCmd.Parameters.AddWithValue("@CatId", dto.CategoryId);
                    joinCmd.Parameters.AddWithValue("@SrvId", nextId);
                    await joinCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await trans.CommitAsync(cancellationToken);

            return new ServiceCreatedResponseDto(
                Id: nextId,
                Name: dto.Name.Trim(),
                Description: dto.Description.Trim(),
                Price: dto.Price,
                CategoryId: dto.CategoryId ?? string.Empty,
                CategoryName: resolvedCategoryName,
                HasImage: dto.ImageBytes != null && dto.ImageBytes.Length > 0);
        }
        catch
        {
            await trans.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<CategoryDto> CreateCategoryAsync(CreateCategoryDto dto, string? requestingAdminId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Category name is required.", nameof(dto));

        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        try
        {
            await using var cmd = new SqlCommand("dbo.sp_CreateServiceCategory", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@ServiceCategoryName", dto.Name.Trim());
            cmd.Parameters.AddWithValue("@ServiceCategoryDescription", dto.Description.Trim());
            cmd.Parameters.AddWithValue("@CreatedByAdminID", (object?)requestingAdminId ?? DBNull.Value);

            var outId = new SqlParameter("@CreatedCategoryID", SqlDbType.NVarChar, 30)
            {
                Direction = ParameterDirection.Output
            };
            cmd.Parameters.Add(outId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new CategoryDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
                );
            }
        }
        catch (SqlException)
        {
            // Fall back to inline transaction
        }

        await using var trans = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            const string nextSql = @"
                SELECT 'CAT-' + RIGHT('0000' + CAST(
                    ISNULL(MAX(TRY_CAST(SUBSTRING(ServiceCategoryID, CHARINDEX('-', ServiceCategoryID) + 1, 20) AS INT)), 0) + 1
                AS VARCHAR(10)), 4)
                FROM dbo.ServiceCategory WITH (UPDLOCK, HOLDLOCK)
                WHERE ServiceCategoryID LIKE 'CAT-%';";

            await using var idCmd = new SqlCommand(nextSql, conn, trans);
            var nextId = (await idCmd.ExecuteScalarAsync(cancellationToken))?.ToString() ?? "CAT-0001";

            const string insertSql = @"
                INSERT INTO dbo.ServiceCategory (ServiceCategoryID, ServiceCategoryName, ServiceCategoryDescription)
                VALUES (@Id, @Name, @Desc);";

            await using var insertCmd = new SqlCommand(insertSql, conn, trans);
            insertCmd.Parameters.AddWithValue("@Id", nextId);
            insertCmd.Parameters.AddWithValue("@Name", dto.Name.Trim());
            insertCmd.Parameters.AddWithValue("@Desc", dto.Description.Trim());
            await insertCmd.ExecuteNonQueryAsync(cancellationToken);

            await trans.CommitAsync(cancellationToken);

            return new CategoryDto(nextId, dto.Name.Trim(), dto.Description.Trim());
        }
        catch
        {
            await trans.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<string> GetNextAdditionalServiceIdAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        try
        {
            await using var cmd = new SqlCommand("dbo.sp_GetNextAdditionalServiceID", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            var outParam = new SqlParameter("@NextID", SqlDbType.NVarChar, 30)
            {
                Direction = ParameterDirection.Output
            };
            cmd.Parameters.Add(outParam);

            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            if (outParam.Value is string idVal && !string.IsNullOrWhiteSpace(idVal))
                return idVal;
            if (result is string scalarVal && !string.IsNullOrWhiteSpace(scalarVal))
                return scalarVal;
        }
        catch (SqlException)
        {
            // Fall through to query
        }

        const string fallbackSql = @"
            DECLARE @CurVal SQL_VARIANT;
            SELECT @CurVal = current_value FROM sys.sequences WHERE name = 'Seq_AdditionalServicesID' AND schema_id = SCHEMA_ID('dbo');
            DECLARE @SeqVal INT = ISNULL(CAST(@CurVal AS INT), 1);
            IF EXISTS (SELECT 1 FROM dbo.AdditionalServices WHERE AdditionalServicesID = 'ADD-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4))
            BEGIN
                SET @SeqVal = @SeqVal + 1;
            END
            SELECT 'ADD-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4);";

        await using var fbCmd = new SqlCommand(fallbackSql, conn);
        var fallbackVal = await fbCmd.ExecuteScalarAsync(cancellationToken);
        return fallbackVal?.ToString() ?? "ADD-0001";
    }

    public async Task<AdditionalServiceResponseDto> CreateAdditionalServiceAsync(CreateAdditionalServiceDto dto, string? requestingAdminId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.ServiceName))
            throw new ArgumentException("Service Name is required.", nameof(dto));

        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        try
        {
            await using var cmd = new SqlCommand("dbo.sp_CreateAdditionalService", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@AdditionalServiceName", dto.ServiceName.Trim());
            cmd.Parameters.AddWithValue("@AdditionalServicesDescription", dto.ServiceDescription?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("@ServicePrice", dto.ServicePrice);
            cmd.Parameters.AddWithValue("@CategoryName", (object?)dto.ServiceCategory?.Trim() ?? DBNull.Value);
            cmd.Parameters.Add("@AdditionalServiceImage", SqlDbType.VarBinary, -1).Value = (object?)dto.ImageBytes ?? DBNull.Value;
            cmd.Parameters.AddWithValue("@AdditionalServiceImageMimeType", string.IsNullOrWhiteSpace(dto.ImageMimeType) ? "image/jpeg" : dto.ImageMimeType.Trim());
            cmd.Parameters.AddWithValue("@CreatedByAdminID", (object?)requestingAdminId ?? DBNull.Value);

            var outId = new SqlParameter("@GeneratedID", SqlDbType.NVarChar, 30)
            {
                Direction = ParameterDirection.Output
            };
            cmd.Parameters.Add(outId);

            var scalarResult = await cmd.ExecuteScalarAsync(cancellationToken);
            string createdId = (outId.Value as string) ?? (scalarResult as string) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(createdId))
            {
                createdId = await GetNextAdditionalServiceIdAsync(cancellationToken);
            }

            return new AdditionalServiceResponseDto(
                Id: createdId,
                Name: dto.ServiceName.Trim(),
                Description: dto.ServiceDescription?.Trim() ?? string.Empty,
                Category: dto.ServiceCategory?.Trim() ?? "Add-On",
                Price: dto.ServicePrice,
                HasImage: dto.ImageBytes != null && dto.ImageBytes.Length > 0,
                ImageUrl: $"/api/management/additional-services/{createdId}/image",
                CreatedAt: DateTime.UtcNow
            );
        }
        catch (SqlException)
        {
            // Direct transaction fallback if stored procedure is not yet compiled
        }

        await using var trans = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            const string seqSql = @"
                DECLARE @SeqVal INT = NEXT VALUE FOR dbo.Seq_AdditionalServicesID;
                DECLARE @NewID NVARCHAR(30) = 'ADD-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4);
                WHILE EXISTS (SELECT 1 FROM dbo.AdditionalServices WITH (UPDLOCK, HOLDLOCK) WHERE AdditionalServicesID = @NewID)
                BEGIN
                    SET @SeqVal = NEXT VALUE FOR dbo.Seq_AdditionalServicesID;
                    SET @NewID = 'ADD-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4);
                END
                SELECT @NewID;";

            await using var idCmd = new SqlCommand(seqSql, conn, trans);
            var nextId = (await idCmd.ExecuteScalarAsync(cancellationToken))?.ToString() ?? "ADD-0001";

            const string insertSql = @"
                INSERT INTO dbo.AdditionalServices (
                    AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription,
                    ServicePrice, CategoryName, AdditionalServiceImage, AdditionalServiceImageMimeType, CreatedAt
                )
                VALUES (@Id, @Name, @Desc, @Price, @Cat, @Img, @Mime, SYSUTCDATETIME());";

            await using var insertCmd = new SqlCommand(insertSql, conn, trans);
            insertCmd.Parameters.AddWithValue("@Id", nextId);
            insertCmd.Parameters.AddWithValue("@Name", dto.ServiceName.Trim());
            insertCmd.Parameters.AddWithValue("@Desc", dto.ServiceDescription?.Trim() ?? string.Empty);
            insertCmd.Parameters.AddWithValue("@Price", dto.ServicePrice);
            insertCmd.Parameters.AddWithValue("@Cat", (object?)dto.ServiceCategory?.Trim() ?? DBNull.Value);
            insertCmd.Parameters.Add("@Img", SqlDbType.VarBinary, -1).Value = (object?)dto.ImageBytes ?? DBNull.Value;
            insertCmd.Parameters.AddWithValue("@Mime", string.IsNullOrWhiteSpace(dto.ImageMimeType) ? "image/jpeg" : dto.ImageMimeType.Trim());
            await insertCmd.ExecuteNonQueryAsync(cancellationToken);

            await trans.CommitAsync(cancellationToken);

            return new AdditionalServiceResponseDto(
                Id: nextId,
                Name: dto.ServiceName.Trim(),
                Description: dto.ServiceDescription?.Trim() ?? string.Empty,
                Category: dto.ServiceCategory?.Trim() ?? "Add-On",
                Price: dto.ServicePrice,
                HasImage: dto.ImageBytes != null && dto.ImageBytes.Length > 0,
                ImageUrl: $"/api/management/additional-services/{nextId}/image",
                CreatedAt: DateTime.UtcNow
            );
        }
        catch
        {
            await trans.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // ── Combo Package Wizard & Bundling (Anti-Race Condition) ──────────────────

    public async Task<string> GetNextComboPackageIdAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        try
        {
            await using var cmd = new SqlCommand("dbo.sp_GetNextComboPackageId", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            var outParam = new SqlParameter("@NextPackageID", SqlDbType.NVarChar, 30)
            {
                Direction = ParameterDirection.Output
            };
            cmd.Parameters.Add(outParam);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            var result = outParam.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(result))
                return result;
        }
        catch (SqlException)
        {
            // Direct query fallback via SQL Sequence or table scan
        }

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

        await using var fbCmd = new SqlCommand(fallbackSql, conn);
        var scalar = await fbCmd.ExecuteScalarAsync(cancellationToken);
        return scalar?.ToString() ?? "PKG-0001";
    }

    public async Task<IReadOnlyList<BundledServiceSelectionItem>> GetAvailableServicesForBundlingAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        var list = new List<BundledServiceSelectionItem>();

        try
        {
            await using var cmd = new SqlCommand("dbo.sp_GetAvailableServicesForBundling", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new BundledServiceSelectionItem(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: reader.IsDBNull(3) ? 0m : reader.GetDecimal(3),
                    ServiceType: reader.GetString(4),
                    HasImage: !reader.IsDBNull(5) && reader.GetBoolean(5)
                ));
            }
            return list;
        }
        catch (SqlException)
        {
            // Direct query fallback
        }

        const string fallbackSql = @"
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
            ORDER BY ServiceType DESC, ServiceName ASC;";

        try
        {
            await using var fbCmd = new SqlCommand(fallbackSql, conn);
            await using var reader = await fbCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new BundledServiceSelectionItem(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: reader.IsDBNull(3) ? 0m : reader.GetDecimal(3),
                    ServiceType: reader.GetString(4),
                    HasImage: !reader.IsDBNull(5) && reader.GetBoolean(5)
                ));
            }
        }
        catch
        {
            // If tables are empty or being configured, return empty list gracefully
        }

        return list;
    }

    public async Task<ComboPackageCreatedResponseDto> CreateComboPackageWithServicesAsync(
        CreateComboPackageModel model,
        byte[]? imageBytes,
        string? imageMimeType,
        string? requestingAdminId = null,
        CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        string? generatedId = null;
        var selectedIdsJson = System.Text.Json.JsonSerializer.Serialize(model.SelectedServiceIds ?? Array.Empty<string>());

        // 1. Attempt using Stored Procedure
        try
        {
            await using var cmd = new SqlCommand("dbo.sp_CreateComboPackageWithServices", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@ComboPackageName", model.Name.Trim());
            cmd.Parameters.AddWithValue("@ComboPackageDescription", model.Description?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("@ComboPackagePrice", model.Price);
            cmd.Parameters.AddWithValue("@ServiceCategoryID", string.IsNullOrWhiteSpace(model.CategoryId) ? (object)DBNull.Value : model.CategoryId.Trim());
            cmd.Parameters.Add("@ComboPackageImage", SqlDbType.VarBinary, -1).Value = (object?)imageBytes ?? DBNull.Value;
            cmd.Parameters.AddWithValue("@ComboPackageImageMimeType", string.IsNullOrWhiteSpace(imageMimeType) ? "image/jpeg" : imageMimeType.Trim());
            cmd.Parameters.AddWithValue("@SelectedServiceIDsJson", selectedIdsJson);
            cmd.Parameters.AddWithValue("@CreatedByAdminID", (object?)requestingAdminId ?? DBNull.Value);

            var outParam = new SqlParameter("@GeneratedPackageID", SqlDbType.NVarChar, 30)
            {
                Direction = ParameterDirection.Output
            };
            cmd.Parameters.Add(outParam);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            generatedId = outParam.Value?.ToString();
        }
        catch (SqlException)
        {
            generatedId = null; // Stored procedure not found or failed; trigger fallback below
        }

        // 2. Transactional Fallback if Stored Procedure was not available
        if (string.IsNullOrWhiteSpace(generatedId))
        {
            await using var trans = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                // Acquire exclusive application lock
                const string lockSql = @"
                    DECLARE @res INT;
                    EXEC @res = sp_getapplock @Resource = 'ComboPackage_IdGen_Lock', @LockMode = 'Exclusive', @LockOwner = 'Transaction';
                    SELECT @res;";
                await using (var lockCmd = new SqlCommand(lockSql, conn, trans))
                {
                    await lockCmd.ExecuteScalarAsync(cancellationToken);
                }

                // Generate ID
                const string idGenSql = @"
                    DECLARE @SeqNum INT;
                    IF EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_ComboPackageID')
                    BEGIN
                        SET @SeqNum = NEXT VALUE FOR dbo.Seq_ComboPackageID;
                    END
                    ELSE
                    BEGIN
                        DECLARE @MaxNum INT = 0;
                        SELECT @MaxNum = ISNULL(MAX(TRY_CAST(SUBSTRING(ComboPackageID, 5, 20) AS INT)), 0)
                        FROM dbo.ComboPackage WITH (UPDLOCK, HOLDLOCK)
                        WHERE ComboPackageID LIKE 'PKG-%';
                        SET @SeqNum = @MaxNum + 1;
                    END
                    SELECT 'PKG-' + RIGHT('0000' + CAST(@SeqNum AS VARCHAR(10)), 4);";

                await using (var idCmd = new SqlCommand(idGenSql, conn, trans))
                {
                    generatedId = (await idCmd.ExecuteScalarAsync(cancellationToken))?.ToString() ?? "PKG-0001";
                }

                // Insert into ComboPackage
                const string insertPkgSql = @"
                    IF COL_LENGTH(N'dbo.ComboPackage', N'ComboPackagePrice') IS NULL
                    BEGIN
                        ALTER TABLE dbo.ComboPackage ADD ComboPackagePrice decimal(18,2) NOT NULL CONSTRAINT DF_ComboPackage_Price DEFAULT 0.00;
                    END;

                    INSERT INTO dbo.ComboPackage (
                        ComboPackageID, ComboPackageName, ComboPackageDescription, ComboPackagePrice, ComboPackageImage, ComboPackageImageMimeType
                    )
                    VALUES (@Id, @Name, @Desc, @Price, @Img, @Mime);";

                await using (var pkgCmd = new SqlCommand(insertPkgSql, conn, trans))
                {
                    pkgCmd.Parameters.AddWithValue("@Id", generatedId);
                    pkgCmd.Parameters.AddWithValue("@Name", model.Name.Trim());
                    pkgCmd.Parameters.AddWithValue("@Desc", model.Description?.Trim() ?? string.Empty);
                    pkgCmd.Parameters.AddWithValue("@Price", model.Price);
                    pkgCmd.Parameters.Add("@Img", SqlDbType.VarBinary, -1).Value = (object?)imageBytes ?? DBNull.Value;
                    pkgCmd.Parameters.AddWithValue("@Mime", string.IsNullOrWhiteSpace(imageMimeType) ? "image/jpeg" : imageMimeType.Trim());
                    await pkgCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // Insert Category Join
                if (!string.IsNullOrWhiteSpace(model.CategoryId))
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
                    catCmd.Parameters.AddWithValue("@CatId", model.CategoryId.Trim());
                    catCmd.Parameters.AddWithValue("@PkgId", generatedId);
                    await catCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // Insert Many-to-Many Service Joins
                if (model.SelectedServiceIds != null && model.SelectedServiceIds.Count > 0)
                {
                    const string joinAlterSql = @"
                        IF OBJECT_ID(N'dbo.ComboPackageJoin', N'U') IS NOT NULL
                        BEGIN
                            ALTER TABLE dbo.ComboPackageJoin ALTER COLUMN ServicesID nvarchar(30) NULL;
                        END;";
                    await using (var alterCmd = new SqlCommand(joinAlterSql, conn, trans))
                    {
                        await alterCmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    foreach (var svcId in model.SelectedServiceIds)
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
                        joinCmd.Parameters.AddWithValue("@PkgId", generatedId);
                        joinCmd.Parameters.AddWithValue("@SvcId", svcId.Trim());
                        await joinCmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                await trans.CommitAsync(cancellationToken);
            }
            catch
            {
                await trans.RollbackAsync(cancellationToken);
                throw;
            }
        }

        // 3. Resolve category name and service names for returned DTO
        var categoryName = model.CategoryId;
        try
        {
            const string catNameSql = "SELECT ServiceCategoryName FROM dbo.ServiceCategory WHERE ServiceCategoryID = @CatId;";
            await using var catCmd = new SqlCommand(catNameSql, conn);
            catCmd.Parameters.AddWithValue("@CatId", model.CategoryId ?? string.Empty);
            var catScalar = await catCmd.ExecuteScalarAsync(cancellationToken);
            if (catScalar != null && !Convert.IsDBNull(catScalar))
                categoryName = catScalar.ToString() ?? model.CategoryId;
        }
        catch { }

        return new ComboPackageCreatedResponseDto(
            PackageId: generatedId,
            Name: model.Name.Trim(),
            Description: model.Description?.Trim(),
            Price: model.Price,
            CategoryId: model.CategoryId,
            CategoryName: categoryName,
            BundledServicesCount: model.SelectedServiceIds?.Count ?? 0,
            BundledServiceNames: model.SelectedServiceIds ?? Array.Empty<string>(),
            HasImage: imageBytes != null && imageBytes.Length > 0,
            CreatedAt: DateTime.UtcNow
        );
    }
}
