using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // All endpoints require a valid JWT by default
public class BookingsController : ControllerBase
{
    private readonly IBookingService _bookingService;

    public BookingsController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    // POST api/bookings
    // Customers and Employees can create bookings; Admins always can.
    [HttpPost]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Employee},{AppRoles.Customer}")]
    public async Task<IActionResult> Create(
        [FromBody] CreateBookingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _bookingService.CreateEventAppointmentAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // GET api/bookings
    [HttpGet]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Employee},{AppRoles.Customer}")]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var bookings = await _bookingService.GetBookingsAsync(cancellationToken);
        return Ok(bookings);
    }
}
