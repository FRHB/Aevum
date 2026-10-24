<<<<<<< HEAD
namespace PhotoAevumStudio.Application.Entities;

public enum BookingStatus { Pending, Confirmed, Shot, Editing, Delivered, Cancelled }

public class Booking
{
    public int Id { get; set; }
    public string CustomerId { get; set; } = null!;
    public ApplicationUser Customer { get; set; } = null!;

    public int ServiceId { get; set; }
    public StudioService Service { get; set; } = null!;

    public DateOnly ScheduleDate { get; set; }
    public TimeSpan ScheduleTime { get; set; }
    public BookingStatus Status { get; set; }

    // Navigation
    public ICollection<EmployeeAssignment> EmployeeAssignments { get; set; } = new List<EmployeeAssignment>();
    public ICollection<EquipmentAssignment> EquipmentAssignments { get; set; } = new List<EquipmentAssignment>();
}

public class EmployeeAssignment
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;

    // Denormalized for DB-level concurrency constraint
    public DateOnly ScheduleDate { get; set; }
    public TimeSpan ScheduleTime { get; set; }
}

public class EquipmentAssignment
{
    public int Id { get; set; }
    public int EquipmentId { get; set; }
    public Equipment Equipment { get; set; } = null!;
    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public DateOnly ScheduleDate { get; set; }
    public TimeSpan ScheduleTime { get; set; }
=======
namespace PhotoAevumStudio.Application.Entities;

public enum BookingStatus { Pending, Confirmed, Shot, Editing, Delivered, Cancelled }

public class Booking
{
    public int Id { get; set; }
    public string CustomerId { get; set; } = null!;
    public ApplicationUser Customer { get; set; } = null!;

    public int ServiceId { get; set; }
    public StudioService Service { get; set; } = null!;

    public DateOnly ScheduleDate { get; set; }
    public TimeSpan ScheduleTime { get; set; }
    public BookingStatus Status { get; set; }

    // Navigation
    public ICollection<EmployeeAssignment> EmployeeAssignments { get; set; } = new List<EmployeeAssignment>();
    public ICollection<EquipmentAssignment> EquipmentAssignments { get; set; } = new List<EquipmentAssignment>();
}

public class EmployeeAssignment
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;

    // Denormalized for DB-level concurrency constraint
    public DateOnly ScheduleDate { get; set; }
    public TimeSpan ScheduleTime { get; set; }
}

public class EquipmentAssignment
{
    public int Id { get; set; }
    public int EquipmentId { get; set; }
    public Equipment Equipment { get; set; } = null!;
    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public DateOnly ScheduleDate { get; set; }
    public TimeSpan ScheduleTime { get; set; }
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
}