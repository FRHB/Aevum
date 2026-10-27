using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly IConsultationNegotiationService _consultationService;
    private readonly XenditSettings _xenditSettings;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IConsultationNegotiationService consultationService,
        IOptions<XenditSettings> xenditOptions,
        ILogger<PaymentsController> logger)
    {
        _consultationService = consultationService;
        _xenditSettings = xenditOptions.Value;
        _logger = logger;
    }

    // GET api/payments/appointment/{id}
    [HttpGet("appointment/{id}")]
    [Authorize]
    public async Task<IActionResult> GetAppointment(string id, CancellationToken cancellationToken)
    {
        var appt = await _consultationService.GetAppointmentByIdAsync(id, cancellationToken);
        if (appt == null) return NotFound(new { message = $"Appointment {id} not found." });
        return Ok(appt);
    }

    // GET api/payments/customer-appointments
    [HttpGet("customer-appointments")]
    [Authorize(Roles = $"{AppRoles.Customer},{AppRoles.Admin},{AppRoles.Employee}")]
    public async Task<IActionResult> GetCustomerAppointments(
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        var list = await _consultationService.GetAppointmentsAsync(userId, status, cancellationToken);
        return Ok(list);
    }

    // POST api/payments/verify-downpayment/{appointmentId}
    // Instant verification upon return from Xendit checkout
    [HttpPost("verify-downpayment/{appointmentId}")]
    [Authorize(Roles = $"{AppRoles.Customer},{AppRoles.Admin},{AppRoles.Employee}")]
    public async Task<IActionResult> VerifyDownpayment(
        string appointmentId,
        [FromQuery] string? invoiceId,
        [FromQuery] string? sessionId,
        CancellationToken cancellationToken)
    {
        var targetId = !string.IsNullOrWhiteSpace(invoiceId) ? invoiceId : sessionId;
        var updated = await _consultationService.VerifyDownpaymentAsync(appointmentId, targetId, cancellationToken);
        return Ok(updated);
    }

    // POST api/payments/request-extension/{appointmentId}
    // Customer requests a 30-minute hold extension
    [HttpPost("request-extension/{appointmentId}")]
    [Authorize(Roles = $"{AppRoles.Customer},{AppRoles.Admin},{AppRoles.Employee}")]
    public async Task<IActionResult> RequestExtension(
        string appointmentId,
        CancellationToken cancellationToken)
    {
        var success = await _consultationService.RequestExtensionAsync(appointmentId, cancellationToken);
        if (!success) return BadRequest(new { message = "Could not extend hold. Appointment may not be in pending downpayment state." });
        return Ok(new { success = true, appointmentId, message = "Hold extended by 30 minutes." });
    }

    // POST api/payments/complete-event/{appointmentId}
    // Team Leader records overtime and settles final balance on-site
    [HttpPost("complete-event/{appointmentId}")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Employee}")]
    public async Task<IActionResult> CompleteEvent(
        string appointmentId,
        [FromBody] CompleteEventRequest request,
        CancellationToken cancellationToken)
    {
        request.EventAppointmentId = appointmentId;
        if (string.IsNullOrWhiteSpace(request.TeamLeaderEmployeeId))
        {
            var empId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
            request.TeamLeaderEmployeeId = empId;
        }

        var result = await _consultationService.CompleteEventSettlementAsync(request, cancellationToken);
        return Ok(result);
    }

    // POST api/payments/xendit-webhook
    // Webhook listener for Xendit asynchronous notifications
    [HttpPost("xendit-webhook")]
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> XenditWebhook(
        [FromHeader(Name = "x-callback-token")] string? callbackToken,
        [FromBody] XenditInvoiceCallbackPayload payload,
        CancellationToken cancellationToken)
    {
        try
        {
            // Verify x-callback-token against configured WebhookToken
            var configuredToken = _xenditSettings.WebhookToken;
            if (!string.IsNullOrWhiteSpace(configuredToken))
            {
                if (string.IsNullOrWhiteSpace(callbackToken) ||
                    !string.Equals(callbackToken, configuredToken, StringComparison.Ordinal))
                {
                    _logger.LogWarning("Rejected Xendit callback with invalid or missing x-callback-token header.");
                    return Unauthorized(new { message = "Invalid callback token." });
                }
            }

            _logger.LogInformation("Processing Xendit callback: Invoice {InvoiceId}, ExternalId {ExternalId}, Status {Status}, Amount {Amount}",
                payload.Id, payload.ExternalId, payload.Status, payload.Amount);

            if (string.IsNullOrWhiteSpace(payload.ExternalId))
            {
                _logger.LogWarning("Xendit webhook payload missing external_id.");
                return BadRequest(new { message = "external_id is required." });
            }

            if (payload.Status.Equals("PAID", StringComparison.OrdinalIgnoreCase) ||
                payload.Status.Equals("SETTLED", StringComparison.OrdinalIgnoreCase))
            {
                await _consultationService.VerifyDownpaymentAsync(payload.ExternalId, payload.Id, cancellationToken);
            }
            else if (payload.Status.Equals("EXPIRED", StringComparison.OrdinalIgnoreCase))
            {
                await _consultationService.HandlePaymentExpiredAsync(payload.ExternalId, payload.Id, cancellationToken);
            }

            return Ok(new { status = "SUCCESS", invoice_id = payload.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Xendit webhook for Invoice {InvoiceId}", payload?.Id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Error processing webhook." });
        }
    }
}
