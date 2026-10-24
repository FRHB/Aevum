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
        await _tokenStore.GetTokenAsync(cancellationToken);

        try
        {
            var result = await _http.GetFromJsonAsync<List<EmployeeViewModel>>("api/management/employees", cancellationToken);
<<<<<<< HEAD
            if (result is not null)
=======
            if (result is not null && result.Count > 0)
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
                return result;
        }
        catch
        {
            // Fall through to direct SQL query against the same configured DefaultConnection
        }

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

    public async Task<IReadOnlyList<string>> GetSpecializationsAsync(CancellationToken cancellationToken = default)
    {
        await _tokenStore.GetTokenAsync(cancellationToken);

        try
        {
            var result = await _http.GetFromJsonAsync<List<string>>("api/management/specializations", cancellationToken);
<<<<<<< HEAD
            if (result is not null)
=======
            if (result is not null && result.Count > 0)
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
                return result;
        }
        catch
        {
            // Fall through to direct SQL query against DefaultConnection
        }

        return _userService.GetSpecializationLookups()
            .Select(s => s.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();
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

<<<<<<< HEAD
        var response = await _http.PostAsJsonAsync("api/auth/register", apiPayload, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            await PersistEmployeeSpecializationsAsync(r.EmployeeId, r.Specializations, cancellationToken);
            return new SaveEmployeeResult(true);
        }

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: cancellationToken);
        return new SaveEmployeeResult(false, problem?.Detail ?? "Failed to create employee.");
=======
        try
        {
            var response = await _http.PostAsJsonAsync("api/auth/register", apiPayload, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                await PersistEmployeeSpecializationsAsync(r.EmployeeId, r.Specializations, cancellationToken);
                return new SaveEmployeeResult(true);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: cancellationToken);
                return new SaveEmployeeResult(false, problem?.Detail ?? "Failed to create employee.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Fall through to direct SQL persistence when the API host is unavailable
        }

        try
        {
            await PersistEmployeeAccountDirectAsync(r, cancellationToken);
            await PersistEmployeeSpecializationsAsync(r.EmployeeId, r.Specializations, cancellationToken);
            return new SaveEmployeeResult(true);
        }
        catch (Exception ex)
        {
            return new SaveEmployeeResult(false, ex.Message);
        }
    }

    private async Task PersistEmployeeAccountDirectAsync(NewEmployeeRequest r, CancellationToken cancellationToken)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var empId = r.EmployeeId.Trim().ToUpperInvariant();
        var email = r.Email.Trim();

        const string checkSql = @"
            SELECT COUNT(1)
            FROM dbo.Employee
            WHERE UPPER(EmployeeID) = @EmpId OR UPPER(EmployeeEmail) = UPPER(@Email);";

        await using (var checkCmd = new SqlCommand(checkSql, conn))
        {
            checkCmd.Parameters.AddWithValue("@EmpId", empId);
            checkCmd.Parameters.AddWithValue("@Email", email);
            var existing = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(cancellationToken));
            if (existing > 0)
            {
                throw new InvalidOperationException($"An employee with ID '{empId}' or email '{email}' already exists.");
            }
        }

        const string insertSql = @"
            INSERT INTO dbo.Employee (EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeMiddleName, EmployeeEmail, EmployeePassword, EmployeeStatus, isFirstLogin)
            VALUES (@EmpId, @First, @Last, @Middle, @Email, @Password, 'Active', 1);";

        await using var insertCmd = new SqlCommand(insertSql, conn);
        insertCmd.Parameters.AddWithValue("@EmpId", empId);
        insertCmd.Parameters.AddWithValue("@First", r.FirstName.Trim());
        insertCmd.Parameters.AddWithValue("@Last", r.LastName.Trim());
        insertCmd.Parameters.AddWithValue("@Middle", (object?)r.MiddleName?.Trim() ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("@Email", email);
        insertCmd.Parameters.AddWithValue("@Password", r.Password);
        await insertCmd.ExecuteNonQueryAsync(cancellationToken);
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
    }

    private async Task PersistEmployeeSpecializationsAsync(
        string employeeId,
        IReadOnlyList<string> specializations,
        CancellationToken cancellationToken)
    {
        if (specializations is null || specializations.Count == 0)
            return;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        int seq = 1;
        foreach (var specName in specializations.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            const string sql = @"
                DECLARE @SpecId NVARCHAR(30);
                SELECT TOP 1 @SpecId = SpecializationID
                FROM dbo.EmployeeSpecialization
<<<<<<< HEAD
                WHERE UPPER(COALESCE(NULLIF(SpecializationName, ''), Specialization)) = UPPER(@SpecName);

                IF @SpecId IS NULL
                BEGIN
                    SET @SpecId = @FallbackSpecId;
                    IF NOT EXISTS (SELECT 1 FROM dbo.EmployeeSpecialization WHERE SpecializationID = @SpecId)
                    BEGIN
                        INSERT INTO dbo.EmployeeSpecialization (SpecializationID, SpecializationName, SpecializationDescription, Specialization, Description, ColorTag, CreatedAt)
                        VALUES (@SpecId, @SpecName, 'Photography', @SpecName, 'Photography', 'Blue', SYSUTCDATETIME());
=======
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
                        INSERT INTO dbo.EmployeeSpecialization (SpecializationID, SpecializationName, SpecializationDescription, Specialization, Description, ColorTag, CreatedAt)
                        VALUES (@SpecId, @SpecName, 'Photography', 'Photography', NULL, 'Blue', SYSUTCDATETIME());
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
                    END
                END

                IF NOT EXISTS (SELECT 1 FROM dbo.EmployeeJointSpecialization WHERE EmployeeID = @EmpId AND SpecializationID = @SpecId)
                BEGIN
<<<<<<< HEAD
                    INSERT INTO dbo.EmployeeJointSpecialization (EmployeeJointSpecializationID, SpecializationID, EmployeeID, AssignedAt)
                    VALUES (@JointId, @SpecId, @EmpId, SYSUTCDATETIME());
=======
                    DECLARE @JointCandidate NVARCHAR(30) = @JointId;
                    IF EXISTS (SELECT 1 FROM dbo.EmployeeJointSpecialization WHERE EmployeeJointSpecializationID = @JointCandidate)
                    BEGIN
                        SET @JointCandidate = CONCAT('EJS-', LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), '-', ''), 12));
                    END

                    INSERT INTO dbo.EmployeeJointSpecialization (EmployeeJointSpecializationID, SpecializationID, EmployeeID, AssignedAt)
                    VALUES (@JointCandidate, @SpecId, @EmpId, SYSUTCDATETIME());
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
                END";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@SpecName", specName.Trim());
<<<<<<< HEAD
            cmd.Parameters.AddWithValue("@FallbackSpecId", $"SP-{employeeId.Replace("EMP-", "", StringComparison.OrdinalIgnoreCase)}-{seq}");
=======
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
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

<<<<<<< HEAD
    private class ProblemDetails
    {
        public string? Detail { get; set; }
    }
=======
    private sealed record ProblemDetails(string? Title, string? Detail, int? Status);
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
}
