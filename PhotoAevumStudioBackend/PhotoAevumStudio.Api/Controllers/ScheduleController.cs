using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Scheduling;

namespace PhotoAevumStudio.Api.Controllers;

/// <summary>Endpoints backing the 5-step "Book Schedule" wizard.</summary>
[ApiController]
[Route("api/schedule")]
[Authorize(Roles = AppRoles.Customer)]
public sealed class ScheduleController(IScheduleBookingService schedule, ILogger<ScheduleController> logger) : ControllerBase
{
    [HttpGet("services")]
    public async Task<ActionResult<IReadOnlyList<ScheduleServiceDto>>> GetServices(CancellationToken ct) =>
        Ok(await schedule.GetServicesAsync(ct));

    [HttpGet("availability")]
    public Task<IActionResult> GetAvailability([FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        Handle(async () => Ok(await schedule.GetMonthAvailabilityAsync(year, month, ct)));

    [HttpPost("meeting-link/preview")]
    public Task<IActionResult> PreviewMeetingLink([FromBody] MeetingLinkPreviewRequest request, CancellationToken ct) =>
        Handle(async () => Ok(await schedule.PreviewMeetingLinkAsync(request.Platform, request.Date, request.SlotStart, ct)));

    [HttpPost("bookings")]
    public Task<IActionResult> Book([FromBody] ScheduleBookingRequest request, CancellationToken ct) =>
        Handle(async () =>
        {
            var customer = new BookingCustomer(
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty,
                User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email") ?? User.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
                User.FindFirstValue("full_name") ?? "Valued Client");

            var result = await schedule.BookAsync(request, customer, ct);
            return StatusCode(StatusCodes.Status201Created, result);
        });

    private async Task<IActionResult> Handle(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (SlotUnavailableException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict, title: "SLOT_TAKEN");
        }
        catch (ScheduleValidationException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "VALIDATION_FAILED");
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return StatusCode(499); // client went away
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled scheduling error.");
            return Problem("We couldn't complete your request. Please try again.", statusCode: StatusCodes.Status500InternalServerError, title: "SCHEDULE_ERROR");
        }
    }
}

public sealed record MeetingLinkPreviewRequest(MeetingPlatform Platform, DateOnly Date, TimeOnly SlotStart);
