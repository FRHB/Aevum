using PhotoAevumStudio.Application.Models;

namespace PhotoAevumStudio.Application.Interfaces;

/// <summary>
/// Manages Zoom Server-to-Server OAuth access tokens with in-memory caching and pre-expiry refresh.
/// </summary>
public interface IZoomTokenService
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates Zoom meetings via the Zoom REST API v2.
/// </summary>
public interface IZoomMeetingService
{
    Task<ZoomMeetingDetails> CreateMeetingAsync(CreateZoomMeetingRequest request, CancellationToken cancellationToken = default);
}
