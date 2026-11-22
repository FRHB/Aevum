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
    private readonly IPaymentNotifier? _notifier;

    public ConsultationsController(
        IConsultationNegotiationService consultationService,
        ILogger<ConsultationsController> logger,
        IPaymentNotifier? notifier = null)
    {
        _consultationService = consultationService;
        _logger = logger;
        _notifier = notifier;
    }

    private bool IsClerkOrAdmin() =>
        User.IsInRole(AppRoles.Admin) ||
        User.IsInRole(AppRoles.SystemAdmin) ||
        User.IsInRole("System Admin") ||
        User.IsInRole(AppRoles.OfficeClerk) ||
        User.IsInRole("OfficeClerk") ||
        (User.FindFirst("studio_id")?.Value?.StartsWith("CLK-", StringComparison.OrdinalIgnoreCase) ?? false);

    private string[] GetCallerIdentifiers() =>
        new[]
        {
            User.FindFirstValue("studio_id"),
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            User.FindFirstValue("sub")
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    // POST api/consultations/schedule
    // Customer schedules a new consultation call
    [HttpPost("schedule")]
    [Authorize(Roles = $"{AppRoles.Customer},{AppRoles.Admin},{AppRoles.SystemAdmin},{AppRoles.OfficeClerk}")]
    public async Task<IActionResult> Schedule(
        [FromBody] CreateConsultationRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.HasAcceptedDataPolicy)
        {
            return BadRequest(new { message = "You must read and agree to the PhotoAevum Studio Data Privacy Policy and Terms before scheduling a consultation." });
        }

        if (!IsClerkOrAdmin())
        {
            var userId = User.FindFirstValue("studio_id")
                         ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? User.FindFirstValue("sub")
                         ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized();
            request.CustomerId = userId;
        }
        else if (string.IsNullOrWhiteSpace(request.CustomerId))
        {
            var userId = User.FindFirstValue("studio_id")
                         ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? User.FindFirstValue("sub")
                         ?? string.Empty;
            request.CustomerId = userId;
        }

        try
        {
            var result = await _consultationService.CreateConsultationAsync(request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Consultation scheduling failed: {Message}", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

    // GET api/consultations
    // Clerk / Admin gets negotiations inbox
    [HttpGet]
    [Authorize(Policy = "ClerkOrAdmin")]
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
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.SystemAdmin},{AppRoles.Employee},{AppRoles.OfficeClerk},{AppRoles.Customer}")]
    public async Task<IActionResult> GetById(
        string id,
        CancellationToken cancellationToken)
    {
        var result = await _consultationService.GetNegotiationByIdAsync(id, cancellationToken);
        if (result == null) return NotFound(new { message = $"Consultation {id} not found." });

        if (!IsClerkOrAdmin())
        {
            var callerIds = GetCallerIdentifiers();
            var isCustomerOwner = callerIds.Any(cid => string.Equals(cid, result.CustomerId, StringComparison.OrdinalIgnoreCase));
            var isAssignedClerk = User.IsInRole(AppRoles.Employee) &&
                                  callerIds.Any(cid => string.Equals(cid, result.EmployeeId, StringComparison.OrdinalIgnoreCase));
            if (!isCustomerOwner && !isAssignedClerk)
                return Forbid();
        }

        return Ok(result);
    }

    // POST api/consultations/{id}/outcome
    // Clerk updates consultation deal outcome
    [HttpPost("{id}/outcome")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> UpdateOutcome(
        string id,
        [FromBody] UpdateNegotiationOutcomeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var success = await _consultationService.UpdateNegotiationOutcomeAsync(id, request, cancellationToken);
            if (!success) return NotFound(new { message = $"Consultation {id} not found." });
            return Ok(new { success = true, negotiationId = id, status = request.NegotiationStatus });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Consultation outcome update blocked: {Message}", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

    // POST api/consultations/{id}/create-event
    // Clerk executes 4-step wizard to create EventAppointment
    [HttpPost("{id}/create-event")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> CreateEvent(
        string id,
        [FromBody] CreateEventAppointmentFromNegotiationRequest request,
        CancellationToken cancellationToken)
    {
        request.NegotiationId = id;
        try
        {
            var appointment = await _consultationService.CreateEventAppointmentFromNegotiationAsync(request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, appointment);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Event creation blocked: {Message}", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

    // PUT api/consultations/appointments/{id}/roster
    [HttpPut("appointments/{id}/roster")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> UpdateRoster(
        string id,
        [FromBody] UpdateAppointmentRosterRequest request,
        CancellationToken cancellationToken)
    {
        if (User?.Identity?.IsAuthenticated == true)
        {
            request.ActorId = User.FindFirstValue("studio_id")
                              ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                              ?? User.FindFirstValue("sub")
                              ?? request.ActorId;
            request.ActorName = User.FindFirstValue("full_name")
                                ?? User.FindFirstValue(ClaimTypes.Name)
                                ?? request.ActorName;
            request.ActorRole = User.IsInRole(AppRoles.SystemAdmin) ? "SystemAdmin"
                : User.IsInRole(AppRoles.Admin) ? "Admin"
                : User.IsInRole(AppRoles.OfficeClerk) ? "Employee"
                : (string.IsNullOrWhiteSpace(request.ActorRole) ? "System" : request.ActorRole);
        }
        else if (string.IsNullOrWhiteSpace(request.ActorRole))
        {
            request.ActorRole = "System";
        }

        try
        {
            EventAppointmentDto? beforeUpdate = null;
            try
            {
                beforeUpdate = await _consultationService.GetAppointmentByIdAsync(id, cancellationToken);
            }
            catch { }

            var success = await _consultationService.UpdateAppointmentRosterAsync(id, request, cancellationToken);
            if (success && _notifier != null)
            {
                try
                {
                    var affectedIds = (beforeUpdate?.AssignedEmployees?.Select(e => e.EmployeeId) ?? Enumerable.Empty<string>())
                        .Concat(request.EmployeeIds ?? Enumerable.Empty<string>())
                        .Where(empId => !string.IsNullOrWhiteSpace(empId))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    await _notifier.EventStatusChangedAsync(id, "Updated", null, beforeUpdate?.CustomerId, cancellationToken);
                    await _notifier.RosterUpdatedAsync(id, beforeUpdate?.CustomerId, affectedIds, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SignalR RosterUpdated broadcast failed after roster update for {AppointmentId}", id);
                }
            }
            return Ok(new { success, appointmentId = id });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Roster update blocked for {AppointmentId}: {Message}", id, ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

    // POST api/consultations/appointments/{id}/crew-replacements/{requestId}/fulfill
    [HttpPost("appointments/{id}/crew-replacements/{requestId:int}/fulfill")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> FulfillCrewReplacement(
        string id,
        int requestId,
        [FromBody] FulfillCrewReplacementRequest request,
        CancellationToken cancellationToken)
    {
        if (User?.Identity?.IsAuthenticated == true)
        {
            request.ActorId = User.FindFirstValue("studio_id")
                              ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                              ?? User.FindFirstValue("sub")
                              ?? request.ActorId;
            request.ActorName = User.FindFirstValue("full_name")
                                ?? User.FindFirstValue(ClaimTypes.Name)
                                ?? request.ActorName;
            request.ActorRole = User.IsInRole(AppRoles.SystemAdmin) ? "SystemAdmin"
                : User.IsInRole(AppRoles.Admin) ? "Admin"
                : User.IsInRole(AppRoles.OfficeClerk) ? "Employee"
                : (string.IsNullOrWhiteSpace(request.ActorRole) ? "System" : request.ActorRole);
        }

        try
        {
            EventAppointmentDto? before = null;
            try { before = await _consultationService.GetAppointmentByIdAsync(id, cancellationToken); } catch { }

            var updated = await _consultationService.FulfillCrewReplacementAsync(id, requestId, request, cancellationToken);
            if (_notifier != null)
            {
                try
                {
                    var affectedIds = (before?.AssignedEmployees?.Select(e => e.EmployeeId) ?? Enumerable.Empty<string>())
                        .Concat(updated.AssignedEmployees.Select(e => e.EmployeeId))
                        .Where(empId => !string.IsNullOrWhiteSpace(empId))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    await _notifier.EventStatusChangedAsync(id, "RosterUpdated", null, updated.CustomerId, cancellationToken);
                    await _notifier.RosterUpdatedAsync(id, updated.CustomerId, affectedIds, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SignalR RosterUpdated broadcast failed after crew replacement for {AppointmentId}", id);
                }
            }
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
