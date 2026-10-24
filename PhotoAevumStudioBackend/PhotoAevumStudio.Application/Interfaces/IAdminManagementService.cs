using PhotoAevumStudio.Application.DTOs;

namespace PhotoAevumStudio.Application.Interfaces;

/// <summary>
/// Service contract for managing studio administrators with strict role-based access control.
/// </summary>
public interface IAdminManagementService
{
    /// <summary>
    /// Fetches administrators adhering strictly to role visibility rules:
    /// - System Admin: Sees all System Admins and regular Admins.
    /// - Admin: Sees only other Admins (System Admins are strictly filtered out).
    /// </summary>
    Task<IReadOnlyList<AdminDto>> GetAdminsAsync(string requestingUserId, bool isSystemAdmin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single administrator by unique identifier.
    /// </summary>
    Task<AdminDto?> GetAdminByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the next available sequential ID (SADM-### or ADM-###) for the given role.
    /// </summary>
    Task<string> GetNextAdminIdAsync(string role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new administrator account and dispatches credentials via email.
    /// Strictly restricted to System Admin callers.
    /// </summary>
    Task<AdminDto> CreateAdminAsync(CreateAdminDto dto, string requestingUserId, bool isSystemAdmin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates administrator profile fields.
    /// Strictly restricted:
    /// - Admins cannot edit any administrator.
    /// - System Admins can update regular Admins, but cannot edit other System Admins.
    /// </summary>
    Task<AdminDto> UpdateAdminAsync(string id, UpdateAdminDto dto, string requestingUserId, bool isSystemAdmin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an administrator account if permitted.
    /// </summary>
    Task<bool> DeleteAdminAsync(string id, string requestingUserId, bool isSystemAdmin, CancellationToken cancellationToken = default);
}
