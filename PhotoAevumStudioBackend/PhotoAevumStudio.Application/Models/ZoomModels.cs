using System.Text.Json.Serialization;

namespace PhotoAevumStudio.Application.Models;

/// <summary>
/// Configuration for Zoom Server-to-Server OAuth, resolved from Infisical or environment variables.
/// </summary>
public sealed class ZoomSettings
{
    public const string SectionName = "Zoom";

    public string AccountId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string? WebhookSecretToken { get; set; }

    public string OAuthBaseUrl { get; set; } = "https://zoom.us/";
    public string ApiBaseUrl { get; set; } = "https://api.zoom.us/v2/";
    public string DefaultTimezone { get; set; } = "UTC";
    public int DefaultDurationMinutes { get; set; } = 30;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountId) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);
}

public sealed record CreateZoomMeetingRequest(
    [property: JsonPropertyName("topic")] string Topic,
    [property: JsonPropertyName("start_time")] string StartTime,
    [property: JsonPropertyName("duration")] int Duration = 30,
    [property: JsonPropertyName("timezone")] string Timezone = "UTC");

public sealed record ZoomMeetingDetails(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("join_url")] string JoinUrl,
    [property: JsonPropertyName("start_url")] string StartUrl,
    [property: JsonPropertyName("topic")] string Topic,
    [property: JsonPropertyName("start_time")] string StartTime,
    [property: JsonPropertyName("duration")] int Duration,
    [property: JsonPropertyName("timezone")] string Timezone);
