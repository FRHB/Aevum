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
    private readonly IPendingPaymentService _pendingPayments;
    private readonly IPaymentNotifier _notifier;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;



    public PaymentsController(

        IConsultationNegotiationService consultationService,

        IPaymentService paymentService,

        IOptions<XenditSettings> xenditOptions,

        ILogger<PaymentsController> logger,
        IPendingPaymentService pendingPayments,
        IPaymentNotifier notifier,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)

    {

        _consultationService = consultationService;

        _paymentService = paymentService;

        _xenditSettings = xenditOptions.Value;

        _logger = logger;
        _pendingPayments = pendingPayments;
        _notifier = notifier;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;

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

    // GET api/payments/team-leader-events
    // Returns ongoing/active events assigned to the authenticated employee as Team Leader (or all if admin queries)
    [HttpGet("team-leader-events")]
    [Authorize(Roles = $"{AppRoles.Employee},{AppRoles.OfficeClerk},{AppRoles.Admin},{AppRoles.SystemAdmin}")]
    public async Task<IActionResult> GetTeamLeaderEvents(
        [FromQuery] string? employeeId,
        CancellationToken cancellationToken)
    {
        var isAdmin = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.SystemAdmin);
        var claimEmpId = User.FindFirstValue("studio_id")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        var effectiveEmployeeId = isAdmin && !string.IsNullOrWhiteSpace(employeeId)
            ? employeeId
            : (!string.IsNullOrWhiteSpace(claimEmpId) ? claimEmpId : employeeId);

        var items = await _consultationService.GetTeamLeaderEventsAsync(effectiveEmployeeId, cancellationToken);
        return Ok(items);
    }

    // POST api/payments/team-leader-events/{appointmentId}/status
    // Updates the Team Leader's Ongoing Event stage: Preparing, On Going, Post Event, Pending Payment
    [HttpPost("team-leader-events/{appointmentId}/status")]
    [Authorize(Roles = $"{AppRoles.Employee},{AppRoles.OfficeClerk},{AppRoles.Admin},{AppRoles.SystemAdmin}")]
    public async Task<IActionResult> UpdateTeamLeaderEventStatus(
        string appointmentId,
        [FromBody] UpdateTeamLeaderStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EmployeeId))
        {
            request.EmployeeId = User.FindFirstValue("studio_id")
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");
        }

        try
        {
            var updated = await _consultationService.UpdateTeamLeaderEventStatusAsync(appointmentId, request, cancellationToken);
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
        // Dev-only shortcut: never available once a real Xendit secret key is configured.
        if (!IsMockXendit())
            return NotFound();

        if (appointmentId.EndsWith("-FINAL", StringComparison.OrdinalIgnoreCase))
        {
            var finalConf = await _pendingPayments.ConfirmByReferenceAsync(appointmentId, null, null, "QRPH", cancellationToken);
            if (finalConf != null)
            {
                await HandlePostPaymentConfirmationAsync(finalConf, appointmentId, "QRPH", cancellationToken);
                return Ok(await _consultationService.GetAppointmentByIdAsync(finalConf.AppointmentId, cancellationToken));
            }
            return NotFound(new { message = $"Final settlement reference {appointmentId} not found or already paid." });
        }

        var appt = await _consultationService.GetAppointmentByIdAsync(appointmentId, cancellationToken);
        if (appt == null)
            return NotFound(new { message = $"Appointment {appointmentId} not found." });
        if (appt.EventStatus == "Pending Payment")
        {
            var confirmation = await _pendingPayments.ConfirmByReferenceAsync(appointmentId, null, null, "QRPH", cancellationToken);
            if (confirmation != null)
                await HandlePostPaymentConfirmationAsync(confirmation, appointmentId, "QRPH", cancellationToken);
            return Ok(await _consultationService.GetAppointmentByIdAsync(appointmentId, cancellationToken));
        }



        var invoiceId = appt.XenditInvoiceId ?? $"inv_sim_{appointmentId}_{Guid.NewGuid():N[..6]}";

        var effectiveMethod = !string.IsNullOrWhiteSpace(paymentMethod) && !paymentMethod.Contains("Card", StringComparison.OrdinalIgnoreCase)
            ? paymentMethod
            : "QR Ph";
        var updated = await _consultationService.VerifyDownpaymentAsync(appointmentId, invoiceId, effectiveMethod, cancellationToken);

        _logger.LogInformation("Simulated payment webhook callback processed successfully for appointment {AppointmentId}", appointmentId);

        return Ok(updated);

    }

    // POST api/payments/{appointmentId}/update-status
    // Dynamically updates booking status during payment flow (e.g. Deal_Won, Deal_Lost, Rescheduled)
    [HttpPost("{appointmentId}/update-status")]
    [AllowAnonymous]
    public async Task<IActionResult> UpdateBookingStatus(
        string appointmentId,
        [FromBody] UpdatePaymentBookingStatusRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _consultationService.UpdateBookingStatusFromPaymentAsync(appointmentId, request, cancellationToken);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update booking status for appointment {AppointmentId}", appointmentId);
            return StatusCode(500, new { message = ex.Message });
        }
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

                // Active Channel: Restrict automated webhook verification strictly to the QR Ph / E-Wallet channel
                var allowedChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "QRPH", "GCASH", "PAYMAYA", "GRABPAY", "SHOPEEPAY", "QR Ph", "EWALLET", "XENDIT_QR", "Xendit_Sandbox"
                };

                var channel = payload.PaymentChannel ?? payload.PaymentMethod ?? "QRPH";
                if (!string.IsNullOrWhiteSpace(payload.PaymentChannel) && !allowedChannels.Contains(payload.PaymentChannel) &&
                    !string.IsNullOrWhiteSpace(payload.PaymentMethod) && !allowedChannels.Contains(payload.PaymentMethod))
                {
                    _logger.LogWarning("Webhook payment rejected for disallowed channel: {Channel} / {Method}. Only QR Ph / E-Wallets are permitted.",
                        payload.PaymentChannel, payload.PaymentMethod);
                    return BadRequest(new { message = "Payment channel not supported. Reservation fee must be paid via QR Ph or supported E-Wallet." });
                }

                await _consultationService.VerifyDownpaymentAsync(payload.ExternalId, payload.Id, channel, cancellationToken);

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

    // POST api/payments/simulate-qr/{externalId}
    // POST api/payment/simulate-qr/{externalId}
    // Dev/test only: asks Xendit to simulate a QR payment. The secret key stays server-side.
    [HttpPost("simulate-qr/{externalId}")]
    [HttpPost("/api/payment/simulate-qr/{externalId}")]
    [AllowAnonymous]
    public async Task<IActionResult> SimulateQrPayment(
        string externalId,
        [FromQuery] decimal? amount,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalId))
            return BadRequest(new { message = "externalId is required." });

        var key = _xenditSettings.SecretKey;
        if (string.IsNullOrWhiteSpace(key) || key.Contains("mock", StringComparison.OrdinalIgnoreCase))
            key = _configuration["XenditSettings:SecretKey"]
                ?? _configuration["XenditSettings__SecretKey"]
                ?? _configuration["XENDIT_SECRET_KEY"]
                ?? key;

        if (string.IsNullOrWhiteSpace(key) || key.Contains("mock", StringComparison.OrdinalIgnoreCase))
        {
            var confirmed = await _pendingPayments.ConfirmByReferenceAsync(externalId, null, amount, "QRPH", cancellationToken);
            if (confirmed != null)
            {
                await HandlePostPaymentConfirmationAsync(confirmed, externalId, "QRPH", cancellationToken);
                return Ok(new { status = "SIMULATED", externalId, xenditStatus = 200 });
            }

            var mockAppt = await _consultationService.GetAppointmentByIdAsync(externalId, cancellationToken);
            if (mockAppt != null)
            {
                var simInvoiceId = mockAppt.XenditInvoiceId ?? $"inv_sim_{externalId}_{Guid.NewGuid():N[..6]}";
                await _consultationService.VerifyDownpaymentAsync(externalId, simInvoiceId, "QR Ph", cancellationToken);
                try { await _notifier.PaymentCompletedAsync(externalId, cancellationToken); } catch { }
                return Ok(new { status = "SIMULATED", externalId, xenditStatus = 200 });
            }

            return Conflict(new { message = "A Xendit test secret key (xnd_development_...) is required to simulate QR payments." });
        }

        // Simulation is a test-mode feature; refuse to run with a live key.
        if (!key.StartsWith("xnd_development", StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "QR simulation is only available with a Xendit test key." });

        decimal? effectiveAmount = amount;
        string? resolvedQrId = externalId.StartsWith("qr_", StringComparison.OrdinalIgnoreCase) ? externalId : null;
        if (resolvedQrId is null || !effectiveAmount.HasValue)
        {
            var appt = await _consultationService.GetAppointmentByIdAsync(externalId, cancellationToken);
            if (appt != null)
            {
                if (!effectiveAmount.HasValue && appt.DownpaymentAmount > 0)
                    effectiveAmount = appt.DownpaymentAmount;
                if (resolvedQrId is null && !string.IsNullOrWhiteSpace(appt.XenditInvoiceId) &&
                    appt.XenditInvoiceId.StartsWith("qr_", StringComparison.OrdinalIgnoreCase))
                    resolvedQrId = appt.XenditInvoiceId;
            }

            if (!effectiveAmount.HasValue)
            {
                var qrBooking = await _pendingPayments.GetQrPaymentAsync(externalId, cancellationToken);
                if (qrBooking is { Amount: > 0 })
                    effectiveAmount = qrBooking.Amount;
            }
        }

        var client = _httpClientFactory.CreateClient();
        var authHeader = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{key}:")));
        var payloadObj = effectiveAmount.HasValue ? new { amount = effectiveAmount.Value } : (object)new { };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.xendit.co/qr_codes/{Uri.EscapeDataString(externalId)}/payments/simulate");
        request.Headers.Authorization = authHeader;
        if (externalId.StartsWith("qr_", StringComparison.OrdinalIgnoreCase))
            request.Headers.Add("api-version", "2022-07-31");
        request.Content = JsonContent.Create(payloadObj);

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var finalStatus = (int)response.StatusCode;

        // If the QR code was created under api-version 2022-07-31, Xendit keys /simulate by qr_id rather than external_id.
        if (!response.IsSuccessStatusCode && resolvedQrId != null && !string.Equals(resolvedQrId, externalId, StringComparison.OrdinalIgnoreCase))
        {
            using var retryReq = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.xendit.co/qr_codes/{Uri.EscapeDataString(resolvedQrId)}/payments/simulate");
            retryReq.Headers.Authorization = authHeader;
            retryReq.Headers.Add("api-version", "2022-07-31");
            retryReq.Content = JsonContent.Create(payloadObj);

            using var retryRes = await client.SendAsync(retryReq, cancellationToken);
            responseBody = await retryRes.Content.ReadAsStringAsync(cancellationToken);
            finalStatus = (int)retryRes.StatusCode;
            _logger.LogInformation("Xendit QR simulate (2022-07-31 fallback {QrId}) for {ExternalId} returned {StatusCode}", resolvedQrId, externalId, finalStatus);

            if (retryRes.IsSuccessStatusCode)
            {
                await TryConfirmSimulatedQrLocallyAsync(externalId, resolvedQrId, effectiveAmount, cancellationToken);
                return Ok(new { status = "SIMULATED", externalId, qrId = resolvedQrId, xenditStatus = finalStatus });
            }
        }
        else
        {
            _logger.LogInformation("Xendit QR simulate for {ExternalId} returned {StatusCode}", externalId, finalStatus);
            if (response.IsSuccessStatusCode)
            {
                await TryConfirmSimulatedQrLocallyAsync(externalId, resolvedQrId, effectiveAmount, cancellationToken);
                return Ok(new { status = "SIMULATED", externalId, xenditStatus = finalStatus });
            }
        }

        // If Xendit returns 410 INACTIVE_QR_CODE (the dynamic QR was already simulated once on Xendit's side)
        // or the booking is still awaiting payment in test mode, finalize the booking confirmation directly.
        if (await TryConfirmSimulatedQrLocallyAsync(externalId, resolvedQrId, effectiveAmount, cancellationToken))
        {
            _logger.LogInformation("Confirmed QR simulation locally for {ExternalId} after Xendit status {StatusCode}", externalId, finalStatus);
            return Ok(new { status = "SIMULATED", externalId, qrId = resolvedQrId, xenditStatus = finalStatus });
        }

        return StatusCode(StatusCodes.Status502BadGateway, new
        {
            message = "Xendit rejected the QR payment simulation.",
            xenditStatus = finalStatus,
            detail = responseBody
        });
    }

    private async Task<bool> TryConfirmSimulatedQrLocallyAsync(
        string externalId,
        string? qrId,
        decimal? amount,
        CancellationToken cancellationToken)
    {
        try
        {
            var confirmed = await _pendingPayments.ConfirmByReferenceAsync(externalId, qrId, amount, "QRPH", cancellationToken);
            if (confirmed != null)
            {
                await HandlePostPaymentConfirmationAsync(confirmed, externalId, "QRPH", cancellationToken);
                return true;
            }

            var appt = await _consultationService.GetAppointmentByIdAsync(externalId, cancellationToken);
            if (appt != null && (string.Equals(appt.EventStatus, "Confirmed", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(appt.EventStatus, "Shot", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var qrInfo = await _pendingPayments.GetQrPaymentAsync(externalId, cancellationToken);
            if (qrInfo != null && string.Equals(qrInfo.EventStatus, "Shot", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local fallback confirmation after QR simulation failed for {ExternalId}", externalId);
        }
        return false;
    }

    private async Task HandlePostPaymentConfirmationAsync(
        PaymentConfirmation confirmation,
        string referenceId,
        string? channel,
        CancellationToken cancellationToken)
    {
        if (confirmation.IsFinalPayment)
        {
            try
            {
                await _consultationService.NotifyTeamLeaderFinalQrPaidAsync(
                    confirmation.AppointmentId,
                    confirmation.Amount,
                    channel ?? "QRPH",
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Team Leader QR paid email notification failed for {AppointmentId}", confirmation.AppointmentId);
            }
        }

        try
        {
            await _notifier.PaymentCompletedAsync(confirmation.AppointmentId, cancellationToken);
            if (!string.Equals(referenceId, confirmation.AppointmentId, StringComparison.OrdinalIgnoreCase))
            {
                await _notifier.PaymentCompletedAsync(referenceId, cancellationToken);
            }
            _logger.LogInformation("SignalR hub successfully broadcasted status change event (PaymentCompleted) for appointment {AppointmentId}.", confirmation.AppointmentId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR push failed for {Reference}; payment already confirmed.", referenceId);
        }
    }
    private bool IsMockXendit() =>
        string.IsNullOrWhiteSpace(_xenditSettings.SecretKey) ||
        _xenditSettings.SecretKey.Contains("mock", StringComparison.OrdinalIgnoreCase);

    // GET api/payments/qr/{appointmentId}
    // Returns the saved Xendit qr_string for the customer's /payments/{id} page (rendered into a QR image by Blazor).
    [HttpGet("qr/{appointmentId}")]
    [Authorize(Roles = $"{AppRoles.Customer},{AppRoles.Admin},{AppRoles.SystemAdmin},{AppRoles.Employee},{AppRoles.OfficeClerk}")]
    public async Task<IActionResult> GetQrPayment(string appointmentId, CancellationToken cancellationToken)
    {
        var info = await _pendingPayments.GetQrPaymentAsync(appointmentId, cancellationToken);
        if (info == null) return NotFound(new { message = "Booking not found." });

        var isStaff = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.SystemAdmin) ||
                      User.IsInRole(AppRoles.Employee) || User.IsInRole(AppRoles.OfficeClerk);
        var callerIds = new[]
        {
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            User.FindFirstValue("studio_id"),
            User.FindFirstValue("sub")
        };
        if (!isStaff && !callerIds.Any(i => string.Equals(i, info.CustomerId, StringComparison.OrdinalIgnoreCase)))
            return NotFound(new { message = "Booking not found." });

        // Only an unpaid, still-locked booking exposes its QR.
        if (info.EventStatus != "Pending Payment") info.QrString = null;
        return Ok(info);
    }

    // GET api/payments/qr/{appointmentId}/public
    // Anonymous, minimal reservation-fee summary for the /checkout/confirm/{id} page opened by scanning the QR on a phone
    // (the phone has no login session). Exposes no customer id, QR payload or contact details.
    [HttpGet("qr/{appointmentId}/public")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicQrPayment(string appointmentId, CancellationToken cancellationToken)
    {
        var info = await _pendingPayments.GetQrPaymentAsync(appointmentId, cancellationToken);
        if (info == null) return NotFound(new { message = "Booking not found." });

        return Ok(new
        {
            info.AppointmentId,
            info.EventName,
            info.EventDate,
            info.EventTime,
            info.EventStatus,
            info.Amount,
            info.LockExpiresAt
        });
    }

    // POST api/payments/xendit-qr-webhook
    // Xendit QR callback. Authenticated by the X-Callback-Token header (XENDIT_CALLBACK_TOKEN env var).
    [HttpPost("xendit-qr-webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> XenditQrWebhook(
        [FromHeader(Name = "X-Callback-Token")] string? callbackToken,
        [FromBody] System.Text.Json.JsonElement payload,
        CancellationToken cancellationToken)
    {
        var expected = _configuration["XENDIT_CALLBACK_TOKEN"]
            ?? _xenditSettings.WebhookToken
            ?? _configuration["XenditSettings:WebhookToken"];
        if (string.IsNullOrWhiteSpace(expected))
        {
            _logger.LogError("XENDIT_CALLBACK_TOKEN is not configured; rejecting QR webhook.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Webhook not configured." });
        }

        var supplied = System.Text.Encoding.UTF8.GetBytes(callbackToken ?? string.Empty);
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        if (supplied.Length != expectedBytes.Length ||
            !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(supplied, expectedBytes))
        {
            _logger.LogWarning("Rejected Xendit QR callback with invalid or missing X-Callback-Token.");
            return Unauthorized(new { message = "Invalid callback token." });
        }

        // Supports the new envelope { event, data: {...} } and the legacy flat payload.
        var data = payload.ValueKind == System.Text.Json.JsonValueKind.Object &&
                   payload.TryGetProperty("data", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.Object
            ? d : payload;

        string? Str(System.Text.Json.JsonElement e, string name) =>
            e.ValueKind == System.Text.Json.JsonValueKind.Object && e.TryGetProperty(name, out var v) &&
            v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;

        var referenceId = Str(data, "reference_id") ?? Str(data, "external_id");
        if (referenceId == null && data.ValueKind == System.Text.Json.JsonValueKind.Object &&
            data.TryGetProperty("qr_code", out var qrCode))
            referenceId = Str(qrCode, "external_id");

        if (string.IsNullOrWhiteSpace(referenceId))
            return BadRequest(new { message = "reference_id is required." });

        _logger.LogInformation("ASP.NET Core API successfully received qr.payment webhook and validated x-callback-token for reference {ReferenceId}.", referenceId);

        var status = Str(data, "status") ?? string.Empty;
        var succeeded = status.Equals("SUCCEEDED", StringComparison.OrdinalIgnoreCase) ||
                        status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase) ||
                        status.Equals("PAID", StringComparison.OrdinalIgnoreCase) ||
                        status.Equals("SETTLED", StringComparison.OrdinalIgnoreCase);
        if (!succeeded)
        {
            _logger.LogInformation("Ignoring Xendit QR callback for {Reference} with status {Status}.", referenceId, status);
            return Ok(new { status = "IGNORED" });
        }

        decimal? paid = data.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        data.TryGetProperty("amount", out var amt) && amt.ValueKind == System.Text.Json.JsonValueKind.Number
            ? amt.GetDecimal() : null;

        var booking = await _pendingPayments.GetQrPaymentAsync(referenceId, cancellationToken);
        if (booking == null)
        {
            _logger.LogWarning("Xendit QR callback for unknown reference {Reference}.", referenceId);
            return Ok(new { status = "UNKNOWN_REFERENCE" });
        }
        if (paid.HasValue && paid.Value < booking.Amount)
        {
            _logger.LogWarning("Xendit QR callback for {Reference} paid {Paid} < required {Required}; not confirming.",
                referenceId, paid, booking.Amount);
            return Ok(new { status = "AMOUNT_MISMATCH" });
        }

        var channelCode = Str(data, "channel_code") ?? "QRPH";
        var confirmation = await _pendingPayments.ConfirmByReferenceAsync(
            referenceId, Str(data, "qr_id") ?? Str(data, "id"), paid, channelCode, cancellationToken);

        if (confirmation == null)
        {
            // Duplicate delivery, or paid after the lock expired and the slot was released (needs manual refund).
            _logger.LogWarning("QR payment for {Reference} not applied (booking status {Status}).", referenceId, booking.EventStatus);
            return Ok(new { status = "NOT_APPLIED" });
        }

        await HandlePostPaymentConfirmationAsync(confirmation, referenceId, channelCode, cancellationToken);

        return Ok(new { status = "SUCCESS", reference_id = referenceId });
    }
}
