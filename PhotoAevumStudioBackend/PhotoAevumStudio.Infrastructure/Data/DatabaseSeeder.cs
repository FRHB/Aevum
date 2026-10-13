using Microsoft.EntityFrameworkCore;
using System.Data;

namespace PhotoAevumStudio.Infrastructure.Data;

/// <summary>
/// Database maintenance and stored procedure management for local development.
/// All hardcoded seed users and passwords have been removed to enforce strict reliance
/// on the local SQL Server database.
/// </summary>
public static class DatabaseSeeder
{
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
    /// Ensures that all required stored procedures from PhotoAevumStudioStoredProcedures.sql exist in the database.
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
            setCmd.CommandText = "SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;";
            await setCmd.ExecuteNonQueryAsync();

            await using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = @"
                SELECT COUNT(1) 
                FROM sys.sql_modules 
                WHERE object_id IN (
                    OBJECT_ID(N'dbo.sp_CreateCustomer', N'P'),
                    OBJECT_ID(N'dbo.sp_CreateEmployee', N'P'),
                    OBJECT_ID(N'dbo.sp_CreateEventAppointment', N'P'),
                    OBJECT_ID(N'dbo.sp_AddEventLineItem', N'P'),
                    OBJECT_ID(N'dbo.sp_AssignEmployeeToEvent', N'P'),
                    OBJECT_ID(N'dbo.sp_AddEquipment', N'P'),
                    OBJECT_ID(N'dbo.sp_AssignEquipmentToEvent', N'P'),
                    OBJECT_ID(N'dbo.sp_UpdateEventStatus', N'P'),
                    OBJECT_ID(N'dbo.sp_CompleteEmployeeFirstLogin', N'P'),
                    OBJECT_ID(N'dbo.sp_CompleteAdminFirstLogin', N'P')
                )
                AND uses_quoted_identifier = 1;";
            var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

            if (count < 10)
            {
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
        }
        catch
        {
            // Stored procedure initialization errors are non-fatal on startup
        }
    }
}
