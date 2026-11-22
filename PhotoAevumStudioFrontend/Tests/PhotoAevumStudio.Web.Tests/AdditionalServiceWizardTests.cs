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
        Assert.Equal("per hour", model.RateType);
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

    [Theory]
    [InlineData("Per Hour", "per hour")]
    [InlineData("per hour", "per hour")]
    [InlineData("Per Picture", "per picture")]
    [InlineData("per single shot", "per single shot")]
    [InlineData("Per Single Shot", "per single shot")]
    [InlineData("Multi-Shot", "multi-shot")]
    [InlineData(null, "per hour")]
    public void ServiceRateTypeHelper_NormalizesAllCanonicalRateTypes(string? input, string expected)
    {
        Assert.Equal(expected, ServiceRateTypeHelper.Normalize(input));
    }

    [Fact]
    public async Task AdditionalServicesController_CreateAndUpdate_PersistsAndReturnsRateType()
    {
        var mockSvc = new Moq.Mock<PhotoAevumStudio.Application.Interfaces.IStudioManagementService>();
        PhotoAevumStudio.Application.DTOs.CreateAdditionalServiceDto? capturedCreate = null;
        PhotoAevumStudio.Application.DTOs.UpdateAdditionalServiceDto? capturedUpdate = null;

        mockSvc
            .Setup(s => s.CreateAdditionalServiceAsync(
                Moq.It.IsAny<PhotoAevumStudio.Application.DTOs.CreateAdditionalServiceDto>(),
                Moq.It.IsAny<string?>(),
                Moq.It.IsAny<CancellationToken>()))
            .Callback<PhotoAevumStudio.Application.DTOs.CreateAdditionalServiceDto, string?, CancellationToken>((dto, _, _) => capturedCreate = dto)
            .Returns((PhotoAevumStudio.Application.DTOs.CreateAdditionalServiceDto dto, string? _, CancellationToken _) =>
                Task.FromResult(new PhotoAevumStudio.Application.DTOs.AdditionalServiceResponseDto(
                    "ADD-0007", dto.ServiceName, dto.ServiceDescription, dto.ServiceCategory, dto.ServicePrice, false, null, DateTime.UtcNow, null, dto.RateType)));

        mockSvc
            .Setup(s => s.UpdateAdditionalServiceAsync(
                "ADD-0007",
                Moq.It.IsAny<PhotoAevumStudio.Application.DTOs.UpdateAdditionalServiceDto>(),
                Moq.It.IsAny<string?>(),
                Moq.It.IsAny<CancellationToken>()))
            .Callback<string, PhotoAevumStudio.Application.DTOs.UpdateAdditionalServiceDto, string?, CancellationToken>((_, dto, _, _) => capturedUpdate = dto)
            .Returns((string id, PhotoAevumStudio.Application.DTOs.UpdateAdditionalServiceDto dto, string? _, CancellationToken _) =>
                Task.FromResult<PhotoAevumStudio.Application.DTOs.AdditionalServiceResponseDto?>(new PhotoAevumStudio.Application.DTOs.AdditionalServiceResponseDto(
                    id, dto.ServiceName, dto.ServiceDescription, dto.ServiceCategory, dto.ServicePrice, false, null, DateTime.UtcNow, null, dto.RateType)));

        var controller = new PhotoAevumStudio.Api.Controllers.AdditionalServicesController(
            mockSvc.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PhotoAevumStudio.Api.Controllers.AdditionalServicesController>.Instance)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(
                        new System.Security.Claims.ClaimsIdentity(new[]
                        {
                            new System.Security.Claims.Claim("studio_id", "ADM-001"),
                            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin")
                        }, "TestAuth"))
                }
            }
        };

        var createRes = await controller.Create(new PhotoAevumStudio.Api.Controllers.CreateAdditionalServiceApiRequest
        {
            ServiceName = "Fine Art Print",
            ServiceCategoryDescription = "Archival giclee print",
            ServiceCategory = "Print Deliverables",
            ServicePrice = 120.00m,
            RateType = "per picture"
        }, CancellationToken.None);

        var createdResult = Assert.IsType<Microsoft.AspNetCore.Mvc.CreatedAtActionResult>(createRes);
        var createdDto = Assert.IsType<PhotoAevumStudio.Application.DTOs.AdditionalServiceResponseDto>(createdResult.Value);
        Assert.NotNull(capturedCreate);
        Assert.Equal("per picture", capturedCreate!.RateType);
        Assert.Equal("per picture", createdDto.RateType);

        var updateRes = await controller.Update("ADD-0007", new PhotoAevumStudio.Api.Controllers.UpdateAdditionalServiceApiRequest
        {
            ServiceName = "Fine Art Print",
            ServiceCategoryDescription = "Updated archival print",
            ServiceCategory = "Print Deliverables",
            ServicePrice = 150.00m,
            RateType = "per single shot"
        }, CancellationToken.None);

        var okResult = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(updateRes);
        var updatedDto = Assert.IsType<PhotoAevumStudio.Application.DTOs.AdditionalServiceResponseDto>(okResult.Value);
        Assert.NotNull(capturedUpdate);
        Assert.Equal("per single shot", capturedUpdate!.RateType);
        Assert.Equal("per single shot", updatedDto.RateType);
    }
}
