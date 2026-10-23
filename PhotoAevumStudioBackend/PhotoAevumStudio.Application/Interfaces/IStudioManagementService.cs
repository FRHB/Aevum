using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Entities;

namespace PhotoAevumStudio.Application.Interfaces;

public interface IStudioManagementService
{
    // Employees
    Task<IReadOnlyList<Employee>> GetEmployeesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetSpecializationsAsync(CancellationToken cancellationToken = default);

    // Services (Entity based)
    Task<IReadOnlyList<StudioService>> GetServicesAsync(CancellationToken cancellationToken = default);

    // Equipment
    Task<IReadOnlyList<Equipment>> GetEquipmentAsync(CancellationToken cancellationToken = default);

    // Stored Procedure Image & Catalog Services
    Task<IReadOnlyList<ServiceSummaryDto>> GetServicesSummaryAsync(CancellationToken cancellationToken = default);
    Task<ServiceDetailDto?> GetServiceDetailWithImageAsync(string serviceId, CancellationToken cancellationToken = default);
    Task<bool> UpdateServiceImageAsync(string serviceId, byte[] imageBytes, string mimeType, string? requestingAdminId, CancellationToken cancellationToken = default);

    // Additional Services & Packages Image & Catalog
    Task<IReadOnlyList<ServiceSummaryDto>> GetAdditionalServicesSummaryAsync(CancellationToken cancellationToken = default);
    Task<ServiceDetailDto?> GetAdditionalServiceDetailWithImageAsync(string addOnId, CancellationToken cancellationToken = default);
    Task<bool> UpdateAdditionalServiceImageAsync(string addOnId, byte[] imageBytes, string mimeType, string? requestingAdminId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceSummaryDto>> GetComboPackagesSummaryAsync(CancellationToken cancellationToken = default);
    Task<ServiceDetailDto?> GetComboPackageDetailWithImageAsync(string packageId, CancellationToken cancellationToken = default);
    Task<bool> UpdateComboPackageImageAsync(string packageId, byte[] imageBytes, string mimeType, string? requestingAdminId, CancellationToken cancellationToken = default);

    // History Logs (SystemAdmin has full access)
    Task<IReadOnlyList<HistoryLogDto>> GetHistoryLogsAsync(string requestingUserId, int topCount = 100, CancellationToken cancellationToken = default);
}
