using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Auth;
using System.Data;

namespace PhotoAevumStudio.Infrastructure.Data;

/// <summary>
/// Database maintenance, role seeding, and stored procedure management.
/// All hardcoded seed users, services, and equipment have been removed to enforce strict reliance
/// on the SQL Server database.
/// </summary>
public static class DatabaseSeeder
{
    /// <summary>
    /// Seeds the baseline application roles (Admin, Employee, Customer, SystemAdmin) into AspNetRoles.
    /// Does not seed any mock users or domain entities.
    /// </summary>
    public static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    /// <summary>
    /// Reconciles __EFMigrationsHistory if database tables already exist (e.g. from an imported SQL script or prior restore),
    /// preventing SqlException on machines with pre-existing schema.
    /// </summary>
    public static async Task ReconcileMigrationHistoryAsync(ApplicationDbContext db)
    {
        if (!await db.Database.CanConnectAsync())
            return;

        const string reconcileScript = @"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = '__EFMigrationsHistory')
            BEGIN
                CREATE TABLE [__EFMigrationsHistory] (
                    [MigrationId] nvarchar(150) NOT NULL,
                    [ProductVersion] nvarchar(32) NOT NULL,
                    CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
                );
            END;

            IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261008080923_AddIdentityAndPhotos')
            BEGIN
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20261008080923_AddIdentityAndPhotos', '10.0.12');
            END;

            IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261008081827_ExpandDomainModels')
            BEGIN
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20261008081827_ExpandDomainModels', '10.0.12');
            END;

            IF (OBJECT_ID(N'dbo.Bookings', N'U') IS NOT NULL OR OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL)
               AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261010090001_UpdateBookingKeysAndDate_FixCascade')
            BEGIN
                IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[AuditLogs] (
                        [Id] int NOT NULL IDENTITY,
                        [UserId] nvarchar(max) NOT NULL,
                        [UserEmail] nvarchar(max) NOT NULL,
                        [Action] nvarchar(max) NOT NULL,
                        [Resource] nvarchar(max) NOT NULL,
                        [ResourceId] nvarchar(max) NULL,
                        [Timestamp] datetime2 NOT NULL,
                        [Details] nvarchar(max) NULL,
                        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
                    );
                END;

                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20261010090001_UpdateBookingKeysAndDate_FixCascade', '10.0.12');
            END;

            IF OBJECT_ID(N'dbo.Admins', N'V') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261010100000_AddAdminsView')
            BEGIN
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20261010100000_AddAdminsView', '10.0.12');
            END;

            IF COL_LENGTH(N'dbo.AspNetUsers', N'MustChangePassword') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261013034024_AddMustChangePasswordToApplicationUser')
            BEGIN
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20261013034024_AddMustChangePasswordToApplicationUser', '10.0.12');
            END;

            IF COL_LENGTH(N'dbo.EmployeeSpecialization', N'SpecializationDescription') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261026081300_AddSpecializationMissingColumns')
            BEGIN
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20261026081300_AddSpecializationMissingColumns', '10.0.12');
            END;
        ";

        try
        {
            await db.Database.ExecuteSqlRawAsync(reconcileScript);
        }
        catch
        {
            // Best effort
        }
    }

    /// <summary>
    /// Ensures that all required schema updates and stored procedures exist in the database.
    /// </summary>
    public static async Task EnsureStoredProceduresAsync(ApplicationDbContext db)
    {
        if (!await db.Database.CanConnectAsync())
            return;

        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var setCmd = conn.CreateCommand();
            setCmd.CommandText = @"SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
                IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Admin', N'isFirstLogin') IS NULL
                    BEGIN
                        ALTER TABLE dbo.Admin ADD isFirstLogin bit NOT NULL CONSTRAINT DF_Admin_isFirstLogin DEFAULT 1;
                    END;
                    IF COL_LENGTH(N'dbo.Admin', N'Email') IS NULL
                    BEGIN
                        ALTER TABLE dbo.Admin ADD Email nvarchar(255) NULL;
                    END;
                END;

                IF OBJECT_ID(N'dbo.SystemAdmin', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.SystemAdmin (
                        SystemAdminID nvarchar(30) NOT NULL PRIMARY KEY,
                        FirstName nvarchar(255) NOT NULL,
                        LastName nvarchar(255) NOT NULL,
                        MiddleName nvarchar(255) NULL,
                        PasswordHash nvarchar(255) NOT NULL,
                        CreatedAt datetime2 NOT NULL CONSTRAINT DF_SystemAdmin_CreatedAt DEFAULT SYSUTCDATETIME()
                    );
                END;

                IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Employees', N'isFirstLogin') IS NULL
                    BEGIN
                        ALTER TABLE dbo.Employees ADD isFirstLogin bit NOT NULL CONSTRAINT DF_Employees_isFirstLogin DEFAULT 1;
                    END;
                    IF COL_LENGTH(N'dbo.Employees', N'DaysOff') IS NULL
                    BEGIN
                        ALTER TABLE dbo.Employees ADD DaysOff nvarchar(50) NOT NULL CONSTRAINT DF_Employees_DaysOff DEFAULT 'Sunday';
                    END;
                    IF COL_LENGTH(N'dbo.Employees', N'Id') IS NULL
                    BEGIN
                        ALTER TABLE dbo.Employees ADD Id int IDENTITY(1,1);
                    END;
                    IF COL_LENGTH(N'dbo.Employees', N'IdentityUserId') IS NULL
                    BEGIN
                        ALTER TABLE dbo.Employees ADD IdentityUserId nvarchar(450) NULL;
                    END;
                    IF COL_LENGTH(N'dbo.Employees', N'Status') IS NULL
                    BEGIN
                        ALTER TABLE dbo.Employees ADD Status nvarchar(50) NOT NULL CONSTRAINT DF_Employees_EF_Status DEFAULT 'Active';
                    END;
                    IF COL_LENGTH(N'dbo.Employees', N'Specializations') IS NULL
                    BEGIN
                        ALTER TABLE dbo.Employees ADD Specializations nvarchar(max) NOT NULL CONSTRAINT DF_Employees_EF_Specializations DEFAULT '[]';
                    END;
                    EXEC(N'UPDATE dbo.Employees SET IdentityUserId = EmployeeID WHERE IdentityUserId IS NULL; UPDATE dbo.Employees SET Status = EmployeeStatus WHERE Status IS NULL;');
                END;

                IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NULL AND OBJECT_ID(N'dbo.EmployeeLeave', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.EmployeeLeaves (
                        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        LeaveId nvarchar(50) NOT NULL,
                        EmployeeStudioId nvarchar(50) NOT NULL,
                        EmployeeName nvarchar(255) NOT NULL,
                        LeaveType nvarchar(50) NOT NULL,
                        StartDate date NOT NULL,
                        EndDate date NOT NULL,
                        Reason nvarchar(500) NULL,
                        Status nvarchar(20) NOT NULL CONSTRAINT DF_Leave_Status DEFAULT 'Pending',
                        ApprovedBy nvarchar(255) NULL,
                        ApprovedAt datetime2 NULL,
                        RejectionReason nvarchar(500) NULL,
                        CreatedAt datetime2 NOT NULL CONSTRAINT DF_Leave_CreatedAt DEFAULT SYSUTCDATETIME()
                    );
                END;

                IF OBJECT_ID(N'dbo.EmployeeTimesheets', N'U') IS NULL AND OBJECT_ID(N'dbo.EmployeeTimesheet', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.EmployeeTimesheets (
                        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        TimesheetId nvarchar(50) NOT NULL,
                        EmployeeStudioId nvarchar(50) NOT NULL,
                        EmployeeName nvarchar(255) NOT NULL,
                        WorkDate date NOT NULL,
                        ClockIn datetime2 NOT NULL,
                        ClockOut datetime2 NULL,
                        TotalHours float NOT NULL CONSTRAINT DF_Timesheet_Hours DEFAULT 0.0,
                        Status nvarchar(20) NOT NULL CONSTRAINT DF_Timesheet_Status DEFAULT 'Open',
                        IsDayOffWork bit NOT NULL CONSTRAINT DF_Timesheet_DayOff DEFAULT 0,
                        Notes nvarchar(500) NULL,
                        AdjustedBy nvarchar(255) NULL,
                        AdjustmentReason nvarchar(500) NULL,
                        CreatedAt datetime2 NOT NULL CONSTRAINT DF_Timesheet_CreatedAt DEFAULT SYSUTCDATETIME()
                    );
                END;

                IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Services', N'ServiceImage') IS NULL
                        ALTER TABLE dbo.Services ADD ServiceImage varbinary(max) NULL;
                    IF COL_LENGTH(N'dbo.Services', N'ServiceImageMimeType') IS NULL
                        ALTER TABLE dbo.Services ADD ServiceImageMimeType nvarchar(100) NULL CONSTRAINT DF_Services_MimeType DEFAULT 'image/jpeg';
                    ALTER TABLE dbo.Services ALTER COLUMN ServicesDescription nvarchar(1000) NOT NULL;
                END;

                IF OBJECT_ID(N'dbo.ServiceCategory', N'U') IS NOT NULL
                BEGIN
                    ALTER TABLE dbo.ServiceCategory ALTER COLUMN ServiceCategoryDescription nvarchar(1000) NOT NULL;
                END;

                IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.AdditionalServices', N'AdditionalServiceImage') IS NULL
                        ALTER TABLE dbo.AdditionalServices ADD AdditionalServiceImage varbinary(max) NULL;
                    IF COL_LENGTH(N'dbo.AdditionalServices', N'AdditionalServiceImageMimeType') IS NULL
                        ALTER TABLE dbo.AdditionalServices ADD AdditionalServiceImageMimeType nvarchar(100) NULL CONSTRAINT DF_AdditionalServices_MimeType DEFAULT 'image/jpeg';
                    ALTER TABLE dbo.AdditionalServices ALTER COLUMN AdditionalServicesDescription nvarchar(1000) NULL;
                END;

                IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.ComboPackage', N'ComboPackageImage') IS NULL
                        ALTER TABLE dbo.ComboPackage ADD ComboPackageImage varbinary(max) NULL;
                    IF COL_LENGTH(N'dbo.ComboPackage', N'ComboPackageImageMimeType') IS NULL
                        ALTER TABLE dbo.ComboPackage ADD ComboPackageImageMimeType nvarchar(100) NULL CONSTRAINT DF_ComboPackage_MimeType DEFAULT 'image/jpeg';
                    IF COL_LENGTH(N'dbo.ComboPackage', N'ComboPackagePrice') IS NULL
                        ALTER TABLE dbo.ComboPackage ADD ComboPackagePrice decimal(18,2) NOT NULL CONSTRAINT DF_ComboPackage_Price DEFAULT 0.00;
                    ALTER TABLE dbo.ComboPackage ALTER COLUMN ComboPackageDescription nvarchar(1000) NOT NULL;
                END;

                IF OBJECT_ID(N'dbo.ComboPackageJoin', N'U') IS NOT NULL
                BEGIN
                    ALTER TABLE dbo.ComboPackageJoin ALTER COLUMN ServicesID nvarchar(30) NULL;
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_ComboPackageID')
                BEGIN
                    DECLARE @InitMaxVal INT = 0;
                    IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL
                    BEGIN
                        SELECT @InitMaxVal = ISNULL(MAX(TRY_CAST(SUBSTRING(ComboPackageID, 5, 20) AS INT)), 0)
                        FROM dbo.ComboPackage
                        WHERE ComboPackageID LIKE 'PKG-%';
                    END;
                    DECLARE @CreateSeqSql NVARCHAR(MAX) = N'CREATE SEQUENCE dbo.Seq_ComboPackageID AS INT START WITH ' 
                        + CAST((@InitMaxVal + 1) AS NVARCHAR(20)) + N' INCREMENT BY 1;';
                    EXEC sp_executesql @CreateSeqSql;
                END;

                IF OBJECT_ID(N'dbo.Equipment', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Equipment', N'EquipmentDescription') IS NULL
                        ALTER TABLE dbo.Equipment ADD EquipmentDescription nvarchar(1000) NULL;
                    IF COL_LENGTH(N'dbo.Equipment', N'ImagePath') IS NULL
                        ALTER TABLE dbo.Equipment ADD ImagePath nvarchar(500) NULL;
                    IF COL_LENGTH(N'dbo.Equipment', N'CreatedAt') IS NULL
                        ALTER TABLE dbo.Equipment ADD CreatedAt datetime2 NOT NULL CONSTRAINT DF_Equipment_CreatedAt DEFAULT SYSUTCDATETIME();
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'EquipmentIdSequence' AND schema_id = SCHEMA_ID('dbo'))
                BEGIN
                    DECLARE @InitEqpMaxVal INT = 0;
                    IF OBJECT_ID(N'dbo.Equipment', N'U') IS NOT NULL
                    BEGIN
                        SELECT @InitEqpMaxVal = ISNULL(MAX(TRY_CAST(SUBSTRING(EquipmentID, 5, 10) AS INT)), 0)
                        FROM dbo.Equipment
                        WHERE EquipmentID LIKE 'EQP-%';
                    END;
                    DECLARE @CreateEqpSeqSql NVARCHAR(MAX) = N'CREATE SEQUENCE dbo.EquipmentIdSequence AS INT START WITH ' 
                        + CAST((@InitEqpMaxVal + 1) AS NVARCHAR(20)) + N' INCREMENT BY 1 MINVALUE 1 NO CYCLE CACHE 10;';
                    EXEC sp_executesql @CreateEqpSeqSql;
                END;
            ";
            await setCmd.ExecuteNonQueryAsync();

            var candidatePaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "database", "PhotoAevumStudioStoredProcedures.sql"),
                Path.Combine(Directory.GetCurrentDirectory(), "database", "PhotoAevumStudioStoredProcedures.sql"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "database", "PhotoAevumStudioStoredProcedures.sql")),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "database", "PhotoAevumStudioStoredProcedures.sql")),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PhotoAevumStudioBackend", "database", "PhotoAevumStudioStoredProcedures.sql")),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "PhotoAevumStudioBackend", "database", "PhotoAevumStudioStoredProcedures.sql")),
                @"c:\Aevum\PhotoAevumStudioBackend\database\PhotoAevumStudioStoredProcedures.sql"
            };

            string? script = null;
            foreach (var p in candidatePaths)
            {
                if (File.Exists(p))
                {
                    script = await File.ReadAllTextAsync(p);
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(script))
            {
                var batches = System.Text.RegularExpressions.Regex.Split(
                    script,
                    @"^\s*GO\s*$",
                    System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                foreach (var batch in batches)
                {
                    var trimmed = batch.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SET QUOTED_IDENTIFIER ON;\nSET ANSI_NULLS ON;\n" + trimmed;
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
        catch
        {
            // Stored procedure initialization errors are non-fatal on startup
        }
    }
}
