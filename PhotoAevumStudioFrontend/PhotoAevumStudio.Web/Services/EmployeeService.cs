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
            if (result is not null)
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
            if (result is not null)
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

        var response = await _http.PostAsJsonAsync("api/auth/register", apiPayload, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            await PersistEmployeeSpecializationsAsync(r.EmployeeId, r.Specializations, cancellationToken);
            return new SaveEmployeeResult(true);
        }

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: cancellationToken);
        return new SaveEmployeeResult(false, problem?.Detail ?? "Failed to create employee.");
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
                WHERE UPPER(COALESCE(NULLIF(SpecializationName, ''), Specialization)) = UPPER(@SpecName);

                IF @SpecId IS NULL
                BEGIN
                    SET @SpecId = @FallbackSpecId;
                    IF NOT EXISTS (SELECT 1 FROM dbo.EmployeeSpecialization WHERE SpecializationID = @SpecId)
                    BEGIN
                        INSERT INTO dbo.EmployeeSpecialization (SpecializationID, SpecializationName, SpecializationDescription, Specialization, Description, ColorTag, CreatedAt)
                        VALUES (@SpecId, @SpecName, 'Photography', @SpecName, 'Photography', 'Blue', SYSUTCDATETIME());
                    END
                END

                IF NOT EXISTS (SELECT 1 FROM dbo.EmployeeJointSpecialization WHERE EmployeeID = @EmpId AND SpecializationID = @SpecId)
                BEGIN
                    INSERT INTO dbo.EmployeeJointSpecialization (EmployeeJointSpecializationID, SpecializationID, EmployeeID, AssignedAt)
                    VALUES (@JointId, @SpecId, @EmpId, SYSUTCDATETIME());
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

    private class ProblemDetails
    {
        public string? Detail { get; set; }
    }
}
