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
public class ContractsController : ControllerBase
{
    private readonly IBookingDocumentService _documentService;
    private readonly IConsultationNegotiationService _consultationService;
    private readonly ILogger<ContractsController> _logger;

    public ContractsController(
        IBookingDocumentService documentService,
        IConsultationNegotiationService consultationService,
        ILogger<ContractsController> logger)
    {
        _documentService = documentService;
        _consultationService = consultationService;
        _logger = logger;
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

    private async Task<bool> CanAccessAppointmentAsync(string appointmentId, CancellationToken ct)
    {
        if (IsClerkOrAdmin()) return true;

        var appt = await _consultationService.GetAppointmentByIdAsync(appointmentId, ct);
        if (appt == null) return false;

        var callerIds = GetCallerIdentifiers();
        if (callerIds.Any(id => string.Equals(id, appt.CustomerId, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (User.IsInRole(AppRoles.Employee) &&
            appt.AssignedEmployees.Any(e => callerIds.Any(id => string.Equals(id, e.EmployeeId, StringComparison.OrdinalIgnoreCase))))
            return true;

        return false;
    }

    // GET api/contracts/terms/active
    [HttpGet("terms/active")]
    [AllowAnonymous]
    public async Task<IActionResult> GetActiveTerms(CancellationToken ct)
    {
        var terms = await _documentService.GetActiveTermsVersionAsync(ct);
        return Ok(terms);
    }

    // GET api/contracts/terms/pdf
    [HttpGet("terms/pdf")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadActiveTermsPdf([FromQuery] int? versionId, CancellationToken ct)
    {
        try
        {
            var (bytes, fileName, contentType) = await _documentService.GetActiveTermsPdfAsync(versionId, ct);
            return File(bytes, contentType, fileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    // GET api/contracts/{appointmentId}
    [HttpGet("{appointmentId}")]
    public async Task<IActionResult> GetBookingDocuments(string appointmentId, CancellationToken ct)
    {
        if (!await CanAccessAppointmentAsync(appointmentId, ct))
            return Forbid();

        var bundle = await _documentService.GetBookingDocumentsBundleAsync(appointmentId, ct);
        if (bundle == null)
            return NotFound(new { message = $"Booking {appointmentId} not found." });

        return Ok(bundle);
    }

    // POST api/contracts/{appointmentId}/configure
    [HttpPost("{appointmentId}/configure")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> ConfigureDraftContract(
        string appointmentId,
        [FromBody] ConfigureBookingContractRequest request,
        CancellationToken ct)
    {
        try
        {
            var contract = await _documentService.GenerateOrUpdateDraftContractAsync(appointmentId, request, ct);
            return Ok(contract);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // POST api/contracts/{appointmentId}/accept
    [HttpPost("{appointmentId}/accept")]
    [Authorize(Roles = $"{AppRoles.Customer},{AppRoles.Admin},{AppRoles.SystemAdmin},{AppRoles.OfficeClerk}")]
    public async Task<IActionResult> AcceptContract(
        string appointmentId,
        [FromBody] AcceptContractRequest request,
        CancellationToken ct)
    {
        var callerId = User.FindFirstValue("studio_id")
                       ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? User.FindFirstValue("sub")
                       ?? string.Empty;

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        try
        {
            var accepted = await _documentService.AcceptContractAsync(
                appointmentId,
                callerId,
                IsClerkOrAdmin(),
                request,
                clientIp,
                userAgent,
                ct);

            _logger.LogInformation(
                "Contract {ContractNumber} for {AppointmentId} accepted by {CallerId} from IP {Ip}.",
                accepted.ContractNumber, appointmentId, callerId, clientIp);

            return Ok(accepted);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // GET api/contracts/{appointmentId}/pdf
    [HttpGet("{appointmentId}/pdf")]
    public async Task<IActionResult> DownloadContractPdf(string appointmentId, CancellationToken ct)
    {
        if (!await CanAccessAppointmentAsync(appointmentId, ct))
            return Forbid();

        try
        {
            var (bytes, fileName, contentType) = await _documentService.GetContractPdfAsync(appointmentId, ct);
            return File(bytes, contentType, fileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    // GET api/contracts/{appointmentId}/receipts/{receiptType}/pdf
    // GET api/receipts/{appointmentId}/{receiptType}/pdf
    [HttpGet("{appointmentId}/receipts/{receiptType}/pdf")]
    [HttpGet("/api/receipts/{appointmentId}/{receiptType}/pdf")]
    public async Task<IActionResult> DownloadReceiptPdf(string appointmentId, string receiptType, CancellationToken ct)
    {
        if (!await CanAccessAppointmentAsync(appointmentId, ct))
            return Forbid();

        try
        {
            var (bytes, fileName, contentType) = await _documentService.GetReceiptPdfAsync(appointmentId, receiptType, ct);
            return File(bytes, contentType, fileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    // GET api/contracts/{appointmentId}/deliverables-gate
    [HttpGet("{appointmentId}/deliverables-gate")]
    public async Task<IActionResult> CheckDeliverablesGate(string appointmentId, CancellationToken ct)
    {
        if (!await CanAccessAppointmentAsync(appointmentId, ct))
            return Forbid();

        var (allowed, remaining, reason) = await _documentService.CheckDeliverablesAccessAsync(appointmentId, ct);
        return Ok(new { allowed, remainingBalance = remaining, reason });
    }
}
