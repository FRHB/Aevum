using Microsoft.EntityFrameworkCore;

namespace PhotoAevumStudio.Infrastructure.Scheduling;

public sealed class Appointment
{
    public int AppointmentId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateOnly SlotDate { get; set; }
    public TimeOnly SlotStart { get; set; }
    public TimeOnly SlotEnd { get; set; }
    public string? EventLocation { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string MeetingLink { get; set; } = string.Empty;
    public string Status { get; set; } = AppointmentStatus.Confirmed;
    public decimal TotalPrice { get; set; }
    public DateTime CreatedAt { get; set; }
    public byte[] RowVer { get; set; } = [];
    public List<AppointmentServiceLine> Services { get; set; } = new();
}

public static class AppointmentStatus
{
    public const string Confirmed = "Confirmed";
    public const string Cancelled = "Cancelled";
}

public sealed class AppointmentServiceLine
{
    public int AppointmentServiceId { get; set; }
    public int AppointmentId { get; set; }
    public string ServicesId { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public decimal ServicePrice { get; set; }
}

/// <summary>Per-day booking aggregate. Uses a rowversion for optimistic concurrency.</summary>
public sealed class BookingOverview
{
    public DateOnly OverviewDate { get; set; }
    public int TotalAppointments { get; set; }
    public decimal TotalRevenue { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    public byte[] RowVer { get; set; } = [];
}

/// <summary>Read-only projection of the existing dbo.Services catalog.</summary>
public sealed class CatalogService
{
    public string ServicesId { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string ServicesDescription { get; set; } = string.Empty;
    public decimal ServicePrice { get; set; }
}

/// <summary>
/// Small DbContext for the booking wizard. Kept separate from ApplicationDbContext so the
/// existing migration snapshot is untouched; tables are created by SchedulingSchema.sql.
/// </summary>
public sealed class SchedulingDbContext(DbContextOptions<SchedulingDbContext> options) : DbContext(options)
{
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppointmentServiceLine> AppointmentServices => Set<AppointmentServiceLine>();
    public DbSet<BookingOverview> BookingOverviews => Set<BookingOverview>();
    public DbSet<CatalogService> CatalogServices => Set<CatalogService>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Appointment>(e =>
        {
            e.ToTable("Appointment", "dbo");
            e.HasKey(a => a.AppointmentId);
            e.Property(a => a.AppointmentId).HasColumnName("AppointmentID");
            e.Property(a => a.CustomerId).HasColumnName("CustomerID").HasMaxLength(450);
            e.Property(a => a.CustomerEmail).HasMaxLength(255);
            e.Property(a => a.CustomerName).HasMaxLength(255);
            e.Property(a => a.EventLocation).HasMaxLength(300);
            e.Property(a => a.Platform).HasMaxLength(20);
            e.Property(a => a.MeetingLink).HasMaxLength(500);
            e.Property(a => a.Status).HasMaxLength(20);
            e.Property(a => a.TotalPrice).HasColumnType("money");
            e.Property(a => a.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(a => a.RowVer).IsRowVersion();
            e.HasMany(a => a.Services).WithOne().HasForeignKey(s => s.AppointmentId);
        });

        b.Entity<AppointmentServiceLine>(e =>
        {
            e.ToTable("AppointmentService", "dbo");
            e.HasKey(s => s.AppointmentServiceId);
            e.Property(s => s.AppointmentServiceId).HasColumnName("AppointmentServiceID");
            e.Property(s => s.AppointmentId).HasColumnName("AppointmentID");
            e.Property(s => s.ServicesId).HasColumnName("ServicesID").HasMaxLength(30);
            e.Property(s => s.ServiceName).HasMaxLength(255);
            e.Property(s => s.ServicePrice).HasColumnType("money");
        });

        b.Entity<BookingOverview>(e =>
        {
            e.ToTable("BookingOverview", "dbo");
            e.HasKey(o => o.OverviewDate);
            e.Property(o => o.OverviewDate).ValueGeneratedNever();
            e.Property(o => o.TotalRevenue).HasColumnType("money");
            e.Property(o => o.RowVer).IsRowVersion();
        });

        b.Entity<CatalogService>(e =>
        {
            e.ToTable("Services", "dbo");
            e.HasKey(s => s.ServicesId);
            e.Property(s => s.ServicesId).HasColumnName("ServicesID");
            e.Property(s => s.ServicePrice).HasColumnType("money");
        });
    }
}
