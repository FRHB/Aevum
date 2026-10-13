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
}
