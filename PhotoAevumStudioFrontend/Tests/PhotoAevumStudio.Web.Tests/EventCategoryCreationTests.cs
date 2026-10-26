using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoAevumStudio.Api.Controllers;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;
using PhotoAevumStudio.Infrastructure.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class EventCategoryCreationTests
{
    private const string ConnectionString =
        "Data Source=.\\SQLEXPRESS;Initial Catalog=PhotoAevumStudio;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True;";

    // ══════════════════════════════════════════════════════════════════════════
    // 1. Entity Framework Core Model & Relationship Configuration Tests
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EventCategory_EfCoreModel_IsMappedToServiceCategoryTable()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        using var db = new ApplicationDbContext(options);
        var model = db.Model;

        var categoryEntity = model.FindEntityType(typeof(EventCategory));
        Assert.NotNull(categoryEntity);
        Assert.Equal("ServiceCategory", categoryEntity.GetTableName());
        Assert.Equal("dbo", categoryEntity.GetSchema());

        var idProp = categoryEntity.FindProperty(nameof(EventCategory.Id));
        Assert.NotNull(idProp);
        Assert.Equal("ServiceCategoryID", idProp.GetColumnName());
        Assert.Equal(30, idProp.GetMaxLength());

        var nameProp = categoryEntity.FindProperty(nameof(EventCategory.Name));
        Assert.NotNull(nameProp);
        Assert.Equal("ServiceCategoryName", nameProp.GetColumnName());
        Assert.False(nameProp.IsNullable);

        var descProp = categoryEntity.FindProperty(nameof(EventCategory.Description));
        Assert.NotNull(descProp);
        Assert.Equal("ServiceCategoryDescription", descProp.GetColumnName());
        Assert.False(descProp.IsNullable);
    }

    [Fact]
    public void ServiceCategoryJoin_EfCoreModel_IsMappedWithCascadeRelationship()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        using var db = new ApplicationDbContext(options);
        var model = db.Model;

        var joinEntity = model.FindEntityType(typeof(ServiceCategoryJoin));
        Assert.NotNull(joinEntity);
        Assert.Equal("ServiceCategoryJoins", joinEntity.GetTableName());
        Assert.Equal("dbo", joinEntity.GetSchema());

        var idProp = joinEntity.FindProperty(nameof(ServiceCategoryJoin.Id));
        Assert.NotNull(idProp);
        Assert.Equal("ServiceCategoryJoinID", idProp.GetColumnName());

        var catIdProp = joinEntity.FindProperty(nameof(ServiceCategoryJoin.CategoryId));
        Assert.NotNull(catIdProp);
        Assert.Equal("ServiceCategoryID", catIdProp.GetColumnName());

        // Navigation check from EventCategory -> Associations
        var categoryEntity = model.FindEntityType(typeof(EventCategory));
        var associationsNav = categoryEntity?.FindNavigation(nameof(EventCategory.Associations));
        Assert.NotNull(associationsNav);
        Assert.True(associationsNav.IsCollection);

        var foreignKey = associationsNav.ForeignKey;
        Assert.NotNull(foreignKey);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2. Controller & Request Mapping Tests
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CategoriesController_Create_PassesAssociationsToServiceAndReturnsCreated()
    {
        var fakeService = new StubStudioManagementService();
        var controller = new CategoriesController(fakeService, NullLogger<CategoriesController>.Instance);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("studio_id", "ADM-0001"),
            new Claim(ClaimTypes.Role, "Admin")
        }, "mock"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        var request = new CreateCategoryRequest
        {
            Name = "Corporate Headshots",
            Description = "Executive portraiture sessions for businesses and branding.",
            AssociatedServiceIds = new List<string> { "SRV-0001" }
        };

        var result = await controller.Create(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(CategoriesController.GetById), createdResult.ActionName);

        var responseDto = Assert.IsType<CategoryDto>(createdResult.Value);
        Assert.Equal("CAT-TEST-001", responseDto.Id);
        Assert.Equal("Corporate Headshots", responseDto.Name);
        Assert.NotNull(responseDto.AssociatedServices);
        Assert.Contains("Corporate Headshots - Standard Session", responseDto.AssociatedServices);
    }

    [Fact]
    public async Task CategoriesController_GetById_ReturnsOkWhenCategoryExists()
    {
        var fakeService = new StubStudioManagementService();
        var controller = new CategoriesController(fakeService, NullLogger<CategoriesController>.Instance);

        var result = await controller.GetById("CAT-TEST-001", CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var category = Assert.IsType<CategoryDto>(okResult.Value);

        Assert.Equal("CAT-TEST-001", category.Id);
        Assert.NotNull(category.AssociatedServices);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 3. Integration / Automatic Association Assignment Logic Tests
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StudioManagementService_CreateCategoryAsync_AutomaticallyAssignsAssociations()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        using var db = new ApplicationDbContext(options);
        var service = new StudioManagementService(db);

        var uniqueSuffix = Guid.NewGuid().ToString("N")[..6];
        var categoryName = $"Portrait Special {uniqueSuffix}";
        var dto = new CreateCategoryDto(
            categoryName,
            "Automatic association test category for studio portrait sessions."
        );

        var result = await service.CreateCategoryAsync(dto, "ADM-0001", CancellationToken.None);

        Assert.NotNull(result);
        Assert.StartsWith("CAT-", result.Id);
        Assert.Equal(categoryName, result.Name);
        Assert.NotNull(result.AssociatedServices);
        Assert.NotEmpty(result.AssociatedServices);

        // Verify retrieval via GetCategoryByIdAsync loads the automatic associations
        var fetched = await service.GetCategoryByIdAsync(result.Id, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal(result.Id, fetched.Id);
        Assert.NotNull(fetched.AssociatedServices);
        Assert.NotEmpty(fetched.AssociatedServices);

        // Verify that EF Core tracks the relationship between EventCategory and ServiceCategoryJoin
        var efCategory = await db.EventCategories
            .AsNoTracking()
            .Include(c => c.Associations)
            .FirstOrDefaultAsync(c => c.Id == result.Id);

        Assert.NotNull(efCategory);
        Assert.NotEmpty(efCategory.Associations);
        Assert.All(efCategory.Associations, j => Assert.Equal(result.Id, j.CategoryId));
    }

    // ── Stub Service for isolated Controller Testing ──────────────────────────
    private class StubStudioManagementService : IStudioManagementService
    {
        public Task<string> GetNextCategoryIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("CAT-TEST-001");

        public Task<CategoryDto> CreateCategoryAsync(CreateCategoryDto dto, string? requestingAdminId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CategoryDto(
                "CAT-TEST-001",
                dto.Name,
                dto.Description,
                dto.AssociatedServiceIds?.Count > 0 
                    ? new List<string> { "Corporate Headshots - Standard Session" } 
                    : new List<string> { "Default Studio Session" }
            ));

        public Task<CategoryDto?> GetCategoryByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<CategoryDto?>(new CategoryDto(id, "Corporate Headshots", "Description", new List<string> { "Standard Session" }));

        public Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CategoryDto>>(new List<CategoryDto>
            {
                new("CAT-TEST-001", "Corporate Headshots", "Description", new List<string> { "Standard Session" })
            });

        public Task<string> GetNextServiceIdAsync(CancellationToken cancellationToken = default) => Task.FromResult("SRV-0001");
        public Task<string> GetNextAdditionalServiceIdAsync(CancellationToken cancellationToken = default) => Task.FromResult("ADD-0001");
        public Task<ServiceCreatedResponseDto> CreateServiceAsync(CreateServiceDto dto, string? requestingAdminId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AdditionalServiceResponseDto> CreateAdditionalServiceAsync(CreateAdditionalServiceDto dto, string? requestingAdminId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<string> GetNextComboPackageIdAsync(CancellationToken cancellationToken = default) => Task.FromResult("PKG-0001");
        public Task<IReadOnlyList<BundledServiceSelectionItem>> GetAvailableServicesForBundlingAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<BundledServiceSelectionItem>>(new List<BundledServiceSelectionItem>());
        public Task<ComboPackageCreatedResponseDto> CreateComboPackageWithServicesAsync(CreateComboPackageModel model, byte[]? imageBytes, string? imageMimeType, string? requestingAdminId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Employee>> GetEmployeesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Employee>>(new List<Employee>());
        public Task<IReadOnlyList<string>> GetSpecializationsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(new List<string>());
        public Task<IReadOnlyList<Equipment>> GetEquipmentAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Equipment>>(new List<Equipment>());
        public Task<IReadOnlyList<StudioService>> GetServicesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StudioService>>(new List<StudioService>());
        public Task<IReadOnlyList<ServiceSummaryDto>> GetServicesSummaryAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ServiceSummaryDto>>(new List<ServiceSummaryDto>());
        public Task<ServiceDetailDto?> GetServiceDetailAsync(string serviceId, CancellationToken cancellationToken = default) => Task.FromResult<ServiceDetailDto?>(null);
        public Task<ServiceDetailDto?> GetServiceDetailWithImageAsync(string serviceId, CancellationToken cancellationToken = default) => Task.FromResult<ServiceDetailDto?>(null);
        public Task<bool> UpdateServiceImageAsync(string serviceId, byte[] imageBytes, string mimeType, string? requestingAdminId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<IReadOnlyList<AdditionalServiceResponseDto>> GetAdditionalServicesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AdditionalServiceResponseDto>>(new List<AdditionalServiceResponseDto>());
        public Task<IReadOnlyList<ServiceSummaryDto>> GetAdditionalServicesSummaryAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ServiceSummaryDto>>(new List<ServiceSummaryDto>());
        public Task<AdditionalServiceResponseDto?> GetAdditionalServiceDetailAsync(string additionalServiceId, CancellationToken cancellationToken = default) => Task.FromResult<AdditionalServiceResponseDto?>(null);
        public Task<ServiceDetailDto?> GetAdditionalServiceDetailWithImageAsync(string addOnId, CancellationToken cancellationToken = default) => Task.FromResult<ServiceDetailDto?>(null);
        public Task<bool> UpdateAdditionalServiceImageAsync(string additionalServiceId, byte[] imageBytes, string mimeType, string? requestingAdminId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<IReadOnlyList<string>> GetAdditionalServiceCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(new List<string>());
        public Task<IReadOnlyList<ComboPackageCreatedResponseDto>> GetComboPackagesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ComboPackageCreatedResponseDto>>(new List<ComboPackageCreatedResponseDto>());
        public Task<IReadOnlyList<ServiceSummaryDto>> GetComboPackagesSummaryAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ServiceSummaryDto>>(new List<ServiceSummaryDto>());
        public Task<ServiceDetailDto?> GetComboPackageDetailWithImageAsync(string packageId, CancellationToken cancellationToken = default) => Task.FromResult<ServiceDetailDto?>(null);
        public Task<bool> UpdateComboPackageImageAsync(string packageId, byte[] imageBytes, string mimeType, string? requestingAdminId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<IReadOnlyList<HistoryLogDto>> GetHistoryLogsAsync(string requestingUserId, int topCount = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HistoryLogDto>>(new List<HistoryLogDto>());
    }
}
