using System.ComponentModel.DataAnnotations;

namespace PhotoAevumStudio.Web.Models;

public class CategoryLookupItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class MainServiceWizardModel
{
    [Required(ErrorMessage = "Service name is required.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Service name must be between 2 and 100 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Service description is required.")]
    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Service price is required.")]
    [Range(0.01, 1000000.00, ErrorMessage = "Price must be greater than zero.")]
    public decimal Price { get; set; } = 150.00m;

    [Required(ErrorMessage = "Please select a service category.")]
    public string CategoryId { get; set; } = string.Empty;

    public string CategoryName { get; set; } = string.Empty;

    /// <summary>
    /// Auto-generated custom Service ID (format: SRV-####). Strictly read-only and assigned by system.
    /// </summary>
    public string GeneratedId { get; set; } = string.Empty;

    // Media
    public byte[]? ImageBytes { get; set; }
    public string? ImageFileName { get; set; }
    public string? ImageMimeType { get; set; }
    public string? ImagePreviewUrl { get; set; }
    public long ImageSize { get; set; }
}

public class CategoryWizardModel
{
    [Required(ErrorMessage = "Service category name is required.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Category name must be between 2 and 100 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Service category description is required.")]
    [StringLength(500, ErrorMessage = "Category description cannot exceed 500 characters.")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Auto-generated custom Category ID (format: CAT-####). Strictly read-only and assigned by system.
    /// </summary>
    public string GeneratedId { get; set; } = string.Empty;
}
