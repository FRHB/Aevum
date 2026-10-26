using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PhotoAevumStudio.Application.Entities;

[Table("PortfolioCategory")]
public class PortfolioCategoryEntity
{
    [Key]
    [MaxLength(100)]
    public string CategoryCode { get; set; } = string.Empty;

    [Required]
    [MaxLength(240)]
    public string CategoryName { get; set; } = string.Empty;

    [MaxLength(800)]
    public string? Description { get; set; }

    [MaxLength(60)]
    public string ParentSection { get; set; } = "Photography";

    [MaxLength(40)]
    public string ColorTag { get; set; } = "Blue";

    public bool IsActive { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("Portfolios")]
public class PortfolioEntity
{
    [Key]
    [MaxLength(50)]
    public string PortfolioId { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string PortfolioName { get; set; } = string.Empty;

    public string? PortfolioDescription { get; set; }

    [Required]
    [MaxLength(100)]
    public string CategoryCode { get; set; } = string.Empty;

    public bool IsFeatured { get; set; } = false;

    [MaxLength(500)]
    public string? HighlightImageFileName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<PortfolioImage> Images { get; set; } = new List<PortfolioImage>();
}

[Table("PortfolioImages")]
public class PortfolioImage
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string PortfolioId { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string FileName { get; set; } = string.Empty;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(PortfolioId))]
    public virtual PortfolioEntity? Portfolio { get; set; }
}
