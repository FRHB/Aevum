using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Entities;

namespace PhotoAevumStudio.Infrastructure.Data;

/// <summary>
/// Seeds application roles, a default Admin account, and baseline studio data.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        string adminEmail,
        string adminPassword)
    {
        // ── Seed roles ───────────────────────────────────────────────────────
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // ── Seed default Admin account ────────────────────────────────────────
        var existingAdmin = await userManager.FindByEmailAsync(adminEmail);
        if (existingAdmin is null)
        {
            var admin = new ApplicationUser
            {
                UserName    = adminEmail,
                Email       = adminEmail,
                FirstName   = "Axiom",
                LastName    = "Bit",
                StudioId    = "SADM-001",
                EmailConfirmed = true,
                MustChangePassword = false
            };

            var result = await userManager.CreateAsync(admin, adminPassword ?? "Axiom@2026");
            if (!result.Succeeded)
                throw new InvalidOperationException(
                   "Failed to seed admin: " + string.Join("; ", result.Errors.Select(e => e.Description)));
            
            await userManager.AddToRoleAsync(admin, AppRoles.Admin);
        }
        else if (existingAdmin.StudioId != "SADM-001")
        {
            existingAdmin.StudioId = "SADM-001";
            existingAdmin.FirstName = "Axiom";
            existingAdmin.LastName = "Bit";
            existingAdmin.PasswordHash = "Axiom@2026";
            await userManager.UpdateAsync(existingAdmin);
        }

        // ── Seed default Services ────────────────────────────────────────────
        if (!await db.Services.AnyAsync())
        {
            db.Services.AddRange(
                new StudioService
                {
                    Name = "Portrait Session",
                    Price = 1500.00m,
                    Duration = 60,
                    Description = "Standard portrait and headshot photography session.",
                    IsActive = true
                },
                new StudioService
                {
                    Name = "Event Coverage",
                    Price = 5000.00m,
                    Duration = 240,
                    Description = "Comprehensive event coverage for weddings, corporate, or milestones.",
                    IsActive = true
                },
                new StudioService
                {
                    Name = "Product Photography",
                    Price = 3000.00m,
                    Duration = 120,
                    Description = "High-definition product catalog and editorial shoot.",
                    IsActive = true
                }
            );
            await db.SaveChangesAsync();
        }

        // ── Seed default Equipment ───────────────────────────────────────────
        if (!await db.Equipment.AnyAsync())
        {
            db.Equipment.AddRange(
                new Equipment { Name = "Sony Alpha A7 IV", SerialNumber = "SN-A7IV-001", Status = "Available" },
                new Equipment { Name = "Canon EOS R5", SerialNumber = "SN-R5-002", Status = "Available" },
                new Equipment { Name = "Godox Studio Strobe Kit", SerialNumber = "SN-GDX-003", Status = "Available" },
                new Equipment { Name = "DJI Ronin RS3 Gimbal", SerialNumber = "SN-DJI-004", Status = "Available" }
            );
            await db.SaveChangesAsync();
        }

        // ── Seed default Employee account if none exist ───────────────────────
        if (!await db.Employees.AnyAsync())
        {
            var empEmail = "photographer@photoaevum.studio";
            var empUser = await userManager.FindByEmailAsync(empEmail);
            if (empUser is null)
            {
                empUser = new ApplicationUser
                {
                    UserName = empEmail,
                    Email = empEmail,
                    FirstName = "Ana",
                    LastName = "Cruz",
                    StudioId = "EMP-001",
                    EmailConfirmed = true,
                    MustChangePassword = false
                };
                var empRes = await userManager.CreateAsync(empUser, "Employee@123");
                if (empRes.Succeeded)
                {
                    await userManager.AddToRoleAsync(empUser, AppRoles.Employee);
                    db.Employees.Add(new Employee
                    {
                        IdentityUserId = empUser.Id,
                        Status = "Active",
                        Specializations = new List<string> { "Portrait", "Event", "Studio" }
                    });
                    await db.SaveChangesAsync();
                }
            }
        }
    }

    /// <summary>
    /// Reconciles __EFMigrationsHistory if database tables already exist (e.g. from an imported SQL script or prior restore),
    /// preventing SqlException (e.g. "There is already an object named 'Services' in the database") on machines with unrecorded migrations.
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

            -- 1. AddIdentityAndPhotos (Identity tables and Photos)
            IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261008080923_AddIdentityAndPhotos')
            BEGIN
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20261008080923_AddIdentityAndPhotos', '10.0.12');
            END;

            -- 2. ExpandDomainModels (Services, Employees, Equipment)
            IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261008081827_ExpandDomainModels')
            BEGIN
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20261008081827_ExpandDomainModels', '10.0.12');
            END;

            -- 3. UpdateBookingKeysAndDate_FixCascade (Bookings, AuditLogs, Assignments)
            IF OBJECT_ID(N'dbo.Bookings', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261010090001_UpdateBookingKeysAndDate_FixCascade')
            BEGIN
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20261010090001_UpdateBookingKeysAndDate_FixCascade', '10.0.12');
            END;

            -- 4. AddAdminsView (Admins view)
            IF OBJECT_ID(N'dbo.Admins', N'V') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261010100000_AddAdminsView')
            BEGIN
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20261010100000_AddAdminsView', '10.0.12');
            END;

            -- 5. AddMustChangePasswordToApplicationUser (MustChangePassword column)
            IF COL_LENGTH(N'dbo.AspNetUsers', N'MustChangePassword') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261013034024_AddMustChangePasswordToApplicationUser')
            BEGIN
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20261013034024_AddMustChangePasswordToApplicationUser', '10.0.12');
            END;
        ";

        try
        {
            await db.Database.ExecuteSqlRawAsync(reconcileScript);
        }
        catch
        {
            // If the database is brand new, ExecuteSqlRawAsync may safely pass; MigrateAsync will create everything.
        }
    }

    /// <summary>
    /// Ensures that the stored procedure dbo.usp_CreateEventAppointment is created or updated in the database.
    /// </summary>
    public static async Task EnsureStoredProceduresAsync(ApplicationDbContext db)
    {
        if (!await db.Database.CanConnectAsync())
            return;

        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            await using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT OBJECT_ID(N'dbo.usp_CreateEventAppointment', N'P');";
            var exists = await checkCmd.ExecuteScalarAsync();

            if (exists is null || exists is DBNull)
            {
                var candidatePaths = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "Data", "Scripts", "usp_CreateEventAppointment.sql"),
                    Path.Combine(AppContext.BaseDirectory, "usp_CreateEventAppointment.sql"),
                    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PhotoAevumStudio.Infrastructure", "Data", "Scripts", "usp_CreateEventAppointment.sql"))
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
                    var cleanedScript = System.Text.RegularExpressions.Regex.Replace(
                        script,
                        @"^\s*GO\s*$",
                        "",
                        System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    await using var createCmd = conn.CreateCommand();
                    createCmd.CommandText = cleanedScript;
                    await createCmd.ExecuteNonQueryAsync();
                }
            }
        }
        catch
        {
            // Ignored if connection fails or permission restricted
        }
    }
}
