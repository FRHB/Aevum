using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;
using PhotoAevumStudio.Application.Scheduling;

namespace PhotoAevumStudio.Infrastructure.Scheduling;

/// <summary>
/// Delegates <see cref="MeetingPlatform.Zoom"/> link creation to the real Zoom Server-to-Server OAuth
/// service when configured, falling back to <see cref="MockMeetingLinkService"/> in local/unconfigured environments.
/// </summary>
public sealed partial class ZoomAwareMeetingLinkService : IMeetingLinkService
{
    private readonly IZoomMeetingService _zoomMeetingService;
    private readonly ZoomSettings _zoomSettings;
    private readonly ILogger<ZoomAwareMeetingLinkService> _logger;
    private readonly IGoogleMeetAdapter? _googleMeetAdapter;
    private readonly MockMeetingLinkService _fallback = new();

    public ZoomAwareMeetingLinkService(
        IZoomMeetingService zoomMeetingService,
        IOptions<ZoomSettings> zoomOptions,
        ILogger<ZoomAwareMeetingLinkService> logger,
        IGoogleMeetAdapter? googleMeetAdapter = null)
    {
        _zoomMeetingService = zoomMeetingService;
        _zoomSettings = zoomOptions.Value;
        _logger = logger;
        _googleMeetAdapter = googleMeetAdapter;
    }

    public async Task<string> CreateMeetingLinkAsync(
        MeetingPlatform platform,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        CancellationToken cancellationToken = default)
    {
        if (platform == MeetingPlatform.GoogleMeet && _googleMeetAdapter is not null)
        {
            return await _googleMeetAdapter.GenerateMeetingLinkAsync(
                date,
                start,
                "Photo Aevum Studio Consultation",
                cancellationToken);
        }

        if (platform != MeetingPlatform.Zoom)
        {
            return await _fallback.CreateMeetingLinkAsync(platform, date, start, end, cancellationToken);
        }

        if (!_zoomSettings.IsConfigured)
        {
            _logger.LogWarning("Zoom Server-to-Server OAuth credentials are not configured; falling back to mock Zoom meeting link.");
            return await _fallback.CreateMeetingLinkAsync(platform, date, start, end, cancellationToken);
        }

        var startUtc = DateTime.SpecifyKind(date.ToDateTime(start), DateTimeKind.Utc);
        var durationMinutes = Math.Max(1, (int)Math.Round((end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes));

        var details = await _zoomMeetingService.CreateMeetingAsync(
            new CreateZoomMeetingRequest(
                Topic: "Photo Aevum Studio Consultation",
                StartTime: startUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                Duration: durationMinutes,
                Timezone: "UTC"),
            cancellationToken);

        return details.JoinUrl;
    }

    public bool IsIssuedLink(MeetingPlatform platform, string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return false;

        if (platform == MeetingPlatform.Zoom)
        {
            return RealZoomJoinUrl().IsMatch(link) || _fallback.IsIssuedLink(platform, link);
        }

        if (platform == MeetingPlatform.GoogleMeet)
        {
            return RealGoogleMeetUrl().IsMatch(link) || _fallback.IsIssuedLink(platform, link);
        }

        return _fallback.IsIssuedLink(platform, link);
    }

    [GeneratedRegex(@"^https://([a-z0-9-]+\.)?zoom\.us/j/\d+", RegexOptions.IgnoreCase)]
    private static partial Regex RealZoomJoinUrl();

    [GeneratedRegex(@"^https://meet\.google\.com/[a-z0-9-]+$", RegexOptions.IgnoreCase)]
    private static partial Regex RealGoogleMeetUrl();
}
