using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ConsultationsController : ControllerBase
{
    private readonly IConsultationNegotiationService _consultationService;
    private readonly ILogger<ConsultationsController> _logger;

    public ConsultationsController(
        IConsultationNegotiationService consultationService,
        ILogger<ConsultationsController> logger)
    {
        _consultationService = consultationService;
        _logger = logger;
    }

    // POST api/consultations/schedule
    // Customer schedules a new consultation call
    [HttpPost("schedule")]
    [Authorize(Roles = $"{AppRoles.Customer},{AppRoles.Admin},{AppRoles.Employee}")]
    public async Task<IActionResult> Schedule(
        [FromBody] CreateConsultationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId))
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
            request.CustomerId = userId;
        }

        var result = await _consultationService.CreateConsultationAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // GET api/consultations
    // Clerk / Admin gets negotiations inbox
    [HttpGet]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Employee}")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? clerkEmployeeId,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var list = await _consultationService.GetNegotiationsAsync(clerkEmployeeId, status, cancellationToken);
        return Ok(list);
    }

    // GET api/consultations/{id}
    [HttpGet("{id}")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Employee},{AppRoles.Customer}")]
    public async Task<IActionResult> GetById(
        string id,
        CancellationToken cancellationToken)
    {
        var result = await _consultationService.GetNegotiationByIdAsync(id, cancellationToken);
        if (result == null) return NotFound(new { message = $"Consultation {id} not found." });
        return Ok(result);
    }

    // POST api/consultations/{id}/outcome
    // Clerk updates consultation deal outcome
    [HttpPost("{id}/outcome")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Employee}")]
    public async Task<IActionResult> UpdateOutcome(
        string id,
        [FromBody] UpdateNegotiationOutcomeRequest request,
        CancellationToken cancellationToken)
    {
        var success = await _consultationService.UpdateNegotiationOutcomeAsync(id, request, cancellationToken);
        if (!success) return NotFound(new { message = $"Consultation {id} not found." });
        return Ok(new { success = true, negotiationId = id, status = request.NegotiationStatus });
    }

    // POST api/consultations/{id}/create-event
    // Clerk executes 4-step wizard to create EventAppointment
    [HttpPost("{id}/create-event")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Employee}")]
    public async Task<IActionResult> CreateEvent(
        string id,
        [FromBody] CreateEventAppointmentFromNegotiationRequest request,
        CancellationToken cancellationToken)
    {
        request.NegotiationId = id;
        var appointment = await _consultationService.CreateEventAppointmentFromNegotiationAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, appointment);
    }
}
