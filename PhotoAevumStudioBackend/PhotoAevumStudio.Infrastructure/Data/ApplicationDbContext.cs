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
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<EmployeeAssignment> EmployeeAssignments => Set<EmployeeAssignment>();
    public DbSet<EquipmentAssignment> EquipmentAssignments => Set<EquipmentAssignment>();
    public DbSet<EmployeeLeave> EmployeeLeaves => Set<EmployeeLeave>();
    public DbSet<EmployeeTimesheet> EmployeeTimesheets => Set<EmployeeTimesheet>();
    public DbSet<EmployeeSpecialization> EmployeeSpecializations => Set<EmployeeSpecialization>();
    public DbSet<EmployeeSpecialization> Specializations => EmployeeSpecializations;

    public DbSet<EventCategory> EventCategories => Set<EventCategory>();
    public DbSet<EventCategory> ServiceCategories => EventCategories;
    public DbSet<ServiceCategoryJoin> ServiceCategoryJoins => Set<ServiceCategoryJoin>();

    public DbSet<PortfolioCategoryEntity> PortfolioCategories => Set<PortfolioCategoryEntity>();
    public DbSet<PortfolioEntity> Portfolios => Set<PortfolioEntity>();
    public DbSet<PortfolioImage> PortfolioImages => Set<PortfolioImage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // Must call base — creates Identity tables

        builder.Ignore<Specialization>();

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
            e.ToTable("Employees");
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

        // ── EmployeeSpecialization ───────────────────────────────────────────
        builder.Entity<EmployeeSpecialization>(e =>
        {
            e.ToTable("EmployeeSpecialization", "dbo");
            e.HasKey(s => s.SpecializationID);
            e.Property(s => s.SpecializationID).HasMaxLength(30).IsRequired();
            e.Property(s => s.SpecializationName).HasMaxLength(255).IsRequired();
            e.Property(s => s.Specialization).HasMaxLength(255).IsRequired();
            e.Property(s => s.SpecializationDescription).HasMaxLength(1000);
            e.Property(s => s.Description).HasMaxLength(1000);
            e.Property(s => s.ColorTag).HasMaxLength(50).IsRequired().HasDefaultValue("Blue");
            e.Property(s => s.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // ── EventCategory / ServiceCategory ──────────────────────────────────
        builder.Entity<EventCategory>(e =>
        {
            e.ToTable("ServiceCategory", "dbo");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasColumnName("ServiceCategoryID").HasMaxLength(30).IsRequired();
            e.Property(c => c.Name).HasColumnName("ServiceCategoryName").HasMaxLength(255).IsRequired();
            e.Property(c => c.Description).HasColumnName("ServiceCategoryDescription").HasMaxLength(1000).IsRequired();

            e.HasMany(c => c.Associations)
             .WithOne(a => a.Category)
             .HasForeignKey(a => a.CategoryId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ServiceCategoryJoin / Category Associations ───────────────────────
        builder.Entity<ServiceCategoryJoin>(e =>
        {
            e.ToTable("ServiceCategoryJoins", "dbo");
            e.HasKey(j => j.Id);
            e.Property(j => j.Id).HasColumnName("ServiceCategoryJoinID").HasMaxLength(30).IsRequired();
            e.Property(j => j.CategoryId).HasColumnName("ServiceCategoryID").HasMaxLength(30).IsRequired();
            e.Property(j => j.ServiceId).HasColumnName("ServicesID").HasMaxLength(30);
            e.Property(j => j.AdditionalServiceId).HasColumnName("AdditionalServicesID").HasMaxLength(30);
            e.Property(j => j.ComboPackageId).HasColumnName("ComboPackageID").HasMaxLength(30);

            e.HasOne(j => j.Category)
             .WithMany(c => c.Associations)
             .HasForeignKey(j => j.CategoryId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Ignore<EventCategoryAssociation>();

        // ── Booking financial configurations ─────────────────────────────────
        builder.Entity<Booking>(e =>
        {
            e.Property(b => b.TotalAmount).HasColumnType("decimal(18,2)");
            e.Property(b => b.DepositAmount).HasColumnType("decimal(18,2)");
            e.Property(b => b.RemainingBalance).HasColumnType("decimal(18,2)");
            e.Property(b => b.PaymentStatus).HasConversion<string>().HasMaxLength(30);
        });

        // ── Payments & Two-Tier Clearing Tracking ─────────────────────────────
        builder.Entity<Payment>(e =>
        {
            e.ToTable("Payments", "dbo");
            e.HasKey(p => p.Id);
            e.Property(p => p.TotalAmount).HasColumnType("decimal(18,2)");
            e.Property(p => p.DepositAmount).HasColumnType("decimal(18,2)");
            e.Property(p => p.RemainingBalance).HasColumnType("decimal(18,2)");
            e.Property(p => p.Amount).HasColumnType("decimal(18,2)");
            e.Property(p => p.PaymentType).HasConversion<string>().HasMaxLength(50);
            e.Property(p => p.CustomerPaymentStatus).HasConversion<string>().HasMaxLength(30);
            e.Property(p => p.EmployeeRemittanceStatus).HasConversion<string>().HasMaxLength(30);
            e.Property(p => p.Channel).HasConversion<string>().HasMaxLength(30);
            e.Property(p => p.EventAppointmentId).HasMaxLength(50);
            e.Property(p => p.EmployeeId).HasMaxLength(50);
            e.Property(p => p.EmployeeName).HasMaxLength(150);
            e.Property(p => p.AdminApproverId).HasMaxLength(50);
            e.Property(p => p.AdminApproverName).HasMaxLength(150);
            e.Property(p => p.ExternalReference).HasMaxLength(100);
            e.Property(p => p.Notes).HasMaxLength(1000);
            e.Property(p => p.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            e.HasOne(p => p.Booking)
             .WithMany(b => b.Payments)
             .HasForeignKey(p => p.BookingId)
             .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
