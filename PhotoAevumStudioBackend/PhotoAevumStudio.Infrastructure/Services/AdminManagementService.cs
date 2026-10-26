using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Utils;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Infrastructure.Services;

/// <summary>
/// Production implementation of IAdminManagementService with strict role-based access controls.
/// </summary>
public class AdminManagementService : IAdminManagementService
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _emailService;
    private readonly ILogger<AdminManagementService> _logger;

    private static readonly Regex ValidAdminIdRegex =
        new(@"^(SADM|ADM)-\d{3}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public AdminManagementService(
        ApplicationDbContext db,
        IEmailService emailService,
        ILogger<AdminManagementService> logger)
    {
        _db = db;
        _emailService = emailService;
        _logger = logger;
    }

    private string GetConnectionString() =>
        _db.Database.GetConnectionString()
        ?? throw new InvalidOperationException("Connection string is not configured.");

    private SqlConnection CreateConnection() => new(GetConnectionString());

    public async Task<IReadOnlyList<AdminDto>> GetAdminsAsync(
        string requestingUserId,
        bool isSystemAdmin,
        CancellationToken cancellationToken = default)
    {
        var admins = new List<AdminDto>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        // 1. If caller is System Admin, query dbo.SystemAdmin for System Admin accounts
        if (isSystemAdmin)
        {
            const string sysAdminSql = @"
                IF OBJECT_ID(N'dbo.SystemAdmin', N'U') IS NOT NULL
                BEGIN
                    SELECT SystemAdminID, FirstName, LastName, MiddleName
                    FROM dbo.SystemAdmin
                    ORDER BY SystemAdminID;
                END";

            await using var sysCmd = new SqlCommand(sysAdminSql, conn);
            await using var sysReader = await sysCmd.ExecuteReaderAsync(cancellationToken);
            while (await sysReader.ReadAsync(cancellationToken))
            {
                var id = sysReader.GetString(0).Trim().ToUpperInvariant();
                var first = sysReader.IsDBNull(1) ? string.Empty : sysReader.GetString(1);
                var last = sysReader.IsDBNull(2) ? string.Empty : sysReader.GetString(2);
                var middle = sysReader.IsDBNull(3) ? null : sysReader.GetString(3);

                var fullName = string.Join(" ", new[] { first, middle, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var email = $"{id.ToLowerInvariant()}@photoaevum.studio";

                admins.Add(new AdminDto(
                    Id: id,
                    FirstName: first,
                    LastName: last,
                    MiddleName: middle,
                    FullName: fullName,
                    Email: email,
                    Role: "System Admin",
                    IsSuperAdmin: true,
                    IsFirstLogin: false,
                    Status: "Active"));

                seenIds.Add(id);
            }
        }

        // 2. Query dbo.Admin for Admin accounts (and any system admins not in dbo.SystemAdmin)
        const string adminSql = @"
            IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL
            BEGIN
                SELECT AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName,
                       CASE WHEN COL_LENGTH(N'dbo.Admin', N'Email') IS NOT NULL THEN Email ELSE NULL END AS Email,
                       CASE WHEN COL_LENGTH(N'dbo.Admin', N'isFirstLogin') IS NOT NULL THEN isFirstLogin ELSE CAST(1 AS bit) END AS isFirstLogin
                FROM dbo.Admin
                ORDER BY AdminID;
            END";

        await using (var adminCmd = new SqlCommand(adminSql, conn))
        await using (var reader = await adminCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0).Trim().ToUpperInvariant();
                if (!ValidAdminIdRegex.IsMatch(id)) continue;
                if (seenIds.Contains(id)) continue; // Avoid duplicate if already loaded from dbo.SystemAdmin

                var rawRole = reader.IsDBNull(1) ? "Admin" : reader.GetString(1);
                var first   = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var last    = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                var middle  = reader.IsDBNull(4) ? null : reader.GetString(4);
                var emailVal = reader.IsDBNull(5) ? null : reader.GetString(5);
                var isFirstLogin = reader.IsDBNull(6) ? false : Convert.ToBoolean(reader.GetValue(6));

                bool isSuper = id.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase)
                               || AdminCredentialGenerator.IsSystemAdminRole(rawRole);

                // ── Strict Role Visibility Rule ──
                // If caller is an Admin (not a System Admin), they CANNOT see any System Admin accounts.
                if (!isSystemAdmin && isSuper)
                {
                    continue;
                }

                var fullName = string.Join(" ", new[] { first, middle, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var email = !string.IsNullOrWhiteSpace(emailVal)
                    ? emailVal
                    : $"{id.ToLowerInvariant()}@photoaevum.studio";

                var normalizedRole = isSuper ? "System Admin" : "Admin";

                admins.Add(new AdminDto(
                    Id: id,
                    FirstName: first,
                    LastName: last,
                    MiddleName: middle,
                    FullName: fullName,
                    Email: email,
                    Role: normalizedRole,
                    IsSuperAdmin: isSuper,
                    IsFirstLogin: isFirstLogin,
                    Status: "Active"));

                seenIds.Add(id);
            }
        }

        return admins;
    }

    public async Task<AdminDto?> GetAdminByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var trimmedId = id.Trim().ToUpperInvariant();

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        // 1. If ID starts with SADM- or might be a System Admin, check dbo.SystemAdmin first
        if (trimmedId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase))
        {
            const string sysSql = @"
                IF OBJECT_ID(N'dbo.SystemAdmin', N'U') IS NOT NULL
                BEGIN
                    SELECT SystemAdminID, FirstName, LastName, MiddleName
                    FROM dbo.SystemAdmin
                    WHERE UPPER(SystemAdminID) = @Id;
                END";

            await using var sysCmd = new SqlCommand(sysSql, conn);
            sysCmd.Parameters.AddWithValue("@Id", trimmedId);
            await using var sysReader = await sysCmd.ExecuteReaderAsync(cancellationToken);
            if (await sysReader.ReadAsync(cancellationToken))
            {
                var sysId = sysReader.GetString(0).Trim().ToUpperInvariant();
                var first = sysReader.IsDBNull(1) ? string.Empty : sysReader.GetString(1);
                var last = sysReader.IsDBNull(2) ? string.Empty : sysReader.GetString(2);
                var middle = sysReader.IsDBNull(3) ? null : sysReader.GetString(3);
                var fullName = string.Join(" ", new[] { first, middle, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var email = $"{sysId.ToLowerInvariant()}@photoaevum.studio";

                return new AdminDto(
                    Id: sysId,
                    FirstName: first,
                    LastName: last,
                    MiddleName: middle,
                    FullName: fullName,
                    Email: email,
                    Role: "System Admin",
                    IsSuperAdmin: true,
                    IsFirstLogin: false,
                    Status: "Active");
            }
        }

        // 2. Check dbo.Admin
        const string sql = @"
            IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL
            BEGIN
                SELECT AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName,
                       CASE WHEN COL_LENGTH(N'dbo.Admin', N'Email') IS NOT NULL THEN Email ELSE NULL END AS Email,
                       CASE WHEN COL_LENGTH(N'dbo.Admin', N'isFirstLogin') IS NOT NULL THEN isFirstLogin ELSE CAST(1 AS bit) END AS isFirstLogin
                FROM dbo.Admin
                WHERE UPPER(AdminID) = @Id;
            END";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", trimmedId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            // Fallback: check dbo.SystemAdmin in case it was not prefixed with SADM
            reader.Close();
            const string sysFallbackSql = @"
                IF OBJECT_ID(N'dbo.SystemAdmin', N'U') IS NOT NULL
                BEGIN
                    SELECT SystemAdminID, FirstName, LastName, MiddleName
                    FROM dbo.SystemAdmin
                    WHERE UPPER(SystemAdminID) = @Id;
                END";
            await using var sysFbCmd = new SqlCommand(sysFallbackSql, conn);
            sysFbCmd.Parameters.AddWithValue("@Id", trimmedId);
            await using var sysFbReader = await sysFbCmd.ExecuteReaderAsync(cancellationToken);
            if (await sysFbReader.ReadAsync(cancellationToken))
            {
                var sysId = sysFbReader.GetString(0).Trim().ToUpperInvariant();
                var first = sysFbReader.IsDBNull(1) ? string.Empty : sysFbReader.GetString(1);
                var last = sysFbReader.IsDBNull(2) ? string.Empty : sysFbReader.GetString(2);
                var middle = sysFbReader.IsDBNull(3) ? null : sysFbReader.GetString(3);
                var fullName = string.Join(" ", new[] { first, middle, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var email = $"{sysId.ToLowerInvariant()}@photoaevum.studio";

                return new AdminDto(
                    Id: sysId,
                    FirstName: first,
                    LastName: last,
                    MiddleName: middle,
                    FullName: fullName,
                    Email: email,
                    Role: "System Admin",
                    IsSuperAdmin: true,
                    IsFirstLogin: false,
                    Status: "Active");
            }

            return null;
        }

        var dbId = reader.GetString(0).Trim().ToUpperInvariant();
        var rawRole = reader.IsDBNull(1) ? "Admin" : reader.GetString(1);
        var adminFirst   = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
        var adminLast    = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
        var adminMiddle  = reader.IsDBNull(4) ? null : reader.GetString(4);
        var emailVal = reader.IsDBNull(5) ? null : reader.GetString(5);
        var isFirstLogin = reader.IsDBNull(6) ? false : Convert.ToBoolean(reader.GetValue(6));

        bool isSuper = dbId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase)
                       || AdminCredentialGenerator.IsSystemAdminRole(rawRole);

        var adminFullName = string.Join(" ", new[] { adminFirst, adminMiddle, adminLast }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var adminEmail = !string.IsNullOrWhiteSpace(emailVal)
            ? emailVal
            : $"{dbId.ToLowerInvariant()}@photoaevum.studio";

        var normalizedRole = isSuper ? "System Admin" : "Admin";

        return new AdminDto(
            Id: dbId,
            FirstName: adminFirst,
            LastName: adminLast,
            MiddleName: adminMiddle,
            FullName: adminFullName,
            Email: adminEmail,
            Role: normalizedRole,
            IsSuperAdmin: isSuper,
            IsFirstLogin: isFirstLogin,
            Status: "Active");
    }

    public async Task<string> GetNextAdminIdAsync(string role, CancellationToken cancellationToken = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var existingIds = new List<string>();
        const string sql = @"
            IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL
            BEGIN
                SELECT AdminID FROM dbo.Admin;
            END
            IF OBJECT_ID(N'dbo.SystemAdmin', N'U') IS NOT NULL
            BEGIN
                SELECT SystemAdminID FROM dbo.SystemAdmin;
            END";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            existingIds.Add(reader.GetString(0));
        }

        if (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                existingIds.Add(reader.GetString(0));
            }
        }

        return AdminCredentialGenerator.GenerateAdminId(role, existingIds);
    }

    public async Task<AdminDto> CreateAdminAsync(
        CreateAdminDto dto,
        string requestingUserId,
        bool isSystemAdmin,
        CancellationToken cancellationToken = default)
    {
        // ── Strict Mutation Enforcement: Role "Admin" cannot add new admins ──
        if (!isSystemAdmin && !requestingUserId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Forbidden: Requester {UserId} attempted to create an admin account without System Admin privileges.", requestingUserId);
            throw new UnauthorizedAccessException("Permission Denied: Only a SystemAdmin (SADM-###) is authorized to create new Admin accounts.");
        }

        if (string.IsNullOrWhiteSpace(dto.FirstName))
            throw new ArgumentException("First Name is required.", nameof(dto.FirstName));
        if (string.IsNullOrWhiteSpace(dto.LastName))
            throw new ArgumentException("Last Name is required.", nameof(dto.LastName));
        if (string.IsNullOrWhiteSpace(dto.Email))
            throw new ArgumentException("Email is required.", nameof(dto.Email));

        bool targetIsSysAdmin = AdminCredentialGenerator.IsSystemAdminRole(dto.Role);
        string normalizedRole = targetIsSysAdmin ? "SystemAdmin" : "Admin";

        // Determine or validate the System-Assigned ID
        string adminId;
        if (!string.IsNullOrWhiteSpace(dto.CustomId))
        {
            adminId = dto.CustomId.Trim().ToUpperInvariant();
            if (!ValidAdminIdRegex.IsMatch(adminId))
            {
                throw new ArgumentException($"Invalid Admin ID format '{adminId}'. Must follow SADM-### or ADM-###.", nameof(dto.CustomId));
            }
            if (targetIsSysAdmin && !adminId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("System Admin IDs must begin with prefix 'SADM-'.", nameof(dto.CustomId));
            }
            if (!targetIsSysAdmin && !adminId.StartsWith("ADM-", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Regular Admin IDs must begin with prefix 'ADM-'.", nameof(dto.CustomId));
            }
        }
        else
        {
            adminId = await GetNextAdminIdAsync(normalizedRole, cancellationToken);
        }

        // Generate temporary password if not passed
        string temporaryPassword = string.IsNullOrWhiteSpace(dto.TemporaryPassword)
            ? AdminCredentialGenerator.GenerateTemporaryPassword(12)
            : dto.TemporaryPassword.Trim();

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        // Check uniqueness across both dbo.Admin and dbo.SystemAdmin
        const string checkSql = @"
            SELECT 1 FROM dbo.Admin WHERE UPPER(AdminID) = @Id
            UNION
            SELECT 1 FROM dbo.SystemAdmin WHERE UPPER(SystemAdminID) = @Id;";
        await using (var checkCmd = new SqlCommand(checkSql, conn))
        {
            checkCmd.Parameters.AddWithValue("@Id", adminId);
            if (await checkCmd.ExecuteScalarAsync(cancellationToken) is not null)
            {
                throw new InvalidOperationException($"An administrator account with ID '{adminId}' already exists.");
            }
        }

        string hashedPassword = BCrypt.Net.BCrypt.HashPassword(temporaryPassword);

        // Attempt creation via sp_CreateAdmin stored procedure with robust SQL fallback
        try
        {
            await using var spCmd = new SqlCommand("dbo.sp_CreateAdmin", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            spCmd.Parameters.AddWithValue("@AdminID", adminId);
            spCmd.Parameters.AddWithValue("@AdminRole", normalizedRole);
            spCmd.Parameters.AddWithValue("@AdminFirstName", dto.FirstName.Trim());
            spCmd.Parameters.AddWithValue("@AdminLastName", dto.LastName.Trim());
            spCmd.Parameters.AddWithValue("@AdminMiddleName", string.IsNullOrWhiteSpace(dto.MiddleName) ? DBNull.Value : dto.MiddleName.Trim());
            spCmd.Parameters.AddWithValue("@AdminEmail", dto.Email.Trim());
            spCmd.Parameters.AddWithValue("@AdminPassword", hashedPassword);
            spCmd.Parameters.AddWithValue("@RequestingUserID", requestingUserId.Trim());

            await spCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException)
        {
            // Direct SQL fallback: System Admin into dbo.SystemAdmin, Admin into dbo.Admin
            const string fallbackSql = @"
                IF (@AdminRole = 'SystemAdmin' OR @AdminID LIKE 'SADM-%')
                BEGIN
                    IF OBJECT_ID(N'dbo.SystemAdmin', N'U') IS NOT NULL
                    BEGIN
                        IF NOT EXISTS (SELECT 1 FROM dbo.SystemAdmin WHERE SystemAdminID = @AdminID)
                        BEGIN
                            INSERT INTO dbo.SystemAdmin (SystemAdminID, FirstName, LastName, MiddleName, PasswordHash, CreatedAt)
                            VALUES (@AdminID, @AdminFirstName, @AdminLastName, @AdminMiddleName, @AdminPassword, SYSUTCDATETIME());
                        END
                    END
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.Admin (AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, Email, AdminPassword, isFirstLogin)
                    VALUES (@AdminID, @AdminRole, @AdminFirstName, @AdminLastName, @AdminMiddleName, @AdminEmail, @AdminPassword, 1);
                END";

            await using var fbCmd = new SqlCommand(fallbackSql, conn);
            fbCmd.Parameters.AddWithValue("@AdminID", adminId);
            fbCmd.Parameters.AddWithValue("@AdminRole", normalizedRole);
            fbCmd.Parameters.AddWithValue("@AdminFirstName", dto.FirstName.Trim());
            fbCmd.Parameters.AddWithValue("@AdminLastName", dto.LastName.Trim());
            fbCmd.Parameters.AddWithValue("@AdminMiddleName", string.IsNullOrWhiteSpace(dto.MiddleName) ? DBNull.Value : dto.MiddleName.Trim());
            fbCmd.Parameters.AddWithValue("@AdminEmail", dto.Email.Trim());
            fbCmd.Parameters.AddWithValue("@AdminPassword", hashedPassword);

            await fbCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Record History Log
        await LogHistoryAsync(
            actionType: "INSERT",
            entityName: "Admin",
            entityId: adminId,
            details: $"Created admin account {adminId} (Role: {normalizedRole})",
            actorType: "SystemAdmin",
            actorId: requestingUserId,
            cancellationToken: cancellationToken);

        var fullName = string.Join(" ", new[] { dto.FirstName, dto.MiddleName, dto.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

        // Trigger welcome email dispatch with ID, temporary password and onboarding details
        try
        {
            await _emailService.SendAdminWelcomeEmailAsync(
                toEmail: dto.Email.Trim(),
                adminName: fullName,
                adminId: adminId,
                temporaryPassword: temporaryPassword,
                role: targetIsSysAdmin ? "System Admin" : "Admin",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch admin welcome email to {Email}. Admin account was preserved.", dto.Email);
        }

        return new AdminDto(
            Id: adminId,
            FirstName: dto.FirstName.Trim(),
            LastName: dto.LastName.Trim(),
            MiddleName: dto.MiddleName?.Trim(),
            FullName: fullName,
            Email: dto.Email.Trim(),
            Role: targetIsSysAdmin ? "System Admin" : "Admin",
            IsSuperAdmin: targetIsSysAdmin,
            IsFirstLogin: true,
            Status: "Active");
    }

    public async Task<AdminDto> UpdateAdminAsync(
        string id,
        UpdateAdminDto dto,
        string requestingUserId,
        bool isSystemAdmin,
        CancellationToken cancellationToken = default)
    {
        // ── Strict Mutation Enforcement ──
        // Role "Admin": Cannot edit the data of ANY admin (read-only view for their permission level)
        if (!isSystemAdmin && !requestingUserId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Forbidden: Admin {UserId} attempted to update admin {TargetId}.", requestingUserId, id);
            throw new UnauthorizedAccessException("Permission Denied: Admins have read-only permissions and cannot edit administrator records.");
        }

        var targetAdmin = await GetAdminByIdAsync(id, cancellationToken);
        if (targetAdmin is null)
        {
            throw new KeyNotFoundException($"Admin account with ID '{id}' was not found.");
        }

        // Role "System Admin": Can update data for "Admin" users. Cannot edit the data of other "System Admin" users.
        if (targetAdmin.IsSuperAdmin)
        {
            if (!string.Equals(targetAdmin.Id, requestingUserId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Forbidden: System Admin {RequesterId} attempted to edit other System Admin {TargetId}.", requestingUserId, id);
                throw new UnauthorizedAccessException("Permission Denied: System Admins cannot edit the data of other System Admin users.");
            }
        }

        if (string.IsNullOrWhiteSpace(dto.FirstName))
            throw new ArgumentException("First Name is required.", nameof(dto.FirstName));
        if (string.IsNullOrWhiteSpace(dto.LastName))
            throw new ArgumentException("Last Name is required.", nameof(dto.LastName));
        if (string.IsNullOrWhiteSpace(dto.Email))
            throw new ArgumentException("Email is required.", nameof(dto.Email));

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        const string updateSql = @"
            UPDATE dbo.Admin
            SET AdminFirstName = @FirstName,
                AdminLastName  = @LastName,
                AdminMiddleName = @MiddleName,
                Email          = @Email
            WHERE UPPER(AdminID) = UPPER(@Id);

            IF OBJECT_ID(N'dbo.SystemAdmin', N'U') IS NOT NULL
            BEGIN
                UPDATE dbo.SystemAdmin
                SET FirstName  = @FirstName,
                    LastName   = @LastName,
                    MiddleName = @MiddleName
                WHERE UPPER(SystemAdminID) = UPPER(@Id);
            END";

        await using (var updateCmd = new SqlCommand(updateSql, conn))
        {
            updateCmd.Parameters.AddWithValue("@Id", id.Trim());
            updateCmd.Parameters.AddWithValue("@FirstName", dto.FirstName.Trim());
            updateCmd.Parameters.AddWithValue("@LastName", dto.LastName.Trim());
            updateCmd.Parameters.AddWithValue("@MiddleName", string.IsNullOrWhiteSpace(dto.MiddleName) ? DBNull.Value : dto.MiddleName.Trim());
            updateCmd.Parameters.AddWithValue("@Email", dto.Email.Trim());

            await updateCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await LogHistoryAsync(
            actionType: "UPDATE",
            entityName: "Admin",
            entityId: id.Trim().ToUpperInvariant(),
            details: $"Updated admin record for {id}",
            actorType: "SystemAdmin",
            actorId: requestingUserId,
            cancellationToken: cancellationToken);

        var updatedFullName = string.Join(" ", new[] { dto.FirstName, dto.MiddleName, dto.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return targetAdmin with
        {
            FirstName = dto.FirstName.Trim(),
            LastName = dto.LastName.Trim(),
            MiddleName = dto.MiddleName?.Trim(),
            FullName = updatedFullName,
            Email = dto.Email.Trim()
        };
    }

    public async Task<bool> DeleteAdminAsync(
        string id,
        string requestingUserId,
        bool isSystemAdmin,
        CancellationToken cancellationToken = default)
    {
        if (!isSystemAdmin && !requestingUserId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Permission Denied: Only a SystemAdmin is authorized to delete admin accounts.");
        }

        var targetAdmin = await GetAdminByIdAsync(id, cancellationToken);
        if (targetAdmin is null) return false;

        if (targetAdmin.IsSuperAdmin)
        {
            throw new InvalidOperationException("System Admin accounts cannot be deleted.");
        }

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        const string deleteSql = "DELETE FROM dbo.Admin WHERE UPPER(AdminID) = UPPER(@Id);";
        await using var cmd = new SqlCommand(deleteSql, conn);
        cmd.Parameters.AddWithValue("@Id", id.Trim());
        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);

        if (rows > 0)
        {
            await LogHistoryAsync(
                actionType: "DELETE",
                entityName: "Admin",
                entityId: id.Trim().ToUpperInvariant(),
                details: $"Deleted admin account {id}",
                actorType: "SystemAdmin",
                actorId: requestingUserId,
                cancellationToken: cancellationToken);
            return true;
        }

        return false;
    }

    private async Task LogHistoryAsync(
        string actionType,
        string entityName,
        string entityId,
        string details,
        string actorType,
        string actorId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);

            const string logSql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (
                        ActionType, EntityName, EntityID, ActionDetails, ChangedByType, ChangedByAdminID
                    )
                    VALUES (
                        @ActionType, @EntityName, @EntityID, @ActionDetails, @ChangedByType, @ChangedByAdminID
                    );
                END";

            await using var cmd = new SqlCommand(logSql, conn);
            cmd.Parameters.AddWithValue("@ActionType", actionType);
            cmd.Parameters.AddWithValue("@EntityName", entityName);
            cmd.Parameters.AddWithValue("@EntityID", entityId);
            cmd.Parameters.AddWithValue("@ActionDetails", details);
            cmd.Parameters.AddWithValue("@ChangedByType", actorType);
            cmd.Parameters.AddWithValue("@ChangedByAdminID", actorId);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log history for {EntityName} {EntityId}", entityName, entityId);
        }
    }
}
