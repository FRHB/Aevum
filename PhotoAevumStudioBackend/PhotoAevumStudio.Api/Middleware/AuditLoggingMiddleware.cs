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
            if (!context.RequestAborted.IsCancellationRequested && AuditedMethods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase) &&
                (context.Request.Path.StartsWithSegments("/api/bookings", StringComparison.OrdinalIgnoreCase) ||
                 context.Request.Path.StartsWithSegments("/api/equipments", StringComparison.OrdinalIgnoreCase) ||
                 context.Request.Path.StartsWithSegments("/api/services", StringComparison.OrdinalIgnoreCase) ||
                 context.Request.Path.StartsWithSegments("/api/management", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    string userId = context.User.FindFirstValue("studio_id")
                        ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? "Anonymous";
                    string userEmail = context.User.FindFirstValue(ClaimTypes.Email) ?? "Anonymous";
                    string? userRole = context.User.FindFirstValue(ClaimTypes.Role) ?? context.User.FindFirstValue("role");

                    string resource = "Booking";
                    if (context.Request.Path.StartsWithSegments("/api/equipments", StringComparison.OrdinalIgnoreCase))
                        resource = "Equipment";
                    else if (context.Request.Path.StartsWithSegments("/api/services", StringComparison.OrdinalIgnoreCase))
                        resource = "Services";
                    else if (context.Request.Path.StartsWithSegments("/api/management", StringComparison.OrdinalIgnoreCase))
                        resource = "Management";

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
}