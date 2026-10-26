namespace PhotoAevumStudio.Application.Entities;

public static class LeaveTypes
{
    public const string SickLeave      = "Sick Leave";
    public const string VacationLeave  = "Vacation Leave";
    public const string MaternityLeave = "Maternity Leave";
    public const string PaternityLeave = "Paternity Leave";

    public static readonly IReadOnlyList<string> All = [SickLeave, VacationLeave, MaternityLeave, PaternityLeave];
}

public static class LeaveStatuses
{
    public const string Pending   = "Pending";
    public const string Approved  = "Approved";
    public const string Rejected  = "Rejected";
    public const string Cancelled = "Cancelled";
}

public class EmployeeLeave
{
    public int Id { get; set; }
    public string LeaveId { get; set; } = string.Empty;
    public string EmployeeStudioId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string LeaveType { get; set; } = LeaveTypes.SickLeave;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = LeaveStatuses.Pending;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
