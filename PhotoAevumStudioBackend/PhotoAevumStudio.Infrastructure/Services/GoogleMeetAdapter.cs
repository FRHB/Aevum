using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;

namespace PhotoAevumStudio.Infrastructure.Services;

public class GoogleMeetAdapter : IGoogleMeetAdapter
{
    private const string GoogleTokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string CalendarApiBaseUrl = "https://www.googleapis.com/calendar/v3";
    private const string CalendarEventsScope = "https://www.googleapis.com/auth/calendar.events";

    private readonly HttpClient _httpClient;
    private readonly GoogleMeetSettings _settings;
    private readonly ILogger<GoogleMeetAdapter> _logger;

    public GoogleMeetAdapter(IOptions<GoogleMeetSettings> options, ILogger<GoogleMeetAdapter> logger)
        : this(new HttpClient(), options, logger)
    {
    }

    public GoogleMeetAdapter(HttpClient httpClient, IOptions<GoogleMeetSettings> options, ILogger<GoogleMeetAdapter> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<string> GenerateMeetingLinkAsync(DateOnly date, TimeOnly time, string? title = null, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(_settings.Provider, "Scaffold", StringComparison.OrdinalIgnoreCase))
        {
            var accessToken = await TryAcquireAccessTokenAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                var realLink = await TryCreateCalendarConferenceAsync(accessToken, date, time, title, cancellationToken);
                if (!string.IsNullOrWhiteSpace(realLink))
                {
                    return realLink;
                }
            }
            else
            {
                _logger.LogWarning(
                    "GoogleMeetSettings Provider is '{Provider}', but neither OAuth credentials (ClientId, ClientSecret, RefreshToken) nor a valid WorkspaceServiceAccountKeyPath are configured. Falling back to scaffold Meet URL.",
                    _settings.Provider);
            }
        }

        return GenerateFallbackLink(date, time);
    }

    private async Task<string?> TryAcquireAccessTokenAsync(CancellationToken cancellationToken)
    {
        var preferServiceAccount = string.Equals(_settings.Provider, "WorkspaceServiceAccount", StringComparison.OrdinalIgnoreCase);

        if (preferServiceAccount && _settings.HasServiceAccountKey)
        {
            return await ExchangeServiceAccountJwtAsync(cancellationToken);
        }

        if (_settings.HasOAuthCredentials)
        {
            return await ExchangeRefreshTokenAsync(cancellationToken);
        }

        if (_settings.HasServiceAccountKey)
        {
            return await ExchangeServiceAccountJwtAsync(cancellationToken);
        }

        return null;
    }

    private async Task<string?> ExchangeRefreshTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _settings.ClientId.Trim(),
                ["client_secret"] = _settings.ClientSecret.Trim(),
                ["refresh_token"] = _settings.RefreshToken.Trim(),
                ["grant_type"] = "refresh_token"
            });

            using var response = await _httpClient.PostAsync(GoogleTokenEndpoint, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Google OAuth2 refresh token exchange failed ({StatusCode}): {Body}", (int)response.StatusCode, body);
                return null;
            }

            var token = JsonSerializer.Deserialize<GoogleTokenResponse>(body);
            if (string.IsNullOrWhiteSpace(token?.AccessToken))
            {
                _logger.LogError("Google OAuth2 token response did not include an access_token.");
                return null;
            }

            return token.AccessToken;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error exchanging Google OAuth2 refresh token.");
            return null;
        }
    }

    private async Task<string?> ExchangeServiceAccountJwtAsync(CancellationToken cancellationToken)
    {
        try
        {
            var keyJson = await File.ReadAllTextAsync(_settings.WorkspaceServiceAccountKeyPath, cancellationToken);
            var sa = JsonSerializer.Deserialize<ServiceAccountKeyFile>(keyJson);
            if (sa is null || string.IsNullOrWhiteSpace(sa.ClientEmail) || string.IsNullOrWhiteSpace(sa.PrivateKey))
            {
                _logger.LogError("Service account key file at {KeyPath} is missing client_email or private_key.", _settings.WorkspaceServiceAccountKeyPath);
                return null;
            }

            var tokenUri = string.IsNullOrWhiteSpace(sa.TokenUri) ? GoogleTokenEndpoint : sa.TokenUri;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var headerJson = JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT" });
            var claims = new Dictionary<string, object>
            {
                ["iss"] = sa.ClientEmail,
                ["scope"] = CalendarEventsScope,
                ["aud"] = tokenUri,
                ["iat"] = now,
                ["exp"] = now + 3600
            };

            if (!string.IsNullOrWhiteSpace(_settings.ImpersonatedUserEmail))
            {
                claims["sub"] = _settings.ImpersonatedUserEmail.Trim();
            }

            var payloadJson = JsonSerializer.Serialize(claims);
            var unsignedJwt = $"{Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson))}.{Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson))}";

            using var rsa = RSA.Create();
            rsa.ImportFromPem(sa.PrivateKey.AsSpan());
            var signature = rsa.SignData(Encoding.ASCII.GetBytes(unsignedJwt), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var signedJwt = $"{unsignedJwt}.{Base64UrlEncode(signature)}";

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = signedJwt
            });

            using var response = await _httpClient.PostAsync(tokenUri, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Google Service Account token exchange failed ({StatusCode}): {Body}", (int)response.StatusCode, body);
                return null;
            }

            var token = JsonSerializer.Deserialize<GoogleTokenResponse>(body);
            if (string.IsNullOrWhiteSpace(token?.AccessToken))
            {
                _logger.LogError("Google Service Account token response did not include an access_token.");
                return null;
            }

            return token.AccessToken;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error acquiring Google Service Account access token.");
            return null;
        }
    }

    private async Task<string?> TryCreateCalendarConferenceAsync(
        string accessToken,
        DateOnly date,
        TimeOnly time,
        string? title,
        CancellationToken cancellationToken)
    {
        try
        {
            var calendarId = string.IsNullOrWhiteSpace(_settings.CalendarId) ? "primary" : _settings.CalendarId.Trim();
            var durationMinutes = _settings.DefaultDurationMinutes > 0 ? _settings.DefaultDurationMinutes : 30;
            var timeZone = string.IsNullOrWhiteSpace(_settings.TimeZone) ? "Asia/Manila" : _settings.TimeZone.Trim();

            var startLocal = date.ToDateTime(time);
            var endLocal = startLocal.AddMinutes(durationMinutes);

            var payload = new
            {
                summary = string.IsNullOrWhiteSpace(title) ? "Photo Aevum Studio Consultation" : title.Trim(),
                start = new
                {
                    dateTime = startLocal.ToString("yyyy-MM-dd'T'HH:mm:ss"),
                    timeZone
                },
                end = new
                {
                    dateTime = endLocal.ToString("yyyy-MM-dd'T'HH:mm:ss"),
                    timeZone
                },
                conferenceData = new
                {
                    createRequest = new
                    {
                        requestId = Guid.NewGuid().ToString("N"),
                        conferenceSolutionKey = new { type = "hangoutsMeet" }
                    }
                }
            };

            var requestUri = $"{CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events?conferenceDataVersion=1";
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Google Calendar event/Meet creation failed ({StatusCode}): {Body}", (int)response.StatusCode, body);
                return null;
            }

            var created = JsonSerializer.Deserialize<CalendarEventResponse>(body);
            var meetLink = created?.HangoutLink;
            if (string.IsNullOrWhiteSpace(meetLink))
            {
                meetLink = created?.ConferenceData?.EntryPoints?
                    .FirstOrDefault(ep => string.Equals(ep.EntryPointType, "video", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(ep.Uri))
                    ?.Uri;
            }

            if (string.IsNullOrWhiteSpace(meetLink))
            {
                _logger.LogError("Google Calendar event was created but no Google Meet link was returned in hangoutLink or conferenceData.");
                return null;
            }

            _logger.LogInformation("Created real Google Meet link {Link} for consultation scheduled {Date} at {Time}.", meetLink, date, time);
            return meetLink;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error creating Google Calendar event with Meet conference.");
            return null;
        }
    }

    private string GenerateFallbackLink(DateOnly date, TimeOnly time)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.StudioFallbackBaseUrl)
            ? "https://meet.google.com/"
            : _settings.StudioFallbackBaseUrl.TrimEnd('/') + "/";

        var part1 = GenerateRandomLetters(3);
        var part2 = GenerateRandomLetters(4);
        var part3 = GenerateRandomLetters(3);
        var link = $"{baseUrl}{part1}-{part2}-{part3}";

        _logger.LogInformation("Provisioned fallback Google Meet link {Link} for consultation scheduled {Date} at {Time}.", link, date, time);
        return link;
    }

    private static string GenerateRandomLetters(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz";
        return new string(Enumerable.Range(0, length)
            .Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)])
            .ToArray());
    }

    private static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record GoogleTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken);

    private sealed record ServiceAccountKeyFile(
        [property: JsonPropertyName("client_email")] string? ClientEmail,
        [property: JsonPropertyName("private_key")] string? PrivateKey,
        [property: JsonPropertyName("token_uri")] string? TokenUri);

    private sealed record CalendarEventResponse(
        [property: JsonPropertyName("hangoutLink")] string? HangoutLink,
        [property: JsonPropertyName("conferenceData")] CalendarConferenceData? ConferenceData);

    private sealed record CalendarConferenceData(
        [property: JsonPropertyName("entryPoints")] List<CalendarEntryPoint>? EntryPoints);

    private sealed record CalendarEntryPoint(
        [property: JsonPropertyName("entryPointType")] string? EntryPointType,
        [property: JsonPropertyName("uri")] string? Uri);
}
