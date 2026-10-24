<<<<<<< HEAD
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
            if (context.Request.Path.StartsWithSegments("/api/bookings", StringComparison.OrdinalIgnoreCase) &&
                AuditedMethods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    // Only metadata is recorded: user identity, method and path.
                    // Deliberately NOT recorded: request body, query string, any headers
                    // (especially Authorization), cookies.
                    //
                    // If a future change needs the body, you MUST do this BEFORE _next():
                    //   context.Request.EnableBuffering();
                    //   ...read it...
                    //   context.Request.Body.Position = 0;
                    // so controllers still receive the body. Never do this for /api/auth/*.
                    var auditLog = new AuditLog
                    {
                        UserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Anonymous",
                        UserEmail = context.User.FindFirstValue(ClaimTypes.Email) ?? "Anonymous",
                        Action = $"{context.Request.Method} Booking",
                        Resource = "Booking",
                        Details = $"Endpoint: {context.Request.Path}",
                        Timestamp = DateTime.UtcNow,
                        //StatusCode = context.Response.StatusCode
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
=======
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
            if (context.Request.Path.StartsWithSegments("/api/bookings", StringComparison.OrdinalIgnoreCase) &&
                AuditedMethods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    // Only metadata is recorded: user identity, method and path.
                    // Deliberately NOT recorded: request body, query string, any headers
                    // (especially Authorization), cookies.
                    //
                    // If a future change needs the body, you MUST do this BEFORE _next():
                    //   context.Request.EnableBuffering();
                    //   ...read it...
                    //   context.Request.Body.Position = 0;
                    // so controllers still receive the body. Never do this for /api/auth/*.
                    var auditLog = new AuditLog
                    {
                        UserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Anonymous",
                        UserEmail = context.User.FindFirstValue(ClaimTypes.Email) ?? "Anonymous",
                        Action = $"{context.Request.Method} Booking",
                        Resource = "Booking",
                        Details = $"Endpoint: {context.Request.Path}",
                        Timestamp = DateTime.UtcNow,
                        //StatusCode = context.Response.StatusCode
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
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
}