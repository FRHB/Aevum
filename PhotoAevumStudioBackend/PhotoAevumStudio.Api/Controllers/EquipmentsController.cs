using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EquipmentsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<EquipmentsController> _logger;

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private const long MaxImageFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    public EquipmentsController(
        ApplicationDbContext db,
        IWebHostEnvironment env,
        ILogger<EquipmentsController> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    private string GetConnectionString() =>
        _db.Database.GetConnectionString()
        ?? throw new InvalidOperationException("Database connection string is not configured.");

    /// <summary>
    /// Returns the projected next sequential Equipment ID (EQP-####) for display in the wizard overview step.
    /// Strictly read-only; does not advance or lock the sequence.
    /// </summary>
    [HttpGet("next-id")]
    [AllowAnonymous] // Allow wizard preview to query without friction
    public async Task<ActionResult<NextEquipmentIdResponseDto>> GetNextEquipmentId(CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(GetConnectionString());
            await conn.OpenAsync(ct);

            // 1. Try calling the dedicated stored procedure
            await using var cmd = new SqlCommand("dbo.sp_GetNextEquipmentIdPreview", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            try
            {
                var result = await cmd.ExecuteScalarAsync(ct);
                if (result != null && result != DBNull.Value && !string.IsNullOrWhiteSpace(result.ToString()))
                {
                    return Ok(new NextEquipmentIdResponseDto(result.ToString()!));
                }
            }
            catch (SqlException)
            {
                // Fallback to direct sequence query if stored procedure has not been seeded yet
            }

            // 2. Direct sequence or max-ID fallback inspection
            const string fallbackSql = @"
                DECLARE @NextVal INT = NULL;

                IF EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'EquipmentIdSequence' AND schema_id = SCHEMA_ID('dbo'))
                BEGIN
                    SELECT @NextVal = CAST(current_value AS INT)
                    FROM sys.sequences
                    WHERE name = 'EquipmentIdSequence' AND schema_id = SCHEMA_ID('dbo');
                    
                    IF @NextVal IS NOT NULL
                        SET @NextVal = @NextVal + 1;
                    ELSE
                        SELECT @NextVal = CAST(start_value AS INT) FROM sys.sequences WHERE name = 'EquipmentIdSequence';
                END;

                DECLARE @MaxTableVal INT = 0;
                IF OBJECT_ID(N'dbo.Equipment', N'U') IS NOT NULL
                BEGIN
                    SELECT @MaxTableVal = ISNULL(MAX(TRY_CAST(SUBSTRING(EquipmentID, 5, 10) AS INT)), 0)
                    FROM dbo.Equipment
                    WHERE EquipmentID LIKE 'EQP-%';
                END;

                IF @NextVal IS NULL OR @MaxTableVal >= @NextVal
                    SET @NextVal = @MaxTableVal + 1;

                SELECT 'EQP-' + RIGHT('0000' + CAST(@NextVal AS VARCHAR(10)), 4);";

            await using var fallbackCmd = new SqlCommand(fallbackSql, conn);
            var nextIdObj = await fallbackCmd.ExecuteScalarAsync(ct);
            var nextId = nextIdObj?.ToString() ?? "EQP-0001";

            return Ok(new NextEquipmentIdResponseDto(nextId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve next equipment ID preview.");
            return Ok(new NextEquipmentIdResponseDto("EQP-0001"));
        }
    }

    /// <summary>
    /// Returns all registered equipment records with their metadata and media paths.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<EquipmentResponseDto>>> GetAll(CancellationToken ct)
    {
        var list = new List<EquipmentResponseDto>();
        try
        {
            await using var conn = new SqlConnection(GetConnectionString());
            await conn.OpenAsync(ct);

            const string sql = @"
                IF OBJECT_ID(N'dbo.Equipment', N'U') IS NOT NULL
                BEGIN
                    SELECT 
                        e.EquipmentID,
                        e.EquipmentName,
                        e.EquipmentDescription,
                        e.EquipmentTypeID,
                        ISNULL(et.EquipmentTypeName, e.EquipmentTypeID) AS EquipmentTypeName,
                        e.EquipmentStatus,
                        e.SerialNo,
                        e.ImagePath,
                        ISNULL(e.CreatedAt, SYSUTCDATETIME()) AS CreatedAt
                    FROM dbo.Equipment e
                    LEFT JOIN dbo.EquipmentType et ON et.EquipmentTypeID = e.EquipmentTypeID
                    ORDER BY e.CreatedAt DESC, e.EquipmentID DESC;
                END;";

            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new EquipmentResponseDto(
                    EquipmentId: reader.GetString(0),
                    EquipmentName: reader.GetString(1),
                    EquipmentDescription: reader.IsDBNull(2) ? null : reader.GetString(2),
                    EquipmentType: reader.GetString(4),
                    EquipmentStatus: reader.GetString(5),
                    SerialNo: reader.IsDBNull(6) ? null : reader.GetString(6),
                    ImagePath: reader.IsDBNull(7) ? null : reader.GetString(7),
                    CreatedAt: reader.GetDateTime(8)
                ));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch equipment catalog from SQL database.");
            return StatusCode(500, new { message = "Failed to fetch equipment list.", error = ex.Message });
        }

        return Ok(list);
    }

    /// <summary>
    /// Returns details for a single equipment record by its ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<EquipmentResponseDto>> GetById(string id, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(GetConnectionString());
            await conn.OpenAsync(ct);

            const string sql = @"
                SELECT 
                    e.EquipmentID,
                    e.EquipmentName,
                    e.EquipmentDescription,
                    e.EquipmentTypeID,
                    ISNULL(et.EquipmentTypeName, e.EquipmentTypeID) AS EquipmentTypeName,
                    e.EquipmentStatus,
                    e.SerialNo,
                    e.ImagePath,
                    ISNULL(e.CreatedAt, SYSUTCDATETIME()) AS CreatedAt
                FROM dbo.Equipment e
                LEFT JOIN dbo.EquipmentType et ON et.EquipmentTypeID = e.EquipmentTypeID
                WHERE e.EquipmentID = @ID;";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ID", id.Trim());

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                return Ok(new EquipmentResponseDto(
                    EquipmentId: reader.GetString(0),
                    EquipmentName: reader.GetString(1),
                    EquipmentDescription: reader.IsDBNull(2) ? null : reader.GetString(2),
                    EquipmentType: reader.GetString(4),
                    EquipmentStatus: reader.GetString(5),
                    SerialNo: reader.IsDBNull(6) ? null : reader.GetString(6),
                    ImagePath: reader.IsDBNull(7) ? null : reader.GetString(7),
                    CreatedAt: reader.GetDateTime(8)
                ));
            }

            return NotFound(new { message = $"Equipment '{id}' was not found." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching equipment with ID {EquipmentId}", id);
            return StatusCode(500, new { message = "Error querying equipment record.", error = ex.Message });
        }
    }

    /// <summary>
    /// Creates a new Equipment record via multipart/form-data.
    /// Securely generates sequential custom ID (EQP-####) using atomic SQL Sequence,
    /// saves the uploaded image to the server disk, and stores the file path in the database.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "StaffOrAbove")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CreateEquipment(
        [FromForm] CreateEquipmentRequestDto dto,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        string? savedFilePhysicalPath = null;
        string? relativeWebPath = null;

        // 1. Process and save the uploaded image file (if provided)
        if (dto.ImageFile != null && dto.ImageFile.Length > 0)
        {
            if (dto.ImageFile.Length > MaxImageFileSizeBytes)
            {
                return BadRequest(new { message = "Image file exceeds maximum allowed limit of 10 MB." });
            }

            var extension = Path.GetExtension(dto.ImageFile.FileName).ToLowerInvariant();
            if (!AllowedImageExtensions.Contains(extension))
            {
                return BadRequest(new { message = $"Invalid image format '{extension}'. Only JPG, PNG, and WEBP formats are accepted." });
            }

            // Validate magic bytes to prevent masqueraded executable files
            if (!await IsValidImageFileHeaderAsync(dto.ImageFile, extension, ct))
            {
                return BadRequest(new { message = "The uploaded file does not match a valid image file signature." });
            }

            try
            {
                var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var uploadsFolder = Path.Combine(webRoot, "uploads", "equipment");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var safeFileName = $"eqp_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{extension}";
                savedFilePhysicalPath = Path.Combine(uploadsFolder, safeFileName);

                await using (var fileStream = new FileStream(savedFilePhysicalPath, FileMode.Create, FileAccess.Write))
                {
                    await dto.ImageFile.CopyToAsync(fileStream, ct);
                }

                relativeWebPath = $"/uploads/equipment/{safeFileName}";
                _logger.LogInformation("Saved equipment image to {PhysicalPath}", savedFilePhysicalPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write uploaded image to disk.");
                return StatusCode(500, new { message = "Could not save uploaded image to server storage.", error = ex.Message });
            }
        }

        // 2. Atomic Database Insertion with Concurrency-Safe ID Generation
        try
        {
            await using var conn = new SqlConnection(GetConnectionString());
            await conn.OpenAsync(ct);

            // Execute stored procedure dbo.sp_CreateEquipmentWithCustomId
            await using var cmd = new SqlCommand("dbo.sp_CreateEquipmentWithCustomId", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.AddWithValue("@EquipmentName", dto.EquipmentName.Trim());
            cmd.Parameters.AddWithValue("@EquipmentDescription", (object?)dto.EquipmentDescription?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@EquipmentTypeID", dto.EquipmentType.Trim());
            cmd.Parameters.AddWithValue("@SerialNo", (object?)dto.SerialNo?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ImagePath", (object?)relativeWebPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedByAdminID", User?.Identity?.Name ?? "ADM-001");

            var outputIdParam = new SqlParameter("@AssignedEquipmentID", SqlDbType.NVarChar, 30)
            {
                Direction = ParameterDirection.Output
            };
            cmd.Parameters.Add(outputIdParam);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var createdId = reader.GetString(0);
                var createdName = reader.GetString(1);
                var createdDesc = reader.IsDBNull(2) ? null : reader.GetString(2);
                var createdType = reader.GetString(4);
                var createdStatus = reader.GetString(5);
                var createdSerial = reader.IsDBNull(6) ? null : reader.GetString(6);
                var createdImg = reader.IsDBNull(7) ? null : reader.GetString(7);
                var createdDate = reader.GetDateTime(8);

                var responseDto = new EquipmentResponseDto(
                    EquipmentId: createdId,
                    EquipmentName: createdName,
                    EquipmentDescription: createdDesc,
                    EquipmentType: createdType,
                    EquipmentStatus: createdStatus,
                    SerialNo: createdSerial,
                    ImagePath: createdImg,
                    CreatedAt: createdDate
                );

                _logger.LogInformation("Equipment successfully registered with system ID {EquipmentId}", createdId);
                return CreatedAtAction(nameof(GetById), new { id = createdId }, responseDto);
            }

            // Fallback if output param was set but reader had no rows
            var assignedId = outputIdParam.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(assignedId))
            {
                var fallbackDto = new EquipmentResponseDto(
                    EquipmentId: assignedId,
                    EquipmentName: dto.EquipmentName.Trim(),
                    EquipmentDescription: dto.EquipmentDescription?.Trim(),
                    EquipmentType: dto.EquipmentType.Trim(),
                    EquipmentStatus: "Available",
                    SerialNo: dto.SerialNo?.Trim(),
                    ImagePath: relativeWebPath,
                    CreatedAt: DateTime.UtcNow
                );
                return CreatedAtAction(nameof(GetById), new { id = assignedId }, fallbackDto);
            }

            throw new InvalidOperationException("Stored procedure completed without returning the created record or ID.");
        }
        catch (SqlException ex)
        {
            // If database insert fails, clean up the written file to prevent orphan files on disk
            if (savedFilePhysicalPath != null && System.IO.File.Exists(savedFilePhysicalPath))
            {
                try
                {
                    System.IO.File.Delete(savedFilePhysicalPath);
                    _logger.LogInformation("Cleaned up orphaned image file {Path} after SQL failure.", savedFilePhysicalPath);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Failed to clean up image file {Path} after SQL failure.", savedFilePhysicalPath);
                }
            }

            _logger.LogError(ex, "SQL error registering equipment.");

            // Check if error was duplicate serial number or validation failure
            if (ex.Number == 2601 || ex.Number == 2627 || ex.Message.Contains("already exists in inventory", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(new { message = $"Serial Number '{dto.SerialNo}' is already registered in inventory.", error = ex.Message });
            }

            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            // Clean up file on unexpected exception
            if (savedFilePhysicalPath != null && System.IO.File.Exists(savedFilePhysicalPath))
            {
                try { System.IO.File.Delete(savedFilePhysicalPath); } catch { }
            }

            _logger.LogError(ex, "Unexpected error registering equipment.");
            return StatusCode(500, new { message = "An error occurred while creating the equipment record.", error = ex.Message });
        }
    }

    /// <summary>
    /// Validates file header magic bytes to verify genuine image content.
    /// </summary>
    private static async Task<bool> IsValidImageFileHeaderAsync(IFormFile file, string extension, CancellationToken ct)
    {
        try
        {
            using var stream = file.OpenReadStream();
            var header = new byte[8];
            int read = await stream.ReadAsync(header.AsMemory(0, header.Length), ct);
            if (read < 4) return false;

            return extension switch
            {
                ".jpg" or ".jpeg" => header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
                ".png" => header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47,
                ".webp" => header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46,
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }
}
