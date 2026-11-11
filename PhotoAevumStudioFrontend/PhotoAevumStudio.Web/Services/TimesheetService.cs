using System.Net.Http.Json;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public class TimesheetService : ITimesheetService
{
    private readonly HttpClient _http;
    private readonly ITokenStore _tokenStore;

    public TimesheetService(IHttpClientFactory httpClientFactory, ITokenStore tokenStore)
    {
        _http = httpClientFactory.CreateClient("AevumApi.Authorized");
        _tokenStore = tokenStore;
    }

    public async Task<ClockStatusViewModel> GetClockStatusAsync(string? employeeId = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) 
            return new ClockStatusViewModel { EmployeeStudioId = employeeId ?? string.Empty };

        try
        {
            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }
            var url = string.IsNullOrWhiteSpace(employeeId) ? "api/timesheet/status" : $"api/timesheet/status?employeeId={Uri.EscapeDataString(employeeId)}";
            var res = await _http.GetFromJsonAsync<ClockStatusViewModel>(url, cancellationToken);
            if (res != null) return res;
        }
        catch { }

        return new ClockStatusViewModel { EmployeeStudioId = employeeId ?? string.Empty };
    }

    public async Task<bool> ClockInAsync(string? employeeId = null, string? notes = null, string? employeeName = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return false;

        try
        {
            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }
            var payload = new
            {
                EmployeeStudioId = employeeId,
                EmployeeName = employeeName,
                Notes = notes
            };
            var res = await _http.PostAsJsonAsync("api/timesheet/clock-in", payload, cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ClockOutAsync(string? employeeId = null, string? notes = null, string? employeeName = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return false;

        try
        {
            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }
            var payload = new
            {
                EmployeeStudioId = employeeId,
                EmployeeName = employeeName,
                Notes = notes
            };
            var res = await _http.PostAsJsonAsync("api/timesheet/clock-out", payload, cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<TodayAttendanceSummaryDto>> GetTodayAttendanceAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return new List<TodayAttendanceSummaryDto>();

        try
        {
            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }
            var res = await _http.GetFromJsonAsync<List<TodayAttendanceSummaryDto>>("api/timesheet/summary-today", cancellationToken);
            if (res != null) return res;
        }
        catch { }

        return new List<TodayAttendanceSummaryDto>();
    }

    public async Task<IReadOnlyList<EmployeeTimesheetHistoryDto>> GetTimesheetHistoryAsync(string? employeeId = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return new List<EmployeeTimesheetHistoryDto>();

        try
        {
            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }
            var url = string.IsNullOrWhiteSpace(employeeId) ? "api/timesheet/my-history" : $"api/timesheet/my-history?employeeId={Uri.EscapeDataString(employeeId)}";
            var res = await _http.GetFromJsonAsync<List<EmployeeTimesheetHistoryDto>>(url, cancellationToken);
            if (res != null) return res;
        }
        catch { }

        return new List<EmployeeTimesheetHistoryDto>();
    }

    public async Task<bool> AdjustTimesheetPunchAsync(int id, DateTime? clockIn, DateTime? clockOut, string? reason, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return false;

        try
        {
            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }
            var res = await _http.PostAsJsonAsync($"api/timesheet/{id}/adjust", new { ClockIn = clockIn, ClockOut = clockOut, AdjustmentReason = reason }, cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
