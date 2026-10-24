using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace PhotoAevumStudio.Application.DTOs;

/// <summary>
/// Multipart form data request payload for creating an equipment item with an optional media file.
/// </summary>
public class CreateEquipmentRequestDto
{
    [Required(ErrorMessage = "Equipment Name is required.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Equipment Name must be between 2 and 100 characters.")]
    public string EquipmentName { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Equipment Description cannot exceed 1000 characters.")]
    public string? EquipmentDescription { get; set; }

    [Required(ErrorMessage = "Equipment Type is required.")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Equipment Type cannot exceed 50 characters.")]
    public string EquipmentType { get; set; } = string.Empty;

    [StringLength(50, ErrorMessage = "Serial number cannot exceed 50 characters.")]
    [RegularExpression(@"^[a-zA-Z0-9\-_./\s]*$", ErrorMessage = "Serial number contains invalid characters.")]
    public string? SerialNo { get; set; }

    /// <summary>
    /// Optional uploaded image file submitted via multipart/form-data.
    /// </summary>
    public IFormFile? ImageFile { get; set; }
}

/// <summary>
/// Response DTO representing an equipment item with its server-generated ID and image path.
/// </summary>
public record EquipmentResponseDto(
    string EquipmentId,
    string EquipmentName,
    string? EquipmentDescription,
    string EquipmentType,
    string EquipmentStatus,
    string? SerialNo,
    string? ImagePath,
    DateTime CreatedAt
);

/// <summary>
/// Response DTO for previewing the next server-projected custom Equipment ID (EQP-####).
/// </summary>
public record NextEquipmentIdResponseDto(
    string NextEquipmentId
);
