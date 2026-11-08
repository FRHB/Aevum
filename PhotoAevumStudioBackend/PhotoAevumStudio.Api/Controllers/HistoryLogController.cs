using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Route("api/history-logs")]
[Authorize]
public class HistoryLogController : ControllerBase
{
    private readonly IAuditLogService _auditLogService;

    public HistoryLogController(IAuditLogService auditLogService)
    {
        _auditLogService = auditLogService;
    }

    [HttpGet]
    public async Task<IActionResult> GetHistoryLogs([FromQuery] int top = 100, CancellationToken ct = default)
    {
        string requestingUserId = User.FindFirstValue("studio_id")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? "SADM-001";

        string? userRole = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");

        var logs = await _auditLogService.GetHistoryLogsAsync(requestingUserId, userRole, top, ct);
        return Ok(logs);
    }

    [HttpGet("audit-logs")]
    public async Task<IActionResult> GetAuditLogs([FromQuery] int top = 100, CancellationToken ct = default)
    {
        string requestingUserId = User.FindFirstValue("studio_id")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? "SADM-001";

        string? userRole = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");

        var logs = await _auditLogService.GetAuditLogsAsync(requestingUserId, userRole, top, ct);
        return Ok(logs);
    }
}
