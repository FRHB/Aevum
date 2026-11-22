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

        _connectionString = new[]
            {
                configuration.GetConnectionString("DefaultConnection"),
                configuration["ConnectionStrings__DefaultConnection"],
                configuration["AEVUM_DB_CONNECTION"],
                Environment.GetEnvironmentVariable("AEVUM_DB_CONNECTION"),
                Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
            }.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim()
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

    }



    public Task<IReadOnlyList<EmployeeViewModel>> GetEmployeesAsync(CancellationToken cancellationToken = default) =>

        GetEmployeesAsync(includeArchived: false, cancellationToken);



    public async Task<IReadOnlyList<EmployeeViewModel>> GetEmployeesAsync(bool includeArchived, CancellationToken cancellationToken = default)

    {

        if (cancellationToken.IsCancellationRequested)

        {

            return GetLocalFallbackEmployees(includeArchived);

        }



        try

        {

            try

            {

                await _tokenStore.GetTokenAsync(cancellationToken);

            }

            catch (OperationCanceledException)

            {

                return GetLocalFallbackEmployees(includeArchived);

            }

            catch { }



            List<EmployeeViewModel> list;

            try

            {

                var url = includeArchived ? "api/management/employees?includeArchived=true" : "api/management/employees";

                var result = await _http.GetFromJsonAsync<List<EmployeeViewModel>>(url, cancellationToken);

                list = result ?? new List<EmployeeViewModel>();

            }

            catch

            {

                list = GetLocalFallbackEmployees(includeArchived);

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

            return GetLocalFallbackEmployees(includeArchived);

        }

        catch

        {

            return GetLocalFallbackEmployees(includeArchived);

        }

    }



    public async Task<IReadOnlyList<EmployeeViewModel>> GetArchivedEmployeesAsync(CancellationToken cancellationToken = default)

    {

        var all = await GetEmployeesAsync(includeArchived: true, cancellationToken);

        return all.Where(e => e.Status.Equals("Archive", StringComparison.OrdinalIgnoreCase) ||

                              e.Status.Equals("Archived", StringComparison.OrdinalIgnoreCase) ||

                              e.Status.Equals("Deleted", StringComparison.OrdinalIgnoreCase))

                  .ToList();

    }



    private List<EmployeeViewModel> GetLocalFallbackEmployees(bool includeArchived = false)

    {

        try

        {

            var dbEmployees = _userService.GetEmployeeSpecializations(includeArchived);

            return dbEmployees.Select(e => new EmployeeViewModel

            {

                Id = e.Id,

                EmployeeId = e.EmployeeId,

                FirstName = e.FirstName,

                LastName = e.LastName,

                MiddleName = e.MiddleName,

                Email = e.Email,

                Role = e.Role,

                Address = e.Address,

                BirthDate = e.BirthDate,

                BirthYear = e.BirthYear,

                DepartmentId = e.DepartmentId,

                Department = e.Department,

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



    public Task<IReadOnlyList<SpecializationLookupItem>> GetSpecializationLookupsAsync(CancellationToken cancellationToken = default)

    {

        try

        {

            var lookups = _userService.GetSpecializationLookups();

            return Task.FromResult<IReadOnlyList<SpecializationLookupItem>>(lookups);

        }

        catch

        {

            return Task.FromResult<IReadOnlyList<SpecializationLookupItem>>(Array.Empty<SpecializationLookupItem>());

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

        if (!string.IsNullOrWhiteSpace(r.Email) && !_userService.IsEmailUnique(r.Email, r.EmployeeId))

        {

            return new SaveEmployeeResult(false, "This email address is already registered under another account role. Please use a different email.");

        }



        await _tokenStore.GetTokenAsync(cancellationToken);



        int? birthYear = r.BirthDate?.Year ?? r.BirthYear;

        var assignedRole = r.Specializations is { Count: > 0 }

            ? string.Join(", ", r.Specializations.Where(s => !string.IsNullOrWhiteSpace(s)))

            : "Employee";

        var systemRole = r.Specializations != null && r.Specializations.Any(s => s.Contains("clerk", StringComparison.OrdinalIgnoreCase))

            ? "Office Clerk"

            : "Employee";



        var apiPayload = new

        {

            r.FirstName,

            r.LastName,

            r.MiddleName,

            r.Email,

            r.Password,

            Role = systemRole,

            AssignedRole = assignedRole,

            Specializations = r.Specializations,

            StudioId = r.EmployeeId,

            Address = r.Address,

            BirthYear = birthYear,

            BirthDate = r.BirthDate,

            DepartmentId = r.DepartmentId,

            DaysOff = r.DaysOff

        };



        bool welcomeEmailSent = false;

        bool handbookAttached = false;

        string? emailWarning = null;



        try

        {

            var apiResponse = await _http.PostAsJsonAsync("api/auth/register", apiPayload, cancellationToken);

            if (apiResponse.IsSuccessStatusCode)

            {

                try

                {

                    var payload = await apiResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: cancellationToken);

                    welcomeEmailSent = payload.TryGetProperty("welcomeEmailSent", out var wes) && wes.ValueKind == System.Text.Json.JsonValueKind.True;

                    handbookAttached = payload.TryGetProperty("handbookAttached", out var ha) && ha.ValueKind == System.Text.Json.JsonValueKind.True;

                    if (payload.TryGetProperty("emailWarning", out var ew) && ew.ValueKind == System.Text.Json.JsonValueKind.String)

                        emailWarning = ew.GetString();

                }

                catch

                {

                    welcomeEmailSent = true;

                    handbookAttached = true;

                }

            }

            else

            {

                emailWarning = $"Employee account was saved, but the backend email service returned HTTP {(int)apiResponse.StatusCode} and could not send the welcome email and Employee Handbook PDF. Please retry sending the welcome email.";

            }

        }

        catch

        {

            // API call failed, attempt database fallback

            emailWarning = "Employee account was saved via database fallback, but the backend email service could not be reached to send the welcome email and Employee Handbook PDF. Please retry sending the welcome email.";

        }



        // Persist full profile details, specializations, and days off in SQL

        try

        {

            await using var conn = SqlResilienceHelper.CreateConnection(_connectionString);

            await conn.OpenAsync(cancellationToken);

            await EnsureEmployeeSchemaAsync(conn, cancellationToken);



            const string sql = @"

                IF NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE UPPER(EmployeeID) = UPPER(@EmpId))

                BEGIN

                    INSERT INTO dbo.Employees (

                        EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeMiddleName, EmployeeEmail,

                        EmployeePassword, EmployeeStatus, isFirstLogin, CreatedAt, DaysOff, Address, BirthYear, BirthDate, DepartmentID

                    )

                    VALUES (

                        @EmpId, @FirstName, @LastName, @MiddleName, @Email,

                        @Password, 'Active', 1, SYSUTCDATETIME(), @DaysOff, @Address, @BirthYear, @BirthDate, @DepartmentID

                    );

                END

                ELSE

                BEGIN

                    UPDATE dbo.Employees

                    SET EmployeeFirstName = @FirstName,

                        EmployeeLastName = @LastName,

                        EmployeeMiddleName = @MiddleName,

                        EmployeeEmail = @Email,

                        DaysOff = @DaysOff,

                        Address = @Address,

                        BirthYear = @BirthYear,

                        BirthDate = @BirthDate,

                        DepartmentID = @DepartmentID

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

            cmd.Parameters.AddWithValue("@Address", (object?)r.Address?.Trim() ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@BirthYear", (object?)birthYear ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@BirthDate", (object?)r.BirthDate?.Date ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@DepartmentID", (object?)r.DepartmentId?.Trim() ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken);



            await PersistEmployeeSpecializationsAsync(r.EmployeeId, r.Specializations ?? Array.Empty<string>(), cancellationToken);

            await PersistEmployeeDaysOffAsync(r.EmployeeId, r.DaysOff, cancellationToken);

            return new SaveEmployeeResult(
                Success: true,
                Error: null,
                WelcomeEmailSent: welcomeEmailSent,
                HandbookAttached: handbookAttached,
                EmailWarning: emailWarning);

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



        if (!string.IsNullOrWhiteSpace(r.Email) && !_userService.IsEmailUnique(r.Email, r.EmployeeId))

        {

            return new SaveEmployeeResult(false, "This email address is already registered under another account role. Please use a different email.");

        }



        int? birthYear = r.BirthDate?.Year ?? r.BirthYear;



        try

        {

            await using var conn = SqlResilienceHelper.CreateConnection(_connectionString);

            await conn.OpenAsync(cancellationToken);

            await EnsureEmployeeSchemaAsync(conn, cancellationToken);



            const string sql = @"

                UPDATE dbo.Employees

                SET EmployeeFirstName = @FirstName,

                    EmployeeLastName = @LastName,

                    EmployeeMiddleName = @MiddleName,

                    EmployeeEmail = @Email,

                    DaysOff = @DaysOff,

                    Address = @Address,

                    BirthYear = @BirthYear,

                    BirthDate = @BirthDate,

                    DepartmentID = @DepartmentID,

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

            cmd.Parameters.AddWithValue("@Address", (object?)r.Address?.Trim() ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@BirthYear", (object?)birthYear ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@BirthDate", (object?)r.BirthDate?.Date ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@DepartmentID", (object?)r.DepartmentId?.Trim() ?? DBNull.Value);

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



            await PersistEmployeeSpecializationsAsync(r.EmployeeId, r.Specializations ?? Array.Empty<string>(), cancellationToken);

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



        public async Task<bool> ArchiveEmployeeAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId)) return false;
        var cleanId = employeeId.Trim().ToUpperInvariant();

        try
        {
            await using var conn = SqlResilienceHelper.CreateConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            const string sql = @"
                IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.Employees
                    SET EmployeeStatus = 'Archive',
                        Status = 'Archive'
                    WHERE UPPER(EmployeeID) = @EmpId;

                    IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                    BEGIN
                        INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedByType, ActionDetails, LoggedAt)
                        VALUES ('STATUS_CHANGE', 'Employee', @EmpId, 'Admin', 'Archived employee record', SYSUTCDATETIME());
                    END
                END";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@EmpId", cleanId);
            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
            return rows > 0;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return _userService.ArchiveEmployee(cleanId);
        }
    }

    public async Task<bool> RestoreEmployeeAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId)) return false;
        var cleanId = employeeId.Trim().ToUpperInvariant();

        try
        {
            await using var conn = SqlResilienceHelper.CreateConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            const string sql = @"
                IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.Employees
                    SET EmployeeStatus = 'Active',
                        Status = 'Active'
                    WHERE UPPER(EmployeeID) = @EmpId;

                    IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                    BEGIN
                        INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedByType, ActionDetails, LoggedAt)
                        VALUES ('STATUS_CHANGE', 'Employee', @EmpId, 'Admin', 'Restored employee to Active', SYSUTCDATETIME());
                    END
                END";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@EmpId", cleanId);
            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
            return rows > 0;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return _userService.RestoreEmployee(cleanId);
        }
    }

    public async Task<bool> DeleteEmployeeAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId)) return false;
        var cleanId = employeeId.Trim().ToUpperInvariant();

        try
        {
            await using var conn = SqlResilienceHelper.CreateConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            const string cleanupSql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.HistoryLog SET EmployeeID = NULL WHERE UPPER(EmployeeID) = @EmpId;
                    UPDATE dbo.HistoryLog SET ChangedByEmployeeID = NULL WHERE UPPER(ChangedByEmployeeID) = @EmpId;
                END
                IF OBJECT_ID(N'dbo.EmployeeTimesheets', N'U') IS NOT NULL
                    DELETE FROM dbo.EmployeeTimesheets WHERE UPPER(EmployeeStudioId) = @EmpId;
                IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NOT NULL
                    DELETE FROM dbo.EmployeeLeaves WHERE UPPER(EmployeeStudioId) = @EmpId;
                IF OBJECT_ID(N'dbo.EmployeeJointSpecialization', N'U') IS NOT NULL
                    DELETE FROM dbo.EmployeeJointSpecialization WHERE UPPER(EmployeeID) = @EmpId;
                IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
                    DELETE FROM dbo.AspNetUsers WHERE UPPER(UserName) = @EmpId;
                IF OBJECT_ID(N'dbo.spDeleteEmployee', N'P') IS NOT NULL
                    EXEC dbo.spDeleteEmployee @EmployeeID = @EmpId;
                ELSE IF OBJECT_ID(N'dbo.sp_DeleteEmployee', N'P') IS NOT NULL
                    EXEC dbo.sp_DeleteEmployee @EmployeeID = @EmpId;
                ELSE
                BEGIN
                    DELETE FROM dbo.Employees WHERE UPPER(EmployeeID) = @EmpId;
                END
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedByType, ActionDetails, LoggedAt)
                    VALUES ('DELETE', 'Employee', @EmpId, 'Admin', 'Permanently deleted employee', SYSUTCDATETIME());
                END";

            await using var cmd = new SqlCommand(cleanupSql, conn);
            cmd.Parameters.AddWithValue("@EmpId", cleanId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return _userService.DeleteEmployee(cleanId);
        }
    }

    public async Task<(bool Success, string Message)> ResetEmployeePasswordAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return (false, "Employee ID is required.");

        var cleanId = employeeId.Trim().ToUpperInvariant();
        try
        {
            await _tokenStore.GetTokenAsync(cancellationToken);
            var response = await _http.PostAsync($"api/management/employees/{Uri.EscapeDataString(cleanId)}/reset-password", null, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    var payload = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: cancellationToken);
                    if (payload.TryGetProperty("message", out var msgProp) && !string.IsNullOrWhiteSpace(msgProp.GetString()))
                        return (true, msgProp.GetString()!);
                }
                catch { }
                return (true, $"Password for {cleanId} has been reset to their initial creation password.");
            }

            if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
                return (false, "Unauthorized: Only Administrators can reset staff passwords.");

            var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return (false, string.IsNullOrWhiteSpace(errBody) ? $"Password reset failed ({(int)response.StatusCode})." : errBody);
        }
        catch (OperationCanceledException)
        {
            return (false, "Operation was cancelled.");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to reset password: {ex.Message}");
        }
    }

    public async Task<SaveEmployeeResult> ResendEmployeeWelcomeEmailAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return new SaveEmployeeResult(false, "Employee ID is required.", false, false, "Employee ID is required.");

        var cleanId = employeeId.Trim().ToUpperInvariant();
        try
        {
            await _tokenStore.GetTokenAsync(cancellationToken);
            var response = await _http.PostAsync($"api/management/employees/{Uri.EscapeDataString(cleanId)}/resend-welcome", null, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: cancellationToken);
                bool emailSent = payload.TryGetProperty("emailSent", out var es) && es.ValueKind == System.Text.Json.JsonValueKind.True;
                bool handbookAttached = payload.TryGetProperty("handbookAttached", out var ha) && ha.ValueKind == System.Text.Json.JsonValueKind.True;
                string? warning = payload.TryGetProperty("warning", out var w) && w.ValueKind == System.Text.Json.JsonValueKind.String ? w.GetString() : null;
                string? message = payload.TryGetProperty("message", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.String ? m.GetString() : null;
                if (!emailSent && string.IsNullOrWhiteSpace(warning))
                    warning = message ?? "Could not deliver welcome email and Employee Handbook PDF.";
                return new SaveEmployeeResult(emailSent && handbookAttached, emailSent ? warning : (warning ?? message), emailSent, handbookAttached, warning);
            }

            if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
                return new SaveEmployeeResult(false, "Unauthorized: Only Administrators can resend employee welcome emails.", false, false, "Unauthorized.");

            var errBody = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(cancellationToken: cancellationToken);
            var detail = errBody?.Detail ?? $"Failed to resend welcome email (HTTP {(int)response.StatusCode}).";
            return new SaveEmployeeResult(false, detail, false, false, detail);
        }
        catch (OperationCanceledException)
        {
            return new SaveEmployeeResult(false, "Operation was cancelled.", false, false, "Operation was cancelled.");
        }
        catch (Exception ex)
        {
            return new SaveEmployeeResult(false, $"Failed to resend welcome email: {ex.Message}", false, false, ex.Message);
        }
    }

    private static volatile bool _employeeSchemaAligned;



    private static async Task EnsureEmployeeSchemaAsync(SqlConnection conn, CancellationToken ct)

    {

        if (_employeeSchemaAligned) return;



        try

        {

            const string sql = @"

                IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL

                BEGIN

                    IF COL_LENGTH(N'dbo.Employees', N'BirthDate') IS NULL

                        EXEC(N'ALTER TABLE dbo.Employees ADD BirthDate date NULL');

                    IF COL_LENGTH(N'dbo.Employees', N'BirthYear') IS NULL

                        EXEC(N'ALTER TABLE dbo.Employees ADD BirthYear int NULL');

                    IF COL_LENGTH(N'dbo.Employees', N'Address') IS NULL

                        EXEC(N'ALTER TABLE dbo.Employees ADD Address nvarchar(500) NULL');

                END";

            await using var cmd = new SqlCommand(sql, conn);

            await cmd.ExecuteNonQueryAsync(ct);

            _employeeSchemaAligned = true;

        }

        catch

        {

            // Best effort

        }

    }



    private async Task PersistEmployeeDaysOffAsync(string employeeId, string daysOff, CancellationToken cancellationToken)

    {

        if (string.IsNullOrWhiteSpace(daysOff)) return;

        try

        {

            await using var conn = SqlResilienceHelper.CreateConnection(_connectionString);

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



        await using var conn = SqlResilienceHelper.CreateConnection(_connectionString);

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

                            EXEC sp_executesql

                                N'INSERT INTO dbo.EmployeeSpecialization (SpecializationID, SpecializationName, SpecializationDescription, Specialization, Description, ColorTag, CreatedAt)

                                  VALUES (@SpecId, @SpecName, ''Photography'', @SpecName, ''Photography'', ''Blue'', SYSUTCDATETIME())',

                                N'@SpecId nvarchar(30), @SpecName nvarchar(255)',

                                @SpecId = @SpecId, @SpecName = @SpecName;

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

                        EXEC sp_executesql

                            N'INSERT INTO dbo.EmployeeJointSpecialization (EmployeeJointSpecializationID, SpecializationID, EmployeeID, AssignedAt)

                              VALUES (@JointCandidate, @SpecId, @EmpId, SYSUTCDATETIME())',

                            N'@JointCandidate nvarchar(30), @SpecId nvarchar(30), @EmpId nvarchar(30)',

                            @JointCandidate = @JointCandidate, @SpecId = @SpecId, @EmpId = @EmpId;

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



        try

        {

            var validSpecs = specializations.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var json = System.Text.Json.JsonSerializer.Serialize(validSpecs);

            const string syncSql = @"

                IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Employees', N'Specializations') IS NOT NULL

                BEGIN

                    UPDATE dbo.Employees SET Specializations = @Specs WHERE UPPER(EmployeeID) = UPPER(@EmpId);

                END";

            await using var syncCmd = new SqlCommand(syncSql, conn);

            syncCmd.Parameters.AddWithValue("@Specs", json);

            syncCmd.Parameters.AddWithValue("@EmpId", employeeId.Trim().ToUpperInvariant());

            await syncCmd.ExecuteNonQueryAsync(cancellationToken);



            // Synchronize DepartmentID in dbo.Employees based on assigned specializations

            const string deptSyncSql = @"

                IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Employees', N'DepartmentID') IS NOT NULL

                BEGIN

                    UPDATE e

                    SET e.DepartmentID = COALESCE(es.DepartmentID, e.DepartmentID)

                    FROM dbo.Employees e

                    INNER JOIN dbo.EmployeeJointSpecialization ejs ON UPPER(ejs.EmployeeID) = UPPER(e.EmployeeID)

                    INNER JOIN dbo.EmployeeSpecialization es ON UPPER(es.SpecializationID) = UPPER(ejs.SpecializationID)

                    WHERE UPPER(e.EmployeeID) = UPPER(@EmpId) AND es.DepartmentID IS NOT NULL;

                END";

            await using var deptSyncCmd = new SqlCommand(deptSyncSql, conn);

            deptSyncCmd.Parameters.AddWithValue("@EmpId", employeeId.Trim().ToUpperInvariant());

            await deptSyncCmd.ExecuteNonQueryAsync(cancellationToken);

        }

        catch

        {

            // Best effort sync

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



    public async Task<bool> ClockInAsync(string? employeeId = null, string? notes = null, string? employeeName = null, CancellationToken cancellationToken = default)

    {

        if (cancellationToken.IsCancellationRequested) return false;

        try

        {

            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }

            var res = await _http.PostAsJsonAsync("api/timesheet/clock-in", new { EmployeeStudioId = employeeId, EmployeeName = employeeName, Notes = notes }, cancellationToken);

            return res.IsSuccessStatusCode;

        }

        catch { return false; }

    }



    public async Task<bool> ClockOutAsync(string? employeeId = null, string? notes = null, string? employeeName = null, CancellationToken cancellationToken = default)

    {

        if (cancellationToken.IsCancellationRequested) return false;

        try

        {

            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }

            var res = await _http.PostAsJsonAsync("api/timesheet/clock-out", new { EmployeeStudioId = employeeId, EmployeeName = employeeName, Notes = notes }, cancellationToken);

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

    public async Task<bool> ApproveTimesheetOvertimeAsync(int id, int? approvedOvertimeMinutes = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return false;
        try
        {
            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }
            var res = await _http.PostAsJsonAsync($"api/timesheet/{id}/approve-ot", new { ApprovedOvertimeMinutes = approvedOvertimeMinutes }, cancellationToken);
            if (res.IsSuccessStatusCode) return true;
        }
        catch { }

        try
        {
            await using var conn = SqlResilienceHelper.CreateConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            const string sql = @"
                IF COL_LENGTH(N'dbo.EmployeeTimesheets', N'ApprovedOvertimeMinutes') IS NOT NULL
                BEGIN
                    UPDATE dbo.EmployeeTimesheets
                    SET ApprovedOvertimeMinutes = CASE WHEN @Minutes IS NOT NULL AND @Minutes > 0 AND @Minutes < PendingOvertimeMinutes THEN @Minutes ELSE PendingOvertimeMinutes END,
                        IsOvertimeApproved = 1,
                        OvertimeApprovedBy = N'Admin',
                        OvertimeApprovedAt = SYSUTCDATETIME(),
                        Status = CASE WHEN Status = N'Completed' THEN N'Approved' ELSE Status END
                    WHERE Id = @Id AND PendingOvertimeMinutes > 0;
                END";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@Minutes", (object?)approvedOvertimeMinutes ?? DBNull.Value);
            return await cmd.ExecuteNonQueryAsync(cancellationToken) > 0;
        }
        catch { return false; }
    }



    // ── Leaves ───────────────────────────────────────────────────────────────



    public Task<bool> RequestLeaveAsync(string leaveType, DateOnly startDate, DateOnly endDate, string? reason, string? employeeId = null, CancellationToken cancellationToken = default)
        => RequestLeaveAsync(leaveType, startDate, endDate, reason, employeeId, isHalfDay: false, attachmentName: null, cancellationToken);

    public async Task<bool> RequestLeaveAsync(string leaveType, DateOnly startDate, DateOnly endDate, string? reason, string? employeeId, bool isHalfDay, string? attachmentName = null, CancellationToken cancellationToken = default)
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
                EmployeeStudioId = employeeId,
                IsHalfDay = isHalfDay,
                AttachmentName = attachmentName
            };
            var res = await _http.PostAsJsonAsync("api/leaves/request", payload, cancellationToken);
            if (res.IsSuccessStatusCode)
                return true;

            if (res.StatusCode == System.Net.HttpStatusCode.BadRequest)
                return false;
        }
        catch { }

        // Direct SQL fallback if API server is unreachable
        try
        {
            if (endDate < startDate || string.IsNullOrWhiteSpace(employeeId))
                return false;

            await using var conn = SqlResilienceHelper.CreateConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            const string empNameSql = @"
                SELECT TOP 1
                    LTRIM(RTRIM(CONCAT(ISNULL(EmployeeFirstName, ''), ' ', ISNULL(EmployeeLastName, '')))),
                    ISNULL(DaysOff, 'Sunday')
                FROM dbo.Employees WHERE EmployeeID = @EmpId;";
            string empName = employeeId;
            string daysOff = employeeId.Equals("EMP-004", StringComparison.OrdinalIgnoreCase) ? "Sunday,Monday" : "Sunday";
            await using (var nameCmd = new SqlCommand(empNameSql, conn))
            {
                nameCmd.Parameters.AddWithValue("@EmpId", employeeId.Trim());
                await using var r = await nameCmd.ExecuteReaderAsync(cancellationToken);
                if (await r.ReadAsync(cancellationToken))
                {
                    if (!r.IsDBNull(0) && !string.IsNullOrWhiteSpace(r.GetString(0)))
                        empName = r.GetString(0);
                    if (!r.IsDBNull(1) && !string.IsNullOrWhiteSpace(r.GetString(1)))
                        daysOff = r.GetString(1);
                }
            }

            var preview = LeaveWorkdayHelper.CalculateCountableWorkdays(employeeId.Trim(), startDate, endDate, isHalfDay, daysOff);
            if (preview.CountableDays <= 0m)
                return false;

            var leaveId = "LV-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
            const string sql = @"
                IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.EmployeeLeaves', N'CountableDays') IS NOT NULL
                    BEGIN
                        INSERT INTO dbo.EmployeeLeaves (LeaveId, EmployeeStudioId, EmployeeName, LeaveType, StartDate, EndDate, CountableDays, IsHalfDay, CreditYear, AttachmentName, Reason, Status, CreatedAt)
                        VALUES (@LeaveId, @EmpId, @EmpName, @LeaveType, @StartDate, @EndDate, @CountableDays, @IsHalfDay, @CreditYear, @AttachmentName, @Reason, 'Pending', SYSUTCDATETIME());
                    END
                    ELSE
                    BEGIN
                        INSERT INTO dbo.EmployeeLeaves (LeaveId, EmployeeStudioId, EmployeeName, LeaveType, StartDate, EndDate, Reason, Status, CreatedAt)
                        VALUES (@LeaveId, @EmpId, @EmpName, @LeaveType, @StartDate, @EndDate, @Reason, 'Pending', SYSUTCDATETIME());
                    END
                END";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@LeaveId", leaveId);
            cmd.Parameters.AddWithValue("@EmpId", employeeId.Trim());
            cmd.Parameters.AddWithValue("@EmpName", empName);
            cmd.Parameters.AddWithValue("@LeaveType", leaveType);
            cmd.Parameters.AddWithValue("@StartDate", startDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@EndDate", endDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@CountableDays", preview.CountableDays);
            cmd.Parameters.AddWithValue("@IsHalfDay", isHalfDay);
            cmd.Parameters.AddWithValue("@CreditYear", startDate.Year);
            cmd.Parameters.AddWithValue("@AttachmentName", (object?)attachmentName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Reason", (object?)reason ?? DBNull.Value);
            return await cmd.ExecuteNonQueryAsync(cancellationToken) > 0;
        }
        catch { return false; }
    }

    public async Task<EmployeeLeaveBalanceSummaryViewModel> GetLeaveBalancesAsync(string employeeId, int year = 2026, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            employeeId = "EMP-003";

        var trimmedId = employeeId.Trim();
        if (year <= 0) year = 2026;

        if (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }
                var res = await _http.GetFromJsonAsync<EmployeeLeaveBalanceSummaryViewModel>($"api/leaves/balances/{Uri.EscapeDataString(trimmedId)}?year={year}", cancellationToken);
                if (res != null) return res;
            }
            catch { }
        }

        // Local fallback calculation using employee roster + filed leaves for the target year
        bool isClerk = LeaveWorkdayHelper.IsOfficeClerkEmployee(trimmedId);
        string daysOff = trimmedId.Equals("EMP-004", StringComparison.OrdinalIgnoreCase) ? "Sunday,Monday" : "Sunday";
        string empName = trimmedId;

        try
        {
            var allEmployees = await GetEmployeesAsync(cancellationToken);
            var emp = allEmployees.FirstOrDefault(e => e.EmployeeId.Equals(trimmedId, StringComparison.OrdinalIgnoreCase));
            if (emp != null)
            {
                empName = string.IsNullOrWhiteSpace(emp.FullName) ? trimmedId : emp.FullName;
                daysOff = string.IsNullOrWhiteSpace(emp.DaysOff) ? daysOff : emp.DaysOff;
                isClerk = emp.IsOfficeClerk || isClerk;
            }
        }
        catch { }

        decimal vlPending = 0m, vlUsed = 0m, slPending = 0m, slUsed = 0m;
        try
        {
            var leaves = await GetLeavesFromSqlFallbackAsync(null, trimmedId, cancellationToken);
            foreach (var lv in leaves.Where(l => l.StartDate.Year == year && (l.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase) || l.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase))))
            {
                decimal days = lv.CountableDays ?? LeaveWorkdayHelper.CalculateCountableWorkdays(trimmedId, lv.StartDate, lv.EndDate, lv.IsHalfDay, daysOff).CountableDays;
                bool isPending = lv.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase);
                if (lv.LeaveType.Equals("Vacation Leave", StringComparison.OrdinalIgnoreCase) || string.Equals(lv.LeaveTypeCode, "VL", StringComparison.OrdinalIgnoreCase))
                {
                    if (isPending) vlPending += days; else vlUsed += days;
                }
                else if (lv.LeaveType.Equals("Sick Leave", StringComparison.OrdinalIgnoreCase) || string.Equals(lv.LeaveTypeCode, "SL", StringComparison.OrdinalIgnoreCase))
                {
                    if (isPending) slPending += days; else slUsed += days;
                }
            }
        }
        catch { }

        var balances = new List<LeaveBalanceItemViewModel>();
        if (isClerk)
        {
            decimal vlAvail = Math.Max(0m, 5.0m - vlPending - vlUsed);
            decimal slAvail = Math.Max(0m, 5.0m - slPending - slUsed);
            balances.Add(new LeaveBalanceItemViewModel
            {
                LeaveTypeId = 1,
                LeaveTypeCode = "VL",
                LeaveTypeName = "Vacation Leave",
                IsPaid = true,
                RequiresCredits = true,
                IsCashConvertible = true,
                TotalAllocated = 5.0m,
                PendingCredits = vlPending,
                UsedCredits = vlUsed,
                AvailableCredits = vlAvail
            });
            balances.Add(new LeaveBalanceItemViewModel
            {
                LeaveTypeId = 2,
                LeaveTypeCode = "SL",
                LeaveTypeName = "Sick Leave",
                IsPaid = true,
                RequiresCredits = true,
                IsCashConvertible = false,
                TotalAllocated = 5.0m,
                PendingCredits = slPending,
                UsedCredits = slUsed,
                AvailableCredits = slAvail
            });
        }

        decimal vlRem = balances.FirstOrDefault(b => b.LeaveTypeCode == "VL")?.AvailableCredits ?? 0m;
        decimal slRem = balances.FirstOrDefault(b => b.LeaveTypeCode == "SL")?.AvailableCredits ?? 0m;

        return new EmployeeLeaveBalanceSummaryViewModel
        {
            EmployeeId = trimmedId,
            EmployeeName = empName,
            Year = year,
            PayBasis = isClerk ? "FixedMonthly" : "CommissionPerEvent",
            IsTitleIExempt = !isClerk,
            DaysOff = daysOff,
            TotalAllocatedPaidDays = isClerk ? 10.0m : 0.0m,
            TotalRemainingPaidDays = vlRem + slRem,
            VacationLeaveRemaining = vlRem,
            SickLeaveRemaining = slRem,
            Balances = balances
        };
    }

    public async Task<CountableWorkdaysPreviewViewModel> PreviewLeaveWorkdaysAsync(
        string employeeId,
        DateOnly startDate,
        DateOnly endDate,
        bool isHalfDay = false,
        string? fallbackDaysOff = null,
        CancellationToken cancellationToken = default)
    {
        var trimmedId = string.IsNullOrWhiteSpace(employeeId) ? "EMP-003" : employeeId.Trim();

        if (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }
                var url = $"api/leaves/calculate-workdays?employeeId={Uri.EscapeDataString(trimmedId)}&startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}&isHalfDay={isHalfDay}";
                var res = await _http.GetFromJsonAsync<CountableWorkdaysPreviewViewModel>(url, cancellationToken);
                if (res != null) return res;
            }
            catch { }
        }

        var local = LeaveWorkdayHelper.CalculateCountableWorkdays(trimmedId, startDate, endDate, isHalfDay, fallbackDaysOff);

        // Also check SQL directly for overlapping assigned shoots/events if possible
        try
        {
            await using var conn = SqlResilienceHelper.CreateConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            const string sql = @"
                IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
                BEGIN
                    SELECT DISTINCT
                        ea.EventAppointmentID,
                        ISNULL(ea.EventName, N'Photoshoot Event') AS EventName,
                        ea.EventDate,
                        ISNULL(ea.EventStatus, N'Confirmed') AS EventStatus
                    FROM dbo.EventAppointment ea
                    LEFT JOIN dbo.EventEmployee ee ON ee.EventAppointmentID = ea.EventAppointmentID
                    WHERE (ea.EmployeeID = @EmpId OR ee.EmployeeID = @EmpId)
                      AND ea.EventDate BETWEEN @StartDate AND @EndDate
                      AND ISNULL(ea.EventStatus, N'Pending') NOT IN (N'Cancelled', N'Rejected', N'Archived')
                    ORDER BY ea.EventDate ASC;
                END";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@EmpId", trimmedId);
            cmd.Parameters.AddWithValue("@StartDate", startDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@EndDate", endDate.ToDateTime(TimeOnly.MinValue));
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                local.Conflicts.Add(new LeaveConflictEventViewModel
                {
                    EventAppointmentId = r.IsDBNull(0) ? string.Empty : r.GetString(0),
                    EventName = r.IsDBNull(1) ? "Photoshoot Event" : r.GetString(1),
                    EventDate = r.IsDBNull(2) ? startDate : DateOnly.FromDateTime(r.GetDateTime(2)),
                    EventStatus = r.IsDBNull(3) ? "Confirmed" : r.GetString(3)
                });
            }
            local.ConflictCount = local.Conflicts.Count;
            local.HasEventConflict = local.ConflictCount > 0;
        }
        catch { }

        return local;
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

        return await GetLeavesFromSqlFallbackAsync("Pending", null, cancellationToken);

    }

    public async Task<IReadOnlyList<LeaveItemViewModel>> GetAllLeavesAsync(string? status = null, string? employeeId = null, CancellationToken cancellationToken = default)

    {

        if (cancellationToken.IsCancellationRequested) return new List<LeaveItemViewModel>();

        try

        {

            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }

            var queryParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(status))
                queryParts.Add($"status={Uri.EscapeDataString(status.Trim())}");
            if (!string.IsNullOrWhiteSpace(employeeId))
                queryParts.Add($"employeeId={Uri.EscapeDataString(employeeId.Trim())}");

            var url = queryParts.Count > 0 ? $"api/leaves?{string.Join("&", queryParts)}" : "api/leaves";
            var res = await _http.GetFromJsonAsync<List<LeaveItemViewModel>>(url, cancellationToken);

            if (res != null) return res;

        }

        catch { }

        return await GetLeavesFromSqlFallbackAsync(status, employeeId, cancellationToken);

    }

    private async Task<IReadOnlyList<LeaveItemViewModel>> GetLeavesFromSqlFallbackAsync(string? status, string? employeeId, CancellationToken cancellationToken)
    {
        var list = new List<LeaveItemViewModel>();
        try
        {
            await using var conn = SqlResilienceHelper.CreateConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            const string sql = @"
                IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NOT NULL
                BEGIN
                    SELECT Id, LeaveId, EmployeeStudioId, EmployeeName, LeaveType, StartDate, EndDate, Reason, Status, ApprovedBy, ApprovedAt, RejectionReason, CreatedAt,
                           CASE WHEN COL_LENGTH(N'dbo.EmployeeLeaves', N'CountableDays') IS NOT NULL THEN CountableDays ELSE NULL END AS CountableDays,
                           CASE WHEN COL_LENGTH(N'dbo.EmployeeLeaves', N'IsHalfDay') IS NOT NULL THEN IsHalfDay ELSE CAST(0 AS bit) END AS IsHalfDay,
                           CASE WHEN COL_LENGTH(N'dbo.EmployeeLeaves', N'AttachmentName') IS NOT NULL THEN AttachmentName ELSE NULL END AS AttachmentName
                    FROM dbo.EmployeeLeaves
                    WHERE (@Status IS NULL OR Status = @Status)
                      AND (@EmpId IS NULL OR EmployeeStudioId = @EmpId)
                    ORDER BY CreatedAt DESC;
                END";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Status", string.IsNullOrWhiteSpace(status) ? DBNull.Value : status.Trim());
            cmd.Parameters.AddWithValue("@EmpId", string.IsNullOrWhiteSpace(employeeId) ? DBNull.Value : employeeId.Trim());
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new LeaveItemViewModel
                {
                    Id = reader.GetInt32(0),
                    LeaveId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    EmployeeStudioId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    EmployeeName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    LeaveType = reader.IsDBNull(4) ? "Sick Leave" : reader.GetString(4),
                    StartDate = DateOnly.FromDateTime(reader.GetDateTime(5)),
                    EndDate = DateOnly.FromDateTime(reader.GetDateTime(6)),
                    Reason = reader.IsDBNull(7) ? null : reader.GetString(7),
                    Status = reader.IsDBNull(8) ? "Pending" : reader.GetString(8),
                    ApprovedBy = reader.IsDBNull(9) ? null : reader.GetString(9),
                    ApprovedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                    RejectionReason = reader.IsDBNull(11) ? null : reader.GetString(11),
                    CreatedAt = reader.IsDBNull(12) ? DateTime.UtcNow : reader.GetDateTime(12),
                    CountableDays = reader.FieldCount > 13 && !reader.IsDBNull(13) ? Convert.ToDecimal(reader.GetValue(13)) : null,
                    IsHalfDay = reader.FieldCount > 14 && !reader.IsDBNull(14) && Convert.ToBoolean(reader.GetValue(14)),
                    AttachmentName = reader.FieldCount > 15 && !reader.IsDBNull(15) ? reader.GetString(15) : null
                });
            }
        }
        catch { }
        return list;
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

        return string.IsNullOrWhiteSpace(employeeId)
            ? new List<LeaveItemViewModel>()
            : await GetLeavesFromSqlFallbackAsync(null, employeeId, cancellationToken);

    }



    public async Task<bool> ApproveLeaveAsync(int leaveId, CancellationToken cancellationToken = default)

    {

        if (cancellationToken.IsCancellationRequested) return false;

        try

        {

            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }

            var res = await _http.PostAsync($"api/leaves/{leaveId}/approve", null, cancellationToken);

            if (res.IsSuccessStatusCode)
                return true;

        }

        catch { }

        try
        {
            await using var conn = SqlResilienceHelper.CreateConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            const string sql = @"
                UPDATE dbo.EmployeeLeaves
                SET Status = 'Approved',
                    ApprovedBy = ISNULL(ApprovedBy, 'Admin'),
                    ApprovedAt = SYSUTCDATETIME()
                WHERE Id = @Id;";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", leaveId);
            return await cmd.ExecuteNonQueryAsync(cancellationToken) > 0;
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

            if (res.IsSuccessStatusCode)
                return true;

        }

        catch { }

        try
        {
            await using var conn = SqlResilienceHelper.CreateConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            const string sql = @"
                UPDATE dbo.EmployeeLeaves
                SET Status = 'Rejected',
                    ApprovedBy = ISNULL(ApprovedBy, 'Admin'),
                    ApprovedAt = SYSUTCDATETIME(),
                    RejectionReason = @Reason
                WHERE Id = @Id;";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", leaveId);
            cmd.Parameters.AddWithValue("@Reason", string.IsNullOrWhiteSpace(reason) ? "Declined by management." : reason.Trim());
            return await cmd.ExecuteNonQueryAsync(cancellationToken) > 0;
        }
        catch { return false; }

    }

    public async Task<SuddenAbsenceResultViewModel?> MarkSuddenAbsenceAsync(
        string employeeStudioId,
        string? employeeName = null,
        DateOnly? date = null,
        string? leaveType = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested || string.IsNullOrWhiteSpace(employeeStudioId))
            return null;

        try
        {
            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }

            var payload = new
            {
                EmployeeStudioId = employeeStudioId.Trim(),
                EmployeeName = employeeName,
                Date = date,
                LeaveType = string.IsNullOrWhiteSpace(leaveType) ? "Emergency / Sudden Sick" : leaveType.Trim(),
                Reason = reason
            };

            var res = await _http.PostAsJsonAsync("api/leaves/sudden-absence", payload, cancellationToken);
            if (res.IsSuccessStatusCode)
            {
                var dto = await res.Content.ReadFromJsonAsync<SuddenAbsenceResultViewModel>(cancellationToken: cancellationToken);
                if (dto != null) return dto;
            }
        }
        catch { }

        return null;
    }

    public async Task<bool> UpdateEmployeeDaysOffAsync(string employeeId, string daysOff, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested || string.IsNullOrWhiteSpace(employeeId))
            return false;

        var normalized = string.IsNullOrWhiteSpace(daysOff) ? "None" : daysOff.Trim();

        try
        {
            try { await _tokenStore.GetTokenAsync(cancellationToken); } catch { }

            var res = await _http.PutAsJsonAsync($"api/management/employees/{Uri.EscapeDataString(employeeId.Trim())}/days-off", new { DaysOff = normalized }, cancellationToken);
            if (res.IsSuccessStatusCode)
                return true;
        }
        catch { }

        try
        {
            await PersistEmployeeDaysOffAsync(employeeId.Trim(), normalized, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }



    private class ProblemDetails

    {

        public string? Detail { get; set; }

    }

}

