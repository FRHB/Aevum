using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Api.Middleware;

public class AuditLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public AuditLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Intercept request path and method
        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method;

        // Only track mutative methods on bookings
        bool isBookingMutation = path.StartsWith("/api/bookings", StringComparison.OrdinalIgnoreCase)
                                 && (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsDelete(method) || HttpMethods.IsPatch(method));

        // Let the request proceed
        await _next(context);

        // After the request completes, if successful, log the audit
        if (isBookingMutation && context.Response.StatusCode is >= 200 and < 300)
        {
            var user = context.User;
            if (user.Identity?.IsAuthenticated == true)
            {
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "UnknownId";
                var userEmail = user.FindFirstValue(ClaimTypes.Email) ?? "UnknownEmail";

                var auditLog = new AuditLog
                {
                    UserId = userId,
                    UserEmail = userEmail,
                    Action = $"{method} Booking",
                    Resource = "Booking",
                    Details = $"Endpoint: {path}",
                    Timestamp = DateTime.UtcNow
                };

                // Use DI to get a scoped DbContext to save the log
                using var scope = context.RequestServices.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                
                db.AuditLogs.Add(auditLog);
                await db.SaveChangesAsync();
            }
        }
    }
}
