using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public class EmployeeService : IEmployeeService
{
    private readonly HttpClient _http;
    private readonly ITokenStore _tokenStore;
    private readonly IUserService _userService;
    private readonly string _connectionString;

    public EmployeeService(
        IHttpClientFactory httpClientFactory,
        ITokenStore tokenStore,
        IUserService userService,
        IConfiguration configuration)
    {
        _http = httpClientFactory.CreateClient("AevumApi.Authorized");
        _tokenStore = tokenStore;
        _userService = userService;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
    }

    public async Task<IReadOnlyList<EmployeeViewModel>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return GetLocalFallbackEmployees();
        }

        try
        {
            try
            {
                await _tokenStore.GetTokenAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return GetLocalFallbackEmployees();
            }
            catch { }

            List<EmployeeViewModel> list;
            try
            {
                var result = await _http.GetFromJsonAsync<List<EmployeeViewModel>>("api/management/employees", cancellationToken);
                list = result ?? new List<EmployeeViewModel>();
            }
            catch
            {
                list = GetLocalFallbackEmployees();
            }

            // Enrich with today's attendance & leave statuses from Timesheet API
            try
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    var attendanceList = await GetTodayAttendanceAsync(cancellationToken);
                    if (attendanceList.Count > 0)
                    {
                        foreach (var emp in list)
                        {
                            var att = attendanceList.FirstOrDefault(a => string.Equals(a.EmployeeStudioId, emp.EmployeeId, StringComparison.OrdinalIgnoreCase));
                            if (att != null)
                            {
                                emp.DaysOff = !string.IsNullOrWhiteSpace(att.DaysOff) ? att.DaysOff : emp.DaysOff;
                                emp.IsClockedIn = att.IsClockedIn;
                                emp.TodayClockIn = att.ClockIn;
                                emp.TodayClockOut = att.ClockOut;
                                emp.TodayHoursWorked = att.TotalHours;
                                emp.IsDayOffToday = att.IsDayOffToday;
                                emp.IsOnLeaveToday = att.IsOnLeaveToday;
                                emp.ActiveLeaveType = att.ActiveLeaveType;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Best effort enrichment
            }

            return list;
        }
        catch (OperationCanceledException)
        {
            return GetLocalFallbackEmployees();
        }
        catch
        {
            return GetLocalFallbackEmployees();
        }
    }

    private List<EmployeeViewModel> GetLocalFallbackEmployees()
    {
        try
        {
            var dbEmployees = _userService.GetEmployeeSpecializations();
            return dbEmployees.Select(e => new EmployeeViewModel
            {
                Id = e.Id,
                EmployeeId = e.EmployeeId,
                FirstName = e.FirstName,
                LastName = e.LastName,
                MiddleName = e.MiddleName,
                Email = e.Email,
                Role = e.Role,
                Specializations = e.Specializations,
                Status = e.Status
            }).ToList();
        }
        catch
        {
            return new List<EmployeeViewModel>();
        }
    }

    public async Task<IReadOnlyList<string>> GetSpecializationsAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return GetLocalFallbackSpecializations();
        }

        try
        {
            try
            {
                await _tokenStore.GetTokenAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return GetLocalFallbackSpecializations();
            }
            catch { }

            try
            {
                var result = await _http.GetFromJsonAsync<List<string>>("api/management/specializations", cancellationToken);
                if (result is not null)
                    return result;
            }
            catch
            {
                // Fall through to direct SQL query against DefaultConnection
            }

            return GetLocalFallbackSpecializations();
        }
        catch (OperationCanceledException)
        {
            return GetLocalFallbackSpecializations();
        }
        catch
        {
            return GetLocalFallbackSpecializations();
        }
    }

    private List<string> GetLocalFallbackSpecializations()
    {
        try
        {
            return _userService.GetSpecializationLookups()
                .Select(s => s.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    public async Task<SaveEmployeeResult> CreateEmployeeAsync(NewEmployeeRequest r, CancellationToken cancellationToken = default)
    {
        await _tokenStore.GetTokenAsync(cancellationToken);

        var apiPayload = new
        {
            r.FirstName,
            r.LastName,
            r.MiddleName,
            r.Email,
            r.Password,
            Role = "Employee",
            StudioId = r.EmployeeId
        };

        try
        {
            var response = await _http.PostAsJsonAsync("api/auth/register", apiPayload, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                await PersistEmployeeSpecializationsAsync(r.EmployeeId, r.Specializations, cancellationToken);
                await PersistEmployeeDaysOffAsync(r.EmployeeId, r.DaysOff, cancellationToken);
                return new SaveEmployeeResult(true);
            }
        }
        catch
        {
            // API call failed, attempt database fallback
        }

        // Direct SQL fallback
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            const string sql = @"
                IF NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE UPPER(EmployeeID) = UPPER(@EmpId))
                BEGIN
                    INSERT INTO dbo.Employees (
                        EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeMiddleName, EmployeeEmail,
                        EmployeePassword, EmployeeStatus, isFirstLogin, CreatedAt, DaysOff
                    )
                    VALUES (
                        @EmpId, @FirstName, @LastName, @MiddleName, @Email,
                        @Password, 'Active', 1, SYSUTCDATETIME(), @DaysOff
                    );
                END
                ELSE
                BEGIN
                    UPDATE dbo.Employees
                    SET EmployeeFirstName = @FirstName,
                        EmployeeLastName = @LastName,
                        EmployeeMiddleName = @MiddleName,
                        EmployeeEmail = @Email,
                        DaysOff = @DaysOff
                    WHERE UPPER(EmployeeID) = UPPER(@EmpId);
                END";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@EmpId", r.EmployeeId.Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue("@FirstName", r.FirstName.Trim());
            cmd.Parameters.AddWithValue("@LastName", r.LastName.Trim());
            cmd.Parameters.AddWithValue("@MiddleName", (object?)r.MiddleName?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Email", (object?)r.Email?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Password", string.IsNullOrWhiteSpace(r.Password) ? "TempPass123!" : r.Password);
            cmd.Parameters.AddWithValue("@DaysOff", string.IsNullOrWhiteSpace(r.DaysOff) ? "Sunday" : r.DaysOff.Trim());
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            await PersistEmployeeSpecializationsAsync(r.EmployeeId, r.Specializations, cancellationToken);
            await PersistEmployeeDaysOffAsync(r.EmployeeId, r.DaysOff, cancellationToken);
            return new SaveEmployeeResult(true);
        }
        catch (Exception dbEx)
        {
            return new SaveEmployeeResult(false, dbEx.Message);
        }
    }

    public async Task<SaveEmployeeResult> UpdateEmployeeAsync(NewEmployeeRequest r, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return new SaveEmployeeResult(false, "Operation canceled.");

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            const string sql = @"
                UPDATE dbo.Employees
                SET EmployeeFirstName = @FirstName,
                    EmployeeLastName = @LastName,
                    EmployeeMiddleName = @MiddleName,
                    EmployeeEmail = @Email,
                    DaysOff = @DaysOff,
                    EmployeePassword = CASE WHEN @Password IS NOT NULL AND LEN(LTRIM(RTRIM(@Password))) > 0 THEN @Password ELSE EmployeePassword END
                WHERE UPPER(EmployeeID) = UPPER(@EmpId);";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@EmpId", r.EmployeeId.Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue("@FirstName", r.FirstName.Trim());
            cmd.Parameters.AddWithValue("@LastName", r.LastName.Trim());
            cmd.Parameters.AddWithValue("@MiddleName", (object?)r.MiddleName?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Email", (object?)r.Email?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Password", string.IsNullOrWhiteSpace(r.Password) ? (object)DBNull.Value : r.Password.Trim());
            cmd.Parameters.AddWithValue("@DaysOff", string.IsNullOrWhiteSpace(r.DaysOff) ? "Sunday" : r.DaysOff.Trim());
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            // Also synchronize AspNetUsers if username/email matches
            try
            {
                const string userSql = @"
                    UPDATE dbo.AspNetUsers
                    SET Email = @Email,
                        NormalizedEmail = UPPER(@Email)
                    WHERE UserName = @EmpId OR Email = @Email;";
                await using var userCmd = new SqlCommand(userSql, conn);
                userCmd.Parameters.AddWithValue("@Email", (object?)r.Email?.Trim() ?? DBNull.Value);
                userCmd.Parameters.AddWithValue("@EmpId", r.EmployeeId.Trim().ToUpperInvariant());
                await userCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch
            {
                // Best effort
            }

            await PersistEmployeeSpecializationsAsync(r.EmployeeId, r.Specializations, cancellationToken);
            await PersistEmployeeDaysOffAsync(r.EmployeeId, r.DaysOff, cancellationToken);
            return new SaveEmployeeResult(true);
        }
        catch (OperationCanceledException)
        {
            return new SaveEmployeeResult(false, "Operation canceled.");
        }
        catch (Exception dbEx)
        {
            return new SaveEmployeeResult(false, dbEx.Message);
        }
    }

    private async Task PersistEmployeeDaysOffAsync(string employeeId, string daysOff, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(daysOff)) return;
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            const string sql = @"
                IF COL_LENGTH(N'dbo.Employees', N'DaysOff') IS NOT NULL
                BEGIN
                    UPDATE dbo.Employees SET DaysOff = @DaysOff WHERE EmployeeID = @EmpId;
                END";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@DaysOff", daysOff.Trim());
            cmd.Parameters.AddWithValue("@EmpId", employeeId.Trim());
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // Best effort
        }
    }

    private async Task PersistEmployeeSpecializationsAsync(
        string employeeId,
        IReadOnlyList<string> specializations,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // Delete existing joints for this employee before adding updated specializations
        const string clearSql = @"DELETE FROM dbo.EmployeeJointSpecialization WHERE UPPER(EmployeeID) = UPPER(@EmpId);";
        await using (var clearCmd = new SqlCommand(clearSql, conn))
        {
            clearCmd.Parameters.AddWithValue("@EmpId", employeeId.Trim().ToUpperInvariant());
            await clearCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        if (specializations is null || specializations.Count == 0)
            return;

        int seq = 1;
        foreach (var specName in specializations.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            const string sql = @"
                DECLARE @SpecId NVARCHAR(30);
                SELECT TOP 1 @SpecId = SpecializationID
                FROM dbo.EmployeeSpecialization
                WHERE UPPER(LTRIM(RTRIM(COALESCE(NULLIF(SpecializationName, ''), Specialization)))) = UPPER(@SpecName)
                ORDER BY SpecializationID;

                IF @SpecId IS NULL
                BEGIN
                    DECLARE @NextSpecNum INT;
                    SELECT @NextSpecNum = ISNULL(MAX(TRY_CAST(SUBSTRING(SpecializationID, 4, 20) AS INT)), 0) + 1
                    FROM dbo.EmployeeSpecialization
                    WHERE SpecializationID LIKE 'SP-[0-9]%'
                      AND TRY_CAST(SUBSTRING(SpecializationID, 4, 20) AS INT) < 900;

                    SET @SpecId = CONCAT('SP-', RIGHT(CONCAT('000', CAST(@NextSpecNum AS VARCHAR(10))), 3));
                    IF NOT EXISTS (SELECT 1 FROM dbo.EmployeeSpecialization WHERE SpecializationID = @SpecId)
                    BEGIN
                        IF COL_LENGTH('dbo.EmployeeSpecialization', 'SpecializationDescription') IS NOT NULL
                        BEGIN
                            INSERT INTO dbo.EmployeeSpecialization (SpecializationID, SpecializationName, SpecializationDescription, Specialization, Description, ColorTag, CreatedAt)
                            VALUES (@SpecId, @SpecName, 'Photography', @SpecName, 'Photography', 'Blue', SYSUTCDATETIME());
                        END
                        ELSE
                        BEGIN
                            INSERT INTO dbo.EmployeeSpecialization (SpecializationID, SpecializationName, Specialization)
                            VALUES (@SpecId, @SpecName, @SpecName);
                        END
                    END
                END

                IF NOT EXISTS (SELECT 1 FROM dbo.EmployeeJointSpecialization WHERE EmployeeID = @EmpId AND SpecializationID = @SpecId)
                BEGIN
                    DECLARE @JointCandidate NVARCHAR(30) = @JointId;
                    IF EXISTS (SELECT 1 FROM dbo.EmployeeJointSpecialization WHERE EmployeeJointSpecializationID = @JointCandidate)
                    BEGIN
                        SET @JointCandidate = CONCAT('EJS-', LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), '-', ''), 12));
                    END

                    IF COL_LENGTH('dbo.EmployeeJointSpecialization', 'AssignedAt') IS NOT NULL
                    BEGIN
                        INSERT INTO dbo.EmployeeJointSpecialization (EmployeeJointSpecializationID, SpecializationID, EmployeeID, AssignedAt)
                        VALUES (@JointCandidate, @SpecId, @EmpId, SYSUTCDATETIME());
                    END
                    ELSE
                    BEGIN
                        INSERT INTO dbo.EmployeeJointSpecialization (EmployeeJointSpecializationID, SpecializationID, EmployeeID)
                        VALUES (@JointCandidate, @SpecId, @EmpId);
                    END
                END";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@SpecName", specName.Trim());
            cmd.Parameters.AddWithValue("@FallbackSpecId", $"SP-{employeeId.Replace("EMP-", "", StringComparison.OrdinalIgnoreCase)}-{seq}");
            cmd.Parameters.AddWithValue("@JointId", $"EJS-{employeeId.Replace("EMP-", "", StringComparison.OrdinalIgnoreCase)}-{seq}");
            cmd.Parameters.AddWithValue("@EmpId", employeeId.Trim().ToUpperInvariant());
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            seq++;
        }
    }

    public async Task<DashboardStatsViewModel> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
    {
        var employees = await GetEmployeesAsync(cancellationToken);
        var bookings = _userService.GetBookings();
        var today = DateTime.Today;
        var endOfWeek = today.AddDays(7);

        var appointmentsToday = bookings.Count(b => b.EventDate.Date == today);
        var bookedThisWeek = bookings.Count(b => b.EventDate.Date >= today && b.EventDate.Date <= endOfWeek);

        return new DashboardStatsViewModel
        {
            AppointmentsToday = appointmentsToday,
            ActiveEmployees = employees.Count(e => e.IsActive),
            OpenSlotsThisWeek = Math.Max(0, (employees.Count(e => e.IsActive) * 7) - bookedThisWeek),
            BookedThisWeek = bookedThisWeek
        };
    }

    public Task<IReadOnlyList<DayAvailabilityViewModel>> GetAvailabilityAsync(int days, CancellationToken cancellationToken = default)
    {
        var bookings = _userService.GetBookings();
        var result = new List<DayAvailabilityViewModel>();
        if (bookings.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<DayAvailabilityViewModel>>(result);
        }

        var startDate = DateOnly.FromDateTime(DateTime.Today);
        for (int i = 0; i < days; i++)
        {
            var date = startDate.AddDays(i);
            var dayBookings = bookings.Where(b => DateOnly.FromDateTime(b.EventDate) == date).ToList();
            var slots = new List<TimeSlotViewModel>
            {
                new(new TimeOnly(9, 0), !dayBookings.Any(b => b.EventTime.Hours == 9)),
                new(new TimeOnly(13, 0), !dayBookings.Any(b => b.EventTime.Hours == 13)),
                new(new TimeOnly(16, 0), !dayBookings.Any(b => b.EventTime.Hours == 16))
            };
            result.Add(new DayAvailabilityViewModel
            {
                Date = date,
                Slots = slots
            });
        }

        return Task.FromResult<IReadOnlyList<DayAvailabilityViewModel>>(result);
    }

    // ── Timesheet & Clock In/Out ─────────────────────────────────────────────

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

    public async Task<ClockStatusViewModel> GetClockStatusAsync(string? employeeId = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return new ClockStatusViewModel { EmployeeStudioId = employeeId ?? string.Empty };
        try
        {
            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }
            var url = string.IsNullOrWhiteSpace(employeeId) ? "api/timesheet/status" : $"api/timesheet/status?employeeId={employeeId}";
            var res = await _http.GetFromJsonAsync<ClockStatusViewModel>(url, cancellationToken);
            if (res != null) return res;
        }
        catch { }
        return new ClockStatusViewModel { EmployeeStudioId = employeeId ?? string.Empty };
    }

    public async Task<bool> ClockInAsync(string? employeeId = null, string? notes = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return false;
        try
        {
            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }
            var res = await _http.PostAsJsonAsync("api/timesheet/clock-in", new { EmployeeStudioId = employeeId, Notes = notes }, cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> ClockOutAsync(string? employeeId = null, string? notes = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return false;
        try
        {
            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }
            var res = await _http.PostAsJsonAsync("api/timesheet/clock-out", new { EmployeeStudioId = employeeId, Notes = notes }, cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<IReadOnlyList<EmployeeTimesheetHistoryDto>> GetTimesheetHistoryAsync(string? employeeId = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return new List<EmployeeTimesheetHistoryDto>();
        try
        {
            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }
            var url = string.IsNullOrWhiteSpace(employeeId) ? "api/timesheet/my-history" : $"api/timesheet/my-history?employeeId={employeeId}";
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
            var payload = new { ClockIn = clockIn, ClockOut = clockOut, AdjustmentReason = reason };
            var res = await _http.PostAsJsonAsync($"api/timesheet/{id}/adjust", payload, cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── Leaves ───────────────────────────────────────────────────────────────

    public async Task<bool> RequestLeaveAsync(string leaveType, DateOnly startDate, DateOnly endDate, string? reason, string? employeeId = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return false;
        try
        {
            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }
            var payload = new
            {
                LeaveType = leaveType,
                StartDate = startDate,
                EndDate = endDate,
                Reason = reason,
                EmployeeStudioId = employeeId
            };
            var res = await _http.PostAsJsonAsync("api/leaves/request", payload, cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<IReadOnlyList<LeaveItemViewModel>> GetPendingLeavesAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return new List<LeaveItemViewModel>();
        try
        {
            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }
            var res = await _http.GetFromJsonAsync<List<LeaveItemViewModel>>("api/leaves/pending", cancellationToken);
            if (res != null) return res;
        }
        catch { }
        return new List<LeaveItemViewModel>();
    }

    public async Task<IReadOnlyList<LeaveItemViewModel>> GetMyLeavesAsync(string? employeeId = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return new List<LeaveItemViewModel>();
        try
        {
            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }
            var url = string.IsNullOrWhiteSpace(employeeId) ? "api/leaves/my-requests" : $"api/leaves/my-requests?employeeId={employeeId}";
            var res = await _http.GetFromJsonAsync<List<LeaveItemViewModel>>(url, cancellationToken);
            if (res != null) return res;
        }
        catch { }
        return new List<LeaveItemViewModel>();
    }

    public async Task<bool> ApproveLeaveAsync(int leaveId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return false;
        try
        {
            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }
            var res = await _http.PostAsync($"api/leaves/{leaveId}/approve", null, cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> RejectLeaveAsync(int leaveId, string? reason = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return false;
        try
        {
            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }
            var res = await _http.PostAsJsonAsync($"api/leaves/{leaveId}/reject", new { Reason = reason }, cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private class ProblemDetails
    {
        public string? Detail { get; set; }
    }
}
