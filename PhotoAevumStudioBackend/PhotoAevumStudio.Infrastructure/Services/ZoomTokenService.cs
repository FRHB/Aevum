using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;

namespace PhotoAevumStudio.Infrastructure.Services;

public sealed class ZoomApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public int? ZoomErrorCode { get; }

    public ZoomApiException(string message, HttpStatusCode statusCode, int? zoomErrorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ZoomErrorCode = zoomErrorCode;
    }
}

internal static class ZoomErrorParser
{
    internal static (int? Code, string Message) Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return (null, "Empty error response from Zoom.");

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            int? code = root.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var c) ? c : null;

            string? msg = null;
            if (root.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
                msg = msgEl.GetString();
            else if (root.TryGetProperty("reason", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.String)
                msg = reasonEl.GetString();
            else if (root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String)
                msg = errEl.GetString();

            return (code, string.IsNullOrWhiteSpace(msg) ? body : msg);
        }
        catch (JsonException)
        {
            return (null, body.Length > 300 ? body[..300] : body);
        }
    }
}

public sealed class ZoomTokenService : IZoomTokenService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ZoomSettings _settings;
    private readonly ILogger<ZoomTokenService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public ZoomTokenService(
        HttpClient httpClient,
        IOptions<ZoomSettings> options,
        ILogger<ZoomTokenService> logger,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        if (!string.IsNullOrEmpty(_cachedToken) && now < _expiresAt)
        {
            return _cachedToken;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            now = _timeProvider.GetUtcNow();
            if (!string.IsNullOrEmpty(_cachedToken) && now < _expiresAt)
            {
                return _cachedToken;
            }

            ValidateCredentials();

            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));

            using var request = new HttpRequestMessage(HttpMethod.Post, "oauth/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "account_credentials",
                    ["account_id"] = _settings.AccountId
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var (zoomCode, zoomMessage) = ZoomErrorParser.Parse(body);
                _logger.LogError(
                    "Zoom OAuth token request failed with status {StatusCode} (ZoomCode: {ZoomCode}): {Reason}. Ensure the Server-to-Server OAuth app is activated in Zoom Marketplace.",
                    (int)response.StatusCode,
                    zoomCode,
                    zoomMessage);

                throw new ZoomApiException(
                    $"Zoom OAuth token request failed ({(int)response.StatusCode}): {zoomMessage}",
                    response.StatusCode,
                    zoomCode);
            }

            var tokenResponse = JsonSerializer.Deserialize<ZoomTokenResponse>(body);
            if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                _logger.LogError("Zoom OAuth token endpoint returned a 2xx response without an access_token.");
                throw new ZoomApiException("Zoom OAuth response did not include an access_token.", response.StatusCode);
            }

            var expiresInSeconds = tokenResponse.ExpiresIn > 0 ? tokenResponse.ExpiresIn : 3600;
            var refreshBufferSeconds = expiresInSeconds > 60 ? 60 : Math.Max(1, expiresInSeconds / 2);

            _cachedToken = tokenResponse.AccessToken;
            _expiresAt = _timeProvider.GetUtcNow().AddSeconds(expiresInSeconds - refreshBufferSeconds);

            _logger.LogInformation("Zoom Server-to-Server OAuth access token acquired (expires in {ExpiresIn}s).", expiresInSeconds);
            return _cachedToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void ValidateCredentials()
    {
        var missing = new List<string>(3);
        if (string.IsNullOrWhiteSpace(_settings.AccountId)) missing.Add("ZOOM_ACCOUNT_ID");
        if (string.IsNullOrWhiteSpace(_settings.ClientId)) missing.Add("ZOOM_CLIENT_ID");
        if (string.IsNullOrWhiteSpace(_settings.ClientSecret)) missing.Add("ZOOM_CLIENT_SECRET");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Zoom Server-to-Server OAuth is not configured. Missing Infisical/environment secrets: {string.Join(", ", missing)}.");
        }
    }

    public void Dispose() => _refreshLock.Dispose();

    private sealed record ZoomTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
