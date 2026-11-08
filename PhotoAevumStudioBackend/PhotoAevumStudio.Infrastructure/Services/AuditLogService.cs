using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Infrastructure.Services;

public class AuditLogService : IAuditLogService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IStudioManagementService _managementService;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(
        ApplicationDbContext dbContext,
        IStudioManagementService managementService,
        ILogger<AuditLogService> logger)
    {
        _dbContext = dbContext;
        _managementService = managementService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AuditLog>> GetAuditLogsAsync(
        string requestingUserId,
        string? userRole = null,
        int top = 100,
        CancellationToken ct = default)
    {
        bool isSadm = string.Equals(userRole, "SystemAdmin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(userRole, "System Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(userRole, "Super Admin", StringComparison.OrdinalIgnoreCase)
            || requestingUserId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase);

        bool isAdmin = !isSadm && (
            string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(userRole, "Administrator", StringComparison.OrdinalIgnoreCase)
            || requestingUserId.StartsWith("ADM-", StringComparison.OrdinalIgnoreCase));

        bool isEmp = !isSadm && !isAdmin && (
            string.Equals(userRole, "Employee", StringComparison.OrdinalIgnoreCase)
            || string.Equals(userRole, "Staff", StringComparison.OrdinalIgnoreCase)
            || string.Equals(userRole, "Office Clerk", StringComparison.OrdinalIgnoreCase)
            || string.Equals(userRole, "OfficeClerk", StringComparison.OrdinalIgnoreCase)
            || string.Equals(userRole, "Clerk", StringComparison.OrdinalIgnoreCase)
            || requestingUserId.StartsWith("EMP-", StringComparison.OrdinalIgnoreCase)
            || requestingUserId.StartsWith("CLK-", StringComparison.OrdinalIgnoreCase));

        var query = _dbContext.AuditLogs.AsNoTracking().AsQueryable();

        // 1. System Admin: full unrestricted access
        if (isSadm)
        {
            return await query
                .OrderByDescending(a => a.Timestamp)
                .Take(top)
                .ToListAsync(ct);
        }

        // 2. Administrator: operational and admin logs, exclude SystemAdmin and private customer sessions
        if (isAdmin)
        {
            query = query.Where(a =>
                !a.UserId.StartsWith("SADM-") &&
                !a.Resource.Equals("SystemAdmin") &&
                !(a.Resource.Equals("Session") && a.UserId.StartsWith("CUST-")));

            return await query
                .OrderByDescending(a => a.Timestamp)
                .Take(top)
                .ToListAsync(ct);
        }

        // 3. Employee: non-admin users should only see logs relevant to their specific role/permissions
        if (isEmp)
        {
            string empId = requestingUserId.Trim();
            query = query.Where(a =>
                !a.UserId.StartsWith("SADM-") &&
                !a.UserId.StartsWith("ADM-") &&
                (a.UserId == empId || a.ResourceId == empId || (a.Details != null && a.Details.Contains(empId))));

            return await query
                .OrderByDescending(a => a.Timestamp)
                .Take(top)
                .ToListAsync(ct);
        }

        // 4. Default / non-admin (e.g., customer)
        query = query.Where(a =>
            !a.UserId.StartsWith("SADM-") &&
            (a.UserId == requestingUserId || a.ResourceId == requestingUserId));

        return await query
            .OrderByDescending(a => a.Timestamp)
            .Take(top)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<HistoryLogDto>> GetHistoryLogsAsync(
        string requestingUserId,
        string? userRole = null,
        int top = 100,
        CancellationToken ct = default)
    {
        return await _managementService.GetHistoryLogsAsync(requestingUserId, top, ct);
    }

    public async Task LogAsync(
        string userId,
        string userEmail,
        string action,
        string resource,
        string? resourceId = null,
        string? details = null,
        CancellationToken ct = default)
    {
        try
        {
            var auditLog = new AuditLog
            {
                UserId = userId,
                UserEmail = userEmail,
                Action = action,
                Resource = resource,
                ResourceId = resourceId,
                Details = details,
                Timestamp = DateTime.UtcNow
            };

            _dbContext.AuditLogs.Add(auditLog);
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist audit log entry.");
        }
    }
}
