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

    // Rate Types & Multi-Rate Pricing
    Task<IReadOnlyList<RateTypeDto>> GetRateTypesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServiceRateDto>> GetServiceRatesAsync(string serviceId, CancellationToken cancellationToken = default);
    Task<bool> AddOrUpdateServiceRateAsync(string serviceId, string rateTypeId, decimal rateAmount, CancellationToken cancellationToken = default);

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

    // Sequential Custom ID Generation & Wizard Workflows
    Task<string> GetNextServiceIdAsync(CancellationToken cancellationToken = default);
    Task<string> GetNextCategoryIdAsync(CancellationToken cancellationToken = default);
    Task<string> GetNextAdditionalServiceIdAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<CategoryDto?> GetCategoryByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<ServiceCreatedResponseDto> CreateServiceAsync(CreateServiceDto dto, string? requestingAdminId = null, CancellationToken cancellationToken = default);
    Task<CategoryDto> CreateCategoryAsync(CreateCategoryDto dto, string? requestingAdminId = null, CancellationToken cancellationToken = default);
    Task<AdditionalServiceResponseDto> CreateAdditionalServiceAsync(CreateAdditionalServiceDto dto, string? requestingAdminId = null, CancellationToken cancellationToken = default);

    // Combo Package Wizard Methods
    Task<string> GetNextComboPackageIdAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BundledServiceSelectionItem>> GetAvailableServicesForBundlingAsync(CancellationToken cancellationToken = default);
    Task<ComboPackageCreatedResponseDto> CreateComboPackageWithServicesAsync(
        CreateComboPackageModel model,
        byte[]? imageBytes,
        string? imageMimeType,
        string? requestingAdminId = null,
        CancellationToken cancellationToken = default);
}
