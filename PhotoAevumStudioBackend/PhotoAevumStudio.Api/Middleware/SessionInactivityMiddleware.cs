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
        "/api/auth/verify-reset-code",
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

            if (string.IsNullOrWhiteSpace(userId))
            {
                await WriteSessionRejectedAsync(context, "SESSION_INVALID", "Session Invalid", "Authentication token is missing user identity.");
                return;
            }

            if (IsTokenRevoked(context.User, userId))
            {
                _logger.LogWarning("Rejected revoked JWT for user {UserId}.", userId);
                await WriteSessionRejectedAsync(context, "SESSION_REVOKED", "Session Revoked", "Your session has been revoked. Please log in again.");
                return;
            }

            var cacheKey = $"SessionActivity_{userId.ToUpperInvariant()}";

            if (!_cache.TryGetValue(cacheKey, out DateTime lastActivityUtc))
            {
                _logger.LogWarning("Rejected authenticated request for user {UserId} with missing/expired session state.", userId);
                await WriteSessionRejectedAsync(context, "SESSION_TIMEOUT", "Session Expired", "Your session has expired or is no longer active. Please log in again.");
                return;
            }

            var idleTime = DateTime.UtcNow - lastActivityUtc;
            if (idleTime > _timeoutDuration)
            {
                _logger.LogWarning("Session expired due to inactivity for user {UserId} (idle: {IdleMinutes:F1}m > allowed: {AllowedMinutes}m).",
                    userId, idleTime.TotalMinutes, _timeoutDuration.TotalMinutes);

                _cache.Remove(cacheKey);
                await WriteSessionRejectedAsync(context, "SESSION_TIMEOUT", "Session Expired", "Your session has expired due to inactivity. Please log in again.");
                return;
            }

            // Update activity timestamp
            _cache.Set(cacheKey, DateTime.UtcNow, TimeSpan.FromHours(4));
        }

        await _next(context);
    }

    private bool IsTokenRevoked(ClaimsPrincipal user, string userId)
    {
        long? issuedAtEpoch = null;
        var iatClaim = user.FindFirstValue("iat");
        if (long.TryParse(iatClaim, out var parsedIat))
        {
            issuedAtEpoch = parsedIat;
        }

        var candidateKeys = new[]
        {
            userId,
            user.FindFirstValue(ClaimTypes.Name),
            user.FindFirstValue(ClaimTypes.Email)
        }
        .Where(k => !string.IsNullOrWhiteSpace(k))
        .Select(k => k!.Trim().ToUpperInvariant())
        .Distinct(StringComparer.Ordinal);

        foreach (var candidate in candidateKeys)
        {
            if (_cache.TryGetValue($"SessionRevokedBefore_{candidate}", out long revokedBeforeEpoch))
            {
                if (!issuedAtEpoch.HasValue || issuedAtEpoch.Value <= revokedBeforeEpoch)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static async Task WriteSessionRejectedAsync(HttpContext context, string code, string title, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://photoaevum.studio/errors/session-timeout",
            title,
            status = 401,
            detail,
            code
        });
    }
}
