using System.Security.Claims;
using Amazon.S3;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Services;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/dropbox")]
[Authorize]
public class CustomerDropboxController : ControllerBase
{
    private readonly ICustomerDropboxService _dropboxService;
    private readonly ILogger<CustomerDropboxController> _logger;

    public CustomerDropboxController(
        ICustomerDropboxService dropboxService,
        ILogger<CustomerDropboxController> logger)
    {
        _dropboxService = dropboxService;
        _logger = logger;
    }

    private string ResolveCustomerId() =>
        User.FindFirstValue("studio_id")
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? string.Empty;

    private string ResolveStaffId() =>
        User.FindFirstValue("studio_id")
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? "Staff";

    // GET api/dropbox/my
    [HttpGet("my")]
    [Authorize(Roles = AppRoles.Customer)]
    public async Task<IActionResult> GetMyDropboxes(CancellationToken cancellationToken)
    {
        var customerId = ResolveCustomerId();
        if (string.IsNullOrWhiteSpace(customerId))
            return Unauthorized(new ProblemDetails { Title = "Customer identity missing", Status = 401 });

        var folders = await _dropboxService.GetCustomerDropboxesAsync(customerId, cancellationToken);
        return Ok(folders);
    }

    // GET api/dropbox/my/{dropboxId}/download-url
    [HttpGet("my/{dropboxId}/download-url")]
    [Authorize(Roles = AppRoles.Customer)]
    public async Task<IActionResult> GetMyDownloadUrl(string dropboxId, CancellationToken cancellationToken)
    {
        var customerId = ResolveCustomerId();
        if (string.IsNullOrWhiteSpace(customerId))
            return Unauthorized(new ProblemDetails { Title = "Customer identity missing", Status = 401 });

        try
        {
            var apiBase = $"{Request.Scheme}://{Request.Host}";
            var dto = await _dropboxService.GetCustomerDownloadInfoAsync(dropboxId, customerId, apiBase, cancellationToken);
            return Ok(dto);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails { Title = "Access Denied", Detail = ex.Message, Status = 403 });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Not Found", Detail = ex.Message, Status = 404 });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Unavailable", Detail = ex.Message, Status = 400 });
        }
    }

    // GET api/dropbox/my/{dropboxId}/download
    [HttpGet("my/{dropboxId}/download")]
    [Authorize(Roles = AppRoles.Customer)]
    public async Task<IActionResult> DownloadMyZip(string dropboxId, CancellationToken cancellationToken)
    {
        var customerId = ResolveCustomerId();
        if (string.IsNullOrWhiteSpace(customerId))
            return Unauthorized(new ProblemDetails { Title = "Customer identity missing", Status = 401 });

        try
        {
            var (fileName, contentType, stream, _) = await _dropboxService.GetCustomerDownloadAsync(dropboxId, customerId, cancellationToken);
            return File(stream, contentType, fileName);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails { Title = "Access Denied", Detail = ex.Message, Status = 403 });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Not Found", Detail = ex.Message, Status = 404 });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Unavailable", Detail = ex.Message, Status = 400 });
        }
    }

    // GET api/dropbox/download/{token}
    [HttpGet("download/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadByToken(string token, CancellationToken cancellationToken)
    {
        if (_dropboxService is not CustomerDropboxService concrete)
            return NotFound();

        var (valid, dropboxId, customerId) = concrete.ValidateSignedDownloadToken(token);
        if (!valid)
            return Unauthorized(new ProblemDetails { Title = "Invalid or expired download token", Status = 401 });

        try
        {
            var (fileName, contentType, stream, _) = await _dropboxService.GetCustomerDownloadAsync(dropboxId, customerId, cancellationToken);
            return File(stream, contentType, fileName);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails { Title = "Access Denied", Detail = ex.Message, Status = 403 });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Not Found", Detail = ex.Message, Status = 404 });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Unavailable", Detail = ex.Message, Status = 400 });
        }
    }

    // GET api/dropbox/admin/events
    [HttpGet("admin/events")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> GetAdminEventContainers(CancellationToken cancellationToken)
    {
        var containers = await _dropboxService.GetAdminEventContainersAsync(cancellationToken);
        return Ok(containers);
    }

    // POST api/dropbox/admin/{eventAppointmentId}/upload
    [HttpPost("admin/{eventAppointmentId}/upload")]
    [Authorize(Policy = "ClerkOrAdmin")]
    [RequestSizeLimit(524_288_000)] // 500 MB
    [RequestFormLimits(MultipartBodyLengthLimit = 524_288_000)]
    public async Task<IActionResult> UploadEventZip(
        string eventAppointmentId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "No ZIP file uploaded",
                Detail = "Please select a .zip archive containing the event photos.",
                Status = 400
            });
        }

        if (!Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid file type",
                Detail = "Only .zip files are allowed for Customer Dropbox uploads.",
                Status = 400
            });
        }

        var staffId = ResolveStaffId();
        try
        {
            await using var stream = file.OpenReadStream();
            var updated = await _dropboxService.UploadZipAsync(
                eventAppointmentId,
                stream,
                file.FileName,
                file.Length,
                file.ContentType,
                staffId,
                cancellationToken);

            _logger.LogInformation(
                "Staff {StaffId} uploaded Dropbox ZIP '{FileName}' ({Size} bytes) for event {EventId} (Customer {CustomerId}).",
                staffId, file.FileName, file.Length, eventAppointmentId, updated.CustomerId);

            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Event container not found", Detail = ex.Message, Status = 404 });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Upload rejected", Detail = ex.Message, Status = 400 });
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "Cloudflare R2 upload failed for event {EventId} (Status: {StatusCode}, Code: {ErrorCode}).", eventAppointmentId, ex.StatusCode, ex.ErrorCode);
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Title = "Cloudflare R2 upload failed",
                Detail = $"R2 storage rejected the upload ({ex.ErrorCode ?? ex.StatusCode.ToString()}): {ex.Message}",
                Status = 502
            });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error saving CustomerDropbox metadata for event {EventId}.", eventAppointmentId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Database error",
                Detail = "Failed to save uploaded ZIP archive metadata.",
                Status = 500
            });
        }
    }

    // POST api/dropbox/admin/purge-expired
    [HttpPost("admin/purge-expired")]
    [Authorize(Policy = "AdminOrAbove")]
    public async Task<IActionResult> PurgeExpired(CancellationToken cancellationToken)
    {
        var result = await _dropboxService.PurgeExpiredDropboxesAsync(cancellationToken);
        return Ok(result);
    }
}
