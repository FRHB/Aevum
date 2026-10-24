using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

/// <summary>
/// Client service for managing Admin records with role-based API and SQL fallback integrations.
/// </summary>
public interface IAdminService
{
    Task<IReadOnlyList<AdminViewModel>> GetAdminsAsync(CancellationToken cancellationToken = default);
    Task<string> GetNextAdminIdAsync(string role, CancellationToken cancellationToken = default);
    Task<SaveAdminResult> CreateAdminAsync(AddAdminWizardModel model, CancellationToken cancellationToken = default);
    Task<SaveAdminResult> UpdateAdminAsync(string id, EditAdminModel model, CancellationToken cancellationToken = default);
    Task<bool> DeleteAdminAsync(string id, CancellationToken cancellationToken = default);
}
