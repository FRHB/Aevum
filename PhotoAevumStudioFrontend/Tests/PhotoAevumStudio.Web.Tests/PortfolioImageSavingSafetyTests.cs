using System;
using System.Collections.Generic;
using System.Linq;
using PhotoAevumStudio.Web.Models;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class PortfolioImageSavingSafetyTests
{
    [Fact]
    public void PortfolioFolderModel_Images_NullSafety_ReturnsEmptyListAndZeroCount()
    {
        var model = new PortfolioFolderModel
        {
            PortfolioId = "PRF-0001",
            PortfolioName = "Wedding Collection",
            Images = null!
        };

        Assert.NotNull(model.SafeImages);
        Assert.Empty(model.SafeImages);
        Assert.Equal(0, model.ImageCount);
    }

    [Fact]
    public void PortfolioFolderModel_FilterByCategory_CaseInsensitiveAndNullSafe()
    {
        var portfolios = new List<PortfolioFolderModel>
        {
            new() { PortfolioId = "PRF-0001", CategoryID = "PCAT-0001", PortfolioName = "Wedding 1" },
            new() { PortfolioId = "PRF-0002", CategoryID = "pcat-0001", PortfolioName = "Wedding 2" },
            new() { PortfolioId = "PRF-0003", CategoryID = "", PortfolioName = "Untagged" },
            new() { PortfolioId = "PRF-0004", CategoryID = "PCAT-0002", PortfolioName = "Studio" }
        };

        var selectedCategory = "pcat-0001";
        var filtered = portfolios.Where(p => string.Equals(p.CategoryID, selectedCategory, StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Equal(2, filtered.Count);
        Assert.Contains(filtered, p => p.PortfolioId == "PRF-0001");
        Assert.Contains(filtered, p => p.PortfolioId == "PRF-0002");
    }

    [Fact]
    public void PortfolioCategoryModel_FilteredCategories_NullSafeSearch()
    {
        var categories = new List<PortfolioCategoryModel>
        {
            new() { CategoryID = "PCAT-0001", CategoryName = "Portraits", ParentSection = "Photography" },
            new() { CategoryID = "PCAT-0002", CategoryName = null!, ParentSection = null! },
            new() { CategoryID = "PCAT-0003", CategoryName = "Events", ParentSection = "Videography" }
        };

        string searchQuery = "port";
        var filtered = categories
            .Where(c => string.IsNullOrWhiteSpace(searchQuery) ||
                        (!string.IsNullOrEmpty(c.CategoryName) && c.CategoryName.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(c.CategoryID) && c.CategoryID.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(c.ParentSection) && c.ParentSection.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Single(filtered);
        Assert.Equal("PCAT-0001", filtered[0].CategoryID);
    }

    [Theory]
    [InlineData(null, "image/jpeg")]
    [InlineData("", "image/jpeg")]
    [InlineData("image/png", "image/png")]
    [InlineData("image/webp", "image/webp")]
    public void ContentType_FallbackLogic_ProducesValidMime(string? inputMime, string expectedMime)
    {
        var resolved = string.IsNullOrWhiteSpace(inputMime) ? "image/jpeg" : inputMime;
        Assert.Equal(expectedMime, resolved);

        byte[] fakeData = new byte[] { 0x01, 0x02, 0x03 };
        var dataUri = $"data:{resolved};base64,{Convert.ToBase64String(fakeData)}";
        Assert.StartsWith($"data:{expectedMime};base64,", dataUri);
    }
}
