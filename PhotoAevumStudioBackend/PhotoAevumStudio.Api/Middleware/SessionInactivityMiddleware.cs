using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PhotoAevumStudio.Api.Middleware;

/// <summary>
/// Enforces an inactive session timeout on authenticated requests.
/// If an authenticated user has not sent a request within the configured inactivity window,
/// the session is terminated and an HTTP 401 SESSION_TIMEOUT ProblemDetails response is returned.
/// </summary>
public class SessionInactivityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SessionInactivityMiddleware> _logger;
    private readonly TimeSpan _timeoutDuration;

    private static readonly string[] ExemptPaths =
    {
        "/api/auth/login",
        "/api/auth/logout",
        "/api/auth/register",
        "/api/auth/register-customer",
        "/api/auth/send-verification",
        "/api/auth/verify-code",
        "/api/auth/forgot-password",
        "/api/auth/reset-password",
        "/api/auth/initial-password-change",
        "/api/auth/first-login/change-password",
        "/health",
        "/health/db"
    };

    public SessionInactivityMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        IConfiguration configuration,
        ILogger<SessionInactivityMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;

        int timeoutMinutes = configuration.GetValue<int?>("Session:TimeoutMinutes") ?? 15;
        if (timeoutMinutes <= 0)
        {
            timeoutMinutes = 15;
        }
        _timeoutDuration = TimeSpan.FromMinutes(timeoutMinutes);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip non-API or exempt endpoints
        if (string.Equals(path, "/", StringComparison.OrdinalIgnoreCase) ||
            ExemptPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            string? userId = context.User.FindFirstValue("studio_id")
                             ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                             ?? context.User.FindFirstValue(ClaimTypes.Name);

            if (!string.IsNullOrWhiteSpace(userId))
            {
                var cacheKey = $"SessionActivity_{userId.ToUpperInvariant()}";

                if (_cache.TryGetValue(cacheKey, out DateTime lastActivityUtc))
                {
                    var idleTime = DateTime.UtcNow - lastActivityUtc;
                    if (idleTime > _timeoutDuration)
                    {
                        _logger.LogWarning("Session expired due to inactivity for user {UserId} (idle: {IdleMinutes:F1}m > allowed: {AllowedMinutes}m).",
                            userId, idleTime.TotalMinutes, _timeoutDuration.TotalMinutes);

                        _cache.Remove(cacheKey);

                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/problem+json";
                        await context.Response.WriteAsJsonAsync(new
                        {
                            type = "https://photoaevum.studio/errors/session-timeout",
                            title = "Session Expired",
                            status = 401,
                            detail = "Your session has expired due to inactivity. Please log in again.",
                            code = "SESSION_TIMEOUT"
                        });
                        return;
                    }
                }

                // Update activity timestamp
                _cache.Set(cacheKey, DateTime.UtcNow, TimeSpan.FromHours(4));
            }
        }

        await _next(context);
    }
}
