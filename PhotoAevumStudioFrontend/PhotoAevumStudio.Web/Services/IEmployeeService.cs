using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public interface IEmployeeService
{
    Task<IReadOnlyList<EmployeeViewModel>> GetEmployeesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetSpecializationsAsync(CancellationToken cancellationToken = default);
    Task<SaveEmployeeResult> CreateEmployeeAsync(NewEmployeeRequest request, CancellationToken cancellationToken = default);
    Task<DashboardStatsViewModel> GetDashboardStatsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DayAvailabilityViewModel>> GetAvailabilityAsync(int days, CancellationToken cancellationToken = default);

    // ── Timesheet & Clock In/Out ─────────────────────────────────────────────
    Task<IReadOnlyList<TodayAttendanceSummaryDto>> GetTodayAttendanceAsync(CancellationToken cancellationToken = default);
    Task<ClockStatusViewModel> GetClockStatusAsync(string? employeeId = null, CancellationToken cancellationToken = default);
    Task<bool> ClockInAsync(string? employeeId = null, string? notes = null, CancellationToken cancellationToken = default);
    Task<bool> ClockOutAsync(string? employeeId = null, string? notes = null, CancellationToken cancellationToken = default);

    // ── Leaves ───────────────────────────────────────────────────────────────
    Task<bool> RequestLeaveAsync(string leaveType, DateOnly startDate, DateOnly endDate, string? reason, string? employeeId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LeaveItemViewModel>> GetPendingLeavesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LeaveItemViewModel>> GetMyLeavesAsync(string? employeeId = null, CancellationToken cancellationToken = default);
    Task<bool> ApproveLeaveAsync(int leaveId, CancellationToken cancellationToken = default);
    Task<bool> RejectLeaveAsync(int leaveId, string? reason = null, CancellationToken cancellationToken = default);
}
