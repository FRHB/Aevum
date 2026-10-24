using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public interface IEmployeeService
{
    Task<IReadOnlyList<EmployeeViewModel>> GetEmployeesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetSpecializationsAsync(CancellationToken cancellationToken = default);
    Task<SaveEmployeeResult> CreateEmployeeAsync(NewEmployeeRequest request, CancellationToken cancellationToken = default);
    Task<DashboardStatsViewModel> GetDashboardStatsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DayAvailabilityViewModel>> GetAvailabilityAsync(int days, CancellationToken cancellationToken = default);
}
