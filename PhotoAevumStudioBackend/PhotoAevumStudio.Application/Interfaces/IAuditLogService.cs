using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Entities;

namespace PhotoAevumStudio.Application.Interfaces;

public interface IAuditLogService
{
    Task<IReadOnlyList<AuditLog>> GetAuditLogsAsync(string requestingUserId, string? userRole = null, int top = 100, CancellationToken ct = default);
    Task<IReadOnlyList<HistoryLogDto>> GetHistoryLogsAsync(string requestingUserId, string? userRole = null, int top = 100, CancellationToken ct = default);
    Task LogAsync(string userId, string userEmail, string action, string resource, string? resourceId = null, string? details = null, CancellationToken ct = default);
}
