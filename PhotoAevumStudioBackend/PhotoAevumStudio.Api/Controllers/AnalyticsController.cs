using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ClerkOrAdmin")]
public sealed class AnalyticsController : ControllerBase
{
    private readonly IStudioAnalyticsService _analytics;
    private readonly ILogger<AnalyticsController> _logger;

    public AnalyticsController(
        IStudioAnalyticsService analytics,
        ILogger<AnalyticsController> logger)
    {
        _analytics = analytics;
        _logger = logger;
    }

    [HttpGet("overview")]
    public async Task<ActionResult<StudioAnalyticsOverviewDto>> GetOverview(
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        [FromQuery] string? status,
        [FromQuery] string? category,
        [FromQuery] string granularity = "month",
        [FromQuery] string scenario = "expected",
        CancellationToken ct = default)
    {
        try
        {
            var query = new StudioAnalyticsQuery(startDate, endDate, status, category, granularity, scenario);
            var overview = await _analytics.GetOverviewAsync(query, ct);
            return Ok(overview);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return StatusCode(499);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute studio analytics overview.");
            return Problem("Unable to compute studio analytics at this time.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("prescriptive/apply")]
    public async Task<ActionResult<ApplyPrescriptiveActionsResultDto>> ApplyPrescriptiveActions(
        [FromBody] ApplyPrescriptiveActionsRequest request,
        CancellationToken ct = default)
    {
        if (request?.Actions is null || request.Actions.Count == 0)
            return BadRequest(new { Message = "Select at least one studio recommendation to apply." });

        var userId = User.FindFirstValue("studio_id")
                     ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? User.FindFirstValue("sub")
                     ?? "STAFF";
        var userEmail = User.FindFirstValue(ClaimTypes.Email)
                        ?? User.FindFirstValue("email")
                        ?? User.Identity?.Name
                        ?? "staff@photoaevumstudio.com";
        var userName = User.FindFirstValue("full_name")
                       ?? User.Identity?.Name
                       ?? userEmail;

        try
        {
            var result = await _analytics.ApplyPrescriptiveActionsAsync(request, userId, userEmail, userName, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist applied prescriptive analytics actions.");
            return Problem("Unable to persist applied studio actions.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        [FromQuery] string? status,
        [FromQuery] string? category,
        [FromQuery] string granularity = "month",
        [FromQuery] string scenario = "expected",
        CancellationToken ct = default)
    {
        try
        {
            var query = new StudioAnalyticsQuery(startDate, endDate, status, category, granularity, scenario);
            var csv = await _analytics.ExportCsvAsync(query, ct);
            var bytes = Encoding.UTF8.GetBytes(csv);
            var fileName = $"photo-aevum-studio-analytics-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export studio analytics CSV.");
            return Problem("Unable to export analytics report.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
