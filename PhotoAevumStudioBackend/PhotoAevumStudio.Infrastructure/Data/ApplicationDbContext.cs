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
    public DbSet<StudioService> Services => Set<StudioService>();
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<EmployeeAssignment> EmployeeAssignments => Set<EmployeeAssignment>();
    public DbSet<EquipmentAssignment> EquipmentAssignments => Set<EquipmentAssignment>();

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
            
            e.HasOne(p => p.Booking)
             .WithMany()
             .HasForeignKey(p => p.BookingId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ── ApplicationUser ──────────────────────────────────────────────────
        builder.Entity<ApplicationUser>(e =>
        {
            e.Ignore(u => u.isFirstLogin);
            e.Ignore(u => u.IsFirstLogin);
            e.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
            e.Property(u => u.LastName).HasMaxLength(100).IsRequired();
            e.Property(u => u.MiddleName).HasMaxLength(100);
            e.Property(u => u.StudioId).HasMaxLength(30);
            e.HasIndex(u => u.StudioId).IsUnique().HasFilter("[StudioId] IS NOT NULL");
        });

        // ── StudioService ────────────────────────────────────────────────────
        builder.Entity<StudioService>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).HasMaxLength(100).IsRequired();
            e.Property(s => s.Price).HasColumnType("decimal(18,2)");
        });

        // ── Employee ─────────────────────────────────────────────────────────
        builder.Entity<Employee>(e =>
        {
            e.HasKey(emp => emp.Id);
            e.HasOne(emp => emp.IdentityUser)
             .WithOne()
             .HasForeignKey<Employee>(emp => emp.IdentityUserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Equipment ────────────────────────────────────────────────────────
        builder.Entity<Equipment>(e =>
        {
            e.HasKey(eq => eq.Id);
            e.Property(eq => eq.Name).HasMaxLength(100).IsRequired();
            e.Property(eq => eq.SerialNumber).HasMaxLength(100).IsRequired();
            
            e.HasOne(eq => eq.AssignedEmployee)
             .WithMany(emp => emp.AssignedEquipment)
             .HasForeignKey(eq => eq.AssignedEmployeeId)
             .OnDelete(DeleteBehavior.SetNull);
        });

                        // Prevent Double-Booking Staff
        builder.Entity<EmployeeAssignment>(e =>
        {
            e.HasIndex(ea => new { ea.EmployeeId, ea.ScheduleDate, ea.ScheduleTime }).IsUnique();
            e.HasOne(ea => ea.Employee)
             .WithMany()
             .HasForeignKey(ea => ea.EmployeeId)
             .OnDelete(DeleteBehavior.NoAction);
             
            e.HasOne(ea => ea.Booking)
             .WithMany(b => b.EmployeeAssignments)
             .HasForeignKey(ea => ea.BookingId)
             .OnDelete(DeleteBehavior.Cascade);
        });

                // Prevent Double-Booking Gear
        builder.Entity<EquipmentAssignment>(e =>
        {
            e.HasIndex(eq => new { eq.EquipmentId, eq.ScheduleDate, eq.ScheduleTime }).IsUnique();
            e.HasOne(ea => ea.Equipment)
             .WithMany()
             .HasForeignKey(ea => ea.EquipmentId)
             .OnDelete(DeleteBehavior.NoAction);
             
            e.HasOne(ea => ea.Booking)
             .WithMany(b => b.EquipmentAssignments)
             .HasForeignKey(ea => ea.BookingId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}




