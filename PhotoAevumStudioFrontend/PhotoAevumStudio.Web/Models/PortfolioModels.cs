using System;
using System.Collections.Generic;

namespace PhotoAevumStudio.Web.Models;

public class PortfolioCategoryModel
{
    public string CategoryID { get; set; } = string.Empty;

    // Backward-compatible alias
    public string CategoryCode
    {
        get => CategoryID;
        set => CategoryID = value;
    }

    public string CategoryName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ParentSection { get; set; } = "Photography";
    public string ColorTag { get; set; } = "Blue";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int PortfolioCount { get; set; }
}

public class PortfolioImageModel
{
    public int Id { get; set; }
    public string PortfolioId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

public class PortfolioFolderModel
{
    public string PortfolioId { get; set; } = string.Empty;
    public string PortfolioName { get; set; } = string.Empty;
    public string? PortfolioDescription { get; set; }
    public string CategoryID { get; set; } = string.Empty;

    // Backward-compatible alias
    public string CategoryCode
    {
        get => CategoryID;
        set => CategoryID = value;
    }

    public string CategoryName { get; set; } = string.Empty;
    public bool IsFeatured { get; set; }
    public string? HighlightImageFileName { get; set; }
    public string HighlightImageUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<PortfolioImageModel> Images { get; set; } = new();
}

public class CreatePortfolioDto
{
    public string PortfolioName { get; set; } = string.Empty;
    public string? PortfolioDescription { get; set; }
    public string CategoryID { get; set; } = string.Empty;

    public string CategoryCode
    {
        get => CategoryID;
        set => CategoryID = value;
    }

    public bool IsFeatured { get; set; }
    public List<string> ImageFileNames { get; set; } = new();
    public List<byte[]> ImageBytes { get; set; } = new();
    public List<string> ImageContentTypes { get; set; } = new();
}

public class UpdatePortfolioDto
{
    public string PortfolioId { get; set; } = string.Empty;
    public string PortfolioName { get; set; } = string.Empty;
    public string? PortfolioDescription { get; set; }
    public string CategoryID { get; set; } = string.Empty;
}
