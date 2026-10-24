using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public interface IServicesApiClient
{
    Task<string> GetNextServiceIdAsync(CancellationToken cancellationToken = default);
    Task<string> GetNextCategoryIdAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CategoryLookupItem>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<(bool Success, string? ErrorMessage, string? CreatedId)> CreateServiceAsync(MainServiceWizardModel model, CancellationToken cancellationToken = default);
    Task<(bool Success, string? ErrorMessage, string? CreatedId)> CreateCategoryAsync(CategoryWizardModel model, CancellationToken cancellationToken = default);
    Task<string> GetNextAdditionalServiceIdAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAdditionalServiceCategoriesAsync(CancellationToken cancellationToken = default);
    Task<(bool Success, string? ErrorMessage, string? CreatedId)> CreateAdditionalServiceAsync(AdditionalServiceWizardFormModel model, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<string>> GetFeaturedCodesAsync(string entityType, CancellationToken cancellationToken = default);
    Task<bool> SetFeaturedAsync(string entityType, string code, bool isFeatured, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PortfolioCategoryDto>> GetPortfolioCategoriesAsync(CancellationToken cancellationToken = default);
    Task<bool> SavePortfolioCategoryAsync(PortfolioCategoryDto category, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CategoryAssociationProfile>> GetCategoryAssociationsAsync(CancellationToken cancellationToken = default);
    Task<bool> SaveCategoryAssociationsAsync(CategoryAssociationProfile profile, CancellationToken cancellationToken = default);

}
