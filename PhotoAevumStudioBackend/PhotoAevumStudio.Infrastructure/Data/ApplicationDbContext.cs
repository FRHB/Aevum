using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Entities;

namespace PhotoAevumStudio.Infrastructure.Data;

/// <summary>
/// Central EF Core DbContext.  Inherits from IdentityDbContext so that
/// ASP.NET Core Identity tables (AspNetUsers, AspNetRoles, etc.) are
/// managed alongside our own domain tables.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    // ── Domain tables ────────────────────────────────────────────────────────
    public DbSet<Photo> Photos => Set<Photo>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // Must call base — creates Identity tables

        // ── Photos ───────────────────────────────────────────────────────────
        builder.Entity<Photo>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Title).HasMaxLength(200).IsRequired();
            e.Property(p => p.Url).HasMaxLength(2000).IsRequired();
            e.Property(p => p.UploadedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // ── ApplicationUser ──────────────────────────────────────────────────
        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
            e.Property(u => u.LastName).HasMaxLength(100).IsRequired();
            e.Property(u => u.MiddleName).HasMaxLength(100);
            e.Property(u => u.StudioId).HasMaxLength(30);
            e.HasIndex(u => u.StudioId).IsUnique().HasFilter("[StudioId] IS NOT NULL");
        });
    }
}