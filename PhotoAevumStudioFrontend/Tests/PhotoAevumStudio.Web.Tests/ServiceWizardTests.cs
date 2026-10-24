using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Web.Models;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class ServiceWizardTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // 1. Service ID & Category ID Formatting Tests (SRV-#### and CAT-####)
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("SRV-0001", true)]
    [InlineData("SRV-0042", true)]
    [InlineData("SRV-9999", true)]
    [InlineData("SRV-10000", true)]
    [InlineData("SRV-1", false)]
    [InlineData("SRV-01", false)]
    [InlineData("CAT-0001", false)]
    [InlineData("srv-0001", true)]
    public void ServiceIdFormat_ValidatesExpectedFormat(string id, bool expectedValid)
    {
        var regex = new Regex(@"^SRV-\d{4,}$", RegexOptions.IgnoreCase);
        Assert.Equal(expectedValid, regex.IsMatch(id));
    }

    [Theory]
    [InlineData("CAT-0001", true)]
    [InlineData("CAT-0005", true)]
    [InlineData("CAT-1234", true)]
    [InlineData("CAT-1", false)]
    [InlineData("SRV-0001", false)]
    [InlineData("cat-0001", true)]
    public void CategoryIdFormat_ValidatesExpectedFormat(string id, bool expectedValid)
    {
        var regex = new Regex(@"^CAT-\d{4,}$", RegexOptions.IgnoreCase);
        Assert.Equal(expectedValid, regex.IsMatch(id));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2. Main Service Wizard Model & Validation Tests
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MainServiceWizardModel_ValidatesRequiredFields()
    {
        var model = new MainServiceWizardModel
        {
            Name = "Portrait Photography",
            Description = "Standard portrait shoot with studio lighting.",
            Price = 2500.00m,
            CategoryId = "CAT-0001"
        };

        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, context, results, true);

        Assert.True(isValid);
        Assert.Empty(results);
    }

    [Fact]
    public void MainServiceWizardModel_FailsWhenNameOrCategoryMissing()
    {
        var model = new MainServiceWizardModel
        {
            Name = "",
            Description = "A session",
            Price = 1000m,
            CategoryId = ""
        };

        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, context, results, true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(MainServiceWizardModel.Name)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(MainServiceWizardModel.CategoryId)));
    }

    [Fact]
    public void MainServiceWizardModel_FailsWhenPriceIsZeroOrNegative()
    {
        var model = new MainServiceWizardModel
        {
            Name = "Family Shoot",
            Description = "Outdoor family photography.",
            Price = 0m,
            CategoryId = "CAT-0002"
        };

        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, context, results, true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(MainServiceWizardModel.Price)));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 3. Category Wizard Model & Validation Tests
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CategoryWizardModel_ValidatesRequiredFields()
    {
        var model = new CategoryWizardModel
        {
            Name = "Event Photography",
            Description = "Corporate and private event celebrations."
        };

        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, context, results, true);

        Assert.True(isValid);
        Assert.Empty(results);
    }

    [Fact]
    public void CategoryWizardModel_FailsWhenNameOrDescriptionMissing()
    {
        var model = new CategoryWizardModel
        {
            Name = "  ",
            Description = ""
        };

        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, context, results, true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CategoryWizardModel.Name)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CategoryWizardModel.Description)));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 4. API DTO Verification
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CreateServiceDto_StoresCorrectValues()
    {
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic bytes
        var dto = new CreateServiceDto(
            Name: "Graduation Shoot",
            Description: "Studio cap & gown session",
            Price: 1800m,
            CategoryId: "CAT-0001",
            ImageBytes: imageBytes,
            ImageMimeType: "image/png",
            ImageFileName: "cap.png"
        );

        Assert.Equal("Graduation Shoot", dto.Name);
        Assert.Equal("Studio cap & gown session", dto.Description);
        Assert.Equal(1800m, dto.Price);
        Assert.Equal("CAT-0001", dto.CategoryId);
        Assert.NotNull(dto.ImageBytes);
        Assert.Equal(4, dto.ImageBytes!.Length);
        Assert.Equal("image/png", dto.ImageMimeType);
        Assert.Equal("cap.png", dto.ImageFileName);
    }
}
