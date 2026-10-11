using Microsoft.AspNetCore.Identity;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Entities;

namespace PhotoAevumStudio.Infrastructure.Data;

/// <summary>
/// Seeds the three application roles and a default Admin account on
/// first startup.  Called from Program.cs after migrations have run.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
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
                MustChangePassword = true,
                existingAdmin.MustChangePassword = true;
            };

            var result = await userManager.CreateAsync(admin, "Axiom@2026");
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
            existingAdmin.PasswordHash = "Axiom@2026"; // using Plaintext hasher
            await userManager.UpdateAsync(existingAdmin);
        }
    }
}


