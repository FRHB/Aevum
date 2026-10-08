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
        if (await userManager.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new ApplicationUser
            {
                UserName    = adminEmail,
                Email       = adminEmail,
                FirstName   = "Studio",
                LastName    = "Admin",
                StudioId    = "PA-ADMIN",
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(admin, adminPassword);
            if (result.Succeeded)
                await userManager.AddToRoleAsync(admin, AppRoles.Admin);
        }
    }
}
