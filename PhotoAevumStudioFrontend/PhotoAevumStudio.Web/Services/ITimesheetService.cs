using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public interface ITimesheetService
{
    Task<ClockStatusViewModel> GetClockStatusAsync(string? employeeId = null, CancellationToken cancellationToken = default);
    Task<bool> ClockInAsync(string? employeeId = null, string? notes = null, string? employeeName = null, CancellationToken cancellationToken = default);
    Task<bool> ClockOutAsync(string? employeeId = null, string? notes = null, string? employeeName = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TodayAttendanceSummaryDto>> GetTodayAttendanceAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmployeeTimesheetHistoryDto>> GetTimesheetHistoryAsync(string? employeeId = null, CancellationToken cancellationToken = default);
    Task<bool> AdjustTimesheetPunchAsync(int id, DateTime? clockIn, DateTime? clockOut, string? reason, CancellationToken cancellationToken = default);
}
