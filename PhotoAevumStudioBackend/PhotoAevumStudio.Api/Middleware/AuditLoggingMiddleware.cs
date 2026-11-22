using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Api.Middleware;

public class AuditLoggingMiddleware
{
    private static readonly string[] AuditedMethods = { "POST", "PUT", "DELETE" };

    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLoggingMiddleware> _logger;

    public AuditLoggingMiddleware(RequestDelegate next, ILogger<AuditLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
    {
        // Auth endpoints carry passwords, verification codes and tokens.
        // Never inspect or audit them: pass straight through.
        if (context.Request.Path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        try
        {
            await _next(context);
        }
        finally
        {
            if (!context.RequestAborted.IsCancellationRequested &&
                AuditedMethods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase) &&
                TryResolveAuditedResource(context.Request.Path, out var resource))
            {
                try
                {
                    string userId = context.User.FindFirstValue("studio_id")
                        ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? "Anonymous";
                    string userEmail = context.User.FindFirstValue(ClaimTypes.Email) ?? "Anonymous";
                    string? userRole = context.User.FindFirstValue(ClaimTypes.Role) ?? context.User.FindFirstValue("role");

                    var auditLog = new AuditLog
                    {
                        UserId = userId,
                        UserEmail = userEmail,
                        Action = $"{context.Request.Method} {resource}",
                        Resource = resource,
                        Details = $"Role: {userRole ?? "Unknown"}; Endpoint: {context.Request.Path}",
                        Timestamp = DateTime.UtcNow
                    };
                    dbContext.AuditLogs.Add(auditLog);
                    await dbContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to write audit log.");
                }
            }
        }
    }

    private static bool TryResolveAuditedResource(PathString path, out string resource)
    {
        if (path.StartsWithSegments("/api/bookings", StringComparison.OrdinalIgnoreCase))
        {
            resource = "Booking";
            return true;
        }
        if (path.StartsWithSegments("/api/equipments", StringComparison.OrdinalIgnoreCase))
        {
            resource = "Equipment";
            return true;
        }
        if (path.StartsWithSegments("/api/services", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/api/additionalservices", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/api/packages", StringComparison.OrdinalIgnoreCase))
        {
            resource = "Services";
            return true;
        }
        if (path.StartsWithSegments("/api/management", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/api/admins", StringComparison.OrdinalIgnoreCase))
        {
            resource = "Management";
            return true;
        }
        if (path.StartsWithSegments("/api/consultations", StringComparison.OrdinalIgnoreCase))
        {
            resource = "Consultation";
            return true;
        }
        if (path.StartsWithSegments("/api/payments", StringComparison.OrdinalIgnoreCase))
        {
            resource = "Payment";
            return true;
        }
        if (path.StartsWithSegments("/api/leaves", StringComparison.OrdinalIgnoreCase))
        {
            resource = "Leave";
            return true;
        }
        if (path.StartsWithSegments("/api/timesheet", StringComparison.OrdinalIgnoreCase))
        {
            resource = "Timesheet";
            return true;
        }
        if (path.StartsWithSegments("/api/dropbox", StringComparison.OrdinalIgnoreCase))
        {
            resource = "Dropbox";
            return true;
        }
        if (path.StartsWithSegments("/api/upload", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/api/portfolio", StringComparison.OrdinalIgnoreCase))
        {
            resource = "Media";
            return true;
        }

        resource = string.Empty;
        return false;
    }
}