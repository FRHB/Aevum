using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoAevumStudio.Api.Controllers;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Web.Components.Layout;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Models;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class StudioAnalyticsWebTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Theory]
    [InlineData("/analytics")]
    [InlineData("/admin/analytics")]
    [InlineData("/analytics/{Stage}")]
    [InlineData("/admin/analytics/{Stage}")]
    public void StudioAnalyticsPage_RegistersAllStageRoutesWithAdminLayout(string expectedRoute)
    {
        var routes = typeof(StudioAnalytics)
            .GetCustomAttributes<RouteAttribute>()
            .Select(r => r.Template)
            .ToArray();

        Assert.Contains(expectedRoute, routes);

        var layout = typeof(StudioAnalytics).GetCustomAttribute<LayoutAttribute>();
        Assert.NotNull(layout);
        Assert.Equal(typeof(AdminLayout), layout!.LayoutType);

        var auth = typeof(StudioAnalytics).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(auth);
        Assert.Contains("Admin", auth!.Roles);
        Assert.Contains("Office Clerk", auth.Roles);
    }

    [Fact]
    public void AnalyticsController_UsesClerkOrAdminAuthorizationPolicy()
    {
        var auth = typeof(AnalyticsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(auth);
        Assert.Equal("ClerkOrAdmin", auth!.Policy);
    }

    [Fact]
    public async Task AnalyticsController_GetOverviewApplyAndExport_ReturnExpectedResponses()
    {
        var sampleOverview = new StudioAnalyticsOverviewDto(
            StartDate: new DateOnly(2026, 9, 1),
            EndDate: new DateOnly(2026, 11, 20),
            PriorStartDate: new DateOnly(2026, 6, 12),
            PriorEndDate: new DateOnly(2026, 8, 31),
            Granularity: "month",
            AppliedStatusFilter: null,
            AppliedServiceCategoryFilter: null,
            AvailableStatuses: ["Pending", "Confirmed", "Completed", "Cancelled", "Expired"],
            AvailableServiceCategories: ["Wedding"],
            GeneratedAtUtc: DateTime.UtcNow,
            HasAnyData: true,
            IsLowData: false,
            Descriptive: new DescriptiveAnalyticsDto(4, 3, 1, null, 50.0, null, 2, 1, 1, 0, 12.0, null, 2, 1, 0, 1, 3, 1, 0, 1, 2, [], []),
            Diagnostic: new DiagnosticAnalyticsDto("Current", "Prior", 4, 0, null, [], []),
            Predictive: new PredictiveAnalyticsDto(3, "expected", 2, 4, 2.0, null, "Low", "Note", "Method", []),
            Prescriptive: new PrescriptiveAnalyticsDto([], []));

        var serviceMock = new Mock<IStudioAnalyticsService>();
        serviceMock
            .Setup(s => s.GetOverviewAsync(It.IsAny<StudioAnalyticsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleOverview);
        serviceMock
            .Setup(s => s.ApplyPrescriptiveActionsAsync(
                It.IsAny<ApplyPrescriptiveActionsRequest>(),
                "ADM-001",
                "admin@photoaevumstudio.com",
                "Studio Admin",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplyPrescriptiveActionsResultDto(1, [], "Persisted"));
        serviceMock
            .Setup(s => s.ExportCsvAsync(It.IsAny<StudioAnalyticsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Section,Metric,Dimension,Value,Notes\nDescriptive,Total Bookings,All,4,");

        var controller = new AnalyticsController(serviceMock.Object, NullLogger<AnalyticsController>.Instance)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                    [
                        new System.Security.Claims.Claim("studio_id", "ADM-001"),
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, "admin@photoaevumstudio.com"),
                        new System.Security.Claims.Claim("full_name", "Studio Admin"),
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin")
                    ], "TestAuth"))
                }
            }
        };

        var overviewAction = await controller.GetOverview(new DateOnly(2026, 9, 1), new DateOnly(2026, 11, 20), null, null, "month", "expected");
        var okOverview = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(overviewAction.Result);
        var dto = Assert.IsType<StudioAnalyticsOverviewDto>(okOverview.Value);
        Assert.Equal(4, dto.Descriptive.TotalBookings);

        var applyAction = await controller.ApplyPrescriptiveActions(new ApplyPrescriptiveActionsRequest(
            [new ApplyPrescriptiveActionItem("follow-up-pending-deposits", "Follow up", "Bookings", "Low Effort", "Protects slot")],
            "Done"));
        var okApply = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(applyAction.Result);
        var applyDto = Assert.IsType<ApplyPrescriptiveActionsResultDto>(okApply.Value);
        Assert.Equal(1, applyDto.AppliedCount);

        var badApply = await controller.ApplyPrescriptiveActions(new ApplyPrescriptiveActionsRequest([]));
        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(badApply.Result);

        var exportAction = await controller.ExportCsv(new DateOnly(2026, 9, 1), new DateOnly(2026, 11, 20), null, null);
        var fileResult = Assert.IsType<Microsoft.AspNetCore.Mvc.FileContentResult>(exportAction);
        Assert.Equal("text/csv; charset=utf-8", fileResult.ContentType);
        Assert.Contains("Total Bookings", Encoding.UTF8.GetString(fileResult.FileContents));
    }

    [Fact]
    public async Task AnalyticsClient_SendsExpectedEndpointsAndParsesPayloads()
    {
        string? lastRequestedUri = null;
        string? lastPostedBody = null;

        var overviewPayload = new StudioAnalyticsOverviewModel(
            StartDate: new DateOnly(2026, 9, 1),
            EndDate: new DateOnly(2026, 11, 20),
            PriorStartDate: new DateOnly(2026, 6, 12),
            PriorEndDate: new DateOnly(2026, 8, 31),
            Granularity: "week",
            AppliedStatusFilter: "Confirmed",
            AppliedServiceCategoryFilter: "Wedding",
            AvailableStatuses: ["Pending", "Confirmed", "Completed", "Cancelled", "Expired"],
            AvailableServiceCategories: ["Wedding", "Portrait"],
            GeneratedAtUtc: DateTime.UtcNow,
            HasAnyData: true,
            IsLowData: false,
            Descriptive: new DescriptiveAnalyticsModel(10, 8, 2, 25.0, 60.0, 5.0, 6, 3, 1, 0, 42.5, 10.0, 6, 3, 1, 1, 5, 1, 0, 1, 4, [], []),
            Diagnostic: new DiagnosticAnalyticsModel("Sep–Nov", "Jun–Aug", 10, 8, 25.0, [], []),
            Predictive: new PredictiveAnalyticsModel(3, "expected", 5, 25, 5.0, 1.2, "Good", "Sample ok", "Method", []),
            Prescriptive: new PrescriptiveAnalyticsModel([], []));

        var applyResult = new ApplyPrescriptiveActionsResultModel(
            1,
            [new AppliedAnalyticsActionModel(1, "follow-up-pending-deposits", "Follow up", "Bookings", "Low Effort", "Protects 2 slots", null, null, "Done", "ADM-1", "Admin", DateTime.UtcNow, "Applied")],
            "Applied and persisted 1 studio recommendation(s).");

        var handler = new StubHandler(async req =>
        {
            lastRequestedUri = req.RequestUri!.PathAndQuery;
            if (req.Method == HttpMethod.Get && req.RequestUri.AbsolutePath.EndsWith("/overview"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(overviewPayload), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Post && req.RequestUri.AbsolutePath.EndsWith("/prescriptive/apply"))
            {
                lastPostedBody = await req.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(applyResult), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri.AbsolutePath.EndsWith("/export"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("Section,Metric,Dimension,Value,Notes\nDescriptive,Total Bookings,All,10,", Encoding.UTF8, "text/csv")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7001/") };
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("AevumApi.Authorized")).Returns(httpClient);

        var tokenStoreMock = new Mock<ITokenStore>();
        tokenStoreMock.Setup(t => t.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("jwt-test-token");

        var client = new AnalyticsClient(factoryMock.Object, tokenStoreMock.Object, NullLogger<AnalyticsClient>.Instance);

        var overviewRes = await client.GetOverviewAsync(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 11, 20),
            statusFilter: "Confirmed",
            categoryFilter: "Wedding",
            granularity: "week",
            scenario: "expected");

        Assert.True(overviewRes.IsSuccess);
        Assert.Equal(10, overviewRes.Value!.Descriptive.TotalBookings);
        Assert.Contains("status=Confirmed", lastRequestedUri);
        Assert.Contains("category=Wedding", lastRequestedUri);
        Assert.Contains("granularity=week", lastRequestedUri);

        var applyRes = await client.ApplyPrescriptiveActionsAsync(new ApplyPrescriptiveActionsRequestModel(
            [new ApplyPrescriptiveActionItemModel("follow-up-pending-deposits", "Follow up", "Bookings", "Low Effort", "Protects 2 slots")],
            "Checked"));

        Assert.True(applyRes.IsSuccess);
        Assert.Equal(1, applyRes.Value!.AppliedCount);
        Assert.Contains("follow-up-pending-deposits", lastPostedBody);

        var csvRes = await client.ExportCsvAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 11, 20));
        Assert.True(csvRes.IsSuccess);
        Assert.Contains("Total Bookings", csvRes.Value);
    }

    [Fact]
    public void StudioAnalyticsRazor_ContainsFourStageLeftRailDarkSlateLayoutAndDomainControls()
    {
        var root = FindRepoRoot();
        var razorPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "StudioAnalytics.razor");
        var cssPath = razorPath + ".css";

        Assert.True(File.Exists(razorPath));
        Assert.True(File.Exists(cssPath));

        var markup = File.ReadAllText(razorPath);
        var css = File.ReadAllText(cssPath);

        // Clean dark slate layout & left-rail four stages
        Assert.Contains("#0b1120", css);
        Assert.Contains("#0f172a", css);
        Assert.Contains("Analytics Stages", markup);
        Assert.Contains("Descriptive", markup);
        Assert.Contains("Diagnostic", markup);
        Assert.Contains("Predictive", markup);
        Assert.Contains("Prescriptive", markup);

        // Shared filters & export
        Assert.Contains("Export CSV", markup);
        Assert.Contains("Service Category", markup);
        Assert.Contains("Booking Status", markup);

        // Stage specifics
        Assert.Contains("SessionCompletionRate", markup);
        Assert.Contains("LoggedCrewHours", markup);
        Assert.Contains("EquipmentMaintenanceHolds", markup);
        Assert.Contains("Diagnostic Finding", markup);
        Assert.Contains("Forecast Uncertainty Band", markup);
        Assert.Contains("Apply Selected Actions", markup);
        Assert.Contains("Persisted Studio Action Log", markup);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request);
    }
}
