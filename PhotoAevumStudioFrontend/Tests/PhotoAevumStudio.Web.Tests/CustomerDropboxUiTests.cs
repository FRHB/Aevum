using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoAevumStudio.Api.BackgroundServices;
using PhotoAevumStudio.Api.Controllers;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Web.Components.Layout;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Models;
using PhotoAevumStudio.Web.Services;
using Xunit;
using RouteAttribute = Microsoft.AspNetCore.Components.RouteAttribute;
using AppRoles = PhotoAevumStudio.Application.Auth.AppRoles;

namespace PhotoAevumStudio.Web.Tests;

public class CustomerDropboxUiTests
{
    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private sealed class StubAuthStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(user));
    }

    private sealed class StubNavManager : NavigationManager
    {
        public StubNavManager() => Initialize("https://localhost/", "https://localhost/");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    [Fact]
    public void DropboxAndUploadPages_UseExpectedRoutesLayoutsAndAuthorization()
    {
        var dropboxRoutes = typeof(CustomerDropbox).GetCustomAttributes<RouteAttribute>().Select(r => r.Template).ToArray();
        var dropboxLayout = typeof(CustomerDropbox).GetCustomAttribute<LayoutAttribute>();
        var dropboxAuth = typeof(CustomerDropbox).GetCustomAttribute<AuthorizeAttribute>();

        Assert.Contains("/my/dropbox", dropboxRoutes);
        Assert.Equal(typeof(CustomerLayout), dropboxLayout?.LayoutType);
        Assert.Equal("Customer", dropboxAuth?.Roles);

        var uploadRoutes = typeof(UploadEventCustomer).GetCustomAttributes<RouteAttribute>().Select(r => r.Template).ToArray();
        var uploadLayout = typeof(UploadEventCustomer).GetCustomAttribute<LayoutAttribute>();
        var uploadAuth = typeof(UploadEventCustomer).GetCustomAttribute<AuthorizeAttribute>();

        Assert.Contains("/admin/upload-event-customer", uploadRoutes);
        Assert.Contains("/staff/upload-event-customer", uploadRoutes);
        Assert.Equal(typeof(SidebarLayout), uploadLayout?.LayoutType);
        Assert.NotNull(uploadAuth);
        Assert.Contains("Admin", uploadAuth!.Roles);
        Assert.Contains("Office Clerk", uploadAuth.Roles);
    }

    [Fact]
    public void Sidebar_ContainsDropboxForCustomers_AndUploadEventCustomerForAdminAndClerk()
    {
        var root = GetRepoRoot();
        var sidebarPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Layout", "Sidebar.razor");
        var text = File.ReadAllText(sidebarPath);

        Assert.Contains("/my/dropbox", text);
        Assert.Contains("Dropbox", text);
        Assert.Contains("/admin/upload-event-customer", text);
        Assert.Contains("/staff/upload-event-customer", text);
        Assert.Contains("Upload Event Customer", text);
    }

    [Fact]
    public void BackgroundService_AndProgramRegistration_Configure30DayAutoDeletion()
    {
        Assert.True(typeof(Microsoft.Extensions.Hosting.BackgroundService).IsAssignableFrom(typeof(CustomerDropboxExpiryService)));

        var root = GetRepoRoot();
        var apiProgram = File.ReadAllText(Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Api", "Program.cs"));

        Assert.Contains("CustomerDropboxExpiryService", apiProgram);
        Assert.Contains("ICustomerDropboxService", apiProgram);
        Assert.Contains("customer-dropbox-expiry", apiProgram);
    }

    [Fact]
    public async Task CustomerDropboxController_EnforcesStrictCustomerIsolation()
    {
        var mockService = new Mock<ICustomerDropboxService>();
        mockService
            .Setup(s => s.GetCustomerDropboxesAsync("CUST-0001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CustomerDropboxFolderDto>
            {
                new()
                {
                    DropboxId = "DRP-0001",
                    EventAppointmentId = "EVT-0001",
                    CustomerId = "CUST-0001",
                    EventName = "Alice Wedding",
                    DropboxStatus = DropboxStatuses.Waiting,
                    DaysRemaining = 29,
                    CountdownLabel = "29 Days Left"
                }
            });

        mockService
            .Setup(s => s.GetCustomerDownloadInfoAsync("DRP-0002", "CUST-0001", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Cross-customer access blocked."));

        var controller = new CustomerDropboxController(mockService.Object, NullLogger<CustomerDropboxController>.Instance);
        var cust1 = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "CUST-0001"),
            new Claim("studio_id", "CUST-0001"),
            new Claim(ClaimTypes.Role, AppRoles.Customer)
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = cust1 }
        };

        var myResult = await controller.GetMyDropboxes(CancellationToken.None) as OkObjectResult;
        Assert.NotNull(myResult);
        var folders = Assert.IsAssignableFrom<IReadOnlyList<CustomerDropboxFolderDto>>(myResult!.Value);
        Assert.Single(folders);
        Assert.Equal("CUST-0001", folders[0].CustomerId);

        // Attempting to download another customer's Dropbox folder must return 403 Forbidden
        var forbiddenResult = await controller.GetMyDownloadUrl("DRP-0002", CancellationToken.None) as ObjectResult;
        Assert.NotNull(forbiddenResult);
        Assert.Equal(StatusCodes.Status403Forbidden, forbiddenResult!.StatusCode);
    }

    [Fact]
    public async Task CustomerDropboxController_RejectsNonZipUploads()
    {
        var mockService = new Mock<ICustomerDropboxService>();
        var controller = new CustomerDropboxController(mockService.Object, NullLogger<CustomerDropboxController>.Instance);
        var admin = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("studio_id", "ADM-0001"),
            new Claim(ClaimTypes.Role, AppRoles.Admin)
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = admin }
        };

        using var ms = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        IFormFile badFile = new FormFile(ms, 0, ms.Length, "file", "photos.jpg");

        var res = await controller.UploadEventZip("EVT-0001", badFile, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(res);
        mockService.Verify(s => s.UploadZipAsync(
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CustomerDropbox_RendersWaitingAndUploadedFoldersWithCountdown()
    {
        var now = DateTime.UtcNow;
        var mockClient = new Mock<ICustomerDropboxClient>();
        mockClient.Setup(c => c.GetMyDropboxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResult<IReadOnlyList<CustomerDropboxFolderItem>>.Ok(new List<CustomerDropboxFolderItem>
            {
                new()
                {
                    DropboxId = "DRP-0001",
                    EventAppointmentId = "EVT-1001",
                    CustomerId = "CUST-0001",
                    EventName = "Grad Portrait",
                    EventDate = new DateOnly(2026, 11, 15),
                    DropboxStatus = "Waiting",
                    DaysRemaining = 29,
                    CountdownLabel = "29 Days Left",
                    ExpiresAt = now.AddDays(30)
                },
                new()
                {
                    DropboxId = "DRP-0002",
                    EventAppointmentId = "EVT-1002",
                    CustomerId = "CUST-0001",
                    EventName = "Family Reunion",
                    EventDate = new DateOnly(2026, 11, 1),
                    DropboxStatus = "Uploaded",
                    OriginalFileName = "family-reunion.zip",
                    FileSizeBytes = 15_728_640,
                    UploadedAt = now,
                    DaysRemaining = 14,
                    CountdownLabel = "14 Days Left",
                    ExpiresAt = now.AddDays(14)
                }
            }));

        var services = new ServiceCollection();
        services.AddSingleton(mockClient.Object);
        services.AddSingleton<NavigationManager, StubNavManager>();

        await using var sp = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<CustomerDropbox>();
            return output.ToHtmlString();
        });

        Assert.Contains("Waiting for uploads...", html);
        Assert.Contains("29 Days Left", html);
        Assert.Contains("family-reunion.zip", html);
        Assert.Contains("Download ZIP", html);
        Assert.Contains("14 Days Left", html);
    }

    [Fact]
    public async Task UploadEventCustomer_RendersPillBoxContainersForAdminAndClerk()
    {
        var now = DateTime.UtcNow;
        var mockClient = new Mock<ICustomerDropboxClient>();
        mockClient.Setup(c => c.GetAdminEventContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResult<IReadOnlyList<CustomerDropboxFolderItem>>.Ok(new List<CustomerDropboxFolderItem>
            {
                new()
                {
                    DropboxId = "DRP-0001",
                    EventAppointmentId = "EVT-2001",
                    CustomerId = "CUST-0007",
                    CustomerName = "Elena Reyes",
                    CustomerEmail = "elena@example.com",
                    EventName = "Debut Editorial",
                    EventDate = new DateOnly(2026, 11, 16),
                    DropboxStatus = "Waiting",
                    DaysRemaining = 29,
                    CountdownLabel = "29 Days Left",
                    ExpiresAt = now.AddDays(30)
                }
            }));

        var mockUserService = new Mock<IUserService>();
        var adminUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("studio_id", "ADM-0001"),
            new Claim(ClaimTypes.Role, AppRoles.Admin)
        }, "TestAuth"));

        var services = new ServiceCollection();
        services.AddSingleton(mockClient.Object);
        services.AddSingleton(mockUserService.Object);
        services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider(adminUser));
        services.AddSingleton<NavigationManager, StubNavManager>();

        await using var sp = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<UploadEventCustomer>();
            return output.ToHtmlString();
        });

        Assert.Contains("aev-event-pillbox", html);
        Assert.Contains("#EVT-2001", html);
        Assert.Contains("Debut Editorial", html);
        Assert.Contains("Elena Reyes", html);
        Assert.Contains("Waiting for Upload", html);
    }
}
