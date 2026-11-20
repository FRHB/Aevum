using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize(Policy = "ClerkOrAdmin")]
public sealed class SalesReportsController : ControllerBase
{
    private readonly ISalesReportService _svc;
    private readonly ILogger<SalesReportsController> _logger;

    public SalesReportsController(ISalesReportService svc, ILogger<SalesReportsController> logger)
    {
        _svc = svc;
        _logger = logger;
    }

    [HttpGet("sales")]
    public async Task<ActionResult<SalesResponseDto>> GetSales(
        [FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate,
        [FromQuery] string? status, [FromQuery] string? search, CancellationToken ct)
    {
        try { return Ok(await _svc.GetSalesAsync(new SalesQuery(startDate, endDate, status, search), ct)); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load sales.");
            return Problem("Unable to load sales at this time.", statusCode: 500);
        }
    }

    [HttpGet("sales/export")]
    public async Task<IActionResult> ExportSales(
        [FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate,
        [FromQuery] string? status, [FromQuery] string? search, CancellationToken ct)
    {
        try
        {
            var csv = await _svc.ExportSalesCsvAsync(new SalesQuery(startDate, endDate, status, search), ct);
            return File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", $"photo-aevum-sales-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export sales.");
            return Problem("Unable to export sales.", statusCode: 500);
        }
    }

    [HttpGet("sales/recordable-bookings")]
    public async Task<ActionResult<IReadOnlyList<RecordableAppointmentDto>>> GetRecordable(CancellationToken ct)
    {
        try { return Ok(await _svc.GetRecordableAppointmentsAsync(ct)); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load bookings with outstanding balance.");
            return Problem("Unable to load bookings.", statusCode: 500);
        }
    }

    [HttpPost("sales")]
    public async Task<ActionResult<RecordSaleResultDto>> RecordSale([FromBody] RecordSaleRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "STAFF";
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email") ?? User.Identity?.Name ?? "staff@photoaevumstudio.com";
        var name = User.FindFirstValue("full_name") ?? User.Identity?.Name ?? email;
        try { return Ok(await _svc.RecordSaleAsync(request, userId, email, name, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { Message = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record sale.");
            return Problem("Unable to record the sale.", statusCode: 500);
        }
    }

    [HttpGet("reports/summary")]
    public async Task<ActionResult<ReportSummaryDto>> GetReport([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        var now = DateTime.Today;
        var y = year ?? now.Year;
        var m = month ?? now.Month;
        if (y < 2000 || y > 2100 || m < 1 || m > 12) return BadRequest(new { Message = "Invalid period." });
        try { return Ok(await _svc.GetReportAsync(y, m, ct)); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute report summary.");
            return Problem("Unable to compute report at this time.", statusCode: 500);
        }
    }
}
