using Microsoft.AspNetCore.Mvc;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BookingsController : ControllerBase
{
    private readonly IBookingService _bookingService;

    public BookingsController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    // POST api/bookings
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateBookingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _bookingService.CreateEventAppointmentAsync(request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (BookingRuleException ex)
        {
            // Business-rule rejection from the stored procedure 
            return BadRequest(new { error = ex.Message });
        }
    }
}