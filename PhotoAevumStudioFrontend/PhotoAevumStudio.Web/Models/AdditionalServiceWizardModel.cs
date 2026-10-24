using System.ComponentModel.DataAnnotations;

namespace PhotoAevumStudio.Web.Models;

/// <summary>
/// State model for the 3-step Additional Service (Add-On) Wizard Modal.
/// Holds state across Page 1 (Details), Page 2 (Media), and Page 3 (Overview & Save).
/// </summary>
public class AdditionalServiceWizardFormModel
{
    // ── Page 1: Details Input ────────────────────────────────────────────────
    [Required(ErrorMessage = "Service category name (Add-On name) is required.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Service name must be between 2 and 100 characters.")]
    public string ServiceCategoryName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Service category description is required.")]
    [StringLength(500, MinimumLength = 5, ErrorMessage = "Description must be between 5 and 500 characters.")]
    public string ServiceCategoryDescription { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select a service category.")]
    public string ServiceCategory { get; set; } = "Portrait Photography";

    [Required(ErrorMessage = "Service price is required.")]
    [Range(0.00, 100000.00, ErrorMessage = "Price must be a valid non-negative amount.")]
    public decimal ServicePrice { get; set; } = 75.00m;

    // ── Page 2: Media ────────────────────────────────────────────────────────
    public byte[]? ImageBytes { get; set; }
    public string? ImageMimeType { get; set; }
    public string? ImageFileName { get; set; }
    public long ImageFileSize { get; set; }
    public string? ImagePreviewUrl { get; set; }

    // ── Page 3: Auto-Generated ID & Overview ─────────────────────────────────
    public string GeneratedId { get; set; } = "ADD-0001";
    public bool IsIdLoaded { get; set; } = false;

    public void Reset()
    {
        ServiceCategoryName = string.Empty;
        ServiceCategoryDescription = string.Empty;
        ServiceCategory = "Portrait Photography";
        ServicePrice = 75.00m;
        ImageBytes = null;
        ImageMimeType = null;
        ImageFileName = null;
        ImageFileSize = 0;
        ImagePreviewUrl = null;
        GeneratedId = "ADD-0001";
        IsIdLoaded = false;
    }
}
