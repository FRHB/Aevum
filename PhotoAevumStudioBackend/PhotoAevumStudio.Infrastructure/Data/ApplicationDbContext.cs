using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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
    public DbSet<LeaveTypeDefinition> LeaveTypeDefinitions => Set<LeaveTypeDefinition>();
    public DbSet<LeaveBalance> LeaveBalances => Set<LeaveBalance>();
    public DbSet<EmployeeTimesheet> EmployeeTimesheets => Set<EmployeeTimesheet>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<EmployeeSpecialization> EmployeeSpecializations => Set<EmployeeSpecialization>();
    public DbSet<EmployeeSpecialization> Specializations => EmployeeSpecializations;

    public DbSet<EventCategory> EventCategories => Set<EventCategory>();
    public DbSet<EventCategory> ServiceCategories => EventCategories;
    public DbSet<ServiceCategoryJoin> ServiceCategoryJoins => Set<ServiceCategoryJoin>();

    public DbSet<PortfolioCategoryEntity> PortfolioCategories => Set<PortfolioCategoryEntity>();
    public DbSet<PortfolioEntity> Portfolios => Set<PortfolioEntity>();
    public DbSet<PortfolioImage> PortfolioImages => Set<PortfolioImage>();
    public DbSet<ArchiveIndex> ArchiveIndices => Set<ArchiveIndex>();
    public DbSet<CustomerDropbox> CustomerDropboxes => Set<CustomerDropbox>();
    public DbSet<StudioAnalyticsAction> StudioAnalyticsActions => Set<StudioAnalyticsAction>();
    public DbSet<TermsVersion> TermsVersions => Set<TermsVersion>();
    public DbSet<BookingContract> BookingContracts => Set<BookingContract>();
    public DbSet<BookingPayment> BookingPayments => Set<BookingPayment>();
    public DbSet<BookingReceipt> BookingReceipts => Set<BookingReceipt>();

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

        // ── Department ───────────────────────────────────────────────────────
        builder.Entity<Department>(e =>
        {
            e.ToTable("Departments", "dbo");
            e.HasKey(d => d.DepartmentID);
            e.Property(d => d.DepartmentID).HasMaxLength(30).IsRequired();
            e.Property(d => d.DepartmentName).HasMaxLength(100).IsRequired();
            e.Property(d => d.DepartmentDescription).HasMaxLength(500);
            e.Property(d => d.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // ── Employee ─────────────────────────────────────────────────────────
        builder.Entity<Employee>(e =>
        {
            e.ToTable("Employees");
            e.HasKey(emp => emp.Id);
            e.Property(emp => emp.Address).HasMaxLength(500);
            e.Property(emp => emp.BirthYear);
            e.Property(emp => emp.DepartmentID).HasMaxLength(30);
            e.Property(emp => emp.PayBasis).HasMaxLength(30).HasDefaultValue("CommissionPerEvent");
            e.Property(emp => emp.IsTitleIExempt).HasDefaultValue(true);
            e.HasOne(emp => emp.DepartmentEntity)
             .WithMany()
             .HasForeignKey(emp => emp.DepartmentID)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(emp => emp.IdentityUser)
             .WithOne()
             .HasForeignKey<Employee>(emp => emp.IdentityUserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<EmployeeLeave>(e =>
        {
            e.ToTable("EmployeeLeaves", "dbo");
            e.HasKey(l => l.Id);
            e.Property(l => l.CountableDays).HasColumnType("decimal(5,2)");
        });

        builder.Entity<LeaveTypeDefinition>(e =>
        {
            e.ToTable("LeaveTypes", "dbo");
            e.HasKey(lt => lt.LeaveTypeID);
            e.Property(lt => lt.Code).HasMaxLength(20).IsRequired();
            e.Property(lt => lt.Name).HasMaxLength(50).IsRequired();
            e.Property(lt => lt.DefaultAnnualCredits).HasColumnType("decimal(5,2)");
            e.Property(lt => lt.GenderRestriction).HasMaxLength(10);
        });

        builder.Entity<LeaveBalance>(e =>
        {
            e.ToTable("LeaveBalances", "dbo");
            e.HasKey(lb => lb.LeaveBalanceID);
            e.Property(lb => lb.EmployeeID).HasMaxLength(30).IsRequired();
            e.Property(lb => lb.TotalAllocated).HasColumnType("decimal(6,2)");
            e.Property(lb => lb.PendingCredits).HasColumnType("decimal(6,2)");
            e.Property(lb => lb.UsedCredits).HasColumnType("decimal(6,2)");
            e.Ignore(lb => lb.AvailableCredits);
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
            e.Property(s => s.DepartmentID).HasMaxLength(30);
            e.Property(s => s.DepartmentName).HasMaxLength(100);
            e.Property(s => s.ColorTag).HasMaxLength(50).IsRequired().HasDefaultValue("Blue");
            e.Property(s => s.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            e.HasOne(s => s.DepartmentEntity)
             .WithMany()
             .HasForeignKey(s => s.DepartmentID)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ── EventCategory / ServiceCategory ──────────────────────────────────
        builder.Entity<EventCategory>(e =>
        {
            e.ToTable("ServiceCategory", "dbo");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasColumnName("ServiceCategoryID").HasMaxLength(30).IsRequired();
            e.Property(c => c.Name).HasColumnName("ServiceCategoryName").HasMaxLength(255).IsRequired();
            e.Property(c => c.Description).HasColumnName("ServiceCategoryDescription").HasMaxLength(1000).IsRequired();
            e.Property(c => c.RateTypeId).HasColumnName("RateTypeID").HasMaxLength(30).IsRequired().HasDefaultValue("RT-0001");
            e.Property(c => c.RateTypeName).HasColumnName("RateTypeName").HasMaxLength(50).IsRequired().HasDefaultValue("Per Hour");

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

        // ── ArchiveIndex ──────────────────────────────────────────────────────
        builder.Entity<ArchiveIndex>(e =>
        {
            e.ToTable("ArchiveIndex", "dbo");
            e.HasKey(a => a.ArchiveId);
            e.Property(a => a.EntityName).HasMaxLength(128).IsRequired();
            e.Property(a => a.BucketName).HasMaxLength(100).IsRequired().HasDefaultValue("archive");
            e.Property(a => a.R2Key).HasMaxLength(500).IsRequired();
            e.Property(a => a.FileUri).HasMaxLength(1000).IsRequired();
            e.Property(a => a.RecordCount).HasDefaultValue(0);
            e.Property(a => a.ArchivedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // ── CustomerDropbox ──────────────────────────────────────────────────
        builder.Entity<CustomerDropbox>(e =>
        {
            e.ToTable("CustomerDropbox", "dbo");
            e.HasKey(d => d.DropboxId);
            e.Property(d => d.DropboxId).HasColumnName("DropboxID").HasMaxLength(30).IsRequired();
            e.Property(d => d.EventAppointmentId).HasColumnName("EventAppointmentID").HasMaxLength(30).IsRequired();
            e.Property(d => d.CustomerId).HasColumnName("CustomerID").HasMaxLength(30).IsRequired();
            e.Property(d => d.BucketName).HasMaxLength(100).IsRequired().HasDefaultValue("dropbox");
            e.Property(d => d.R2ObjectKey).HasMaxLength(500);
            e.Property(d => d.OriginalFileName).HasMaxLength(255);
            e.Property(d => d.ContentType).HasMaxLength(100);
            e.Property(d => d.DropboxStatus).HasMaxLength(30).IsRequired().HasDefaultValue("Waiting");
            e.Property(d => d.UploadedByEmployeeId).HasColumnName("UploadedByEmployeeID").HasMaxLength(30);
            e.Property(d => d.EventCompletedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(d => d.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // ── StudioAnalyticsActions ───────────────────────────────────────────
        builder.Entity<StudioAnalyticsAction>(e =>
        {
            e.ToTable("StudioAnalyticsActions", "dbo");
            e.HasKey(a => a.Id);
            e.Property(a => a.ActionKey).HasMaxLength(120).IsRequired();
            e.Property(a => a.Title).HasMaxLength(255).IsRequired();
            e.Property(a => a.Category).HasMaxLength(80).IsRequired();
            e.Property(a => a.EffortTag).HasMaxLength(50).IsRequired();
            e.Property(a => a.EstimatedImpact).HasMaxLength(255).IsRequired();
            e.Property(a => a.TriggerSignal).HasMaxLength(500);
            e.Property(a => a.RelatedReferences).HasMaxLength(1000);
            e.Property(a => a.Notes).HasMaxLength(1000);
            e.Property(a => a.AppliedByUserId).HasMaxLength(100).IsRequired();
            e.Property(a => a.AppliedByName).HasMaxLength(200).IsRequired();
            e.Property(a => a.AppliedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(a => a.Status).HasMaxLength(40).IsRequired().HasDefaultValue("Applied");
        });

        // ── TermsVersions ────────────────────────────────────────────────────
        builder.Entity<TermsVersion>(e =>
        {
            e.ToTable("TermsVersions", "dbo");
            e.HasKey(t => t.TermsVersionId);
            e.Property(t => t.VersionCode).HasMaxLength(30).IsRequired();
            e.Property(t => t.Title).HasMaxLength(200).IsRequired();
            e.Property(t => t.OvertimeHourlyRate).HasColumnType("decimal(18,2)");
            e.Property(t => t.TermsBodyHtml).IsRequired();
            e.Property(t => t.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(t => t.VersionCode).IsUnique();
        });

        // ── BookingContracts ─────────────────────────────────────────────────
        builder.Entity<BookingContract>(e =>
        {
            e.ToTable("BookingContracts", "dbo");
            e.HasKey(c => c.BookingContractId);
            e.Property(c => c.ContractNumber).HasMaxLength(40).IsRequired();
            e.Property(c => c.EventAppointmentId).HasColumnName("EventAppointmentID").HasMaxLength(30).IsRequired();
            e.Property(c => c.NegotiationId).HasColumnName("NegotiationID").HasMaxLength(30);
            e.Property(c => c.CustomerId).HasColumnName("CustomerID").HasMaxLength(30).IsRequired();
            e.Property(c => c.TermsVersionCode).HasMaxLength(30).IsRequired();
            e.Property(c => c.StudioName).HasMaxLength(200).IsRequired();
            e.Property(c => c.StudioAddress).HasMaxLength(500).IsRequired();
            e.Property(c => c.StudioPhone).HasMaxLength(100).IsRequired();
            e.Property(c => c.StudioEmail).HasMaxLength(200).IsRequired();
            e.Property(c => c.ClientName).HasMaxLength(200).IsRequired();
            e.Property(c => c.ClientAddress).HasMaxLength(500);
            e.Property(c => c.ClientPhone).HasMaxLength(100);
            e.Property(c => c.ClientEmail).HasMaxLength(200).IsRequired();
            e.Property(c => c.SessionType).HasMaxLength(150).IsRequired();
            e.Property(c => c.Venue).HasMaxLength(255).IsRequired();
            e.Property(c => c.PackageName).HasMaxLength(255).IsRequired();
            e.Property(c => c.PackageInclusions).HasMaxLength(1000).IsRequired();
            e.Property(c => c.TotalPackageAmount).HasColumnType("decimal(18,2)");
            e.Property(c => c.ReservationFeeAmount).HasColumnType("decimal(18,2)");
            e.Property(c => c.RemainingBalanceAmount).HasColumnType("decimal(18,2)");
            e.Property(c => c.RepresentativeName).HasMaxLength(200).IsRequired();
            e.Property(c => c.ContractStatus).HasMaxLength(30).IsRequired().HasDefaultValue("PendingAcceptance");
            e.Property(c => c.ClientIpAddress).HasMaxLength(100);
            e.Property(c => c.UserAgent).HasMaxLength(500);
            e.Property(c => c.ClickwrapReference).HasMaxLength(100);
            e.Property(c => c.RenderedContractHtml).IsRequired();
            e.Property(c => c.PdfBucketName).HasMaxLength(100);
            e.Property(c => c.PdfObjectKey).HasMaxLength(500);
            e.Property(c => c.PdfUri).HasMaxLength(1000);
            e.Property(c => c.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(c => c.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(c => c.ContractNumber).IsUnique();
            e.HasIndex(c => c.EventAppointmentId);
            e.HasOne(c => c.TermsVersion)
             .WithMany()
             .HasForeignKey(c => c.TermsVersionId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        // ── BookingPayments ──────────────────────────────────────────────────
        builder.Entity<BookingPayment>(e =>
        {
            e.ToTable("BookingPayments", "dbo");
            e.HasKey(p => p.BookingPaymentId);
            e.Property(p => p.EventAppointmentId).HasColumnName("EventAppointmentID").HasMaxLength(30).IsRequired();
            e.Property(p => p.PaymentStage).HasMaxLength(30).IsRequired();
            e.Property(p => p.PaymentMethod).HasMaxLength(50).IsRequired();
            e.Property(p => p.Amount).HasColumnType("decimal(18,2)");
            e.Property(p => p.OvertimeAmount).HasColumnType("decimal(18,2)");
            e.Property(p => p.GatewayReferenceId).HasMaxLength(150).IsRequired();
            e.Property(p => p.ExternalReference).HasMaxLength(150);
            e.Property(p => p.Status).HasMaxLength(30).IsRequired().HasDefaultValue("Paid");
            e.Property(p => p.PaidAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(p => p.VerifiedByEmployeeId).HasColumnName("VerifiedByEmployeeID").HasMaxLength(30);
            e.Property(p => p.Notes).HasMaxLength(1000);
            e.Property(p => p.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(p => new { p.EventAppointmentId, p.PaymentStage });
            e.HasOne(p => p.BookingContract)
             .WithMany()
             .HasForeignKey(p => p.BookingContractId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        // ── BookingReceipts ──────────────────────────────────────────────────
        builder.Entity<BookingReceipt>(e =>
        {
            e.ToTable("BookingReceipts", "dbo");
            e.HasKey(r => r.BookingReceiptId);
            e.Property(r => r.ReceiptNumber).HasMaxLength(50).IsRequired();
            e.Property(r => r.EventAppointmentId).HasColumnName("EventAppointmentID").HasMaxLength(30).IsRequired();
            e.Property(r => r.ReceiptType).HasMaxLength(40).IsRequired();
            e.Property(r => r.TotalContractCharges).HasColumnType("decimal(18,2)");
            e.Property(r => r.Stage1PaidAmount).HasColumnType("decimal(18,2)");
            e.Property(r => r.OvertimeAddOnAmount).HasColumnType("decimal(18,2)");
            e.Property(r => r.Stage2PaidAmount).HasColumnType("decimal(18,2)");
            e.Property(r => r.RemainingBalanceDue).HasColumnType("decimal(18,2)");
            e.Property(r => r.PaymentMethod).HasMaxLength(50).IsRequired();
            e.Property(r => r.GatewayReferenceId).HasMaxLength(150);
            e.Property(r => r.IssuedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(r => r.RenderedReceiptHtml).IsRequired();
            e.Property(r => r.PdfBucketName).HasMaxLength(100);
            e.Property(r => r.PdfObjectKey).HasMaxLength(500);
            e.Property(r => r.PdfUri).HasMaxLength(1000);
            e.Property(r => r.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(r => r.ReceiptNumber).IsUnique();
            e.HasIndex(r => new { r.EventAppointmentId, r.ReceiptType }).IsUnique();
            e.HasOne(r => r.BookingPayment)
             .WithMany()
             .HasForeignKey(r => r.BookingPaymentId)
             .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
