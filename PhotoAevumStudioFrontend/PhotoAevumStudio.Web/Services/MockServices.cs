using System.Collections.Concurrent;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public class MockEmployeeService : IEmployeeService
{
    private readonly object _lock = new();
    private readonly List<EmployeeViewModel> _employees = new()
    {
        new() { Id = 1, EmployeeId = "PA-001", FirstName = "Maria", LastName = "Santos", Email = "maria@photoaevum.test", Role = "Senior Photographer", Specializations = { "Portrait Photography", "Retouching" } },
        new() { Id = 2, EmployeeId = "PA-002", FirstName = "Daniel", MiddleName = "R.", LastName = "Cruz", Email = "daniel@photoaevum.test", Role = "Lighting Technician", Specializations = { "Brand & Editorial", "Studio Lighting" } },
        new() { Id = 3, EmployeeId = "PA-003", FirstName = "Aiko", LastName = "Tanaka", Email = "aiko@photoaevum.test", Role = "Videographer", Specializations = { "Videography" } },
        new() { Id = 4, EmployeeId = "PA-004", FirstName = "Luis", LastName = "Reyes", Email = "luis@photoaevum.test", Role = "Photographer", Specializations = { "Event Coverage", "Portrait Photography" }, Status = "On leave" },
    };

    private static readonly string[] Specializations =
    {
        "Portrait Photography", "Brand & Editorial", "Videography",
        "Retouching", "Studio Lighting", "Event Coverage"
    };

    public async Task<IReadOnlyList<EmployeeViewModel>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(700, cancellationToken);
        lock (_lock) return _employees.ToList();
    }

    public async Task<IReadOnlyList<string>> GetSpecializationsAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(250, cancellationToken);
        return Specializations;
    }

    public async Task<SaveEmployeeResult> CreateEmployeeAsync(NewEmployeeRequest r, CancellationToken cancellationToken = default)
    {
        await Task.Delay(900, cancellationToken);

        lock (_lock)
        {
            if (_employees.Any(e => e.EmployeeId.Equals(r.EmployeeId, StringComparison.OrdinalIgnoreCase)))
                return new SaveEmployeeResult(false, $"Employee ID {r.EmployeeId} is already in use. Choose a different one.");

            if (_employees.Any(e => e.Email.Equals(r.Email, StringComparison.OrdinalIgnoreCase)))
                return new SaveEmployeeResult(false, "An employee with this email already exists.");

            var newId = _employees.Any() ? _employees.Max(e => e.Id) + 1 : 1;

            _employees.Add(new EmployeeViewModel
            {
                Id = newId,
                EmployeeId = r.EmployeeId.Trim(),
                FirstName = r.FirstName.Trim(),
                MiddleName = string.IsNullOrWhiteSpace(r.MiddleName) ? null : r.MiddleName.Trim(),
                LastName = r.LastName.Trim(),
                Email = r.Email.Trim(),
                Role = "Staff",
                Specializations = r.Specializations.ToList(),
                Status = "Active"
            });
        }
        return new SaveEmployeeResult(true);
    }

    public async Task<DashboardStatsViewModel> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(500, cancellationToken);
        var week = BuildWeek(7);
        int active;
        lock (_lock) active = _employees.Count(e => e.IsActive);

        return new DashboardStatsViewModel
        {
            AppointmentsToday = week[0].BookedCount,
            ActiveEmployees = active,
            OpenSlotsThisWeek = week.Sum(d => d.OpenCount),
            BookedThisWeek = week.Sum(d => d.BookedCount)
        };
    }

    public async Task<IReadOnlyList<DayAvailabilityViewModel>> GetAvailabilityAsync(int days, CancellationToken cancellationToken = default)
    {
        await Task.Delay(600, cancellationToken);
        return BuildWeek(days);
    }

    private static List<DayAvailabilityViewModel> BuildWeek(int days)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var result = new List<DayAvailabilityViewModel>();
        for (var offset = 0; offset < days; offset++)
        {
            var day = new DayAvailabilityViewModel { Date = today.AddDays(offset) };
            for (var hour = 9; hour <= 16; hour++)
            {
                var booked = (offset * 5 + hour) % 3 == 0;
                day.Slots.Add(new TimeSlotViewModel(new TimeOnly(hour, 0), !booked));
            }
            result.Add(day);
        }
        return result;
    }
}

public class MockAuthService : IAuthService
{
    private sealed record PendingCode(string Code, DateTime ExpiresUtc);
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, PendingCode> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RegistrationRequest> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<MockAuthService> _logger;

    public MockAuthService(ILogger<MockAuthService> logger) => _logger = logger;

    public async Task<bool> IsUserLoggedInAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(400, cancellationToken);
        return false;
    }

    public async Task<bool> SendVerificationCodeAsync(string email, CancellationToken cancellationToken = default)
    {
        await Task.Delay(1200, cancellationToken);
        var code = Random.Shared.Next(0, 1_000_000).ToString("D6");
        _pending[email] = new PendingCode(code, DateTime.UtcNow.Add(CodeLifetime));

        _logger.LogInformation("[MOCK EMAIL] Verification code for {Email}: {Code}", email, code);
        return true;
    }

    public async Task<bool> VerifyCodeAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        await Task.Delay(900, cancellationToken);
        if (!_pending.TryGetValue(email, out var pending)) return false;

        if (pending.ExpiresUtc < DateTime.UtcNow)
        {
            _pending.TryRemove(email, out _);
            return false;
        }

        if (!string.Equals(pending.Code, code, StringComparison.Ordinal)) return false;

        _pending.TryRemove(email, out _);
        return true;
    }

    public async Task<bool> RegisterAsync(RegistrationRequest request, CancellationToken cancellationToken = default)
    {
        await Task.Delay(600, cancellationToken);
        _users[request.Email] = request with { Password = "[hashed]" };
        _logger.LogInformation("[MOCK API] Registered {Email}", request.Email);
        return true;
    }
}