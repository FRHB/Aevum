using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using PhotoAevumStudio.Web.Models;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class AdditionalServiceWizardTests
{
    private static IList<ValidationResult> ValidateModel(object model)
    {
        var validationResults = new List<ValidationResult>();
        var ctx = new ValidationContext(model, serviceProvider: null, items: null);
        Validator.TryValidateObject(model, ctx, validationResults, validateAllProperties: true);
        return validationResults;
    }

    [Fact]
    public void ValidModel_PassesValidation()
    {
        var model = new AdditionalServiceWizardFormModel
        {
            ServiceCategoryName = "4K Drone B-Roll Footage",
            ServiceCategoryDescription = "Professional 4K drone cinematography for outdoor wedding ceremonies and receptions.",
            ServiceCategory = "Aerial & Drone Operations",
            ServicePrice = 250.00m,
            GeneratedId = "ADD-0005"
        };

        var results = ValidateModel(model);

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void MissingServiceName_FailsValidation(string? invalidName)
    {
        var model = new AdditionalServiceWizardFormModel
        {
            ServiceCategoryName = invalidName!,
            ServiceCategoryDescription = "Valid description here.",
            ServiceCategory = "Portrait Photography",
            ServicePrice = 50.00m
        };

        var results = ValidateModel(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(model.ServiceCategoryName)));
    }

    [Fact]
    public void ServiceName_TooShort_FailsValidation()
    {
        var model = new AdditionalServiceWizardFormModel
        {
            ServiceCategoryName = "A",
            ServiceCategoryDescription = "Valid description here.",
            ServiceCategory = "Portrait Photography",
            ServicePrice = 50.00m
        };

        var results = ValidateModel(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(model.ServiceCategoryName)));
    }

    [Fact]
    public void ServiceName_TooLong_FailsValidation()
    {
        var model = new AdditionalServiceWizardFormModel
        {
            ServiceCategoryName = new string('X', 101),
            ServiceCategoryDescription = "Valid description here.",
            ServiceCategory = "Portrait Photography",
            ServicePrice = 50.00m
        };

        var results = ValidateModel(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(model.ServiceCategoryName)));
    }

    [Theory]
    [InlineData(-1.00)]
    [InlineData(-100.50)]
    public void NegativePrice_FailsValidation(decimal negativePrice)
    {
        var model = new AdditionalServiceWizardFormModel
        {
            ServiceCategoryName = "Valid Name",
            ServiceCategoryDescription = "Valid description here.",
            ServiceCategory = "Portrait Photography",
            ServicePrice = negativePrice
        };

        var results = ValidateModel(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(model.ServicePrice)));
    }

    [Fact]
    public void MissingDescription_FailsValidation()
    {
        var model = new AdditionalServiceWizardFormModel
        {
            ServiceCategoryName = "Valid Name",
            ServiceCategoryDescription = "",
            ServiceCategory = "Portrait Photography",
            ServicePrice = 100.00m
        };

        var results = ValidateModel(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(model.ServiceCategoryDescription)));
    }

    [Fact]
    public void Reset_RestoresDefaults()
    {
        var model = new AdditionalServiceWizardFormModel
        {
            ServiceCategoryName = "Custom Title",
            ServiceCategoryDescription = "Custom Description",
            ServiceCategory = "Custom Framing & Prints",
            ServicePrice = 300.00m,
            ImageBytes = new byte[] { 1, 2, 3 },
            ImageFileName = "test.png",
            ImagePreviewUrl = "data:image/png;base64,AQID",
            GeneratedId = "ADD-0099",
            IsIdLoaded = true
        };

        model.Reset();

        Assert.Empty(model.ServiceCategoryName);
        Assert.Empty(model.ServiceCategoryDescription);
        Assert.Equal("Portrait Photography", model.ServiceCategory);
        Assert.Equal(75.00m, model.ServicePrice);
        Assert.Null(model.ImageBytes);
        Assert.Null(model.ImageFileName);
        Assert.Null(model.ImagePreviewUrl);
        Assert.Equal("ADD-0001", model.GeneratedId);
        Assert.False(model.IsIdLoaded);
    }

    [Theory]
    [InlineData("ADD-0001")]
    [InlineData("ADD-0042")]
    [InlineData("ADD-9999")]
    public void GeneratedId_MatchesStrictPattern(string generatedId)
    {
        var regex = new Regex(@"^ADD-\d{4}$");
        Assert.Matches(regex, generatedId);
    }
}
