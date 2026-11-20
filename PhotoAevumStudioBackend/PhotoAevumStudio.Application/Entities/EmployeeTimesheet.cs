namespace PhotoAevumStudio.Application.Entities;

public static class TimesheetStatuses
{
    public const string Open      = "Open";
    public const string Completed = "Completed";
    public const string Approved  = "Approved";
    public const string Adjusted  = "Adjusted";
}

public static class TimesheetEntryTypes
{
    public const string Work        = "Work";
    public const string PaidLeave   = "Paid Leave";
    public const string UnpaidLeave = "Unpaid Leave";
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
    public int RegularMinutes { get; set; }
    public int UndertimeMinutes { get; set; }
    public int PendingOvertimeMinutes { get; set; }
    public int ApprovedOvertimeMinutes { get; set; }
    public bool IsOvertimeApproved { get; set; }
    public string? OvertimeApprovedBy { get; set; }
    public DateTime? OvertimeApprovedAt { get; set; }
    public string EntryType { get; set; } = TimesheetEntryTypes.Work;
    public int? LeaveRequestID { get; set; }
    public string Status { get; set; } = TimesheetStatuses.Open;
    public bool IsDayOffWork { get; set; }
    public string? Notes { get; set; }
    public string? AdjustedBy { get; set; }
    public string? AdjustmentReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Computes DOLE Article 88 compliant regular, undertime, and pending overtime minutes.
    /// Undertime is NEVER subtracted from overtime.
    /// For Office Clerks (isTitleIExempt == false), regular minutes cap at 480 (8.0h),
    /// shortfall below 480 is recorded in UndertimeMinutes, and excess beyond 480 is recorded in PendingOvertimeMinutes.
    /// </summary>
    public void ApplyDoleShiftMinutes(bool isTitleIExempt, int? explicitUndertimeMinutes = null)
    {
        if (EntryType == TimesheetEntryTypes.PaidLeave || EntryType == TimesheetEntryTypes.UnpaidLeave)
        {
            return;
        }

        if (!ClockOut.HasValue)
        {
            RegularMinutes = 0;
            UndertimeMinutes = 0;
            PendingOvertimeMinutes = 0;
            return;
        }

        int workedMinutes = Math.Max(0, (int)Math.Round((ClockOut.Value - ClockIn).TotalMinutes));
        TotalHours = Math.Round(workedMinutes / 60.0, 2);

        if (isTitleIExempt)
        {
            RegularMinutes = workedMinutes;
            UndertimeMinutes = 0;
            PendingOvertimeMinutes = 0;
            ApprovedOvertimeMinutes = 0;
            IsOvertimeApproved = false;
            return;
        }

        const int StandardShiftMinutes = 480; // 8.0 hours
        RegularMinutes = Math.Min(workedMinutes, StandardShiftMinutes);

        // Late arrival relative to 09:00 AM standard shift start (if clocked in after 09:00 AM on a regular workday)
        int lateArrivalMinutes = 0;
        if (!IsDayOffWork)
        {
            var scheduledStart = ClockIn.Date.AddHours(9);
            if (ClockIn > scheduledStart && ClockIn.Hour < 15)
            {
                lateArrivalMinutes = (int)Math.Round((ClockIn - scheduledStart).TotalMinutes);
            }
        }

        if (workedMinutes >= StandardShiftMinutes)
        {
            int excessMinutes = workedMinutes - StandardShiftMinutes;
            // DOLE Art. 88: Never subtract undertime/late minutes from overtime minutes.
            UndertimeMinutes = explicitUndertimeMinutes ?? lateArrivalMinutes;
            PendingOvertimeMinutes = excessMinutes;
            if (IsOvertimeApproved)
            {
                ApprovedOvertimeMinutes = excessMinutes;
            }
        }
        else
        {
            int shortfall = StandardShiftMinutes - workedMinutes;
            UndertimeMinutes = explicitUndertimeMinutes ?? Math.Max(shortfall, lateArrivalMinutes);
            PendingOvertimeMinutes = 0;
            ApprovedOvertimeMinutes = 0;
            IsOvertimeApproved = false;
        }
    }
}
