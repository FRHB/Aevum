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

public sealed class ZoomMeetingService : IZoomMeetingService
{
    private const int ScheduledMeetingType = 2;

    private readonly HttpClient _httpClient;
    private readonly IZoomTokenService _tokenService;
    private readonly ZoomSettings _settings;
    private readonly ILogger<ZoomMeetingService> _logger;

    public ZoomMeetingService(
        HttpClient httpClient,
        IZoomTokenService tokenService,
        IOptions<ZoomSettings> options,
        ILogger<ZoomMeetingService> logger)
    {
        _httpClient = httpClient;
        _tokenService = tokenService;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<ZoomMeetingDetails> CreateMeetingAsync(
        CreateZoomMeetingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Topic))
            throw new ArgumentException("Meeting topic is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.StartTime))
            throw new ArgumentException("Meeting start_time is required (ISO 8601 UTC string, e.g. 2026-12-01T10:00:00Z).", nameof(request));

        var duration = request.Duration > 0
            ? request.Duration
            : (_settings.DefaultDurationMinutes > 0 ? _settings.DefaultDurationMinutes : 30);

        var timezone = !string.IsNullOrWhiteSpace(request.Timezone)
            ? request.Timezone.Trim()
            : (!string.IsNullOrWhiteSpace(_settings.DefaultTimezone) ? _settings.DefaultTimezone : "UTC");

        var accessToken = await _tokenService.GetAccessTokenAsync(cancellationToken);

        var payload = new ZoomCreateMeetingPayload(
            Topic: request.Topic.Trim(),
            Type: ScheduledMeetingType,
            StartTime: request.StartTime.Trim(),
            Duration: duration,
            Timezone: timezone);

        var json = JsonSerializer.Serialize(payload);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "users/me/meetings")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (zoomCode, zoomMessage) = ZoomErrorParser.Parse(body);
            var scopeHint = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? " Verify that the Zoom Server-to-Server OAuth app has the 'meeting:write:meeting' scope enabled and is Activated in the Zoom Marketplace."
                : string.Empty;

            _logger.LogError(
                "Zoom meeting creation failed with status {StatusCode} (ZoomCode: {ZoomCode}): {Reason}.{Hint}",
                (int)response.StatusCode,
                zoomCode,
                zoomMessage,
                scopeHint);

            throw new ZoomApiException(
                $"Zoom meeting creation failed ({(int)response.StatusCode}): {zoomMessage}.{scopeHint}",
                response.StatusCode,
                zoomCode);
        }

        var created = JsonSerializer.Deserialize<ZoomCreateMeetingResponse>(body);
        if (created is null || created.Id <= 0 || string.IsNullOrWhiteSpace(created.JoinUrl))
        {
            _logger.LogError("Zoom meeting creation returned a 2xx response with incomplete meeting details.");
            throw new ZoomApiException("Zoom meeting creation response was missing required fields (id, join_url).", response.StatusCode);
        }

        _logger.LogInformation("Created Zoom meeting {MeetingId} for topic '{Topic}' at {StartTime}.", created.Id, created.Topic ?? payload.Topic, created.StartTime ?? payload.StartTime);

        return new ZoomMeetingDetails(
            Id: created.Id,
            JoinUrl: created.JoinUrl,
            StartUrl: created.StartUrl ?? string.Empty,
            Topic: created.Topic ?? payload.Topic,
            StartTime: created.StartTime ?? payload.StartTime,
            Duration: created.Duration > 0 ? created.Duration : duration,
            Timezone: created.Timezone ?? timezone);
    }

    private sealed record ZoomCreateMeetingPayload(
        [property: JsonPropertyName("topic")] string Topic,
        [property: JsonPropertyName("type")] int Type,
        [property: JsonPropertyName("start_time")] string StartTime,
        [property: JsonPropertyName("duration")] int Duration,
        [property: JsonPropertyName("timezone")] string Timezone);

    private sealed record ZoomCreateMeetingResponse(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("join_url")] string? JoinUrl,
        [property: JsonPropertyName("start_url")] string? StartUrl,
        [property: JsonPropertyName("topic")] string? Topic,
        [property: JsonPropertyName("start_time")] string? StartTime,
        [property: JsonPropertyName("duration")] int Duration,
        [property: JsonPropertyName("timezone")] string? Timezone);
}
