using System.Net.Http.Json;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public class EmployeeService : IEmployeeService
{
    private readonly HttpClient _http;

    public EmployeeService(IHttpClientFactory httpClientFactory)
    {
        // Use the authorized client which attaches the JWT
        _http = httpClientFactory.CreateClient("AevumApi.Authorized");
    }

    public async Task<IReadOnlyList<EmployeeViewModel>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _http.GetFromJsonAsync<List<EmployeeViewModel>>("api/management/employees", cancellationToken);
        return result ?? new List<EmployeeViewModel>();
    }

    public async Task<IReadOnlyList<string>> GetSpecializationsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _http.GetFromJsonAsync<List<string>>("api/management/specializations", cancellationToken);
        return result ?? new List<string>();
    }

    public async Task<SaveEmployeeResult> CreateEmployeeAsync(NewEmployeeRequest r, CancellationToken cancellationToken = default)
    {
        // POST to AuthController to register the employee
        var request = new RegistrationRequest(
            r.FirstName,
            r.LastName,
            r.MiddleName,
            r.Email,
            null, // Cellphone
            r.Password
        )
        {
            // Note: RegistrationRequest doesn't have Role/StudioId in its constructor if it was mapped for Customers.
            // But we can send it as JSON. We need a proper payload matching the API.
        };

        // We should construct a dynamic or proper DTO matching the API's RegisterRequest
        var apiPayload = new
        {
            r.FirstName,
            r.LastName,
            r.MiddleName,
            r.Email,
            r.Password,
            Role = "Employee",
            r.EmployeeId // maps to StudioId
        };

        var response = await _http.PostAsJsonAsync("api/auth/register", apiPayload, cancellationToken);
        
        if (response.IsSuccessStatusCode)
        {
            return new SaveEmployeeResult(true);
        }
        else
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: cancellationToken);
            return new SaveEmployeeResult(false, problem?.Detail ?? "Failed to create employee.");
        }
    }

    public async Task<DashboardStatsViewModel> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
    {
        // Placeholder for phase 4 or full implementation
        return new DashboardStatsViewModel
        {
            AppointmentsToday = 0,
            ActiveEmployees = (await GetEmployeesAsync(cancellationToken)).Count(e => e.IsActive),
            OpenSlotsThisWeek = 0,
            BookedThisWeek = 0
        };
    }

    public async Task<IReadOnlyList<DayAvailabilityViewModel>> GetAvailabilityAsync(int days, CancellationToken cancellationToken = default)
    {
        // Placeholder for phase 4 schedule implementation
        return new List<DayAvailabilityViewModel>();
    }

    private class ProblemDetails
    {
        public string? Detail { get; set; }
    }
}
