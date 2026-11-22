using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public interface IEmployeeService
{
    Task<IReadOnlyList<EmployeeViewModel>> GetEmployeesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmployeeViewModel>> GetEmployeesAsync(bool includeArchived, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmployeeViewModel>> GetArchivedEmployeesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetSpecializationsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SpecializationLookupItem>> GetSpecializationLookupsAsync(CancellationToken cancellationToken = default);
    Task<SaveEmployeeResult> CreateEmployeeAsync(NewEmployeeRequest request, CancellationToken cancellationToken = default);
    Task<SaveEmployeeResult> ResendEmployeeWelcomeEmailAsync(string employeeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SaveEmployeeResult(false, "Resend welcome email is not configured.", false, false, "Resend welcome email is not configured."));
    Task<SaveEmployeeResult> UpdateEmployeeAsync(NewEmployeeRequest request, CancellationToken cancellationToken = default);
    Task<bool> ArchiveEmployeeAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<bool> RestoreEmployeeAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<bool> DeleteEmployeeAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> ResetEmployeePasswordAsync(string employeeId, CancellationToken cancellationToken = default) =>
        Task.FromResult((false, "Password reset is not configured."));
    Task<DashboardStatsViewModel> GetDashboardStatsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DayAvailabilityViewModel>> GetAvailabilityAsync(int days, CancellationToken cancellationToken = default);

    // ── Timesheet & Clock In/Out ─────────────────────────────────────────────
    Task<IReadOnlyList<TodayAttendanceSummaryDto>> GetTodayAttendanceAsync(CancellationToken cancellationToken = default);
    Task<ClockStatusViewModel> GetClockStatusAsync(string? employeeId = null, CancellationToken cancellationToken = default);
    Task<bool> ClockInAsync(string? employeeId = null, string? notes = null, string? employeeName = null, CancellationToken cancellationToken = default);
    Task<bool> ClockOutAsync(string? employeeId = null, string? notes = null, string? employeeName = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmployeeTimesheetHistoryDto>> GetTimesheetHistoryAsync(string? employeeId = null, CancellationToken cancellationToken = default);
    Task<bool> AdjustTimesheetPunchAsync(int id, DateTime? clockIn, DateTime? clockOut, string? reason, CancellationToken cancellationToken = default);
    Task<bool> ApproveTimesheetOvertimeAsync(int id, int? approvedOvertimeMinutes = null, CancellationToken cancellationToken = default) => Task.FromResult(false);

    // ── Leaves & Days Off ────────────────────────────────────────────────────
    Task<bool> RequestLeaveAsync(string leaveType, DateOnly startDate, DateOnly endDate, string? reason, string? employeeId = null, CancellationToken cancellationToken = default);
    Task<bool> RequestLeaveAsync(string leaveType, DateOnly startDate, DateOnly endDate, string? reason, string? employeeId, bool isHalfDay, string? attachmentName = null, CancellationToken cancellationToken = default)
        => RequestLeaveAsync(leaveType, startDate, endDate, reason, employeeId, cancellationToken);
    Task<EmployeeLeaveBalanceSummaryViewModel> GetLeaveBalancesAsync(string employeeId, int year = 2026, CancellationToken cancellationToken = default)
        => Task.FromResult(new EmployeeLeaveBalanceSummaryViewModel { EmployeeId = employeeId, Year = year });
    Task<CountableWorkdaysPreviewViewModel> PreviewLeaveWorkdaysAsync(string employeeId, DateOnly startDate, DateOnly endDate, bool isHalfDay = false, string? fallbackDaysOff = null, CancellationToken cancellationToken = default)
        => Task.FromResult(LeaveWorkdayHelper.CalculateCountableWorkdays(employeeId, startDate, endDate, isHalfDay, fallbackDaysOff));
    Task<IReadOnlyList<LeaveItemViewModel>> GetPendingLeavesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LeaveItemViewModel>> GetAllLeavesAsync(string? status = null, string? employeeId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LeaveItemViewModel>> GetMyLeavesAsync(string? employeeId = null, CancellationToken cancellationToken = default);
    Task<bool> ApproveLeaveAsync(int leaveId, CancellationToken cancellationToken = default);
    Task<bool> RejectLeaveAsync(int leaveId, string? reason = null, CancellationToken cancellationToken = default);
    Task<SuddenAbsenceResultViewModel?> MarkSuddenAbsenceAsync(string employeeStudioId, string? employeeName = null, DateOnly? date = null, string? leaveType = null, string? reason = null, CancellationToken cancellationToken = default)
        => Task.FromResult<SuddenAbsenceResultViewModel?>(null);
    Task<bool> UpdateEmployeeDaysOffAsync(string employeeId, string daysOff, CancellationToken cancellationToken = default);
}
