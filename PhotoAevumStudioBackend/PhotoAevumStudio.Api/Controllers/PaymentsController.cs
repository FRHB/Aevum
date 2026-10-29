using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Mvc;

using Microsoft.Extensions.Options;

using PhotoAevumStudio.Application;

using PhotoAevumStudio.Application.Auth;

using PhotoAevumStudio.Application.DTOs;

using PhotoAevumStudio.Application.Entities;

using PhotoAevumStudio.Application.Interfaces;

using PhotoAevumStudio.Application.Models;



namespace PhotoAevumStudio.Api.Controllers;



[ApiController]

[Route("api/[controller]")]

public class PaymentsController : ControllerBase

{

    private readonly IConsultationNegotiationService _consultationService;

    private readonly IPaymentService _paymentService;

    private readonly XenditSettings _xenditSettings;

    private readonly ILogger<PaymentsController> _logger;



    public PaymentsController(

        IConsultationNegotiationService consultationService,

        IPaymentService paymentService,

        IOptions<XenditSettings> xenditOptions,

        ILogger<PaymentsController> logger)

    {

        _consultationService = consultationService;

        _paymentService = paymentService;

        _xenditSettings = xenditOptions.Value;

        _logger = logger;

    }



    // GET api/payments/calculate-deposit?total={amount}

    [HttpGet("calculate-deposit")]

    [AllowAnonymous]

    public IActionResult CalculateDeposit([FromQuery] decimal total)

    {

        var breakdown = _paymentService.CalculateReservationFee(total);

        return Ok(breakdown);

    }



    // POST api/payments/submit-customer-payment

    // Endpoint for Customer Payment Submission (calculates 15% deposit and updates customer status to Cleared)

    [HttpPost("submit-customer-payment")]

    [Authorize(Roles = $"{AppRoles.Customer},{AppRoles.Admin},{AppRoles.SystemAdmin},{AppRoles.Employee},{AppRoles.OfficeClerk}")]

    public async Task<IActionResult> SubmitCustomerPayment(

        [FromBody] CustomerSubmitPaymentRequest request,

        CancellationToken cancellationToken)

    {

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

        var result = await _paymentService.SubmitCustomerPaymentAsync(request, userId, cancellationToken);

        return Ok(result);

    }



    // POST api/payments/log-employee-cash

    // Endpoint for Employee Cash Collection logging (EmployeeRemittanceStatus = PendingAdminApproval)

    [HttpPost("log-employee-cash")]

    [Authorize(Roles = $"{AppRoles.Employee},{AppRoles.Admin}")]

    public async Task<IActionResult> LogEmployeeCash(

        [FromBody] EmployeeLogCashCollectionRequest request,

        CancellationToken cancellationToken)

    {

        var empId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? request.EmployeeId;

        var empName = User.FindFirstValue("full_name") ?? User.FindFirstValue(ClaimTypes.Name) ?? request.EmployeeName;



        var fullRequest = request with

        {

            EmployeeId = string.IsNullOrWhiteSpace(request.EmployeeId) ? empId : request.EmployeeId,

            EmployeeName = string.IsNullOrWhiteSpace(request.EmployeeName) ? empName : request.EmployeeName

        };



        var result = await _paymentService.LogEmployeeCashCollectionAsync(fullRequest, empId, cancellationToken);

        return Ok(result);

    }



    // POST api/payments/{id}/approve-remittance

    // Endpoint for Admin Approval updating employee status to Cleared

    [HttpPost("{id}/approve-remittance")]

    [Authorize(Roles = AppRoles.Admin)]

    public async Task<IActionResult> ApproveRemittance(

        int id,

        [FromBody] AdminApproveRemittanceRequest? request,

        CancellationToken cancellationToken)

    {

        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? request?.AdminId ?? "Admin";

        var adminName = User.FindFirstValue("full_name") ?? User.FindFirstValue(ClaimTypes.Name) ?? request?.AdminName ?? "System Administrator";



        var result = await _paymentService.ApproveRemittanceAsync(id, adminId, adminName, request?.Notes, cancellationToken);

        return Ok(result);

    }



    // GET api/payments/pending-remittances

    // Endpoint for Admin to inspect cash payments logged by employees pending clearance

    [HttpGet("pending-remittances")]

    [Authorize(Roles = AppRoles.Admin)]

    public async Task<IActionResult> GetPendingRemittances(CancellationToken cancellationToken)

    {

        var list = await _paymentService.GetPendingRemittancesAsync(cancellationToken);

        return Ok(list);

    }



    // GET api/payments/employee-ledger

    [HttpGet("employee-ledger")]

    [Authorize(Roles = $"{AppRoles.Employee},{AppRoles.Admin}")]

    public async Task<IActionResult> GetEmployeeLedger(

        [FromQuery] string? employeeId,

        CancellationToken cancellationToken)

    {

        var currentEmpId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

        var targetEmpId = !string.IsNullOrWhiteSpace(employeeId) && User.IsInRole(AppRoles.Admin)

            ? employeeId

            : currentEmpId;



        var list = await _paymentService.GetEmployeeLedgerAsync(targetEmpId, cancellationToken);

        return Ok(list);

    }



    // GET api/payments/history

    [HttpGet("history")]

    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.SystemAdmin},{AppRoles.Employee},{AppRoles.OfficeClerk}")]

    public async Task<IActionResult> GetAllPayments(CancellationToken cancellationToken)

    {

        var list = await _paymentService.GetAllPaymentsAsync(cancellationToken);

        return Ok(list);

    }



    // GET api/payments/booking/{bookingId}

    [HttpGet("booking/{bookingId:int}")]

    [Authorize]

    public async Task<IActionResult> GetPaymentsByBooking(int bookingId, CancellationToken cancellationToken)

    {

        var list = await _paymentService.GetPaymentsByBookingAsync(bookingId, cancellationToken);

        return Ok(list);

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

    [Authorize(Roles = $"{AppRoles.Customer},{AppRoles.Admin},{AppRoles.SystemAdmin},{AppRoles.Employee},{AppRoles.OfficeClerk}")]

    public async Task<IActionResult> GetCustomerAppointments(

        [FromQuery] string? status,

        CancellationToken cancellationToken)

    {

        var isStaffOrAdmin = User.IsInRole(AppRoles.Admin) || 

                             User.IsInRole(AppRoles.SystemAdmin) || 

                             User.IsInRole(AppRoles.Employee) || 

                             User.IsInRole(AppRoles.OfficeClerk) ||

                             User.IsInRole("OfficeClerk");



        var userId = isStaffOrAdmin ? null : (User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? User.FindFirstValue("studio_id"));

        if (string.IsNullOrWhiteSpace(userId)) userId = null;



        var list = await _consultationService.GetAppointmentsAsync(userId, status, cancellationToken);

        return Ok(list);

    }



    // POST api/payments/verify-downpayment/{appointmentId}

    // Instant verification upon return from Xendit checkout

    [HttpPost("verify-downpayment/{appointmentId}")]

    [Authorize(Roles = $"{AppRoles.Customer},{AppRoles.Admin},{AppRoles.SystemAdmin},{AppRoles.Employee},{AppRoles.OfficeClerk}")]

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

    [Authorize(Roles = $"{AppRoles.Customer},{AppRoles.Admin},{AppRoles.SystemAdmin},{AppRoles.Employee},{AppRoles.OfficeClerk}")]

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

    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.SystemAdmin},{AppRoles.Employee},{AppRoles.OfficeClerk}")]

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



    // POST api/payments/simulate-webhook/{appointmentId}

    // Triggers simulated Xendit webhook callback to verify payment, settle downpayment, and send customer confirmation email

    [HttpPost("simulate-webhook/{appointmentId}")]

    [AllowAnonymous]

    public async Task<IActionResult> SimulateWebhook(

        string appointmentId,

        [FromQuery] string? paymentMethod,

        CancellationToken cancellationToken)

    {

        var appt = await _consultationService.GetAppointmentByIdAsync(appointmentId, cancellationToken);

        if (appt == null)

            return NotFound(new { message = $"Appointment {appointmentId} not found." });



        var invoiceId = appt.XenditInvoiceId ?? $"inv_sim_{appointmentId}_{Guid.NewGuid():N[..6]}";

        var updated = await _consultationService.VerifyDownpaymentAsync(appointmentId, invoiceId, paymentMethod, cancellationToken);

        _logger.LogInformation("Simulated payment webhook callback processed successfully for appointment {AppointmentId}", appointmentId);

        return Ok(updated);

    }



    // POST api/payments/xendit-webhook

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

