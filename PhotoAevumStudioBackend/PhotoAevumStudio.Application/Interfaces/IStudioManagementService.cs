using PhotoAevumStudio.Application.Entities;

namespace PhotoAevumStudio.Application.Interfaces;

public interface IStudioManagementService
{
    // Employees
    Task<IReadOnlyList<Employee>> GetEmployeesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetSpecializationsAsync(CancellationToken cancellationToken = default);

    // Services
    Task<IReadOnlyList<StudioService>> GetServicesAsync(CancellationToken cancellationToken = default);

    // Equipment
    Task<IReadOnlyList<Equipment>> GetEquipmentAsync(CancellationToken cancellationToken = default);
}
