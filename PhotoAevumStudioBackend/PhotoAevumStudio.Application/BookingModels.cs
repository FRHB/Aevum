namespace PhotoAevumStudio.Application;

// Sent by the frontend to the API.
public class CreateBookingRequest
{
    public string CustomerId { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string EventDescription { get; set; } = string.Empty;
    public DateOnly EventDate { get; set; }
    public TimeOnly EventTime { get; set; }

    public List<string> ServicesIds { get; set; } = new();
    public List<string> AdditionalServicesIds { get; set; } = new();
    public List<string> ComboPackageIds { get; set; } = new();
    public List<string> EmployeeIds { get; set; } = new();
    public List<string> EquipmentIds { get; set; } = new();
}

// Returned to the frontend 
public class BookingResultDto
{
    public string EventAppointmentId { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public DateOnly EventDate { get; set; }
    public TimeOnly EventTime { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int LineItemCount { get; set; }
    public int EmployeesAssigned { get; set; }
    public int EquipmentReserved { get; set; }
}

public class BookingListItemDto
{
    public string EventAppointmentId { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string EventDescription { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateOnly EventDate { get; set; }
    public TimeOnly EventTime { get; set; }
    public string EventStatus { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

// Thrown when the stored procedure rejects a booking (past date, double-booked
// employee, unknown ID, etc.). The message is safe to show to the user.
public class BookingRuleException : Exception
{
    public BookingRuleException(string message) : base(message) { }
}
