using System.ComponentModel.DataAnnotations;

namespace PhotoAevumStudio.Web.Models;

/// <summary>
/// Comprehensive Form model for the 4-Step Combo Package Service Wizard.
/// </summary>
public class PackageWizardFormModel : IValidatableObject
{
    // ── Step 1: Details Input ────────────────────────────────────────────────
    [Required(ErrorMessage = "Combo Package Name is required.")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "Package name must be between 3 and 100 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Combo Package Description is required.")]
    [StringLength(500, MinimumLength = 5, ErrorMessage = "Description must be between 5 and 500 characters.")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select a service category.")]
    public string CategoryId { get; set; } = string.Empty;

    public string CategoryName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Package price is required.")]
    [Range(0.01, 1000000.00, ErrorMessage = "Package price must be greater than zero.")]
    public decimal Price { get; set; } = 499.00m;
    public string Category { get; set; } = "Main Services";

    // ── Step 2: Service Selection (Many-to-Many) ─────────────────────────────
    public HashSet<string> SelectedServiceIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ── Step 3: Media Upload ─────────────────────────────────────────────────
    public byte[]? ImageBytes { get; set; }
    public string? ImageFileName { get; set; }
    public string? ImageMimeType { get; set; }
    public long ImageFileSize { get; set; }
    public string? ImagePreviewBase64 { get; set; }

    // ── Step 4: Overview & Auto-Generated ID ─────────────────────────────────
    /// <summary>
    /// Strictly read-only custom ID assigned via backend logic (format: PKG-####).
    /// </summary>
    public string GeneratedPackageId { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name))
            yield return new ValidationResult("Package name is required.", new[] { nameof(Name) });

        if (string.IsNullOrWhiteSpace(CategoryId))
            yield return new ValidationResult("Service category is required.", new[] { nameof(CategoryId) });

        if (Price <= 0)
            yield return new ValidationResult("Price must be greater than zero.", new[] { nameof(Price) });
    }
}

/// <summary>
/// Category item for the Wizard Step 1 dropdown.
/// </summary>
public class CategoryLookupModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>
/// Selectable service option for Wizard Step 2 many-to-many bundling.
/// </summary>
public class BundleServiceOption
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string ServiceType { get; set; } = "Main"; // "Main" or "Additional"
    public bool HasImage { get; set; }
    public bool IsSelected { get; set; }
}


public class CreateComboPackageModel
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public List<string> SelectedServiceIds { get; set; } = new();
}
