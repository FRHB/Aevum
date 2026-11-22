using System.Security.Claims;
using Amazon.S3;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using PhotoAevumStudio.Api.Services;
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
    private readonly DropboxChunkUploadCoordinator? _chunkCoordinator;

    public CustomerDropboxController(
        ICustomerDropboxService dropboxService,
        ILogger<CustomerDropboxController> logger,
        DropboxChunkUploadCoordinator? chunkCoordinator = null)
    {
        _dropboxService = dropboxService;
        _logger = logger;
        _chunkCoordinator = chunkCoordinator;
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
    public async Task<IActionResult> GetMyDownloadUrl(
        string dropboxId,
        CancellationToken cancellationToken = default,
        [FromQuery] string? fileId = null)
    {
        var customerId = ResolveCustomerId();
        if (string.IsNullOrWhiteSpace(customerId))
            return Unauthorized(new ProblemDetails { Title = "Customer identity missing", Status = 401 });

        try
        {
            var apiBase = $"{Request.Scheme}://{Request.Host}";
            var dto = string.IsNullOrWhiteSpace(fileId)
                ? await _dropboxService.GetCustomerDownloadInfoAsync(dropboxId, customerId, apiBase, cancellationToken)
                : await _dropboxService.GetCustomerDownloadInfoAsync(dropboxId, customerId, apiBase, fileId, cancellationToken);
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
    public async Task<IActionResult> DownloadMyZip(
        string dropboxId,
        CancellationToken cancellationToken = default,
        [FromQuery] string? fileId = null)
    {
        var customerId = ResolveCustomerId();
        if (string.IsNullOrWhiteSpace(customerId))
            return Unauthorized(new ProblemDetails { Title = "Customer identity missing", Status = 401 });

        try
        {
            var (fileName, contentType, stream, _) = string.IsNullOrWhiteSpace(fileId)
                ? await _dropboxService.GetCustomerDownloadAsync(dropboxId, customerId, cancellationToken)
                : await _dropboxService.GetCustomerDownloadAsync(dropboxId, customerId, fileId, cancellationToken);
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

        var (valid, dropboxId, customerId, dropboxFileId) = concrete.ValidateSignedDownloadTokenWithFile(token);
        if (!valid)
            return Unauthorized(new ProblemDetails { Title = "Invalid or expired download token", Status = 401 });

        try
        {
            var (fileName, contentType, stream, _) = string.IsNullOrWhiteSpace(dropboxFileId)
                ? await _dropboxService.GetCustomerDownloadAsync(dropboxId, customerId, cancellationToken)
                : await _dropboxService.GetCustomerDownloadAsync(dropboxId, customerId, dropboxFileId, cancellationToken);
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

        if (file.Length > DropboxCountdownHelper.MaxZipSizeBytes)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "File too large",
                Detail = $"ZIP file '{Path.GetFileName(file.FileName)}' exceeds the 500 MB maximum limit per ZIP file.",
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
                file.Headers != null ? file.ContentType : "application/zip",
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

    // POST api/dropbox/admin/{eventAppointmentId}/upload/init
    [HttpPost("admin/{eventAppointmentId}/upload/init")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> InitChunkedUpload(
        string eventAppointmentId,
        [FromBody] DropboxUploadInitRequest request,
        CancellationToken cancellationToken)
    {
        if (_chunkCoordinator == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails { Title = "Chunk upload unavailable", Status = 503 });

        try
        {
            await _dropboxService.ValidateUploadTargetAsync(eventAppointmentId, cancellationToken);
            var staffId = ResolveStaffId();
            var session = _chunkCoordinator.CreateSession(
                eventAppointmentId,
                staffId,
                request.FileName,
                request.FileSizeBytes,
                request.ContentType);

            return Ok(session);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Event container not found", Detail = ex.Message, Status = 404 });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Upload rejected", Detail = ex.Message, Status = 400 });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid request", Detail = ex.Message, Status = 400 });
        }
    }

    // POST api/dropbox/admin/{eventAppointmentId}/upload/chunk
    [HttpPost("admin/{eventAppointmentId}/upload/chunk")]
    [Authorize(Policy = "ClerkOrAdmin")]
    [RequestSizeLimit(16_777_216)] // 16 MB per chunk
    [RequestFormLimits(MultipartBodyLengthLimit = 16_777_216)]
    public async Task<IActionResult> UploadChunk(
        string eventAppointmentId,
        [FromQuery] string sessionId,
        [FromQuery] int chunkIndex,
        IFormFile? chunk,
        CancellationToken cancellationToken)
    {
        if (_chunkCoordinator == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails { Title = "Chunk upload unavailable", Status = 503 });

        if (chunk == null || chunk.Length <= 0)
            return BadRequest(new ProblemDetails { Title = "Empty chunk", Detail = "Chunk payload is required.", Status = 400 });

        try
        {
            var staffId = ResolveStaffId();
            await using var stream = chunk.OpenReadStream();
            var res = await _chunkCoordinator.AppendChunkAsync(
                sessionId,
                eventAppointmentId,
                staffId,
                chunkIndex,
                stream,
                chunk.Length,
                cancellationToken);
            return Ok(res);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Upload session not found", Detail = ex.Message, Status = 404 });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails { Title = "Access Denied", Detail = ex.Message, Status = 403 });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Chunk rejected", Detail = ex.Message, Status = 400 });
        }
    }

    // POST api/dropbox/admin/{eventAppointmentId}/upload/complete
    [HttpPost("admin/{eventAppointmentId}/upload/complete")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public IActionResult CompleteChunkedUpload(
        string eventAppointmentId,
        [FromQuery] string sessionId)
    {
        if (_chunkCoordinator == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails { Title = "Chunk upload unavailable", Status = 503 });

        try
        {
            var staffId = ResolveStaffId();
            var status = _chunkCoordinator.StartFinalize(sessionId, eventAppointmentId, staffId);
            return Accepted(status);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Upload session not found", Detail = ex.Message, Status = 404 });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails { Title = "Access Denied", Detail = ex.Message, Status = 403 });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Finalize rejected", Detail = ex.Message, Status = 400 });
        }
    }

    // GET api/dropbox/admin/upload/status/{sessionId}
    [HttpGet("admin/upload/status/{sessionId}")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public IActionResult GetChunkedUploadStatus(string sessionId)
    {
        if (_chunkCoordinator == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails { Title = "Chunk upload unavailable", Status = 503 });

        try
        {
            var staffId = ResolveStaffId();
            var status = _chunkCoordinator.GetStatus(sessionId, staffId);
            return Ok(status);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Upload session not found", Detail = ex.Message, Status = 404 });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails { Title = "Access Denied", Detail = ex.Message, Status = 403 });
        }
    }

    // DELETE api/dropbox/admin/upload/{sessionId}
    [HttpDelete("admin/upload/{sessionId}")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public IActionResult AbortChunkedUpload(string sessionId)
    {
        if (_chunkCoordinator == null)
            return NoContent();

        try
        {
            var staffId = ResolveStaffId();
            _chunkCoordinator.AbortSession(sessionId, staffId);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails { Title = "Access Denied", Detail = ex.Message, Status = 403 });
        }
    }

    // POST api/dropbox/admin/{eventAppointmentId}/upload-multiple
    [HttpPost("admin/{eventAppointmentId}/upload-multiple")]
    [Authorize(Policy = "ClerkOrAdmin")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<IActionResult> UploadEventZips(
        string eventAppointmentId,
        [FromForm] List<IFormFile>? files,
        CancellationToken cancellationToken)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "No ZIP files uploaded",
                Detail = "Please select one or more .zip archives (max 500 MB per ZIP file).",
                Status = 400
            });
        }

        foreach (var file in files)
        {
            if (file == null || file.Length <= 0)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Empty ZIP file",
                    Detail = "Every selected ZIP file must be non-empty.",
                    Status = 400
                });
            }

            if (!Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid file type",
                    Detail = $"File '{Path.GetFileName(file.FileName)}' is not a .zip file. Only .zip files are allowed.",
                    Status = 400
                });
            }

            if (file.Length > DropboxCountdownHelper.MaxZipSizeBytes)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "File too large",
                    Detail = $"ZIP file '{Path.GetFileName(file.FileName)}' exceeds the 500 MB limit per individual ZIP file.",
                    Status = 400
                });
            }
        }

        var staffId = ResolveStaffId();
        CustomerDropboxFolderDto? updated = null;
        try
        {
            foreach (var file in files)
            {
                await using var stream = file.OpenReadStream();
                updated = await _dropboxService.UploadZipAsync(
                    eventAppointmentId,
                    stream,
                    file.FileName,
                    file.Length,
                    file.Headers != null ? file.ContentType : "application/zip",
                    staffId,
                    cancellationToken);

                _logger.LogInformation(
                    "Staff {StaffId} uploaded Dropbox ZIP '{FileName}' ({Size} bytes) for event {EventId} (Customer {CustomerId}).",
                    staffId, file.FileName, file.Length, eventAppointmentId, updated.CustomerId);
            }

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
            _logger.LogError(ex, "Cloudflare R2 multi-ZIP upload failed for event {EventId} (Status: {StatusCode}, Code: {ErrorCode}).", eventAppointmentId, ex.StatusCode, ex.ErrorCode);
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

    // DELETE api/dropbox/admin/{eventAppointmentId}/files/{dropboxFileId}
    [HttpDelete("admin/{eventAppointmentId}/files/{dropboxFileId}")]
    [Authorize(Policy = "ClerkOrAdmin")]
    public async Task<IActionResult> DeleteEventZipFile(
        string eventAppointmentId,
        string dropboxFileId,
        CancellationToken cancellationToken)
    {
        try
        {
            var staffId = ResolveStaffId();
            var updated = await _dropboxService.DeleteDropboxFileAsync(eventAppointmentId, dropboxFileId, staffId, cancellationToken);
            _logger.LogInformation(
                "Staff {StaffId} deleted Dropbox ZIP {FileId} for event {EventId}.",
                staffId, dropboxFileId, eventAppointmentId);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "ZIP file not found", Detail = ex.Message, Status = 404 });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Delete failed", Detail = ex.Message, Status = 400 });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid request", Detail = ex.Message, Status = 400 });
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
