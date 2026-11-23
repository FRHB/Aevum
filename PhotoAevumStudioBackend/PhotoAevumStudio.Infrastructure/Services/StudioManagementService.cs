using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Infrastructure.Services;

public class StudioManagementService : IStudioManagementService
{
    private const string CacheKeyServicesSummary = "Catalog:ServicesSummary";
    private const string CacheKeyAdditionalSummary = "Catalog:AdditionalServicesSummary";
    private const string CacheKeyPackagesSummary = "Catalog:ComboPackagesSummary";
    private const string CacheKeyCategories = "Catalog:Categories";
    private const string CacheKeyRateTypes = "Catalog:RateTypes";
    private static readonly TimeSpan CatalogCacheTtl = TimeSpan.FromMinutes(5);

    private readonly ApplicationDbContext _db;
    private readonly IR2StorageService? _r2Service;
    private readonly IMemoryCache? _cache;
    private readonly IEmailService? _emailService;

    public StudioManagementService(
        ApplicationDbContext db,
        IR2StorageService? r2Service = null,
        IMemoryCache? cache = null,
        IEmailService? emailService = null)
    {
        _db = db;
        _r2Service = r2Service;
        _cache = cache;
        _emailService = emailService;
    }

    private void InvalidateCatalogCache()
    {
        if (_cache is null) return;
        _cache.Remove(CacheKeyServicesSummary);
        _cache.Remove(CacheKeyAdditionalSummary);
        _cache.Remove(CacheKeyPackagesSummary);
        _cache.Remove(CacheKeyCategories);
        _cache.Remove(CacheKeyRateTypes);
    }

    private async Task<(string? FileName, string? ImageUrl)> UploadServiceAssetToR2Async(
        byte[]? imageBytes,
        string? fileName,
        string? mimeType,
        string folderPrefix,
        CancellationToken ct)
    {
        if (imageBytes == null || imageBytes.Length == 0 || _r2Service == null)
            return (null, null);

        var r2FileKey = await _r2Service.UploadServiceImageAsync(imageBytes, fileName, mimeType, folderPrefix, ct);
        if (string.IsNullOrWhiteSpace(r2FileKey))
            return (null, null);

        var r2Url = _r2Service.GetPresignedUrl(r2FileKey, _r2Service.ServicesBucket);
        return (r2FileKey, r2Url);
    }

    private string? ResolveR2ServiceImageUrl(string? imgFile, string? imgUrl, byte[]? imgBytes = null, string? mimeType = null)
    {
        var key = !string.IsNullOrWhiteSpace(imgFile)
            ? R2StorageService.ExtractObjectKey(imgFile)
            : R2StorageService.ExtractObjectKey(imgUrl);

        if (_r2Service != null && !string.IsNullOrWhiteSpace(key) &&
            (key.StartsWith("services/", StringComparison.OrdinalIgnoreCase) ||
             key.StartsWith("packages/", StringComparison.OrdinalIgnoreCase) ||
             key.StartsWith("equipment/", StringComparison.OrdinalIgnoreCase) ||
             key.StartsWith("portfolio/", StringComparison.OrdinalIgnoreCase)))
        {
            var presigned = _r2Service.GetPresignedUrl(key, _r2Service.ServicesBucket);
            if (!string.IsNullOrWhiteSpace(presigned))
                return presigned;
        }

        if (!string.IsNullOrWhiteSpace(imgUrl) &&
            !imgUrl.Contains("r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase) &&
            !imgUrl.Contains("pub-photoaevum.r2.dev", StringComparison.OrdinalIgnoreCase) &&
            !imgUrl.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
        {
            return imgUrl;
        }

        if (imgBytes != null && imgBytes.Length > 0)
        {
            var mime = string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType;
            return $"data:{mime};base64,{Convert.ToBase64String(imgBytes)}";
        }

        return null;
    }

    private string GetConnectionString() =>
        _db.Database.GetConnectionString()
        ?? throw new InvalidOperationException("Database connection string is not configured.");

    public async Task<IReadOnlyList<Employee>> GetEmployeesAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

        var specializationsByEmployee = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var departmentsByEmployee = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var departmentIdsByEmployee = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        const string specJoinSql = @"
            IF OBJECT_ID(N'dbo.EmployeeJointSpecialization', N'U') IS NOT NULL
               AND OBJECT_ID(N'dbo.EmployeeSpecialization', N'U') IS NOT NULL
            BEGIN
                SELECT ejs.EmployeeID,
                       COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization, es.SpecializationID) AS SpecName,
                       COALESCE(NULLIF(d.DepartmentName, ''), NULLIF(es.DepartmentName, ''), 'General') AS DeptName,
                       COALESCE(es.DepartmentID, d.DepartmentID) AS DeptId
                FROM dbo.EmployeeJointSpecialization ejs
                INNER JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID
                LEFT JOIN dbo.Departments d ON d.DepartmentID = es.DepartmentID;
            END";

        await using (var specCmd = new SqlCommand(specJoinSql, conn))
        await using (var specReader = await specCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await specReader.ReadAsync(cancellationToken))
            {
                var empId = specReader.GetString(0);
                var specName = specReader.IsDBNull(1) ? string.Empty : specReader.GetString(1);
                var deptName = specReader.IsDBNull(2) ? string.Empty : specReader.GetString(2);
                var specDeptId = specReader.IsDBNull(3) ? null : specReader.GetString(3);

                if (!string.IsNullOrWhiteSpace(specName))
                {
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

                if (!string.IsNullOrWhiteSpace(deptName) && !string.Equals(deptName, "General", StringComparison.OrdinalIgnoreCase))
                {
                    if (!departmentsByEmployee.TryGetValue(empId, out var dList))
                    {
                        dList = new List<string>();
                        departmentsByEmployee[empId] = dList;
                    }
                    if (!dList.Contains(deptName, StringComparer.OrdinalIgnoreCase))
                    {
                        dList.Add(deptName);
                    }
                    if (!string.IsNullOrWhiteSpace(specDeptId) && !departmentIdsByEmployee.ContainsKey(empId))
                    {
                        departmentIdsByEmployee[empId] = specDeptId;
                    }
                }
            }
        }

        var employees = new List<Employee>();
        string filterClause = includeArchived
            ? string.Empty
            : "WHERE e.EmployeeStatus NOT IN ('Archive', 'Archived', 'Deleted') AND e.Status NOT IN ('Archive', 'Archived', 'Deleted')";

        string empSql = $@"
            SELECT e.EmployeeID, e.EmployeeFirstName, e.EmployeeLastName, e.EmployeeMiddleName, e.EmployeeEmail, e.EmployeeStatus,
                   CASE WHEN COL_LENGTH(N'dbo.Employees', N'DaysOff') IS NOT NULL THEN ISNULL(e.DaysOff, 'Sunday') ELSE 'Sunday' END AS DaysOff,
                   CASE WHEN COL_LENGTH(N'dbo.Employees', N'Address') IS NOT NULL THEN e.Address ELSE NULL END AS Address,
                   CASE WHEN COL_LENGTH(N'dbo.Employees', N'BirthYear') IS NOT NULL THEN e.BirthYear ELSE NULL END AS BirthYear,
                   CASE WHEN COL_LENGTH(N'dbo.Employees', N'DepartmentID') IS NOT NULL THEN e.DepartmentID ELSE NULL END AS DepartmentID,
                   d.DepartmentName,
                   CASE WHEN COL_LENGTH(N'dbo.Employees', N'BirthDate') IS NOT NULL THEN e.BirthDate ELSE NULL END AS BirthDate,
                   CASE WHEN COL_LENGTH(N'dbo.Employees', N'Specializations') IS NOT NULL THEN e.Specializations ELSE NULL END AS EfSpecs
            FROM dbo.Employees e
            LEFT JOIN dbo.Departments d ON d.DepartmentID = e.DepartmentID
            {filterClause}
            ORDER BY e.EmployeeID;";

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
            var daysOff = reader.IsDBNull(6) ? "Sunday" : reader.GetString(6);
            var address = reader.IsDBNull(7) ? null : reader.GetString(7);
            int? birthYear = reader.IsDBNull(8) ? null : reader.GetInt32(8);
            var deptId = reader.IsDBNull(9) ? null : reader.GetString(9);
            var deptName = reader.IsDBNull(10) ? null : reader.GetString(10);
            DateTime? birthDate = reader.FieldCount > 11 && !reader.IsDBNull(11) ? reader.GetDateTime(11) : null;
            var efSpecsJson = reader.FieldCount > 12 && !reader.IsDBNull(12) ? reader.GetString(12) : null;
            birthYear ??= birthDate?.Year;

            specializationsByEmployee.TryGetValue(empId, out var specs);
            if ((specs == null || specs.Count == 0) && !string.IsNullOrWhiteSpace(efSpecsJson) && efSpecsJson != "[]")
            {
                try
                {
                    var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(efSpecsJson);
                    if (parsed != null && parsed.Count > 0)
                    {
                        specs = parsed;
                    }
                }
                catch
                {
                    // Ignore JSON parse errors
                }
            }

            // Connect department directly to specialization(s)
            if (departmentsByEmployee.TryGetValue(empId, out var dList) && dList.Count > 0)
            {
                deptName = string.Join(", ", dList);
                deptId = departmentIdsByEmployee.GetValueOrDefault(empId) ?? deptId;
            }
            deptName ??= "General";

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
                    UserName = email,
                    Address = address,
                    BirthYear = birthYear,
                    BirthDate = birthDate,
                    DepartmentId = deptId
                },
                Address = address,
                BirthYear = birthYear,
                BirthDate = birthDate,
                DepartmentID = deptId,
                DepartmentEntity = new Department { DepartmentID = deptId ?? "DEP-0001", DepartmentName = deptName },
                Status = status,
                DaysOff = daysOff,
                Specializations = specs ?? new List<string>()
            });
        }

        return employees;
    }

    public async Task<IReadOnlyList<string>> GetSpecializationsAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

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
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

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
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

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

    private static volatile bool _schemaAligned;

    private static async Task EnsureServicesRateTypeSchemaAsync(SqlConnection conn, CancellationToken ct)
    {
        if (_schemaAligned) return;

        try
        {
            const string alignSql = @"
                IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Employees', N'BirthDate') IS NULL
                        ALTER TABLE dbo.Employees ADD BirthDate date NULL;
                    IF COL_LENGTH(N'dbo.Employees', N'BirthYear') IS NULL
                        ALTER TABLE dbo.Employees ADD BirthYear int NULL;
                    IF COL_LENGTH(N'dbo.Employees', N'Address') IS NULL
                        ALTER TABLE dbo.Employees ADD Address nvarchar(500) NULL;
                END;
                IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Services', N'RateType') IS NULL
                        ALTER TABLE dbo.Services ADD RateType nvarchar(50) NOT NULL CONSTRAINT DF_Services_RateType DEFAULT 'Per Hour';
                    IF COL_LENGTH(N'dbo.Services', N'ImageFileName') IS NULL
                        ALTER TABLE dbo.Services ADD ImageFileName nvarchar(500) NULL;
                    IF COL_LENGTH(N'dbo.Services', N'ImageUrl') IS NULL
                        ALTER TABLE dbo.Services ADD ImageUrl nvarchar(500) NULL;
                END;
                IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.AdditionalServices', N'RateType') IS NULL
                        ALTER TABLE dbo.AdditionalServices ADD RateType nvarchar(50) NOT NULL CONSTRAINT DF_AdditionalServices_RateType DEFAULT 'Per Hour';
                    IF COL_LENGTH(N'dbo.AdditionalServices', N'ImageFileName') IS NULL
                        ALTER TABLE dbo.AdditionalServices ADD ImageFileName nvarchar(500) NULL;
                    IF COL_LENGTH(N'dbo.AdditionalServices', N'ImageUrl') IS NULL
                        ALTER TABLE dbo.AdditionalServices ADD ImageUrl nvarchar(500) NULL;
                END;
                IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.ComboPackage', N'RateType') IS NULL
                        ALTER TABLE dbo.ComboPackage ADD RateType nvarchar(50) NOT NULL CONSTRAINT DF_ComboPackage_RateType DEFAULT 'Per Hour';
                    IF COL_LENGTH(N'dbo.ComboPackage', N'ImageFileName') IS NULL
                        ALTER TABLE dbo.ComboPackage ADD ImageFileName nvarchar(500) NULL;
                    IF COL_LENGTH(N'dbo.ComboPackage', N'ImageUrl') IS NULL
                        ALTER TABLE dbo.ComboPackage ADD ImageUrl nvarchar(500) NULL;
                END;
                IF OBJECT_ID(N'dbo.PortfolioImages', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.PortfolioImages', N'ImageUrl') IS NULL
                        ALTER TABLE dbo.PortfolioImages ADD ImageUrl nvarchar(500) NULL;
                    IF COL_LENGTH(N'dbo.PortfolioImages', N'ContentType') IS NULL
                        ALTER TABLE dbo.PortfolioImages ADD ContentType nvarchar(100) NULL CONSTRAINT DF_PortfolioImages_ContentType DEFAULT 'image/jpeg';
                    IF COL_LENGTH(N'dbo.PortfolioImages', N'ImageData') IS NULL
                        ALTER TABLE dbo.PortfolioImages ADD ImageData varbinary(max) NULL;
                END;";
            await using var alignCmd = new SqlCommand(alignSql, conn);
            await alignCmd.ExecuteNonQueryAsync(ct);
            _schemaAligned = true;
        }
        catch { /* Best-effort schema alignment */ }
    }

    public async Task<IReadOnlyList<ServiceSummaryDto>> GetServicesSummaryAsync(CancellationToken cancellationToken = default)
    {
        if (_cache != null && _cache.TryGetValue(CacheKeyServicesSummary, out IReadOnlyList<ServiceSummaryDto>? cached) && cached != null)
            return cached;

        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

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
                var rateType = reader.FieldCount > 5 && !reader.IsDBNull(5) ? reader.GetString(5) : "per hour";
                var imgFile = reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetString(6) : null;
                var imgUrl = reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetString(7) : null;
                imgUrl = ResolveR2ServiceImageUrl(imgFile, imgUrl);

                list.Add(new ServiceSummaryDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: reader.IsDBNull(3) ? 0m : reader.GetDecimal(3),
                    HasImage: (!reader.IsDBNull(4) && reader.GetBoolean(4)) || !string.IsNullOrWhiteSpace(imgFile) || !string.IsNullOrWhiteSpace(imgUrl),
                    RateType: rateType,
                    ImageFileName: imgFile,
                    ImageUrl: imgUrl
                ));
            }
        }
        catch (SqlException)
        {
            // Direct query fallback with schema safety
            var hasRateType = false;
            try
            {
                await using var chkCmd = new SqlCommand("SELECT COL_LENGTH(N'dbo.Services', N'RateType');", conn);
                var chkVal = await chkCmd.ExecuteScalarAsync(cancellationToken);
                hasRateType = chkVal != null && chkVal != DBNull.Value;
            }
            catch { }

            var rateTypeCol = hasRateType ? "ISNULL(RateType, 'per hour')" : "'per hour'";
            var fallbackSql = $@"
                SELECT ServicesID, ServiceName, ServicesDescription, ServicePrice,
                       CAST(CASE WHEN ServiceImage IS NOT NULL OR ImageFileName IS NOT NULL OR ImageUrl IS NOT NULL THEN 1 ELSE 0 END AS bit),
                       {rateTypeCol}, ImageFileName, ImageUrl
                FROM dbo.Services ORDER BY ServicesID;";
            await using var fbCmd = new SqlCommand(fallbackSql, conn);
            await using var reader = await fbCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var rateType = reader.FieldCount > 5 && !reader.IsDBNull(5) ? reader.GetString(5) : "per hour";
                var imgFile = reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetString(6) : null;
                var imgUrl = reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetString(7) : null;
                imgUrl = ResolveR2ServiceImageUrl(imgFile, imgUrl);

                list.Add(new ServiceSummaryDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: reader.IsDBNull(3) ? 0m : reader.GetDecimal(3),
                    HasImage: !reader.IsDBNull(4) && reader.GetBoolean(4),
                    RateType: rateType,
                    ImageFileName: imgFile,
                    ImageUrl: imgUrl
                ));
            }
        }

        _cache?.Set(CacheKeyServicesSummary, (IReadOnlyList<ServiceSummaryDto>)list, CatalogCacheTtl);
        return list;
    }

    public async Task<ServiceDetailDto?> GetServiceDetailWithImageAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

        try
        {
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
                var rateType = reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetString(7) : "per hour";
                var imgFile = reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetString(8) : null;
                var imgUrl = reader.FieldCount > 9 && !reader.IsDBNull(9) ? reader.GetString(9) : null;
                imgUrl = ResolveR2ServiceImageUrl(imgFile, imgUrl, imageBytes, mime);

                return new ServiceDetailDto(id, name, desc, price, imageBytes, mime, hasImage || !string.IsNullOrWhiteSpace(imgFile), rateType, imgFile, imgUrl);
            }
        }
        catch (SqlException)
        {
            var hasRateType = false;
            try
            {
                await using var chkCmd = new SqlCommand("SELECT COL_LENGTH(N'dbo.Services', N'RateType');", conn);
                var chkVal = await chkCmd.ExecuteScalarAsync(cancellationToken);
                hasRateType = chkVal != null && chkVal != DBNull.Value;
            }
            catch { }

            var rateTypeCol = hasRateType ? "ISNULL(RateType, 'per hour')" : "'per hour'";
            var fallbackSql = $@"
                SELECT ServicesID, ServiceName, ServicesDescription, ServicePrice, ServiceImage, ServiceImageMimeType,
                       CAST(CASE WHEN ServiceImage IS NOT NULL OR ImageFileName IS NOT NULL OR ImageUrl IS NOT NULL THEN 1 ELSE 0 END AS bit),
                       {rateTypeCol}, ImageFileName, ImageUrl
                FROM dbo.Services WHERE ServicesID = @ServicesID;";
            await using var fbCmd = new SqlCommand(fallbackSql, conn);
            fbCmd.Parameters.AddWithValue("@ServicesID", serviceId.Trim());
            await using var reader = await fbCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0);
                var name = reader.GetString(1);
                var desc = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var price = reader.IsDBNull(3) ? 0m : reader.GetDecimal(3);
                byte[]? imageBytes = reader.IsDBNull(4) ? null : (byte[])reader[4];
                var mime = reader.IsDBNull(5) ? "image/jpeg" : reader.GetString(5);
                var hasImage = !reader.IsDBNull(6) && reader.GetBoolean(6);
                var rateType = reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetString(7) : "per hour";
                var imgFile = reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetString(8) : null;
                var imgUrl = reader.FieldCount > 9 && !reader.IsDBNull(9) ? reader.GetString(9) : null;
                imgUrl = ResolveR2ServiceImageUrl(imgFile, imgUrl, imageBytes, mime);

                return new ServiceDetailDto(id, name, desc, price, imageBytes, mime, hasImage || !string.IsNullOrWhiteSpace(imgFile), rateType, imgFile, imgUrl);
            }
        }

        return null;
    }

    public async Task<bool> UpdateServiceImageAsync(string serviceId, byte[] imageBytes, string mimeType, string? requestingAdminId, CancellationToken cancellationToken = default)
    {
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

        var (r2FileName, r2Url) = await UploadServiceAssetToR2Async(imageBytes, $"{serviceId.Trim()}.jpg", mimeType, "services/main", cancellationToken);

        await using var cmd = new SqlCommand("dbo.sp_UpdateServiceImage", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@ServicesID", serviceId.Trim());
        cmd.Parameters.Add("@ServiceImage", SqlDbType.VarBinary, -1).Value = imageBytes;
        cmd.Parameters.AddWithValue("@ServiceImageMimeType", string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType);
        cmd.Parameters.AddWithValue("@ImageFileName", (object?)r2FileName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ImageUrl", (object?)r2Url ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UpdatedByAdminID", (object?)requestingAdminId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        InvalidateCatalogCache();
        return true;
    }

    // ── Additional Services ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<ServiceSummaryDto>> GetAdditionalServicesSummaryAsync(CancellationToken cancellationToken = default)
    {
        if (_cache != null && _cache.TryGetValue(CacheKeyAdditionalSummary, out IReadOnlyList<ServiceSummaryDto>? cached) && cached != null)
            return cached;

        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

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
                var imgFile = reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetString(6) : null;
                var imgUrl = reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetString(7) : null;
                var rateType = reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetString(8) : "per hour";
                imgUrl = ResolveR2ServiceImageUrl(imgFile, imgUrl);

                list.Add(new ServiceSummaryDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: price,
                    HasImage: hasImg || !string.IsNullOrWhiteSpace(imgFile),
                    ImageFileName: imgFile,
                    ImageUrl: imgUrl,
                    RateType: rateType
                ));
            }
        }
        catch (SqlException)
        {
            const string fallbackSql = @"
                SELECT AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription,
                       ISNULL(ServicePrice, 0.00),
                       CAST(CASE WHEN AdditionalServiceImage IS NOT NULL OR ImageFileName IS NOT NULL OR ImageUrl IS NOT NULL THEN 1 ELSE 0 END AS bit),
                       ImageFileName, ImageUrl,
                       ISNULL(RateType, 'Per Hour') AS RateType
                FROM dbo.AdditionalServices ORDER BY AdditionalServicesID;";
            await using var fbCmd = new SqlCommand(fallbackSql, conn);
            await using var reader = await fbCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var imgFile = reader.FieldCount > 5 && !reader.IsDBNull(5) ? reader.GetString(5) : null;
                var imgUrl = reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetString(6) : null;
                var rateType = reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetString(7) : "per hour";
                imgUrl = ResolveR2ServiceImageUrl(imgFile, imgUrl);

                list.Add(new ServiceSummaryDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: reader.IsDBNull(3) ? 0m : reader.GetDecimal(3),
                    HasImage: !reader.IsDBNull(4) && reader.GetBoolean(4),
                    ImageFileName: imgFile,
                    ImageUrl: imgUrl,
                    RateType: rateType
                ));
            }
        }

        _cache?.Set(CacheKeyAdditionalSummary, (IReadOnlyList<ServiceSummaryDto>)list, CatalogCacheTtl);
        return list;
    }

    public async Task<ServiceDetailDto?> GetAdditionalServiceDetailWithImageAsync(string addOnId, CancellationToken cancellationToken = default)
    {
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

        try
        {
            await using var cmd = new SqlCommand("dbo.sp_GetAdditionalServiceDetailWithImage", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@AdditionalServicesID", addOnId.Trim());

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var hasCategoryCol = reader.FieldCount >= 8;
                var price = reader.IsDBNull(3) ? 0m : reader.GetDecimal(3);
                var imgIdx = hasCategoryCol ? 5 : 3;
                var mimeIdx = hasCategoryCol ? 6 : 4;
                var hasImgIdx = hasCategoryCol ? 7 : 5;
                var imgFile = reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetString(8) : null;
                var imgUrl = reader.FieldCount > 9 && !reader.IsDBNull(9) ? reader.GetString(9) : null;
                var rateType = reader.FieldCount > 10 && !reader.IsDBNull(10) ? reader.GetString(10) : "per hour";
                byte[]? imgBytes = reader.IsDBNull(imgIdx) ? null : (byte[])reader[imgIdx];
                var mime = reader.IsDBNull(mimeIdx) ? "image/jpeg" : reader.GetString(mimeIdx);
                imgUrl = ResolveR2ServiceImageUrl(imgFile, imgUrl, imgBytes, mime);

                return new ServiceDetailDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: price,
                    Image: imgBytes,
                    MimeType: mime,
                    HasImage: (!reader.IsDBNull(hasImgIdx) && reader.GetBoolean(hasImgIdx)) || !string.IsNullOrWhiteSpace(imgFile),
                    ImageFileName: imgFile,
                    ImageUrl: imgUrl,
                    RateType: rateType
                );
            }
        }
        catch (SqlException)
        {
            const string fallbackSql = @"
                SELECT AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription,
                       ISNULL(ServicePrice, 0.00), ISNULL(CategoryName, 'Add-On'),
                       AdditionalServiceImage, ISNULL(AdditionalServiceImageMimeType, 'image/jpeg'),
                       CAST(CASE WHEN AdditionalServiceImage IS NOT NULL OR ImageFileName IS NOT NULL OR ImageUrl IS NOT NULL THEN 1 ELSE 0 END AS bit),
                       ImageFileName, ImageUrl, ISNULL(RateType, 'Per Hour')
                FROM dbo.AdditionalServices
                WHERE AdditionalServicesID = @Id;";
            await using var fbCmd = new SqlCommand(fallbackSql, conn);
            fbCmd.Parameters.AddWithValue("@Id", addOnId.Trim());
            await using var reader = await fbCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var imgFile = !reader.IsDBNull(8) ? reader.GetString(8) : null;
                var imgUrl = !reader.IsDBNull(9) ? reader.GetString(9) : null;
                var rateType = !reader.IsDBNull(10) ? reader.GetString(10) : "per hour";
                byte[]? imgBytes = reader.IsDBNull(5) ? null : (byte[])reader[5];
                var mime = reader.IsDBNull(6) ? "image/jpeg" : reader.GetString(6);
                imgUrl = ResolveR2ServiceImageUrl(imgFile, imgUrl, imgBytes, mime);

                return new ServiceDetailDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: reader.IsDBNull(3) ? 0m : reader.GetDecimal(3),
                    Image: imgBytes,
                    MimeType: mime,
                    HasImage: (!reader.IsDBNull(7) && reader.GetBoolean(7)) || !string.IsNullOrWhiteSpace(imgFile),
                    ImageFileName: imgFile,
                    ImageUrl: imgUrl,
                    RateType: rateType
                );
            }
        }

        return null;
    }

    public async Task<bool> UpdateAdditionalServiceImageAsync(string addOnId, byte[] imageBytes, string mimeType, string? requestingAdminId, CancellationToken cancellationToken = default)
    {
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

        var (r2FileName, r2Url) = await UploadServiceAssetToR2Async(imageBytes, $"{addOnId.Trim()}.jpg", mimeType, "services/addons", cancellationToken);

        await using var cmd = new SqlCommand("dbo.sp_UpdateAdditionalServiceImage", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@AdditionalServicesID", addOnId.Trim());
        cmd.Parameters.Add("@AdditionalServiceImage", SqlDbType.VarBinary, -1).Value = imageBytes;
        cmd.Parameters.AddWithValue("@AdditionalServiceImageMimeType", string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType);
        cmd.Parameters.AddWithValue("@ImageFileName", (object?)r2FileName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ImageUrl", (object?)r2Url ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UpdatedByAdminID", (object?)requestingAdminId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        InvalidateCatalogCache();
        return true;
    }

    // ── Combo Packages ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ServiceSummaryDto>> GetComboPackagesSummaryAsync(CancellationToken cancellationToken = default)
    {
        if (_cache != null && _cache.TryGetValue(CacheKeyPackagesSummary, out IReadOnlyList<ServiceSummaryDto>? cached) && cached != null)
            return cached;

        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

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
                var hasPriceCol = reader.FieldCount >= 5;
                var price = hasPriceCol && !reader.IsDBNull(3) ? reader.GetDecimal(3) : 0m;
                var hasImgIdx = hasPriceCol ? 4 : 3;
                var imgFile = reader.FieldCount > 5 && !reader.IsDBNull(5) ? reader.GetString(5) : null;
                var imgUrl = reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetString(6) : null;
                var rateType = reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetString(7) : "per hour";
                imgUrl = ResolveR2ServiceImageUrl(imgFile, imgUrl);

                list.Add(new ServiceSummaryDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: price,
                    HasImage: (!reader.IsDBNull(hasImgIdx) && reader.GetBoolean(hasImgIdx)) || !string.IsNullOrWhiteSpace(imgFile),
                    ImageFileName: imgFile,
                    ImageUrl: imgUrl,
                    RateType: rateType
                ));
            }
        }
        catch (SqlException)
        {
            const string fallbackSql = @"
                SELECT ComboPackageID, ComboPackageName, ComboPackageDescription,
                       ISNULL(ComboPackagePrice, 0.00) AS ComboPackagePrice,
                       CAST(CASE WHEN ComboPackageImage IS NOT NULL OR ImageFileName IS NOT NULL OR ImageUrl IS NOT NULL THEN 1 ELSE 0 END AS bit),
                       ImageFileName, ImageUrl,
                       ISNULL(RateType, 'Per Hour') AS RateType
                FROM dbo.ComboPackage ORDER BY ComboPackageID;";
            await using var fbCmd = new SqlCommand(fallbackSql, conn);
            await using var reader = await fbCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var imgFile = reader.FieldCount > 5 && !reader.IsDBNull(5) ? reader.GetString(5) : null;
                var imgUrl = reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetString(6) : null;
                var rateType = reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetString(7) : "per hour";
                imgUrl = ResolveR2ServiceImageUrl(imgFile, imgUrl);

                list.Add(new ServiceSummaryDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: reader.IsDBNull(3) ? 0m : reader.GetDecimal(3),
                    HasImage: !reader.IsDBNull(4) && reader.GetBoolean(4),
                    ImageFileName: imgFile,
                    ImageUrl: imgUrl,
                    RateType: rateType
                ));
            }
        }

        _cache?.Set(CacheKeyPackagesSummary, (IReadOnlyList<ServiceSummaryDto>)list, CatalogCacheTtl);
        return list;
    }

    public async Task<ServiceDetailDto?> GetComboPackageDetailWithImageAsync(string packageId, CancellationToken cancellationToken = default)
    {
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

        try
        {
            await using var cmd = new SqlCommand("dbo.sp_GetComboPackageDetailWithImage", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@ComboPackageID", packageId.Trim());

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var hasPriceCol = reader.FieldCount >= 7;
                var price = hasPriceCol && !reader.IsDBNull(3) ? reader.GetDecimal(3) : 0m;
                var imgIdx = hasPriceCol ? 4 : 3;
                var mimeIdx = hasPriceCol ? 5 : 4;
                var hasImgIdx = hasPriceCol ? 6 : 5;
                var imgFile = reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetString(7) : null;
                var imgUrl = reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetString(8) : null;
                var rateType = reader.FieldCount > 9 && !reader.IsDBNull(9) ? reader.GetString(9) : "per hour";
                byte[]? imgBytes = reader.IsDBNull(imgIdx) ? null : (byte[])reader[imgIdx];
                var mime = reader.IsDBNull(mimeIdx) ? "image/jpeg" : reader.GetString(mimeIdx);
                imgUrl = ResolveR2ServiceImageUrl(imgFile, imgUrl, imgBytes, mime);

                return new ServiceDetailDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: price,
                    Image: imgBytes,
                    MimeType: mime,
                    HasImage: (!reader.IsDBNull(hasImgIdx) && reader.GetBoolean(hasImgIdx)) || !string.IsNullOrWhiteSpace(imgFile),
                    ImageFileName: imgFile,
                    ImageUrl: imgUrl,
                    RateType: rateType
                );
            }
        }
        catch (SqlException)
        {
            const string fallbackSql = @"
                SELECT ComboPackageID, ComboPackageName, ComboPackageDescription,
                       ISNULL(ComboPackagePrice, 0.00),
                       ComboPackageImage, ISNULL(ComboPackageImageMimeType, 'image/jpeg'),
                       CAST(CASE WHEN ComboPackageImage IS NOT NULL OR ImageFileName IS NOT NULL OR ImageUrl IS NOT NULL THEN 1 ELSE 0 END AS bit),
                       ImageFileName, ImageUrl, ISNULL(RateType, 'Per Hour')
                FROM dbo.ComboPackage
                WHERE ComboPackageID = @Id;";
            await using var fbCmd = new SqlCommand(fallbackSql, conn);
            fbCmd.Parameters.AddWithValue("@Id", packageId.Trim());
            await using var reader = await fbCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var imgFile = !reader.IsDBNull(7) ? reader.GetString(7) : null;
                var imgUrl = !reader.IsDBNull(8) ? reader.GetString(8) : null;
                var rateType = !reader.IsDBNull(9) ? reader.GetString(9) : "per hour";
                byte[]? imgBytes = reader.IsDBNull(4) ? null : (byte[])reader[4];
                var mime = reader.IsDBNull(5) ? "image/jpeg" : reader.GetString(5);
                imgUrl = ResolveR2ServiceImageUrl(imgFile, imgUrl, imgBytes, mime);

                return new ServiceDetailDto(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Price: reader.IsDBNull(3) ? 0m : reader.GetDecimal(3),
                    Image: imgBytes,
                    MimeType: mime,
                    HasImage: (!reader.IsDBNull(6) && reader.GetBoolean(6)) || !string.IsNullOrWhiteSpace(imgFile),
                    ImageFileName: imgFile,
                    ImageUrl: imgUrl,
                    RateType: rateType
                );
            }
        }

        return null;
    }

    public async Task<bool> UpdateComboPackageImageAsync(string packageId, byte[] imageBytes, string mimeType, string? requestingAdminId, CancellationToken cancellationToken = default)
    {
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

        var (r2FileName, r2Url) = await UploadServiceAssetToR2Async(imageBytes, $"{packageId.Trim()}.jpg", mimeType, "services/packages", cancellationToken);

        await using var cmd = new SqlCommand("dbo.sp_UpdateComboPackageImage", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@ComboPackageID", packageId.Trim());
        cmd.Parameters.Add("@ComboPackageImage", SqlDbType.VarBinary, -1).Value = imageBytes;
        cmd.Parameters.AddWithValue("@ComboPackageImageMimeType", string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType);
        cmd.Parameters.AddWithValue("@ImageFileName", (object?)r2FileName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ImageUrl", (object?)r2Url ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UpdatedByAdminID", (object?)requestingAdminId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        InvalidateCatalogCache();
        return true;
    }

    // ── History Logs (SystemAdmin full access) ────────────────────────────────

    public async Task<IReadOnlyList<HistoryLogDto>> GetHistoryLogsAsync(string requestingUserId, int topCount = 100, CancellationToken cancellationToken = default)
    {
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

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
            // Direct query fallback with role-based filtering
            bool isSadm = requestingUserId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase);
            bool isEmp = !isSadm && (requestingUserId.StartsWith("EMP-", StringComparison.OrdinalIgnoreCase) || requestingUserId.StartsWith("CLK-", StringComparison.OrdinalIgnoreCase));

            string fallbackSql;
            if (isSadm)
            {
                fallbackSql = @"
                    SELECT TOP (@TopCount) HistoryLogID, ActionType, EntityName, EntityID, ActionDetails,
                           EventAppointmentID, CustomerID, EmployeeID, ChangedByType, ChangedBy, LoggedAt
                    FROM dbo.HistoryLog
                    ORDER BY LoggedAt DESC;";
            }
            else if (isEmp)
            {
                fallbackSql = @"
                    SELECT TOP (@TopCount) HistoryLogID, ActionType, EntityName, EntityID, ActionDetails,
                           EventAppointmentID, CustomerID, EmployeeID, ChangedByType, ChangedBy, LoggedAt
                    FROM dbo.HistoryLog
                    WHERE ChangedByEmployeeID = @RequestingUserID
                       OR EmployeeID = @RequestingUserID
                       OR EntityName IN ('Equipment', 'EventAppointment', 'Session')
                    ORDER BY LoggedAt DESC;";
            }
            else
            {
                fallbackSql = @"
                    SELECT TOP (@TopCount) HistoryLogID, ActionType, EntityName, EntityID, ActionDetails,
                           EventAppointmentID, CustomerID, EmployeeID, ChangedByType, ChangedBy, LoggedAt
                    FROM dbo.HistoryLog
                    WHERE ChangedByAdminID = @RequestingUserID
                       OR EntityName IN ('Services', 'AdditionalServices', 'ComboPackage', 'Equipment', 'EventAppointment')
                    ORDER BY LoggedAt DESC;";
            }

            await using var fbCmd = new SqlCommand(fallbackSql, conn);
            fbCmd.Parameters.AddWithValue("@TopCount", topCount);
            fbCmd.Parameters.AddWithValue("@RequestingUserID", requestingUserId.Trim());
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
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

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
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

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
        if (_cache != null && _cache.TryGetValue(CacheKeyCategories, out IReadOnlyList<CategoryDto>? cached) && cached != null)
            return cached;

        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

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
                var catId = reader.GetString(0);
                var catName = reader.GetString(1);
                var catDesc = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var rateTypeId = reader.FieldCount > 3 && !reader.IsDBNull(3) ? reader.GetString(3) : "RT-0001";
                var rateTypeName = reader.FieldCount > 4 && !reader.IsDBNull(4) ? reader.GetString(4) : "Per Hour";

                list.Add(new CategoryDto(
                    Id: catId,
                    Name: catName,
                    Description: catDesc,
                    RateTypeId: rateTypeId,
                    RateTypeName: rateTypeName
                ));
            }
        }
        catch (SqlException)
        {
            // Direct query fallback
        }

        if (list.Count == 0)
        {
            const string fallbackSql = @"
                SELECT ServiceCategoryID, ServiceCategoryName, ServiceCategoryDescription,
                       ISNULL(RateTypeID, 'RT-0001') AS RateTypeID,
                       ISNULL(RateTypeName, 'Per Hour') AS RateTypeName
                FROM dbo.ServiceCategory
                ORDER BY ServiceCategoryName;";

            await using var fbCmd = new SqlCommand(fallbackSql, conn);
            await using var readerFb = await fbCmd.ExecuteReaderAsync(cancellationToken);
            while (await readerFb.ReadAsync(cancellationToken))
            {
                var catId = readerFb.GetString(0);
                var catName = readerFb.GetString(1);
                var catDesc = readerFb.IsDBNull(2) ? string.Empty : readerFb.GetString(2);
                var rateTypeId = readerFb.FieldCount > 3 && !readerFb.IsDBNull(3) ? readerFb.GetString(3) : "RT-0001";
                var rateTypeName = readerFb.FieldCount > 4 && !readerFb.IsDBNull(4) ? readerFb.GetString(4) : "Per Hour";

                list.Add(new CategoryDto(
                    Id: catId,
                    Name: catName,
                    Description: catDesc,
                    RateTypeId: rateTypeId,
                    RateTypeName: rateTypeName
                ));
            }
        }

        // Enrich with associated services from dbo.ServiceCategoryJoins
        if (list.Count > 0)
        {
            var assocMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            const string joinSql = @"
                IF OBJECT_ID(N'dbo.ServiceCategoryJoins', N'U') IS NOT NULL
                BEGIN
                    SELECT j.ServiceCategoryID,
                           COALESCE(s.ServiceName, a.AdditionalServiceName, p.ComboPackageName) AS AssocName
                    FROM dbo.ServiceCategoryJoins j
                    LEFT JOIN dbo.Services s ON s.ServicesID = j.ServicesID
                    LEFT JOIN dbo.AdditionalServices a ON a.AdditionalServicesID = j.AdditionalServicesID
                    LEFT JOIN dbo.ComboPackage p ON p.ComboPackageID = j.ComboPackageID
                    WHERE COALESCE(s.ServiceName, a.AdditionalServiceName, p.ComboPackageName) IS NOT NULL;
                END";

            await using var joinCmd = new SqlCommand(joinSql, conn);
            await using var joinReader = await joinCmd.ExecuteReaderAsync(cancellationToken);
            while (await joinReader.ReadAsync(cancellationToken))
            {
                var catId = joinReader.GetString(0);
                var name = joinReader.GetString(1);
                if (!assocMap.TryGetValue(catId, out var names))
                {
                    names = new List<string>();
                    assocMap[catId] = names;
                }
                if (!names.Contains(name))
                    names.Add(name);
            }

            var enriched = new List<CategoryDto>(list.Count);
            foreach (var c in list)
            {
                assocMap.TryGetValue(c.Id, out var assocNames);
                enriched.Add(c with { AssociatedServices = assocNames ?? new List<string>() });
            }
            _cache?.Set(CacheKeyCategories, (IReadOnlyList<CategoryDto>)enriched, CatalogCacheTtl);
            return enriched;
        }

        _cache?.Set(CacheKeyCategories, (IReadOnlyList<CategoryDto>)list, CatalogCacheTtl);
        return list;
    }

    public async Task<CategoryDto?> GetCategoryByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        // Try EF Core query with navigation Include first
        try
        {
            var entity = await _db.EventCategories
                .AsNoTracking()
                .Include(c => c.Associations)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

            if (entity != null)
            {
                var names = await ResolveAssociatedNamesAsync(entity.Associations.ToList(), cancellationToken);
                return new CategoryDto(entity.Id, entity.Name, entity.Description, names);
            }
        }
        catch
        {
            // Fall back to GetCategoriesAsync lookup
        }

        var categories = await GetCategoriesAsync(cancellationToken);
        return categories.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ServiceCreatedResponseDto> CreateServiceAsync(CreateServiceDto dto, string? requestingAdminId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Service name is required.", nameof(dto));

        var (r2FileName, r2Url) = await UploadServiceAssetToR2Async(
            dto.ImageBytes,
            dto.ImageFileName,
            dto.ImageMimeType,
            "services/main",
            cancellationToken);

        var effectiveFileName = r2FileName ?? dto.ImageFileName;
        var effectiveUrl = r2Url ?? dto.ImageUrl;

        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

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
            cmd.Parameters.AddWithValue("@ImageFileName", (object?)effectiveFileName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ImageUrl", (object?)effectiveUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedByAdminID", (object?)requestingAdminId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RateType", (object?)dto.RateType ?? "per hour");

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
                InvalidateCatalogCache();

                return new ServiceCreatedResponseDto(id, name, desc, price, catId ?? string.Empty, catName, hasImg, effectiveFileName, effectiveUrl);
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
                    ServicesID, ServiceName, ServicesDescription, ServicePrice, ServiceImage, ServiceImageMimeType, RateType, ImageFileName, ImageUrl
                )
                VALUES (
                    @Id, @Name, @Desc, @Price, @Img, @Mime, @RateType, @ImgFile, @ImgUrl
                );";

            await using var insertCmd = new SqlCommand(insertSql, conn, trans);
            insertCmd.Parameters.AddWithValue("@Id", nextId);
            insertCmd.Parameters.AddWithValue("@Name", dto.Name.Trim());
            insertCmd.Parameters.AddWithValue("@Desc", dto.Description.Trim());
            insertCmd.Parameters.AddWithValue("@Price", dto.Price);
            insertCmd.Parameters.AddWithValue("@Img", (object?)dto.ImageBytes ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@Mime", (object?)dto.ImageMimeType ?? "image/jpeg");
            insertCmd.Parameters.AddWithValue("@RateType", (object?)dto.RateType ?? "per hour");
            insertCmd.Parameters.AddWithValue("@ImgFile", (object?)effectiveFileName ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@ImgUrl", (object?)effectiveUrl ?? DBNull.Value);
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
            InvalidateCatalogCache();

            return new ServiceCreatedResponseDto(
                Id: nextId,
                Name: dto.Name.Trim(),
                Description: dto.Description.Trim(),
                Price: dto.Price,
                CategoryId: dto.CategoryId ?? string.Empty,
                CategoryName: resolvedCategoryName,
                HasImage: (dto.ImageBytes != null && dto.ImageBytes.Length > 0) || !string.IsNullOrWhiteSpace(effectiveFileName),
                ImageFileName: effectiveFileName,
                ImageUrl: effectiveUrl);
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

        var nextId = await GetNextCategoryIdAsync(cancellationToken);

        // 1. Determine relevant or default associations to assign to this category
        var joins = await DetermineCategoryAssociationsAsync(nextId, dto, cancellationToken);

        // 2. Primary: Save via Entity Framework Core models and relationships
        try
        {
            var rateTypeId = !string.IsNullOrWhiteSpace(dto.RateTypeId) ? dto.RateTypeId : "RT-0001";
            var rateTypeName = !string.IsNullOrWhiteSpace(dto.RateTypeName) ? dto.RateTypeName : "Per Hour";

            var eventCategory = new EventCategory
            {
                Id = nextId,
                Name = dto.Name.Trim(),
                Description = dto.Description.Trim(),
                RateTypeId = rateTypeId,
                RateTypeName = rateTypeName,
                Associations = joins
            };

            _db.EventCategories.Add(eventCategory);
            await _db.SaveChangesAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(requestingAdminId))
            {
                await TryAddHistoryLogAsync("ServiceCategory", nextId, "New service category created via Wizard.", requestingAdminId, cancellationToken);
            }

            var serviceNames = await ResolveAssociatedNamesAsync(joins, cancellationToken);
            InvalidateCatalogCache();

            return new CategoryDto(
                Id: eventCategory.Id,
                Name: eventCategory.Name,
                Description: eventCategory.Description,
                AssociatedServices: serviceNames,
                RateTypeId: eventCategory.RateTypeId,
                RateTypeName: eventCategory.RateTypeName
            );
        }
        catch
        {
            // Clear tracking if EF Core fails (e.g. constraints, disconnected mode, or driver fallback)
            _db.ChangeTracker.Clear();

            // Fallback: Execute transactional SQL ensuring both category and associations are saved
            return await CreateCategoryViaSqlAsync(nextId, dto, joins, requestingAdminId, cancellationToken);
        }
    }

    private static string GenerateJoinId() =>
        "SCJ-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private async Task<List<ServiceCategoryJoin>> DetermineCategoryAssociationsAsync(
        string categoryId,
        CreateCategoryDto dto,
        CancellationToken ct)
    {
        var joins = new List<ServiceCategoryJoin>();
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(ct);

        // 1. Check explicitly provided associations
        if (dto.AssociatedServiceIds != null && dto.AssociatedServiceIds.Count > 0)
        {
            foreach (var sid in dto.AssociatedServiceIds.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                var clean = sid.Trim();
                const string checkSql = "SELECT TOP 1 ServicesID FROM dbo.Services WHERE ServicesID = @Id OR ServiceName = @Id;";
                await using var cmd = new SqlCommand(checkSql, conn);
                cmd.Parameters.AddWithValue("@Id", clean);
                var found = await cmd.ExecuteScalarAsync(ct);
                if (found != null)
                {
                    var foundId = found.ToString()!;
                    if (!joins.Any(j => j.ServiceId == foundId))
                    {
                        joins.Add(new ServiceCategoryJoin
                        {
                            Id = GenerateJoinId(),
                            CategoryId = categoryId,
                            ServiceId = foundId
                        });
                    }
                }
            }
        }

        if (dto.AssociatedAdditionalServiceIds != null && dto.AssociatedAdditionalServiceIds.Count > 0)
        {
            foreach (var aid in dto.AssociatedAdditionalServiceIds.Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                var clean = aid.Trim();
                const string checkSql = "SELECT TOP 1 AdditionalServicesID FROM dbo.AdditionalServices WHERE AdditionalServicesID = @Id OR AdditionalServiceName = @Id;";
                await using var cmd = new SqlCommand(checkSql, conn);
                cmd.Parameters.AddWithValue("@Id", clean);
                var found = await cmd.ExecuteScalarAsync(ct);
                if (found != null)
                {
                    var foundId = found.ToString()!;
                    if (!joins.Any(j => j.AdditionalServiceId == foundId))
                    {
                        joins.Add(new ServiceCategoryJoin
                        {
                            Id = GenerateJoinId(),
                            CategoryId = categoryId,
                            AdditionalServiceId = foundId
                        });
                    }
                }
            }
        }

        if (dto.AssociatedComboPackageIds != null && dto.AssociatedComboPackageIds.Count > 0)
        {
            foreach (var pid in dto.AssociatedComboPackageIds.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                var clean = pid.Trim();
                const string checkSql = "SELECT TOP 1 ComboPackageID FROM dbo.ComboPackage WHERE ComboPackageID = @Id OR ComboPackageName = @Id;";
                await using var cmd = new SqlCommand(checkSql, conn);
                cmd.Parameters.AddWithValue("@Id", clean);
                var found = await cmd.ExecuteScalarAsync(ct);
                if (found != null)
                {
                    var foundId = found.ToString()!;
                    if (!joins.Any(j => j.ComboPackageId == foundId))
                    {
                        joins.Add(new ServiceCategoryJoin
                        {
                            Id = GenerateJoinId(),
                            CategoryId = categoryId,
                            ComboPackageId = foundId
                        });
                    }
                }
            }
        }

        if (dto.Associations != null && dto.Associations.Count > 0)
        {
            foreach (var assoc in dto.Associations.Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                var clean = assoc.Trim();
                // Check Services
                const string srvSql = "SELECT TOP 1 ServicesID FROM dbo.Services WHERE ServicesID = @Val OR ServiceName = @Val;";
                await using var srvCmd = new SqlCommand(srvSql, conn);
                srvCmd.Parameters.AddWithValue("@Val", clean);
                var srvFound = await srvCmd.ExecuteScalarAsync(ct);
                if (srvFound != null)
                {
                    var foundId = srvFound.ToString()!;
                    if (!joins.Any(j => j.ServiceId == foundId))
                    {
                        joins.Add(new ServiceCategoryJoin { Id = GenerateJoinId(), CategoryId = categoryId, ServiceId = foundId });
                    }
                    continue;
                }

                // Check AdditionalServices
                const string addSql = "SELECT TOP 1 AdditionalServicesID FROM dbo.AdditionalServices WHERE AdditionalServicesID = @Val OR AdditionalServiceName = @Val;";
                await using var addCmd = new SqlCommand(addSql, conn);
                addCmd.Parameters.AddWithValue("@Val", clean);
                var addFound = await addCmd.ExecuteScalarAsync(ct);
                if (addFound != null)
                {
                    var foundId = addFound.ToString()!;
                    if (!joins.Any(j => j.AdditionalServiceId == foundId))
                    {
                        joins.Add(new ServiceCategoryJoin { Id = GenerateJoinId(), CategoryId = categoryId, AdditionalServiceId = foundId });
                    }
                    continue;
                }

                // Check ComboPackage
                const string pkgSql = "SELECT TOP 1 ComboPackageID FROM dbo.ComboPackage WHERE ComboPackageID = @Val OR ComboPackageName = @Val;";
                await using var pkgCmd = new SqlCommand(pkgSql, conn);
                pkgCmd.Parameters.AddWithValue("@Val", clean);
                var pkgFound = await pkgCmd.ExecuteScalarAsync(ct);
                if (pkgFound != null)
                {
                    var foundId = pkgFound.ToString()!;
                    if (!joins.Any(j => j.ComboPackageId == foundId))
                    {
                        joins.Add(new ServiceCategoryJoin { Id = GenerateJoinId(), CategoryId = categoryId, ComboPackageId = foundId });
                    }
                }
            }
        }

        // If explicit associations were provided and resolved, return them
        if (joins.Count > 0)
        {
            return joins;
        }

        // 2. Automatic assignment of default or relevant associations
        // 2a. Match existing services by keyword from category name
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "and", "&", "the", "for", "with", "studio", "category", "photography", "photo", "services", "service" };
        var keywords = dto.Name
            .Split(new[] { ' ', '-', '/', ',', '&' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !stopWords.Contains(w))
            .ToList();

        if (keywords.Count > 0)
        {
            foreach (var kw in keywords)
            {
                const string kwSql = @"
                    SELECT TOP 2 ServicesID 
                    FROM dbo.Services 
                    WHERE ServiceName LIKE @Keyword OR ServicesDescription LIKE @Keyword
                    ORDER BY ServicesID;";
                await using var kwCmd = new SqlCommand(kwSql, conn);
                kwCmd.Parameters.AddWithValue("@Keyword", $"%{kw}%");
                await using var reader = await kwCmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var sid = reader.GetString(0);
                    if (!joins.Any(j => j.ServiceId == sid))
                    {
                        joins.Add(new ServiceCategoryJoin
                        {
                            Id = GenerateJoinId(),
                            CategoryId = categoryId,
                            ServiceId = sid
                        });
                    }
                }
            }
        }

        if (joins.Count > 0)
        {
            return joins;
        }

        // 2b. Check predefined category templates and find matching services
        var templates = new (string Pattern, string[] Services)[]
        {
            ("Portrait", new[] { "Studio Headshots", "Senior Portraits", "Family Studio Session" }),
            ("Maternity", new[] { "Maternity Session", "Newborn Photography", "Family Studio Session" }),
            ("Newborn", new[] { "Newborn Photography", "Maternity Session", "Baby Portraits" }),
            ("Wedding", new[] { "Full Day Nuptial", "Pre-Nuptial Shoot", "Reception Highlights" }),
            ("Nuptial", new[] { "Full Day Nuptial", "Pre-Nuptial Shoot", "Reception Highlights" }),
            ("Commercial", new[] { "Product Catalog", "Brand Campaigns", "Food & Beverage" }),
            ("Branding", new[] { "Product Catalog", "Brand Campaigns", "Food & Beverage" }),
            ("Event", new[] { "Corporate Gala", "Concerts & Live", "Milestone Celebrations" }),
            ("Gala", new[] { "Corporate Gala", "Concerts & Live", "Milestone Celebrations" }),
            ("Aerial", new[] { "4K Drone Aerials", "Venue Fly-Through", "Architectural Scans" }),
            ("Drone", new[] { "4K Drone Aerials", "Venue Fly-Through", "Architectural Scans" }),
            ("Post", new[] { "Editorial Skin Retouch", "Color Grading", "Batch RAW Processing" }),
            ("Retouch", new[] { "Editorial Skin Retouch", "Color Grading", "Batch RAW Processing" }),
            ("Analog", new[] { "Vintage Tintype", "Analog 35mm Roll" }),
            ("Specialty", new[] { "Vintage Tintype", "Analog 35mm Roll" })
        };

        foreach (var (pattern, defaultServiceNames) in templates)
        {
            if (dto.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var sName in defaultServiceNames)
                {
                    const string tmplSql = "SELECT TOP 1 ServicesID FROM dbo.Services WHERE ServiceName = @Name OR ServiceName LIKE @LikeName;";
                    await using var tmplCmd = new SqlCommand(tmplSql, conn);
                    tmplCmd.Parameters.AddWithValue("@Name", sName);
                    tmplCmd.Parameters.AddWithValue("@LikeName", $"%{sName}%");
                    var sVal = await tmplCmd.ExecuteScalarAsync(ct);
                    if (sVal != null)
                    {
                        var sid = sVal.ToString()!;
                        if (!joins.Any(j => j.ServiceId == sid))
                        {
                            joins.Add(new ServiceCategoryJoin
                            {
                                Id = GenerateJoinId(),
                                CategoryId = categoryId,
                                ServiceId = sid
                            });
                        }
                    }
                }
                if (joins.Count > 0)
                    return joins;
            }
        }

        // 2c. If existing services exist in dbo.Services, associate the first available service
        const string anyServiceSql = "SELECT TOP 1 ServicesID FROM dbo.Services ORDER BY ServicesID;";
        await using var anyCmd = new SqlCommand(anyServiceSql, conn);
        var anyVal = await anyCmd.ExecuteScalarAsync(ct);
        if (anyVal != null)
        {
            joins.Add(new ServiceCategoryJoin
            {
                Id = GenerateJoinId(),
                CategoryId = categoryId,
                ServiceId = anyVal.ToString()!
            });
            return joins;
        }

        // 2d. If dbo.Services has no rows, automatically create a baseline default service
        var defaultSrvId = await EnsureDefaultServiceExistsAsync(dto.Name.Trim(), conn, ct);
        joins.Add(new ServiceCategoryJoin
        {
            Id = GenerateJoinId(),
            CategoryId = categoryId,
            ServiceId = defaultSrvId
        });

        return joins;
    }

    private async Task<string> EnsureDefaultServiceExistsAsync(string categoryName, SqlConnection conn, CancellationToken ct)
    {
        const string checkSql = "SELECT TOP 1 ServicesID FROM dbo.Services ORDER BY ServicesID;";
        await using (var checkCmd = new SqlCommand(checkSql, conn))
        {
            var existing = await checkCmd.ExecuteScalarAsync(ct);
            if (existing != null)
                return existing.ToString()!;
        }

        const string nextSql = @"
            SELECT 'SRV-' + RIGHT('0000' + CAST(
                ISNULL(MAX(TRY_CAST(SUBSTRING(ServicesID, CHARINDEX('-', ServicesID) + 1, 20) AS INT)), 0) + 1
            AS VARCHAR(10)), 4)
            FROM dbo.Services WITH (UPDLOCK, HOLDLOCK)
            WHERE ServicesID LIKE 'SRV-%';";

        await using var nextCmd = new SqlCommand(nextSql, conn);
        var srvId = (await nextCmd.ExecuteScalarAsync(ct))?.ToString() ?? "SRV-0001";

        const string insertSql = @"
            INSERT INTO dbo.Services (ServicesID, ServiceName, ServicesDescription, ServicePrice, RateType, ServiceImageMimeType, CreatedAt)
            VALUES (@Id, @Name, @Desc, @Price, @RateType, @Mime, SYSUTCDATETIME());";

        await using var insertCmd = new SqlCommand(insertSql, conn);
        insertCmd.Parameters.AddWithValue("@Id", srvId);
        insertCmd.Parameters.AddWithValue("@Name", $"{categoryName} - Standard Session");
        insertCmd.Parameters.AddWithValue("@Desc", $"Default baseline session service for {categoryName}.");
        insertCmd.Parameters.AddWithValue("@Price", 150.00m);
        insertCmd.Parameters.AddWithValue("@RateType", "Per Hour");
        insertCmd.Parameters.AddWithValue("@Mime", "image/jpeg");
        await insertCmd.ExecuteNonQueryAsync(ct);

        return srvId;
    }

    private async Task<CategoryDto> CreateCategoryViaSqlAsync(
        string categoryId,
        CreateCategoryDto dto,
        List<ServiceCategoryJoin> joins,
        string? requestingAdminId,
        CancellationToken ct)
    {
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(ct);
        await using var trans = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        try
        {
            var rateTypeId = !string.IsNullOrWhiteSpace(dto.RateTypeId) ? dto.RateTypeId : "RT-0001";
            var rateTypeName = !string.IsNullOrWhiteSpace(dto.RateTypeName) ? dto.RateTypeName : "Per Hour";

            const string insertCatSql = @"
                IF NOT EXISTS (SELECT 1 FROM dbo.ServiceCategory WHERE ServiceCategoryID = @Id)
                BEGIN
                    INSERT INTO dbo.ServiceCategory (ServiceCategoryID, ServiceCategoryName, ServiceCategoryDescription, RateTypeID, RateTypeName)
                    VALUES (@Id, @Name, @Desc, @RateTypeID, @RateTypeName);
                END";

            await using (var cmd = new SqlCommand(insertCatSql, conn, trans))
            {
                cmd.Parameters.AddWithValue("@Id", categoryId);
                cmd.Parameters.AddWithValue("@Name", dto.Name.Trim());
                cmd.Parameters.AddWithValue("@Desc", dto.Description.Trim());
                cmd.Parameters.AddWithValue("@RateTypeID", rateTypeId);
                cmd.Parameters.AddWithValue("@RateTypeName", rateTypeName);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            foreach (var j in joins)
            {
                const string insertJoinSql = @"
                    IF NOT EXISTS (SELECT 1 FROM dbo.ServiceCategoryJoins WHERE ServiceCategoryJoinID = @JId)
                    BEGIN
                        INSERT INTO dbo.ServiceCategoryJoins (ServiceCategoryJoinID, ServiceCategoryID, ServicesID, AdditionalServicesID, ComboPackageID)
                        VALUES (@JId, @CatId, @SrvId, @AddId, @PkgId);
                    END";
                await using var jCmd = new SqlCommand(insertJoinSql, conn, trans);
                jCmd.Parameters.AddWithValue("@JId", j.Id);
                jCmd.Parameters.AddWithValue("@CatId", categoryId);
                jCmd.Parameters.AddWithValue("@SrvId", (object?)j.ServiceId ?? DBNull.Value);
                jCmd.Parameters.AddWithValue("@AddId", (object?)j.AdditionalServiceId ?? DBNull.Value);
                jCmd.Parameters.AddWithValue("@PkgId", (object?)j.ComboPackageId ?? DBNull.Value);
                await jCmd.ExecuteNonQueryAsync(ct);
            }

            await trans.CommitAsync(ct);

            if (!string.IsNullOrWhiteSpace(requestingAdminId))
            {
                await TryAddHistoryLogAsync("ServiceCategory", categoryId, "New service category created via Wizard.", requestingAdminId, ct);
            }

            var serviceNames = await ResolveAssociatedNamesAsync(joins, ct);
            return new CategoryDto(categoryId, dto.Name.Trim(), dto.Description.Trim(), serviceNames, RateTypeId: rateTypeId, RateTypeName: rateTypeName);
        }
        catch
        {
            await trans.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<List<string>> ResolveAssociatedNamesAsync(List<ServiceCategoryJoin> joins, CancellationToken ct)
    {
        var names = new List<string>();
        if (joins.Count == 0) return names;

        var serviceIds = joins.Where(j => !string.IsNullOrWhiteSpace(j.ServiceId)).Select(j => j.ServiceId!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var addOnIds = joins.Where(j => !string.IsNullOrWhiteSpace(j.AdditionalServiceId)).Select(j => j.AdditionalServiceId!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var pkgIds = joins.Where(j => !string.IsNullOrWhiteSpace(j.ComboPackageId)).Select(j => j.ComboPackageId!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(ct);

        async Task QueryBatchAsync(string sqlTemplate, List<string> ids)
        {
            if (ids.Count == 0) return;
            var paramNames = ids.Select((_, i) => $"@p{i}").ToArray();
            var sql = string.Format(sqlTemplate, string.Join(",", paramNames));
            await using var cmd = new SqlCommand(sql, conn);
            for (int i = 0; i < ids.Count; i++)
                cmd.Parameters.AddWithValue(paramNames[i], ids[i]);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (!reader.IsDBNull(0))
                {
                    var name = reader.GetString(0);
                    if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name))
                        names.Add(name);
                }
            }
        }

        await QueryBatchAsync("SELECT ServiceName FROM dbo.Services WHERE ServicesID IN ({0});", serviceIds);
        await QueryBatchAsync("SELECT AdditionalServiceName FROM dbo.AdditionalServices WHERE AdditionalServicesID IN ({0});", addOnIds);
        await QueryBatchAsync("SELECT ComboPackageName FROM dbo.ComboPackage WHERE ComboPackageID IN ({0});", pkgIds);

        return names;
    }

    private async Task TryAddHistoryLogAsync(string entityName, string entityId, string details, string? adminId, CancellationToken ct)
    {
        try
        {
            await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
            await conn.OpenAsync(ct);
            const string sql = @"
                IF OBJECT_ID(N'dbo.sp_AddHistoryLog', N'P') IS NOT NULL
                BEGIN
                    EXEC dbo.sp_AddHistoryLog
                        @ActionType = 'INSERT',
                        @EntityName = @Entity,
                        @EntityID = @EntityId,
                        @ActionDetails = @Details,
                        @ChangedByType = 'Admin',
                        @ActorID = @AdminId;
                END";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Entity", entityName);
            cmd.Parameters.AddWithValue("@EntityId", entityId);
            cmd.Parameters.AddWithValue("@Details", details);
            cmd.Parameters.AddWithValue("@AdminId", (object?)adminId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch
        {
            // History log failure is non-fatal
        }
    }

    public async Task<string> GetNextAdditionalServiceIdAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

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

        var (imageFileName, imageUrl) = await UploadServiceAssetToR2Async(
            dto.ImageBytes, dto.ImageFileName, dto.ImageMimeType, "services/addons", cancellationToken);
        var effectiveRateType = string.IsNullOrWhiteSpace(dto.RateType) ? "per hour" : dto.RateType.Trim();

        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

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
            cmd.Parameters.AddWithValue("@ImageFileName", (object?)imageFileName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ImageUrl", (object?)imageUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedByAdminID", (object?)requestingAdminId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RateType", effectiveRateType);

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
            InvalidateCatalogCache();

            return new AdditionalServiceResponseDto(
                Id: createdId,
                Name: dto.ServiceName.Trim(),
                Description: dto.ServiceDescription?.Trim() ?? string.Empty,
                Category: dto.ServiceCategory?.Trim() ?? "Add-On",
                Price: dto.ServicePrice,
                HasImage: (dto.ImageBytes != null && dto.ImageBytes.Length > 0) || !string.IsNullOrWhiteSpace(imageUrl),
                ImageUrl: imageUrl ?? $"/api/management/additional-services/{createdId}/image",
                CreatedAt: DateTime.UtcNow,
                ImageFileName: imageFileName,
                RateType: effectiveRateType
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
                    ServicePrice, CategoryName, RateType, AdditionalServiceImage, AdditionalServiceImageMimeType,
                    ImageFileName, ImageUrl, CreatedAt
                )
                VALUES (@Id, @Name, @Desc, @Price, @Cat, @RateType, @Img, @Mime, @ImgFileName, @ImgUrl, SYSUTCDATETIME());";

            await using var insertCmd = new SqlCommand(insertSql, conn, trans);
            insertCmd.Parameters.AddWithValue("@Id", nextId);
            insertCmd.Parameters.AddWithValue("@Name", dto.ServiceName.Trim());
            insertCmd.Parameters.AddWithValue("@Desc", dto.ServiceDescription?.Trim() ?? string.Empty);
            insertCmd.Parameters.AddWithValue("@Price", dto.ServicePrice);
            insertCmd.Parameters.AddWithValue("@Cat", (object?)dto.ServiceCategory?.Trim() ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@RateType", effectiveRateType);
            insertCmd.Parameters.Add("@Img", SqlDbType.VarBinary, -1).Value = (object?)dto.ImageBytes ?? DBNull.Value;
            insertCmd.Parameters.AddWithValue("@Mime", string.IsNullOrWhiteSpace(dto.ImageMimeType) ? "image/jpeg" : dto.ImageMimeType.Trim());
            insertCmd.Parameters.AddWithValue("@ImgFileName", (object?)imageFileName ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@ImgUrl", (object?)imageUrl ?? DBNull.Value);
            await insertCmd.ExecuteNonQueryAsync(cancellationToken);

            await trans.CommitAsync(cancellationToken);
            InvalidateCatalogCache();

            return new AdditionalServiceResponseDto(
                Id: nextId,
                Name: dto.ServiceName.Trim(),
                Description: dto.ServiceDescription?.Trim() ?? string.Empty,
                Category: dto.ServiceCategory?.Trim() ?? "Add-On",
                Price: dto.ServicePrice,
                HasImage: (dto.ImageBytes != null && dto.ImageBytes.Length > 0) || !string.IsNullOrWhiteSpace(imageUrl),
                ImageUrl: imageUrl ?? $"/api/management/additional-services/{nextId}/image",
                CreatedAt: DateTime.UtcNow,
                ImageFileName: imageFileName,
                RateType: effectiveRateType
            );
        }
        catch
        {
            await trans.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<AdditionalServiceResponseDto?> UpdateAdditionalServiceAsync(
        string id,
        UpdateAdditionalServiceDto dto,
        string? requestingAdminId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Additional Service ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(dto.ServiceName))
            throw new ArgumentException("Service Name is required.", nameof(dto));

        var cleanId = id.Trim();
        var effectiveRateType = string.IsNullOrWhiteSpace(dto.RateType) ? "per hour" : dto.RateType.Trim();

        var (imageFileName, imageUrl) = (dto.ImageBytes != null && dto.ImageBytes.Length > 0)
            ? await UploadServiceAssetToR2Async(dto.ImageBytes, dto.ImageFileName ?? $"{cleanId}.jpg", dto.ImageMimeType, "services/addons", cancellationToken)
            : (dto.ImageFileName, dto.ImageUrl);

        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

        try
        {
            await using var cmd = new SqlCommand("dbo.sp_UpdateAdditionalService", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@AdditionalServicesID", cleanId);
            cmd.Parameters.AddWithValue("@AdditionalServiceName", dto.ServiceName.Trim());
            cmd.Parameters.AddWithValue("@AdditionalServicesDescription", dto.ServiceDescription?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("@ServicePrice", dto.ServicePrice);
            cmd.Parameters.AddWithValue("@CategoryName", (object?)dto.ServiceCategory?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RateType", effectiveRateType);
            cmd.Parameters.Add("@AdditionalServiceImage", SqlDbType.VarBinary, -1).Value = (object?)dto.ImageBytes ?? DBNull.Value;
            cmd.Parameters.AddWithValue("@AdditionalServiceImageMimeType", string.IsNullOrWhiteSpace(dto.ImageMimeType) ? (object)DBNull.Value : dto.ImageMimeType.Trim());
            cmd.Parameters.AddWithValue("@ImageFileName", (object?)imageFileName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ImageUrl", (object?)imageUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedByAdminID", (object?)requestingAdminId ?? DBNull.Value);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var rId = reader.GetString(0);
                var rName = reader.GetString(1);
                var rDesc = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var rPrice = reader.IsDBNull(3) ? 0m : reader.GetDecimal(3);
                var rCat = reader.IsDBNull(4) ? "Add-On" : reader.GetString(4);
                var rHasImg = !reader.IsDBNull(5) && reader.GetBoolean(5);
                var rImgFile = reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetString(6) : imageFileName;
                var rImgUrl = reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetString(7) : imageUrl;
                var rCreated = reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetDateTime(8) : DateTime.UtcNow;
                var rRateType = reader.FieldCount > 9 && !reader.IsDBNull(9) ? reader.GetString(9) : effectiveRateType;
                rImgUrl = ResolveR2ServiceImageUrl(rImgFile, rImgUrl);
                InvalidateCatalogCache();

                return new AdditionalServiceResponseDto(
                    Id: rId,
                    Name: rName,
                    Description: rDesc,
                    Category: rCat,
                    Price: rPrice,
                    HasImage: rHasImg || !string.IsNullOrWhiteSpace(rImgFile),
                    ImageUrl: rImgUrl ?? (rHasImg ? $"/api/management/additional-services/{rId}/image" : null),
                    CreatedAt: rCreated,
                    ImageFileName: rImgFile,
                    RateType: rRateType
                );
            }
        }
        catch (SqlException)
        {
            // Fallback to direct SQL UPDATE below
        }

        const string updateSql = @"
            UPDATE dbo.AdditionalServices
            SET AdditionalServiceName = @Name,
                AdditionalServicesDescription = @Desc,
                ServicePrice = @Price,
                CategoryName = COALESCE(@Cat, CategoryName),
                RateType = @RateType,
                AdditionalServiceImage = COALESCE(@Img, AdditionalServiceImage),
                AdditionalServiceImageMimeType = COALESCE(@Mime, AdditionalServiceImageMimeType),
                ImageFileName = COALESCE(@ImgFileName, ImageFileName),
                ImageUrl = COALESCE(@ImgUrl, ImageUrl)
            WHERE AdditionalServicesID = @Id;

            SELECT AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription,
                   ISNULL(ServicePrice, 0.00), ISNULL(CategoryName, 'Add-On'),
                   CAST(CASE WHEN AdditionalServiceImage IS NOT NULL OR ImageFileName IS NOT NULL OR ImageUrl IS NOT NULL THEN 1 ELSE 0 END AS bit),
                   ImageFileName, ImageUrl, CreatedAt, ISNULL(RateType, 'Per Hour')
            FROM dbo.AdditionalServices
            WHERE AdditionalServicesID = @Id;";

        await using var fbCmd = new SqlCommand(updateSql, conn);
        fbCmd.Parameters.AddWithValue("@Id", cleanId);
        fbCmd.Parameters.AddWithValue("@Name", dto.ServiceName.Trim());
        fbCmd.Parameters.AddWithValue("@Desc", dto.ServiceDescription?.Trim() ?? string.Empty);
        fbCmd.Parameters.AddWithValue("@Price", dto.ServicePrice);
        fbCmd.Parameters.AddWithValue("@Cat", (object?)dto.ServiceCategory?.Trim() ?? DBNull.Value);
        fbCmd.Parameters.AddWithValue("@RateType", effectiveRateType);
        fbCmd.Parameters.Add("@Img", SqlDbType.VarBinary, -1).Value = (object?)dto.ImageBytes ?? DBNull.Value;
        fbCmd.Parameters.AddWithValue("@Mime", string.IsNullOrWhiteSpace(dto.ImageMimeType) ? (object)DBNull.Value : dto.ImageMimeType.Trim());
        fbCmd.Parameters.AddWithValue("@ImgFileName", (object?)imageFileName ?? DBNull.Value);
        fbCmd.Parameters.AddWithValue("@ImgUrl", (object?)imageUrl ?? DBNull.Value);

        await using var fbReader = await fbCmd.ExecuteReaderAsync(cancellationToken);
        if (await fbReader.ReadAsync(cancellationToken))
        {
            var rId = fbReader.GetString(0);
            var rName = fbReader.GetString(1);
            var rDesc = fbReader.IsDBNull(2) ? string.Empty : fbReader.GetString(2);
            var rPrice = fbReader.IsDBNull(3) ? 0m : fbReader.GetDecimal(3);
            var rCat = fbReader.IsDBNull(4) ? "Add-On" : fbReader.GetString(4);
            var rHasImg = !fbReader.IsDBNull(5) && fbReader.GetBoolean(5);
            var rImgFile = !fbReader.IsDBNull(6) ? fbReader.GetString(6) : imageFileName;
            var rImgUrl = !fbReader.IsDBNull(7) ? fbReader.GetString(7) : imageUrl;
            var rCreated = !fbReader.IsDBNull(8) ? fbReader.GetDateTime(8) : DateTime.UtcNow;
            var rRateType = !fbReader.IsDBNull(9) ? fbReader.GetString(9) : effectiveRateType;
            rImgUrl = ResolveR2ServiceImageUrl(rImgFile, rImgUrl);
            InvalidateCatalogCache();

            return new AdditionalServiceResponseDto(
                Id: rId,
                Name: rName,
                Description: rDesc,
                Category: rCat,
                Price: rPrice,
                HasImage: rHasImg || !string.IsNullOrWhiteSpace(rImgFile),
                ImageUrl: rImgUrl ?? (rHasImg ? $"/api/management/additional-services/{rId}/image" : null),
                CreatedAt: rCreated,
                ImageFileName: rImgFile,
                RateType: rRateType
            );
        }

        return null;
    }

    // ── Combo Package Wizard & Bundling (Anti-Race Condition) ──────────────────

    public async Task<string> GetNextComboPackageIdAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

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
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

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
                    HasImage: !reader.IsDBNull(5) && reader.GetBoolean(5),
                    RateType: reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetString(6) : "per hour"
                ));
            }
            return list;
        }
        catch (SqlException)
        {
            // Direct query fallback
        }

        const string fallbackSql = @"
            SELECT ServicesID, ServiceName, ServicesDescription, ServicePrice, 'Main' AS ServiceType,
                   CAST(CASE WHEN ServiceImage IS NOT NULL OR ImageUrl IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImage,
                   ISNULL(RateType, 'Per Hour') AS RateType
            FROM dbo.Services
            UNION ALL
            SELECT AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription, ISNULL(ServicePrice, 0.00) AS ServicePrice, 'Additional' AS ServiceType,
                   CAST(CASE WHEN AdditionalServiceImage IS NOT NULL OR ImageUrl IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImage,
                   ISNULL(RateType, 'Per Hour') AS RateType
            FROM dbo.AdditionalServices
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
                    HasImage: !reader.IsDBNull(5) && reader.GetBoolean(5),
                    RateType: reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetString(6) : "per hour"
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
        var (imageFileName, imageUrl) = await UploadServiceAssetToR2Async(
            imageBytes, null, imageMimeType, "services/packages", cancellationToken);
        var effectiveRateType = string.IsNullOrWhiteSpace(model.RateType) ? "per hour" : model.RateType.Trim();

        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

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
            cmd.Parameters.AddWithValue("@ImageFileName", (object?)imageFileName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ImageUrl", (object?)imageUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SelectedServiceIDsJson", selectedIdsJson);
            cmd.Parameters.AddWithValue("@CreatedByAdminID", (object?)requestingAdminId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RateType", effectiveRateType);

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
                        ComboPackageID, ComboPackageName, ComboPackageDescription, ComboPackagePrice, RateType, ComboPackageImage, ComboPackageImageMimeType, ImageFileName, ImageUrl
                    )
                    VALUES (@Id, @Name, @Desc, @Price, @RateType, @Img, @Mime, @ImgFileName, @ImgUrl);";

                await using (var pkgCmd = new SqlCommand(insertPkgSql, conn, trans))
                {
                    pkgCmd.Parameters.AddWithValue("@Id", generatedId);
                    pkgCmd.Parameters.AddWithValue("@Name", model.Name.Trim());
                    pkgCmd.Parameters.AddWithValue("@Desc", model.Description?.Trim() ?? string.Empty);
                    pkgCmd.Parameters.AddWithValue("@Price", model.Price);
                    pkgCmd.Parameters.AddWithValue("@RateType", effectiveRateType);
                    pkgCmd.Parameters.Add("@Img", SqlDbType.VarBinary, -1).Value = (object?)imageBytes ?? DBNull.Value;
                    pkgCmd.Parameters.AddWithValue("@Mime", string.IsNullOrWhiteSpace(imageMimeType) ? "image/jpeg" : imageMimeType.Trim());
                    pkgCmd.Parameters.AddWithValue("@ImgFileName", (object?)imageFileName ?? DBNull.Value);
                    pkgCmd.Parameters.AddWithValue("@ImgUrl", (object?)imageUrl ?? DBNull.Value);
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

        InvalidateCatalogCache();

        return new ComboPackageCreatedResponseDto(
            PackageId: generatedId,
            Name: model.Name.Trim(),
            Description: model.Description?.Trim(),
            Price: model.Price,
            CategoryId: model.CategoryId,
            CategoryName: categoryName,
            BundledServicesCount: model.SelectedServiceIds?.Count ?? 0,
            BundledServiceNames: model.SelectedServiceIds ?? Array.Empty<string>(),
            HasImage: (imageBytes != null && imageBytes.Length > 0) || !string.IsNullOrWhiteSpace(imageUrl),
            CreatedAt: DateTime.UtcNow,
            ImageFileName: imageFileName,
            ImageUrl: imageUrl ?? $"/api/management/packages/{generatedId}/image",
            RateType: effectiveRateType
        );
    }

    public async Task<ComboPackageCreatedResponseDto?> UpdateComboPackageWithServicesAsync(
        string id,
        UpdateComboPackageModel model,
        byte[]? imageBytes,
        string? imageMimeType,
        string? requestingAdminId = null,
        string? imageFileName = null,
        string? imageUrl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Package ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(model.Name))
            throw new ArgumentException("Package Name is required.", nameof(model));

        var cleanId = id.Trim();
        var effectiveRateType = string.IsNullOrWhiteSpace(model.RateType) ? "per hour" : model.RateType.Trim();

        if (imageBytes != null && imageBytes.Length > 0 && string.IsNullOrWhiteSpace(imageUrl))
        {
            (imageFileName, imageUrl) = await UploadServiceAssetToR2Async(
                imageBytes, imageFileName ?? $"{cleanId}.jpg", imageMimeType, "services/packages", cancellationToken);
        }

        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

        var hasSelectedServices = model.SelectedServiceIds != null && model.SelectedServiceIds.Count > 0;
        var selectedIdsJson = hasSelectedServices
            ? System.Text.Json.JsonSerializer.Serialize(model.SelectedServiceIds)
            : null;

        bool updatedViaSp = false;
        try
        {
            await using var cmd = new SqlCommand("dbo.sp_UpdateComboPackageWithServices", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@ComboPackageID", cleanId);
            cmd.Parameters.AddWithValue("@ComboPackageName", model.Name.Trim());
            cmd.Parameters.AddWithValue("@ComboPackageDescription", model.Description?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("@ComboPackagePrice", model.Price);
            cmd.Parameters.AddWithValue("@ServiceCategoryID", string.IsNullOrWhiteSpace(model.CategoryId) ? (object)DBNull.Value : model.CategoryId.Trim());
            cmd.Parameters.Add("@ComboPackageImage", SqlDbType.VarBinary, -1).Value = (object?)imageBytes ?? DBNull.Value;
            cmd.Parameters.AddWithValue("@ComboPackageImageMimeType", string.IsNullOrWhiteSpace(imageMimeType) ? (object)DBNull.Value : imageMimeType.Trim());
            cmd.Parameters.AddWithValue("@ImageFileName", (object?)imageFileName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ImageUrl", (object?)imageUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SelectedServiceIDsJson", (object?)selectedIdsJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedByAdminID", (object?)requestingAdminId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RateType", effectiveRateType);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            updatedViaSp = true;
        }
        catch (SqlException)
        {
            updatedViaSp = false;
        }

        if (!updatedViaSp)
        {
            await using var trans = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);
            try
            {
                const string updatePkgSql = @"
                    UPDATE dbo.ComboPackage
                    SET ComboPackageName = @Name,
                        ComboPackageDescription = @Desc,
                        ComboPackagePrice = @Price,
                        RateType = @RateType,
                        ComboPackageImage = COALESCE(@Img, ComboPackageImage),
                        ComboPackageImageMimeType = COALESCE(@Mime, ComboPackageImageMimeType),
                        ImageFileName = COALESCE(@ImgFileName, ImageFileName),
                        ImageUrl = COALESCE(@ImgUrl, ImageUrl)
                    WHERE ComboPackageID = @Id;";

                int affected;
                await using (var pkgCmd = new SqlCommand(updatePkgSql, conn, trans))
                {
                    pkgCmd.Parameters.AddWithValue("@Id", cleanId);
                    pkgCmd.Parameters.AddWithValue("@Name", model.Name.Trim());
                    pkgCmd.Parameters.AddWithValue("@Desc", model.Description?.Trim() ?? string.Empty);
                    pkgCmd.Parameters.AddWithValue("@Price", model.Price);
                    pkgCmd.Parameters.AddWithValue("@RateType", effectiveRateType);
                    pkgCmd.Parameters.Add("@Img", SqlDbType.VarBinary, -1).Value = (object?)imageBytes ?? DBNull.Value;
                    pkgCmd.Parameters.AddWithValue("@Mime", string.IsNullOrWhiteSpace(imageMimeType) ? (object)DBNull.Value : imageMimeType.Trim());
                    pkgCmd.Parameters.AddWithValue("@ImgFileName", (object?)imageFileName ?? DBNull.Value);
                    pkgCmd.Parameters.AddWithValue("@ImgUrl", (object?)imageUrl ?? DBNull.Value);
                    affected = await pkgCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                if (affected == 0)
                {
                    await trans.RollbackAsync(cancellationToken);
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(model.CategoryId))
                {
                    const string catJoinSql = @"
                        IF NOT EXISTS (SELECT 1 FROM dbo.ServiceCategory WHERE ServiceCategoryID = @CatId)
                        BEGIN
                            INSERT INTO dbo.ServiceCategory (ServiceCategoryID, ServiceCategoryName, ServiceCategoryDescription)
                            VALUES (@CatId, @CatId, @CatId);
                        END;
                        DELETE FROM dbo.ServiceCategoryJoins WHERE ComboPackageID = @PkgId;
                        INSERT INTO dbo.ServiceCategoryJoins (ServiceCategoryJoinID, ServiceCategoryID, ServicesID, AdditionalServicesID, ComboPackageID)
                        VALUES ('SCJ-' + SUBSTRING(REPLACE(CAST(NEWID() AS nvarchar(36)), '-', ''), 1, 10), @CatId, NULL, NULL, @PkgId);";

                    await using var catCmd = new SqlCommand(catJoinSql, conn, trans);
                    catCmd.Parameters.AddWithValue("@CatId", model.CategoryId.Trim());
                    catCmd.Parameters.AddWithValue("@PkgId", cleanId);
                    await catCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                if (hasSelectedServices)
                {
                    await using (var delCmd = new SqlCommand("DELETE FROM dbo.ComboPackageJoin WHERE ComboPackageID = @PkgId;", conn, trans))
                    {
                        delCmd.Parameters.AddWithValue("@PkgId", cleanId);
                        await delCmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    foreach (var svcId in model.SelectedServiceIds!)
                    {
                        if (string.IsNullOrWhiteSpace(svcId)) continue;
                        const string joinSql = @"
                            DECLARE @JID nvarchar(30) = 'CPJ-' + SUBSTRING(REPLACE(CAST(NEWID() AS nvarchar(36)), '-', ''), 1, 10);
                            IF EXISTS (SELECT 1 FROM dbo.Services WHERE ServicesID = @SvcId)
                                INSERT INTO dbo.ComboPackageJoin (ComboPackageJoinID, ComboPackageID, ServicesID, AdditionalServicesID) VALUES (@JID, @PkgId, @SvcId, NULL);
                            ELSE IF EXISTS (SELECT 1 FROM dbo.AdditionalServices WHERE AdditionalServicesID = @SvcId)
                                INSERT INTO dbo.ComboPackageJoin (ComboPackageJoinID, ComboPackageID, ServicesID, AdditionalServicesID) VALUES (@JID, @PkgId, NULL, @SvcId);
                            ELSE
                                INSERT INTO dbo.ComboPackageJoin (ComboPackageJoinID, ComboPackageID, ServicesID, AdditionalServicesID) VALUES (@JID, @PkgId, @SvcId, NULL);";

                        await using var joinCmd = new SqlCommand(joinSql, conn, trans);
                        joinCmd.Parameters.AddWithValue("@PkgId", cleanId);
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

        InvalidateCatalogCache();

        return new ComboPackageCreatedResponseDto(
            PackageId: cleanId,
            Name: model.Name.Trim(),
            Description: model.Description?.Trim(),
            Price: model.Price,
            CategoryId: model.CategoryId,
            CategoryName: model.CategoryId,
            BundledServicesCount: model.SelectedServiceIds?.Count ?? 0,
            BundledServiceNames: model.SelectedServiceIds ?? Array.Empty<string>(),
            HasImage: (imageBytes != null && imageBytes.Length > 0) || !string.IsNullOrWhiteSpace(imageUrl),
            CreatedAt: DateTime.UtcNow,
            ImageFileName: imageFileName,
            ImageUrl: imageUrl ?? $"/api/management/packages/{cleanId}/image",
            RateType: effectiveRateType
        );
    }

    // ── Rate Types & Multi-Rate Pricing ──────────────────────────────────────

    public async Task<IReadOnlyList<RateTypeDto>> GetRateTypesAsync(CancellationToken cancellationToken = default)
    {
        if (_cache != null && _cache.TryGetValue(CacheKeyRateTypes, out IReadOnlyList<RateTypeDto>? cached) && cached != null)
            return cached;

        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

        var list = new List<RateTypeDto>();
        try
        {
            await using var cmd = new SqlCommand("dbo.sp_GetRateTypes", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new RateTypeDto(
                    RateTypeId: reader.GetString(0),
                    RateTypeName: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? null : reader.GetString(2),
                    UnitLabel: reader.IsDBNull(3) ? null : reader.GetString(3)
                ));
            }
        }
        catch
        {
            const string fallbackSql = "SELECT RateTypeID, RateTypeName, Description, UnitLabel FROM dbo.RateType WHERE IsActive = 1 ORDER BY RateTypeID;";
            try
            {
                await using var fbCmd = new SqlCommand(fallbackSql, conn);
                await using var reader = await fbCmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    list.Add(new RateTypeDto(
                        RateTypeId: reader.GetString(0),
                        RateTypeName: reader.GetString(1),
                        Description: reader.IsDBNull(2) ? null : reader.GetString(2),
                        UnitLabel: reader.IsDBNull(3) ? null : reader.GetString(3)
                    ));
                }
            }
            catch
            {
                list.AddRange(new[]
                {
                    new RateTypeDto("RT-001", "per hour", "Billed on an hourly rate", "hr"),
                    new RateTypeDto("RT-002", "per picture", "Billed per edited picture delivered", "picture"),
                    new RateTypeDto("RT-003", "per single shot", "Billed for an individual studio shot", "shot"),
                    new RateTypeDto("RT-004", "multi-shot", "Billed for a multi-shot sequence or burst bundle", "session")
                });
            }
        }
        _cache?.Set(CacheKeyRateTypes, (IReadOnlyList<RateTypeDto>)list, CatalogCacheTtl);
        return list;
    }

    public async Task<IReadOnlyList<ServiceRateDto>> GetServiceRatesAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

        var list = new List<ServiceRateDto>();
        try
        {
            await using var cmd = new SqlCommand("dbo.sp_GetServiceRates", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@ServicesID", serviceId.Trim());

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new ServiceRateDto(
                    ServiceRateId: reader.GetString(0),
                    ServiceId: reader.GetString(1),
                    RateTypeId: reader.GetString(2),
                    RateTypeName: reader.GetString(3),
                    RateAmount: reader.GetDecimal(4),
                    UnitLabel: reader.IsDBNull(5) ? null : reader.GetString(5),
                    IsActive: !reader.IsDBNull(6) && reader.GetBoolean(6)
                ));
            }
        }
        catch
        {
            const string fallbackSql = @"
                SELECT sr.ServiceRateID, sr.ServicesID, sr.RateTypeID, rt.RateTypeName, sr.RateAmount, rt.UnitLabel, sr.IsActive
                FROM dbo.ServiceRate sr
                INNER JOIN dbo.RateType rt ON rt.RateTypeID = sr.RateTypeID
                WHERE sr.ServicesID = @ServicesID AND sr.IsActive = 1
                ORDER BY rt.RateTypeName;";
            try
            {
                await using var fbCmd = new SqlCommand(fallbackSql, conn);
                fbCmd.Parameters.AddWithValue("@ServicesID", serviceId.Trim());
                await using var reader = await fbCmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    list.Add(new ServiceRateDto(
                        ServiceRateId: reader.GetString(0),
                        ServiceId: reader.GetString(1),
                        RateTypeId: reader.GetString(2),
                        RateTypeName: reader.GetString(3),
                        RateAmount: reader.GetDecimal(4),
                        UnitLabel: reader.IsDBNull(5) ? null : reader.GetString(5),
                        IsActive: !reader.IsDBNull(6) && reader.GetBoolean(6)
                    ));
                }
            }
            catch { }
        }
        return list;
    }

    public async Task<bool> AddOrUpdateServiceRateAsync(string serviceId, string rateTypeId, decimal rateAmount, CancellationToken cancellationToken = default)
    {
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

        try
        {
            await using var cmd = new SqlCommand("dbo.sp_AddServiceRate", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@ServicesID", serviceId.Trim());
            cmd.Parameters.AddWithValue("@RateTypeID", rateTypeId.Trim());
            cmd.Parameters.AddWithValue("@RateAmount", rateAmount);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch
        {
            const string sql = @"
                IF EXISTS (SELECT 1 FROM dbo.ServiceRate WHERE ServicesID = @SrvId AND RateTypeID = @RtId)
                BEGIN
                    UPDATE dbo.ServiceRate
                    SET RateAmount = @Amount, IsActive = 1
                    WHERE ServicesID = @SrvId AND RateTypeID = @RtId;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.ServiceRate (ServiceRateID, ServicesID, RateTypeID, RateAmount, EffectiveDate, IsActive, CreatedAt)
                    VALUES (CONCAT('SRATE-', LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), '-', ''), 12)), @SrvId, @RtId, @Amount, CAST(GETDATE() AS DATE), 1, SYSUTCDATETIME());
                END";
            try
            {
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SrvId", serviceId.Trim());
                cmd.Parameters.AddWithValue("@RtId", rateTypeId.Trim());
                cmd.Parameters.AddWithValue("@Amount", rateAmount);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                InvalidateCatalogCache();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<IReadOnlyList<DepartmentDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

        var list = new List<DepartmentDto>();
        const string sql = @"
            IF OBJECT_ID(N'dbo.Departments', N'U') IS NOT NULL
            BEGIN
                SELECT DepartmentID, DepartmentName, DepartmentDescription, CreatedAt
                FROM dbo.Departments
                ORDER BY DepartmentName ASC;
            END";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new DepartmentDto(
                DepartmentId: reader.GetString(0),
                DepartmentName: reader.GetString(1),
                DepartmentDescription: reader.IsDBNull(2) ? null : reader.GetString(2),
                CreatedAt: reader.GetDateTime(3)
            ));
        }

        return list;
    }

    public async Task<string> GetNextDepartmentIdAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

        const string sql = @"
            IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_DepartmentID')
            BEGIN
                CREATE SEQUENCE dbo.Seq_DepartmentID AS INT START WITH 8 INCREMENT BY 1;
            END;
            DECLARE @SeqVal INT = NEXT VALUE FOR dbo.Seq_DepartmentID;
            SELECT 'DEP-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4);";

        await using var cmd = new SqlCommand(sql, conn);
        var res = await cmd.ExecuteScalarAsync(cancellationToken);
        return res?.ToString() ?? "DEP-0008";
    }

    public async Task<DepartmentDto> CreateDepartmentAsync(CreateDepartmentDto dto, string? requestingAdminId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.DepartmentName))
            throw new ArgumentException("Department name is required.", nameof(dto));

        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);
        await EnsureServicesRateTypeSchemaAsync(conn, cancellationToken);

        var nextId = await GetNextDepartmentIdAsync(cancellationToken);

        const string insertSql = @"
            IF EXISTS (SELECT 1 FROM dbo.Departments WHERE LOWER(DepartmentName) = LOWER(@Name))
            BEGIN
                THROW 50020, 'A department with this name already exists.', 1;
            END;

            WHILE EXISTS (SELECT 1 FROM dbo.Departments WHERE DepartmentID = @Id)
            BEGIN
                DECLARE @SeqVal INT = NEXT VALUE FOR dbo.Seq_DepartmentID;
                SET @Id = 'DEP-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4);
            END;

            INSERT INTO dbo.Departments (DepartmentID, DepartmentName, DepartmentDescription, CreatedAt)
            VALUES (@Id, @Name, @Desc, SYSUTCDATETIME());

            IF OBJECT_ID(N'dbo.sp_AddHistoryLog', N'P') IS NOT NULL
            BEGIN
                EXEC dbo.sp_AddHistoryLog
                    @ActionType = 'INSERT',
                    @EntityName = 'Department',
                    @EntityID = @Id,
                    @ActionDetails = 'Created new department via API.',
                    @ChangedByType = 'Admin',
                    @ActorID = @AdminId;
            END;

            SELECT DepartmentID, DepartmentName, DepartmentDescription, CreatedAt
            FROM dbo.Departments
            WHERE DepartmentID = @Id;";

        await using var cmd = new SqlCommand(insertSql, conn);
        cmd.Parameters.AddWithValue("@Id", nextId);
        cmd.Parameters.AddWithValue("@Name", dto.DepartmentName.Trim());
        cmd.Parameters.AddWithValue("@Desc", (object?)dto.DepartmentDescription?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AdminId", (object?)requestingAdminId ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new DepartmentDto(
                DepartmentId: reader.GetString(0),
                DepartmentName: reader.GetString(1),
                DepartmentDescription: reader.IsDBNull(2) ? null : reader.GetString(2),
                CreatedAt: reader.GetDateTime(3)
            );
        }

        return new DepartmentDto(nextId, dto.DepartmentName.Trim(), dto.DepartmentDescription?.Trim(), DateTime.UtcNow);
    }

    public async Task<bool> UpdateEmployeeDaysOffAsync(string employeeId, string daysOff, string? requestingAdminId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            throw new ArgumentException("Employee ID is required.", nameof(employeeId));

        var normalizedDays = NormalizeDaysOff(daysOff);

        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            IF COL_LENGTH(N'dbo.Employees', N'DaysOff') IS NULL
                ALTER TABLE dbo.Employees ADD DaysOff nvarchar(50) NOT NULL CONSTRAINT DF_Employees_DaysOff DEFAULT 'Sunday';

            UPDATE dbo.Employees
            SET DaysOff = @DaysOff
            WHERE EmployeeID = @EmpId;

            DECLARE @Affected INT = @@ROWCOUNT;

            IF @Affected > 0 AND OBJECT_ID(N'dbo.sp_AddHistoryLog', N'P') IS NOT NULL
            BEGIN
                EXEC dbo.sp_AddHistoryLog
                    @ActionType = 'UPDATE',
                    @EntityName = 'Employees',
                    @EntityID = @EmpId,
                    @ActionDetails = @Details,
                    @ChangedByType = 'Admin',
                    @ActorID = @AdminId;
            END;

            SELECT @Affected;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@EmpId", employeeId.Trim());
        cmd.Parameters.AddWithValue("@DaysOff", normalizedDays);
        cmd.Parameters.AddWithValue("@Details", $"Updated staff Days Off for {employeeId.Trim()} to '{normalizedDays}'.");
        cmd.Parameters.AddWithValue("@AdminId", (object?)requestingAdminId ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null && Convert.ToInt32(result) > 0;
    }

    private static string NormalizeDaysOff(string? daysOff)
    {
        if (string.IsNullOrWhiteSpace(daysOff) || daysOff.Trim().Equals("None", StringComparison.OrdinalIgnoreCase))
            return "None";

        var validDays = new[]
        {
            DayOfWeek.Monday.ToString(),
            DayOfWeek.Tuesday.ToString(),
            DayOfWeek.Wednesday.ToString(),
            DayOfWeek.Thursday.ToString(),
            DayOfWeek.Friday.ToString(),
            DayOfWeek.Saturday.ToString(),
            DayOfWeek.Sunday.ToString()
        };

        var tokens = daysOff.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => validDays.FirstOrDefault(v => v.Equals(t, StringComparison.OrdinalIgnoreCase)))
            .Where(v => v != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return tokens.Count == 0 ? "None" : string.Join(", ", tokens!);
    }

    public async Task<(bool Success, string Message)> ResetEmployeePasswordToInitialAsync(
        string employeeId,
        string? requestingAdminId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return (false, "Employee ID is required.");

        var cleanEmpId = employeeId.Trim().ToUpperInvariant();
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        const string lookupSql = @"
            SELECT TOP 1
                EmployeeID,
                EmployeeFirstName,
                EmployeeLastName,
                EmployeeEmail,
                BirthDate,
                BirthYear
            FROM dbo.Employees
            WHERE UPPER(LTRIM(RTRIM(EmployeeID))) = @EmpId;";

        string? dbEmpId = null;
        string firstName = string.Empty;
        string lastName = string.Empty;
        string? email = null;
        DateTime? birthDate = null;
        int? birthYear = null;

        await using (var cmd = new SqlCommand(lookupSql, conn))
        {
            cmd.Parameters.AddWithValue("@EmpId", cleanEmpId);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                dbEmpId = reader.GetString(0).Trim();
                firstName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
                lastName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim();
                email = reader.IsDBNull(3) ? null : reader.GetString(3).Trim();
                birthDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4);
                birthYear = reader.IsDBNull(5) ? null : reader.GetInt32(5);
            }
        }

        if (string.IsNullOrWhiteSpace(dbEmpId))
            return (false, $"Staff member '{cleanEmpId}' was not found.");

        var initialPassword = PhotoAevumStudio.Application.Utils.EmployeeCredentialGenerator.GenerateInitialPassword(12);
        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(initialPassword);

        const string updateSql = @"
            UPDATE dbo.Employees
            SET EmployeePassword = @Hash,
                isFirstLogin = 1
            WHERE UPPER(LTRIM(RTRIM(EmployeeID))) = @EmpId;

            IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
            BEGIN
                UPDATE dbo.AspNetUsers
                SET PasswordHash = @Hash,
                    MustChangePassword = 1
                WHERE UPPER(LTRIM(RTRIM(StudioId))) = @EmpId
                   OR UPPER(LTRIM(RTRIM(UserName))) = @EmpId
                   OR (@Email IS NOT NULL AND UPPER(LTRIM(RTRIM(Email))) = UPPER(@Email));
            END;

            IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
            BEGIN
                DECLARE @ActorType nvarchar(20) = CASE WHEN @AdminId LIKE 'SADM-%' THEN 'SystemAdmin' ELSE 'Admin' END;
                IF OBJECT_ID(N'dbo.sp_AddHistoryLog', N'P') IS NOT NULL
                BEGIN
                    EXEC dbo.sp_AddHistoryLog
                        @ActionType = 'PASSWORD_RESET',
                        @EntityName = 'Employee',
                        @EntityID = @EmpId,
                        @ActionDetails = @Details,
                        @ChangedByType = @ActorType,
                        @ActorID = @AdminId;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ActionDetails, ChangedByType, LoggedAt)
                    VALUES ('PASSWORD_RESET', 'Employee', @EmpId, @Details, @ActorType, SYSUTCDATETIME());
                END
            END;";

        await using (var updCmd = new SqlCommand(updateSql, conn))
        {
            updCmd.Parameters.AddWithValue("@EmpId", dbEmpId);
            updCmd.Parameters.AddWithValue("@Hash", hashedPassword);
            updCmd.Parameters.AddWithValue("@Email", (object?)email ?? DBNull.Value);
            updCmd.Parameters.AddWithValue("@AdminId", string.IsNullOrWhiteSpace(requestingAdminId) ? DBNull.Value : requestingAdminId.Trim());
            updCmd.Parameters.AddWithValue("@Details", $"Admin reset password for {dbEmpId} to initial creation password and flagged for forced password change on next login.");
            await updCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        if (_emailService != null && !string.IsNullOrWhiteSpace(email))
        {
            var fullName = string.Join(" ", new[] { firstName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (string.IsNullOrWhiteSpace(fullName)) fullName = dbEmpId;
            var htmlBody = StudioEmailTemplates.StaffPasswordResetNotification(fullName, dbEmpId, initialPassword, "/login");
            await _emailService.TrySendEmailAsync(
                email,
                $"Photo Aevum Studio — Password Reset ({dbEmpId})",
                htmlBody,
                isHtml: true,
                cancellationToken: cancellationToken);
        }

        return (true, $"Password for {dbEmpId} has been reset to their initial creation password. A notification email has been sent and they will be prompted to change it on next login.");
    }

    public async Task<(bool Success, bool EmailSent, bool HandbookAttached, string Message, string? Warning)> ResendEmployeeWelcomeEmailAsync(
        string employeeId,
        string? requestingAdminId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return (false, false, false, "Employee ID is required.", "Employee ID is required.");

        var cleanEmpId = employeeId.Trim().ToUpperInvariant();
        await using var conn = SqlResilienceHelper.CreateConnection(GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        const string lookupSql = @"
            SELECT TOP 1
                EmployeeID,
                EmployeeFirstName,
                EmployeeLastName,
                EmployeeEmail
            FROM dbo.Employees
            WHERE UPPER(LTRIM(RTRIM(EmployeeID))) = @EmpId;";

        string? dbEmpId = null;
        string firstName = string.Empty;
        string lastName = string.Empty;
        string? email = null;

        await using (var cmd = new SqlCommand(lookupSql, conn))
        {
            cmd.Parameters.AddWithValue("@EmpId", cleanEmpId);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                dbEmpId = reader.GetString(0).Trim();
                firstName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
                lastName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim();
                email = reader.IsDBNull(3) ? null : reader.GetString(3).Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(dbEmpId))
            return (false, false, false, $"Staff member '{cleanEmpId}' was not found.", $"Staff member '{cleanEmpId}' was not found.");

        if (string.IsNullOrWhiteSpace(email))
            return (false, false, false, $"Staff member '{dbEmpId}' does not have a valid email address.", "Employee email address is missing.");

        var specializations = new List<string>();
        const string specSql = @"
            IF OBJECT_ID(N'dbo.EmployeeJointSpecialization', N'U') IS NOT NULL
               AND OBJECT_ID(N'dbo.EmployeeSpecialization', N'U') IS NOT NULL
            BEGIN
                SELECT s.SpecializationName
                FROM dbo.EmployeeJointSpecialization js
                INNER JOIN dbo.EmployeeSpecialization s ON js.SpecializationID = s.SpecializationID
                WHERE UPPER(LTRIM(RTRIM(js.EmployeeID))) = @EmpId
                  AND s.SpecializationName IS NOT NULL;
            END";
        await using (var specCmd = new SqlCommand(specSql, conn))
        {
            specCmd.Parameters.AddWithValue("@EmpId", dbEmpId);
            await using var specReader = await specCmd.ExecuteReaderAsync(cancellationToken);
            while (await specReader.ReadAsync(cancellationToken))
            {
                var specName = specReader.IsDBNull(0) ? null : specReader.GetString(0).Trim();
                if (!string.IsNullOrWhiteSpace(specName))
                    specializations.Add(specName);
            }
        }

        var initialPassword = PhotoAevumStudio.Application.Utils.EmployeeCredentialGenerator.GenerateInitialPassword(12);
        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(initialPassword);

        const string updateSql = @"
            UPDATE dbo.Employees
            SET EmployeePassword = @Hash,
                isFirstLogin = 1
            WHERE UPPER(LTRIM(RTRIM(EmployeeID))) = @EmpId;

            IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
            BEGIN
                UPDATE dbo.AspNetUsers
                SET PasswordHash = @Hash,
                    MustChangePassword = 1
                WHERE UPPER(LTRIM(RTRIM(StudioId))) = @EmpId
                   OR UPPER(LTRIM(RTRIM(UserName))) = @EmpId
                   OR (@Email IS NOT NULL AND UPPER(LTRIM(RTRIM(Email))) = UPPER(@Email));
            END;

            IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
            BEGIN
                DECLARE @ActorType nvarchar(20) = CASE WHEN @AdminId LIKE 'SADM-%' THEN 'SystemAdmin' ELSE 'Admin' END;
                IF OBJECT_ID(N'dbo.sp_AddHistoryLog', N'P') IS NOT NULL
                BEGIN
                    EXEC dbo.sp_AddHistoryLog
                        @ActionType = 'WELCOME_EMAIL_RESEND',
                        @EntityName = 'Employee',
                        @EntityID = @EmpId,
                        @ActionDetails = @Details,
                        @ChangedByType = @ActorType,
                        @ActorID = @AdminId;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ActionDetails, ChangedByType, LoggedAt)
                    VALUES ('WELCOME_EMAIL_RESEND', 'Employee', @EmpId, @Details, @ActorType, SYSUTCDATETIME());
                END
            END;";

        await using (var updCmd = new SqlCommand(updateSql, conn))
        {
            updCmd.Parameters.AddWithValue("@EmpId", dbEmpId);
            updCmd.Parameters.AddWithValue("@Hash", hashedPassword);
            updCmd.Parameters.AddWithValue("@Email", email);
            updCmd.Parameters.AddWithValue("@AdminId", string.IsNullOrWhiteSpace(requestingAdminId) ? DBNull.Value : requestingAdminId.Trim());
            updCmd.Parameters.AddWithValue("@Details", $"Re-issued onboarding credentials and resent welcome email with Employee Handbook PDF for {dbEmpId}.");
            await updCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        if (_emailService == null)
        {
            const string noEmailMsg = "Credentials were updated, but the email service is unavailable.";
            return (true, false, false, noEmailMsg, noEmailMsg);
        }

        var fullName = string.Join(" ", new[] { firstName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (string.IsNullOrWhiteSpace(fullName)) fullName = dbEmpId;
        var assignedRole = specializations.Count > 0 ? string.Join(", ", specializations) : "Employee";

        var dispatch = await _emailService.TrySendEmployeeWelcomeEmailAsync(
            toEmail: email,
            employeeName: fullName,
            employeeId: dbEmpId,
            initialPassword: initialPassword,
            cancellationToken: cancellationToken,
            assignedRole: assignedRole);

        if (dispatch.EmailSent && dispatch.HandbookAttached)
        {
            return (true, true, true, $"Welcome email and Employee Handbook PDF sent to {email} for {dbEmpId}.", null);
        }

        return (true, dispatch.EmailSent, dispatch.HandbookAttached,
            dispatch.WarningMessage ?? "Welcome email delivery completed with a warning.",
            dispatch.WarningMessage);
    }

    public async Task<bool> SetCatalogItemArchivedAsync(
        string kind,
        string id,
        bool archive,
        string? requestingAdminId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        var cleanId = id.Trim();
        var normKind = (kind ?? string.Empty).Trim().ToLowerInvariant();
        if (normKind is not ("service" or "package" or "addon" or "category"))
            return false;

        await using var conn = new SqlConnection(_db.Database.GetConnectionString()!);
        await conn.OpenAsync(cancellationToken);

        var sql = normKind switch
        {
            "service" => @"
                IF COL_LENGTH(N'dbo.Services', N'IsArchived') IS NULL
                    ALTER TABLE dbo.Services ADD IsArchived bit NOT NULL CONSTRAINT DF_Services_IsArchived DEFAULT 0;
                IF COL_LENGTH(N'dbo.Services', N'ArchivedAt') IS NULL
                    ALTER TABLE dbo.Services ADD ArchivedAt datetime2 NULL;
                UPDATE dbo.Services
                SET IsArchived = @Archive,
                    ArchivedAt = CASE WHEN @Archive = 1 THEN SYSUTCDATETIME() ELSE NULL END
                WHERE UPPER(ServicesID) = UPPER(@Id);",
            "package" => @"
                IF COL_LENGTH(N'dbo.ComboPackage', N'IsArchived') IS NULL
                    ALTER TABLE dbo.ComboPackage ADD IsArchived bit NOT NULL CONSTRAINT DF_ComboPackage_IsArchived DEFAULT 0;
                IF COL_LENGTH(N'dbo.ComboPackage', N'ArchivedAt') IS NULL
                    ALTER TABLE dbo.ComboPackage ADD ArchivedAt datetime2 NULL;
                UPDATE dbo.ComboPackage
                SET IsArchived = @Archive,
                    ArchivedAt = CASE WHEN @Archive = 1 THEN SYSUTCDATETIME() ELSE NULL END
                WHERE UPPER(ComboPackageID) = UPPER(@Id);",
            "addon" => @"
                IF COL_LENGTH(N'dbo.AdditionalServices', N'IsArchived') IS NULL
                    ALTER TABLE dbo.AdditionalServices ADD IsArchived bit NOT NULL CONSTRAINT DF_AdditionalServices_IsArchived DEFAULT 0;
                IF COL_LENGTH(N'dbo.AdditionalServices', N'ArchivedAt') IS NULL
                    ALTER TABLE dbo.AdditionalServices ADD ArchivedAt datetime2 NULL;
                UPDATE dbo.AdditionalServices
                SET IsArchived = @Archive,
                    ArchivedAt = CASE WHEN @Archive = 1 THEN SYSUTCDATETIME() ELSE NULL END
                WHERE UPPER(AdditionalServicesID) = UPPER(@Id);",
            _ => @"
                IF COL_LENGTH(N'dbo.ServiceCategory', N'Status') IS NULL
                    ALTER TABLE dbo.ServiceCategory ADD Status nvarchar(30) NOT NULL CONSTRAINT DF_ServiceCategory_Status DEFAULT 'Active';
                IF COL_LENGTH(N'dbo.ServiceCategory', N'IsArchived') IS NULL
                    ALTER TABLE dbo.ServiceCategory ADD IsArchived bit NOT NULL CONSTRAINT DF_ServiceCategory_IsArchived DEFAULT 0;
                IF COL_LENGTH(N'dbo.ServiceCategory', N'ArchivedAt') IS NULL
                    ALTER TABLE dbo.ServiceCategory ADD ArchivedAt datetime2 NULL;
                UPDATE dbo.ServiceCategory
                SET IsArchived = @Archive,
                    Status = CASE WHEN @Archive = 1 THEN N'Archived' ELSE N'Active' END,
                    ArchivedAt = CASE WHEN @Archive = 1 THEN SYSUTCDATETIME() ELSE NULL END
                WHERE UPPER(ServiceCategoryID) = UPPER(@Id);"
        };

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", cleanId);
        cmd.Parameters.AddWithValue("@Archive", archive ? 1 : 0);
        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);

        if (rows > 0 && archive && normKind is "service" or "package" or "addon")
        {
            const string unfeatureSql = @"
                IF OBJECT_ID(N'dbo.FeaturedService', N'U') IS NOT NULL
                    DELETE FROM dbo.FeaturedService
                    WHERE UPPER(ServicesID) = UPPER(@Id)
                       OR UPPER(AdditionalServicesID) = UPPER(@Id)
                       OR UPPER(ComboPackageID) = UPPER(@Id);";
            await using var ufCmd = new SqlCommand(unfeatureSql, conn);
            ufCmd.Parameters.AddWithValue("@Id", cleanId);
            try { await ufCmd.ExecuteNonQueryAsync(cancellationToken); } catch { }
        }

        if (rows > 0)
        {
            const string logSql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedByType, ActionDetails, LoggedAt)
                    VALUES (@Action, @Entity, @Id, N'Admin', @Details, SYSUTCDATETIME());
                END";
            await using var logCmd = new SqlCommand(logSql, conn);
            logCmd.Parameters.AddWithValue("@Action", archive ? "ARCHIVE" : "RESTORE");
            logCmd.Parameters.AddWithValue("@Entity", normKind);
            logCmd.Parameters.AddWithValue("@Id", cleanId);
            logCmd.Parameters.AddWithValue("@Details", $"{(archive ? "Archived" : "Restored")} {normKind} '{cleanId}'.");
            try { await logCmd.ExecuteNonQueryAsync(cancellationToken); } catch { }
        }

        return rows > 0;
    }

    public async Task<(bool Success, string Message)> DeleteCatalogItemWithSnapshotAsync(
        string kind,
        string id,
        string? requestingAdminId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return (false, "Item ID is required.");

        var cleanId = id.Trim();
        var normKind = (kind ?? string.Empty).Trim().ToLowerInvariant();
        if (normKind is not ("service" or "package" or "addon" or "category"))
            return (false, "Unsupported catalog item type.");

        await using var conn = new SqlConnection(_db.Database.GetConnectionString()!);
        await conn.OpenAsync(cancellationToken);

        // Ensure EventLineItem snapshot columns and relaxed CHECK constraint exist
        const string ensureSnapshotSchema = @"
            IF OBJECT_ID(N'dbo.EventLineItem', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.EventLineItem', N'ServiceNameSnapshot') IS NULL
                    ALTER TABLE dbo.EventLineItem ADD ServiceNameSnapshot nvarchar(255) NULL;
                IF COL_LENGTH(N'dbo.EventLineItem', N'ServiceKindSnapshot') IS NULL
                    ALTER TABLE dbo.EventLineItem ADD ServiceKindSnapshot nvarchar(30) NULL;
                IF OBJECT_ID(N'dbo.CK_EventLineItem_ExactlyOne', N'C') IS NOT NULL
                    ALTER TABLE dbo.EventLineItem DROP CONSTRAINT CK_EventLineItem_ExactlyOne;
                ALTER TABLE dbo.EventLineItem ADD CONSTRAINT CK_EventLineItem_ExactlyOne CHECK (
                    ((CASE WHEN ServicesID IS NOT NULL THEN 1 ELSE 0 END) +
                     (CASE WHEN AdditionalServicesID IS NOT NULL THEN 1 ELSE 0 END) +
                     (CASE WHEN ComboPackageID IS NOT NULL THEN 1 ELSE 0 END) = 1)
                    OR (ServicesID IS NULL AND AdditionalServicesID IS NULL AND ComboPackageID IS NULL AND ServiceNameSnapshot IS NOT NULL));
            END;";
        await using (var schemaCmd = new SqlCommand(ensureSnapshotSchema, conn))
        {
            await schemaCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            string? itemName = null;
            var selectNameSql = normKind switch
            {
                "service" => "SELECT ServiceName FROM dbo.Services WHERE UPPER(ServicesID) = UPPER(@Id);",
                "package" => "SELECT ComboPackageName FROM dbo.ComboPackage WHERE UPPER(ComboPackageID) = UPPER(@Id);",
                "addon" => "SELECT AdditionalServiceName FROM dbo.AdditionalServices WHERE UPPER(AdditionalServicesID) = UPPER(@Id);",
                _ => "SELECT ServiceCategoryName FROM dbo.ServiceCategory WHERE UPPER(ServiceCategoryID) = UPPER(@Id);"
            };

            await using (var nameCmd = new SqlCommand(selectNameSql, conn, tx))
            {
                nameCmd.Parameters.AddWithValue("@Id", cleanId);
                var scalar = await nameCmd.ExecuteScalarAsync(cancellationToken);
                itemName = scalar == null || scalar == DBNull.Value ? null : Convert.ToString(scalar);
            }

            if (itemName == null)
            {
                await tx.RollbackAsync(cancellationToken);
                return (false, $"Item '{cleanId}' was not found.");
            }

            if (normKind is "service" or "package" or "addon")
            {
                var fkCol = normKind switch
                {
                    "service" => "ServicesID",
                    "package" => "ComboPackageID",
                    _ => "AdditionalServicesID"
                };
                var kindLabel = normKind switch
                {
                    "service" => "Service",
                    "package" => "Combo Package",
                    _ => "Add-on"
                };

                var snapshotSql = $@"
                    IF OBJECT_ID(N'dbo.EventLineItem', N'U') IS NOT NULL
                    BEGIN
                        UPDATE dbo.EventLineItem
                        SET ServiceNameSnapshot = COALESCE(ServiceNameSnapshot, @ItemName),
                            ServiceKindSnapshot = COALESCE(ServiceKindSnapshot, @KindLabel),
                            {fkCol} = NULL
                        WHERE UPPER({fkCol}) = UPPER(@Id);
                    END
                    IF OBJECT_ID(N'dbo.ComboPackageJoin', N'U') IS NOT NULL
                        DELETE FROM dbo.ComboPackageJoin WHERE UPPER({fkCol}) = UPPER(@Id);
                    IF OBJECT_ID(N'dbo.ServiceCategoryJoins', N'U') IS NOT NULL
                        DELETE FROM dbo.ServiceCategoryJoins WHERE UPPER({fkCol}) = UPPER(@Id);
                    IF OBJECT_ID(N'dbo.FeaturedService', N'U') IS NOT NULL
                        DELETE FROM dbo.FeaturedService WHERE UPPER({fkCol}) = UPPER(@Id);";

                await using (var snapCmd = new SqlCommand(snapshotSql, conn, tx))
                {
                    snapCmd.Parameters.AddWithValue("@Id", cleanId);
                    snapCmd.Parameters.AddWithValue("@ItemName", itemName);
                    snapCmd.Parameters.AddWithValue("@KindLabel", kindLabel);
                    await snapCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                if (normKind == "service")
                {
                    const string rateSql = @"
                        IF OBJECT_ID(N'dbo.ServiceRate', N'U') IS NOT NULL
                            DELETE FROM dbo.ServiceRate WHERE UPPER(ServicesID) = UPPER(@Id);";
                    await using var rateCmd = new SqlCommand(rateSql, conn, tx);
                    rateCmd.Parameters.AddWithValue("@Id", cleanId);
                    await rateCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            else
            {
                const string catJoinSql = @"
                    IF OBJECT_ID(N'dbo.ServiceCategoryJoins', N'U') IS NOT NULL
                        DELETE FROM dbo.ServiceCategoryJoins WHERE UPPER(ServiceCategoryID) = UPPER(@Id);";
                await using var catJoinCmd = new SqlCommand(catJoinSql, conn, tx);
                catJoinCmd.Parameters.AddWithValue("@Id", cleanId);
                await catJoinCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            var delSql = normKind switch
            {
                "service" => "DELETE FROM dbo.Services WHERE UPPER(ServicesID) = UPPER(@Id);",
                "package" => "DELETE FROM dbo.ComboPackage WHERE UPPER(ComboPackageID) = UPPER(@Id);",
                "addon" => "DELETE FROM dbo.AdditionalServices WHERE UPPER(AdditionalServicesID) = UPPER(@Id);",
                _ => "DELETE FROM dbo.ServiceCategory WHERE UPPER(ServiceCategoryID) = UPPER(@Id);"
            };

            await using (var delCmd = new SqlCommand(delSql, conn, tx))
            {
                delCmd.Parameters.AddWithValue("@Id", cleanId);
                await delCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            const string logSql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedByType, ActionDetails, LoggedAt)
                    VALUES (N'DELETE', @Entity, @Id, N'Admin', @Details, SYSUTCDATETIME());
                END";
            await using (var logCmd = new SqlCommand(logSql, conn, tx))
            {
                logCmd.Parameters.AddWithValue("@Entity", normKind);
                logCmd.Parameters.AddWithValue("@Id", cleanId);
                logCmd.Parameters.AddWithValue("@Details", $"Permanently deleted {normKind} '{itemName}' ({cleanId}) with EventLineItem snapshot.");
                await logCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            return (true, $"'{itemName}' was permanently deleted. Existing event and ledger history remains preserved.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return (false, $"Unable to delete {normKind} '{cleanId}': {ex.Message}");
        }
    }
}
