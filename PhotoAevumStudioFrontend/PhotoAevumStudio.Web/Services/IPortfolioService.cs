using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public interface IPortfolioService
{
    Task<List<PortfolioCategoryModel>> GetCategoriesAsync(CancellationToken ct = default);
    Task<PortfolioCategoryModel?> CreateCategoryAsync(string name, string? description, string parentSection, string colorTag, CancellationToken ct = default);
    Task<bool> DeleteCategoryAsync(string categoryId, CancellationToken ct = default);
    Task<List<PortfolioFolderModel>> GetPortfoliosAsync(string? category = null, bool? featuredOnly = null, CancellationToken ct = default);
    Task<List<PortfolioFolderModel>> GetFeaturedPortfoliosAsync(CancellationToken ct = default);
    Task<PortfolioFolderModel?> CreatePortfolioAsync(string name, string? description, string categoryId, bool isFeatured, List<(string FileName, string ContentType, byte[] Data)> images, CancellationToken ct = default);
    Task<bool> UpdatePortfolioAsync(string portfolioId, string name, string? description, string categoryId, List<(string FileName, string ContentType, byte[] Data)>? newImages = null, CancellationToken ct = default);
    Task<bool> ToggleFeatureAsync(string portfolioId, CancellationToken ct = default);
    Task<bool> DeletePortfolioAsync(string portfolioId, CancellationToken ct = default);
}
