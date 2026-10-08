namespace PhotoAevumStudio.Application.Entities;

public enum BookingStatus { Pending, Confirmed, Shot, Editing, Delivered, Cancelled }

public class Booking
{
    public int Id { get; set; }
    public string CustomerId { get; set; } = null!;
    public ApplicationUser Customer { get; set; } = null!;

    public int ServiceId { get; set; }
    public StudioService Service { get; set; } = null!;

    public DateTime ScheduleDate { get; set; }
    public TimeSpan ScheduleTime { get; set; }
    public BookingStatus Status { get; set; }

    // Navigation
    public ICollection<EmployeeAssignment> EmployeeAssignments { get; set; } = new List<EmployeeAssignment>();
    public ICollection<EquipmentAssignment> EquipmentAssignments { get; set; } = new List<EquipmentAssignment>();
}

public class EmployeeAssignment
{
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;

    // Denormalized for DB-level concurrency constraint
    public DateTime ScheduleDate { get; set; }
    public TimeSpan ScheduleTime { get; set; }
}

public class EquipmentAssignment
{
    public int EquipmentId { get; set; }
    public Equipment Equipment { get; set; } = null!;
    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public DateTime ScheduleDate { get; set; }
    public TimeSpan ScheduleTime { get; set; }
}