using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;
using PhotoAevumStudio.Infrastructure.Services;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/zoom/meetings")]
[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.SystemAdmin},{AppRoles.OfficeClerk},{AppRoles.Employee}")]
public sealed class ZoomMeetingsController(
    IZoomMeetingService zoomMeetings,
    ILogger<ZoomMeetingsController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateMeeting([FromBody] CreateZoomMeetingRequest request, CancellationToken ct)
    {
        try
        {
            var created = await zoomMeetings.CreateMeetingAsync(request, ct);
            return Created($"/api/zoom/meetings/{created.Id}", created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Zoom meeting creation configuration error.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
        catch (ZoomApiException ex)
        {
            return StatusCode((int)ex.StatusCode, new { error = ex.Message, zoomCode = ex.ZoomErrorCode });
        }
    }
}
