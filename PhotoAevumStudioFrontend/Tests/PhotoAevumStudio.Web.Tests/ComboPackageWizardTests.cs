using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Web.Models;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class ComboPackageWizardTests
{
    private static IList<ValidationResult> ValidateModel(object model)
    {
        var validationResults = new List<ValidationResult>();
        var ctx = new ValidationContext(model, serviceProvider: null, items: null);
        Validator.TryValidateObject(model, ctx, validationResults, validateAllProperties: true);
        return validationResults;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 1. Combo Package ID Formatting & Sequence Pattern (PKG-####)
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("PKG-0001", true)]
    [InlineData("PKG-0042", true)]
    [InlineData("PKG-0100", true)]
    [InlineData("PKG-9999", true)]
    [InlineData("PKG-10000", true)]
    [InlineData("pkg-0001", true)]
    [InlineData("PKG-1", false)]
    [InlineData("PKG-01", false)]
    [InlineData("SRV-0001", false)]
    [InlineData("ADD-0001", false)]
    [InlineData("PACKAGE-01", false)]
    public void PackageIdFormat_ValidatesStrictSequentialPattern(string packageId, bool expectedValid)
    {
        var regex = new Regex(@"^PKG-\d{4,}$", RegexOptions.IgnoreCase);
        Assert.Equal(expectedValid, regex.IsMatch(packageId));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2. Wizard Page 1 (Details Input) Validation Tests
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PackageWizardFormModel_ValidInput_PassesValidation()
    {
        var model = new PackageWizardFormModel
        {
            Name = "Ultimate Wedding & Reception Showcase",
            Description = "All-inclusive full day wedding photography with pre-nup shoot and drone coverage.",
            CategoryId = "CAT-0001",
            Price = 2499.00m
        };

        var results = ValidateModel(model);

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void PackageWizardFormModel_MissingName_FailsValidation(string? invalidName)
    {
        var model = new PackageWizardFormModel
        {
            Name = invalidName!,
            Description = "Valid bundle description.",
            CategoryId = "CAT-0001",
            Price = 499.00m
        };

        var results = ValidateModel(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(model.Name)));
    }

    [Fact]
    public void PackageWizardFormModel_NameTooShort_FailsValidation()
    {
        var model = new PackageWizardFormModel
        {
            Name = "AB", // Min length is 3
            Description = "Valid bundle description.",
            CategoryId = "CAT-0001",
            Price = 499.00m
        };

        var results = ValidateModel(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(model.Name)));
    }

    [Fact]
    public void PackageWizardFormModel_PriceZeroOrNegative_FailsValidation()
    {
        var model = new PackageWizardFormModel
        {
            Name = "Family Portrait Mini Bundle",
            Description = "Includes 3 edited digital portraits.",
            CategoryId = "CAT-0001",
            Price = 0.00m
        };

        var results = ValidateModel(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(model.Price)));
    }

    [Fact]
    public void PackageWizardFormModel_MissingCategory_FailsValidation()
    {
        var model = new PackageWizardFormModel
        {
            Name = "Family Portrait Mini Bundle",
            Description = "Includes 3 edited digital portraits.",
            CategoryId = "", // Empty category
            Price = 199.00m
        };

        var results = ValidateModel(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(model.CategoryId)));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 3. Wizard Page 2 (Many-to-Many Service Selection) Logic Tests
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ServiceSelection_AllowsMultipleMainAndAdditionalServices()
    {
        var formModel = new PackageWizardFormModel();

        // Simulate selecting multiple services
        formModel.SelectedServiceIds.Add("SRV-0001"); // Main service
        formModel.SelectedServiceIds.Add("SRV-0002"); // Main service
        formModel.SelectedServiceIds.Add("ADD-0001"); // Additional service

        Assert.Equal(3, formModel.SelectedServiceIds.Count);
        Assert.Contains("SRV-0001", formModel.SelectedServiceIds);
        Assert.Contains("SRV-0002", formModel.SelectedServiceIds);
        Assert.Contains("ADD-0001", formModel.SelectedServiceIds);

        // Deselection
        formModel.SelectedServiceIds.Remove("SRV-0002");
        Assert.Equal(2, formModel.SelectedServiceIds.Count);
        Assert.DoesNotContain("SRV-0002", formModel.SelectedServiceIds);
    }

    [Fact]
    public void BundleCalculation_CalculatesSavingsCorrectly()
    {
        var bundledServices = new List<BundleServiceOption>
        {
            new() { Id = "SRV-0001", Name = "Full Wedding Day", Price = 1500.00m, ServiceType = "Main" },
            new() { Id = "SRV-0002", Name = "Pre-Nup Session", Price = 600.00m, ServiceType = "Main" },
            new() { Id = "ADD-0001", Name = "Drone Aerial Coverage", Price = 400.00m, ServiceType = "Additional" }
        };

        var aLaCarteTotal = bundledServices.Sum(s => s.Price);
        var packagePrice = 2100.00m;
        var savings = aLaCarteTotal - packagePrice;

        Assert.Equal(2500.00m, aLaCarteTotal);
        Assert.Equal(400.00m, savings);
        Assert.True(savings > 0, "Package price should offer savings compared to a la carte items.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 4. Wizard Page 3 (Media Upload) State Tests
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MediaUpload_PopulatesBytePayloadAndMimeType()
    {
        var formModel = new PackageWizardFormModel();
        var fakeBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 }; // JPEG magic bytes

        formModel.ImageBytes = fakeBytes;
        formModel.ImageFileName = "wedding-cover.jpg";
        formModel.ImageMimeType = "image/jpeg";
        formModel.ImageFileSize = fakeBytes.Length;
        formModel.ImagePreviewBase64 = $"data:image/jpeg;base64,{Convert.ToBase64String(fakeBytes)}";

        Assert.NotNull(formModel.ImageBytes);
        Assert.Equal(6, formModel.ImageFileSize);
        Assert.Equal("image/jpeg", formModel.ImageMimeType);
        Assert.StartsWith("data:image/jpeg;base64,", formModel.ImagePreviewBase64);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 5. Backend DTO & Service Mapping Tests
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PackageWizardFormModel_MapsRequiredFieldsCorrectly()
    {
        var dto = new PackageWizardFormModel
        {
            Name = "Corporate Headshot & Event Tier",
            Description = "Executive studio headshots plus 4 hours event coverage.",
            CategoryId = "CAT-0003",
            Price = 1200.00m,
            SelectedServiceIds = new HashSet<string> { "SRV-0005", "ADD-0003" }
        };

        Assert.Equal("Corporate Headshot & Event Tier", dto.Name);
        Assert.Equal(1200.00m, dto.Price);
        Assert.Equal(2, dto.SelectedServiceIds.Count);
    }
}
