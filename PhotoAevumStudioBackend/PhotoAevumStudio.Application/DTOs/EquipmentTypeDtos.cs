namespace PhotoAevumStudio.Application.DTOs;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Request payload to create an Equipment Type record.
/// </summary>
public record CreateEquipmentTypeRequest
{
    [Required(ErrorMessage = "Equipment Type Name is required.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Equipment Type Name must be between 2 and 100 characters.")]
    public string EquipmentTypeName { get; init; } = string.Empty;

    [Required(ErrorMessage = "Equipment Type ID is required.")]
    [RegularExpression(@"^ETYP-\d{4,}$", ErrorMessage = "ID format must follow 'ETYP-####'.")]
    public string EquipmentTypeId { get; init; } = string.Empty;
}

/// <summary>
/// Response holding the Equipment Type entity details.
/// </summary>
public record EquipmentTypeResponse(
    string EquipmentTypeId,
    string EquipmentTypeName
);

/// <summary>
/// Response containing the atomically generated sequential ID.
/// </summary>
public record GeneratedEquipmentTypeIdResponse(string GeneratedId);
