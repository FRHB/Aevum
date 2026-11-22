using System.Security.Claims;
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
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.SystemAdmin},{AppRoles.OfficeClerk},{AppRoles.Employee},{AppRoles.Customer}")]
    public async Task<IActionResult> Create(
        [FromBody] CreateBookingRequest request,
        CancellationToken cancellationToken)
    {
        var isPrivileged = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.SystemAdmin) || User.IsInRole(AppRoles.OfficeClerk);
        if (!isPrivileged)
        {
            var callerId = User.FindFirstValue("studio_id")
                           ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                           ?? User.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(callerId))
                return Unauthorized();
            request.CustomerId = callerId;
        }

        var result = await _bookingService.CreateEventAppointmentAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // GET api/bookings
    [HttpGet]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.SystemAdmin},{AppRoles.OfficeClerk},{AppRoles.Employee},{AppRoles.Customer}")]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var bookings = await _bookingService.GetBookingsAsync(cancellationToken);
        var isStaffOrAdmin = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.SystemAdmin) ||
                             User.IsInRole(AppRoles.OfficeClerk) || User.IsInRole(AppRoles.Employee);
        if (!isStaffOrAdmin)
        {
            var callerIds = new[]
            {
                User.FindFirstValue("studio_id"),
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                User.FindFirstValue("sub")
            }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

            bookings = bookings
                .Where(b => callerIds.Any(id => string.Equals(id, b.CustomerId, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        return Ok(bookings);
    }
}
