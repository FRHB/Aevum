namespace PhotoAevumStudio.Infrastructure.Services;

public sealed record AnalyticsBookingRecord(
    string BookingId,
    string Source, // "EventAppointment" | "ScheduleAppointment"
    string Title,
    string CustomerLabel,
    DateOnly EventDate,
    TimeOnly EventTime,
    int DurationMinutes,
    string RawStatus,
    string NormalizedStatus, // "Completed" | "Confirmed" | "Pending" | "Cancelled" | "Expired"
    string DownpaymentStatus,
    DateTime? LockExpiresAtUtc,
    string? AssignedEmployeeName,
    DateTime CreatedAtUtc,
    IReadOnlyList<AnalyticsLineItemRecord> LineItems);

public sealed record AnalyticsLineItemRecord(
    string ItemId,
    string Name,
    string Kind, // "Service" | "Combo Package" | "Add-on" | "Consultation"
    string CategoryName,
    int Quantity);

public sealed record AnalyticsTimesheetRecord(
    string TimesheetId,
    string EmployeeStudioId,
    string EmployeeName,
    DateOnly WorkDate,
    double Hours,
    string Status,
    bool IsDayOffWork);

public sealed record AnalyticsEmployeeRecord(
    string EmployeeId,
    string FullName,
    string Status,
    string DaysOff,
    IReadOnlyList<string> Specializations);

public sealed record AnalyticsLeaveRecord(
    string LeaveId,
    string EmployeeStudioId,
    string EmployeeName,
    string LeaveType,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    string? Reason);

public sealed record AnalyticsEquipmentRecord(
    string EquipmentId,
    string EquipmentName,
    string EquipmentTypeName,
    string EquipmentStatus);

public sealed record AnalyticsMaintenanceRecord(
    int MaintenanceScheduleId,
    string EquipmentId,
    string EquipmentName,
    string ServiceType,
    string Status,
    string? SubStatus,
    string? IssueDetails,
    DateTime ScheduledDateUtc,
    DateTime UpdatedAtUtc,
    DateTime? ResolvedAtUtc);

public sealed record AnalyticsEquipmentAssignmentRecord(
    string EventAppointmentId,
    string EquipmentId,
    string EquipmentName,
    DateOnly EventDate,
    string EventStatus,
    bool IsAdditionalRequest);

public sealed record AnalyticsNegotiationRecord(
    string NegotiationId,
    string? EventAppointmentId,
    string CustomerId,
    string? AssignedEmployeeName,
    DateOnly? ConsultationDate,
    string NegotiationStatus,
    string? InquiryNotes,
    DateTime CreatedAtUtc);

public sealed record AnalyticsAuditRecord(
    string Source, // "AuditLog" | "HistoryLog"
    string ReferenceId,
    string Action,
    string Resource,
    string Details,
    DateTime TimestampUtc);

public sealed record AnalyticsAppliedActionRecord(
    int Id,
    string ActionKey,
    string Title,
    string Category,
    string EffortTag,
    string EstimatedImpact,
    string? TriggerSignal,
    string? RelatedReferences,
    string? Notes,
    string AppliedByUserId,
    string AppliedByName,
    DateTime AppliedAtUtc,
    string Status);

public sealed class StudioAnalyticsSnapshot
{
    public DateOnly Today { get; init; } = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
    public IReadOnlyList<AnalyticsBookingRecord> Bookings { get; init; } = [];
    public IReadOnlyList<AnalyticsTimesheetRecord> Timesheets { get; init; } = [];
    public IReadOnlyList<AnalyticsEmployeeRecord> Employees { get; init; } = [];
    public IReadOnlyList<AnalyticsLeaveRecord> Leaves { get; init; } = [];
    public IReadOnlyList<string> RegisteredSpecializations { get; init; } = [];
    public IReadOnlyList<AnalyticsEquipmentRecord> Equipment { get; init; } = [];
    public IReadOnlyList<AnalyticsMaintenanceRecord> MaintenanceSchedules { get; init; } = [];
    public IReadOnlyList<AnalyticsEquipmentAssignmentRecord> EquipmentAssignments { get; init; } = [];
    public IReadOnlyList<AnalyticsNegotiationRecord> Negotiations { get; init; } = [];
    public IReadOnlyList<AnalyticsAuditRecord> AuditRecords { get; init; } = [];
    public IReadOnlyList<AnalyticsAppliedActionRecord> AppliedActions { get; init; } = [];
    public IReadOnlyList<string> AvailableServiceCategories { get; init; } = [];
}
