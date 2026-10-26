namespace PhotoAevumStudio.Application.Entities;

public static class TimesheetStatuses
{
    public const string Open      = "Open";
    public const string Completed = "Completed";
    public const string Approved  = "Approved";
    public const string Adjusted  = "Adjusted";
}

public class EmployeeTimesheet
{
    public int Id { get; set; }
    public string TimesheetId { get; set; } = string.Empty;
    public string EmployeeStudioId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateOnly WorkDate { get; set; }
    public DateTime ClockIn { get; set; }
    public DateTime? ClockOut { get; set; }
    public double TotalHours { get; set; }
    public string Status { get; set; } = TimesheetStatuses.Open;
    public bool IsDayOffWork { get; set; }
    public string? Notes { get; set; }
    public string? AdjustedBy { get; set; }
    public string? AdjustmentReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
