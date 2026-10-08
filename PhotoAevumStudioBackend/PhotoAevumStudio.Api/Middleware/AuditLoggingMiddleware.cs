using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Api.Middleware;

public class AuditLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLoggingMiddleware> _logger;

    public AuditLoggingMiddleware(RequestDelegate next, ILogger<AuditLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
    {
        try
        {
            await _next(context);
        }
        finally
        {
            if (context.Request.Path.StartsWithSegments("/api/bookings") &&
               (context.Request.Method == "POST" || context.Request.Method == "PUT" || context.Request.Method == "DELETE"))
            {
                try
                {
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
}
