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
using Microsoft.JSInterop;
using Moq;
using PhotoAevumStudio.Api.BackgroundServices;
using PhotoAevumStudio.Api.Controllers;
using PhotoAevumStudio.Api.Services;
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
        services.AddSingleton<DropboxUploadTicketStore>();
        services.AddSingleton(Mock.Of<ITokenStore>());
        services.AddSingleton<DropboxBackgroundUploadService>();
        services.AddSingleton(Mock.Of<IJSRuntime>());

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

    [Fact]
    public async Task ProgressReportingStream_TracksCumulativeBytes_AndLeavesInnerOpen()
    {
        var payload = new byte[1024];
        Random.Shared.NextBytes(payload);
        using var inner = new MemoryStream(payload);
        var reported = new List<long>();

        using (var wrapper = new ProgressReportingStream(inner, total => reported.Add(total)))
        {
            var buf = new byte[256];
            Assert.Equal(256, wrapper.Read(buf, 0, buf.Length));
            Assert.Equal(256, wrapper.Read(buf.AsSpan()));
            Assert.Equal(256, await wrapper.ReadAsync(buf, 0, buf.Length));
            Assert.Equal(256, await wrapper.ReadAsync(buf.AsMemory()));
            Assert.Equal(0, await wrapper.ReadAsync(buf.AsMemory()));
        }

        Assert.Equal(new long[] { 256, 512, 768, 1024 }, reported);
        Assert.True(inner.CanRead);
    }

    [Fact]
    public async Task ProgressReportingStream_WithStreamContent_ReportsFullUploadBytes()
    {
        var payload = new byte[40_000];
        using var inner = new MemoryStream(payload);
        long lastReported = 0;

        using var wrapper = new ProgressReportingStream(inner, total => lastReported = total);
        using var form = new MultipartFormDataContent();
        using var content = new StreamContent(wrapper);
        form.Add(content, "file", "sample.zip");

        using var sink = new MemoryStream();
        await form.CopyToAsync(sink);

        Assert.Equal(payload.Length, lastReported);
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(-5, 100, 0)]
    [InlineData(50, 0, 0)]
    [InlineData(1, 200, 0)]
    [InlineData(999, 1000, 99)]
    [InlineData(1000, 1000, 100)]
    [InlineData(1200, 1000, 100)]
    public void UploadEventCustomer_CalculateUploadPercent_HandlesBoundsAndFlooring(long sent, long total, int expected)
    {
        Assert.Equal(expected, UploadEventCustomer.CalculateUploadPercent(sent, total));
    }

    [Fact]
    public void UploadEventCustomer_MarkupContainsProgressBarAndAriaAttributes()
    {
        var root = GetRepoRoot();
        var razorPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "UploadEventCustomer.razor");
        var cssPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "UploadEventCustomer.razor.css");

        var razor = File.ReadAllText(razorPath);
        var css = File.ReadAllText(cssPath);

        Assert.Contains("role=\"progressbar\"", razor);
        Assert.Contains("aria-valuenow=\"@_uploadPercent\"", razor);
        Assert.Contains("aev-upload-progress", razor);
        Assert.Contains("aev-circle-progress", razor);
        Assert.Contains("aev-pill-upload-circle", razor);
        Assert.Contains("Continue in Background", razor);
        Assert.Contains("ProgressReportingStream", razor);
        Assert.Contains(".aev-upload-progress", css);
        Assert.Contains(".aev-upload-progress__fill", css);
        Assert.Contains(".aev-circle-progress", css);
    }

    [Fact]
    public void WebProgram_ConfiguresInfiniteTimeoutForUploadHttpClient_AndMapsBff()
    {
        var root = GetRepoRoot();
        var webProgram = File.ReadAllText(Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Program.cs"));

        Assert.Contains("AevumApi.Upload", webProgram);
        Assert.Contains("Timeout.InfiniteTimeSpan", webProgram);
        Assert.Contains("MapDropboxUploadBffEndpoints", webProgram);
    }

    [Fact]
    public void DropboxUploadTicketStore_IssuesValidatesAndRemovesTickets()
    {
        var store = new DropboxUploadTicketStore();
        var ticket = store.CreateTicket("jwt-token-123", "EVT-0018", "Sample.zip", 8_790_000, "application/zip");

        Assert.False(string.IsNullOrWhiteSpace(ticket.TicketId));
        Assert.True(store.TryGetValidTicket(ticket.TicketId, out var found));
        Assert.NotNull(found);
        Assert.Equal("EVT-0018", found!.EventAppointmentId);
        Assert.Equal("jwt-token-123", found.JwtToken);

        store.RemoveTicket(ticket.TicketId);
        Assert.False(store.TryGetValidTicket(ticket.TicketId, out _));
    }

    [Fact]
    public async Task DropboxBackgroundUploadService_TracksProgressCompletionAndDismissal()
    {
        var tokenStore = new Mock<ITokenStore>();
        tokenStore.Setup(t => t.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("jwt-123");
        var ticketStore = new DropboxUploadTicketStore();
        var svc = new DropboxBackgroundUploadService(tokenStore.Object, ticketStore);

        var js = new Mock<IJSRuntime>();
        js.Setup(j => j.InvokeAsync<bool>("aevumDropboxUpload.start", It.IsAny<object?[]>()))
            .ReturnsAsync(true);

        var container = new CustomerDropboxFolderItem
        {
            DropboxId = "DRP-0018",
            EventAppointmentId = "EVT-0018",
            CustomerId = "CUST-0004",
            CustomerName = "Fernest Rumon Baclig",
            EventName = "Photoshoot Event"
        };

        var (started, error, job) = await svc.StartBackgroundUploadAsync(
            container,
            "Sample.zip",
            8_790_000,
            "application/zip",
            "zipUploadInput",
            js.Object);

        Assert.True(started);
        Assert.Null(error);
        Assert.NotNull(job);
        Assert.True(svc.HasActiveUploads);
        Assert.NotNull(svc.GetActiveJobForEvent("EVT-0018"));

        svc.ReportProgress(job!.JobId, 4_000_000, 45, false);
        Assert.Equal(45, job.Percent);
        Assert.Equal(DropboxUploadJobStatus.Uploading, job.Status);

        svc.ReportProgress(job.JobId, 8_790_000, 95, true);
        Assert.Equal(95, job.Percent);
        Assert.Equal(DropboxUploadJobStatus.Finalizing, job.Status);

        svc.ReportCompletedFromJson(job.JobId, "{\"eventAppointmentId\":\"EVT-0018\",\"dropboxStatus\":\"Uploaded\",\"originalFileName\":\"Sample.zip\",\"fileSizeBytes\":8790000}");
        Assert.False(svc.HasActiveUploads);
        Assert.Equal(100, job.Percent);
        Assert.Equal(DropboxUploadJobStatus.Completed, job.Status);
        Assert.Equal("Sample.zip", job.CompletedItem?.OriginalFileName);

        svc.DismissJob(job.JobId);
        Assert.Empty(svc.Jobs);
    }

    [Fact]
    public async Task DropboxChunkUploadCoordinator_AppendsChunksValidatesZipAndCompletesInBackground()
    {
        var zipPayload = new byte[1024];
        zipPayload[0] = 0x50;
        zipPayload[1] = 0x4B;
        zipPayload[2] = 0x03;
        zipPayload[3] = 0x04;

        var mockDropboxService = new Mock<ICustomerDropboxService>();
        mockDropboxService
            .Setup(s => s.UploadZipAsync(
                "EVT-0018",
                It.IsAny<Stream>(),
                "Sample.zip",
                1024,
                "application/zip",
                "ADM-0001",
                It.IsAny<Action<long>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerDropboxFolderDto
            {
                DropboxId = "DRP-0018",
                EventAppointmentId = "EVT-0018",
                CustomerId = "CUST-0004",
                DropboxStatus = DropboxStatuses.Uploaded,
                OriginalFileName = "Sample.zip",
                FileSizeBytes = 1024
            });

        var services = new ServiceCollection();
        services.AddSingleton(mockDropboxService.Object);
        await using var sp = services.BuildServiceProvider();
        var coordinator = new DropboxChunkUploadCoordinator(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DropboxChunkUploadCoordinator>.Instance);

        var init = coordinator.CreateSession("EVT-0018", "ADM-0001", "Sample.zip", 1024, "application/zip");
        Assert.False(string.IsNullOrWhiteSpace(init.SessionId));

        using var chunk0 = new MemoryStream(zipPayload, 0, 512);
        var res0 = await coordinator.AppendChunkAsync(init.SessionId, "EVT-0018", "ADM-0001", 0, chunk0, 512, CancellationToken.None);
        Assert.Equal(512, res0.BytesReceived);

        using var chunk1 = new MemoryStream(zipPayload, 512, 512);
        var res1 = await coordinator.AppendChunkAsync(init.SessionId, "EVT-0018", "ADM-0001", 1, chunk1, 512, CancellationToken.None);
        Assert.Equal(1024, res1.BytesReceived);

        var finalize = coordinator.StartFinalize(init.SessionId, "EVT-0018", "ADM-0001");
        Assert.True(finalize.Status is "Finalizing" or "Completed");

        DropboxUploadStatusResponse? status = null;
        for (var i = 0; i < 40; i++)
        {
            status = coordinator.GetStatus(init.SessionId, "ADM-0001");
            if (status.Status == "Completed") break;
            await Task.Delay(50);
        }

        Assert.NotNull(status);
        Assert.Equal("Completed", status!.Status);
        Assert.Equal("Sample.zip", status.Result?.OriginalFileName);
    }

    [Fact]
    public async Task DropboxChunkUploadCoordinator_RejectsNonZipSignatureOnFirstChunk()
    {
        var services = new ServiceCollection();
        await using var sp = services.BuildServiceProvider();
        var coordinator = new DropboxChunkUploadCoordinator(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DropboxChunkUploadCoordinator>.Instance);

        var init = coordinator.CreateSession("EVT-0018", "ADM-0001", "fake.zip", 100, "application/zip");
        using var badChunk = new MemoryStream(new byte[] { 0x00, 0x01, 0x02, 0x03 });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.AppendChunkAsync(init.SessionId, "EVT-0018", "ADM-0001", 0, badChunk, 4, CancellationToken.None));

        coordinator.AbortSession(init.SessionId, "ADM-0001");
    }

    [Fact]
    public void CustomerDropboxFolderItem_ComputesDeliveryWindow_ActiveRetention_AndExpiredStates()
    {
        var now = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);

        var waitingItem = new CustomerDropboxFolderItem
        {
            DropboxStatus = "Waiting",
            DeliveryBusinessDays = 7,
            DeliveryDaysRemaining = 5,
            ExpiresAt = null
        };
        Assert.True(waitingItem.IsWaitingForUpload);
        Assert.False(waitingItem.HasFile);
        Assert.False(waitingItem.IsEffectivelyExpired);
        Assert.Equal("5 Business Days Left", waitingItem.EffectiveCountdownLabel);
        Assert.Equal(71, waitingItem.DeliveryWindowPercent);

        var uploadedItem = new CustomerDropboxFolderItem
        {
            DropboxStatus = "Uploaded",
            OriginalFileName = "gallery.zip",
            DaysRemaining = 29,
            ExpiresAt = now.AddDays(30)
        };
        Assert.False(uploadedItem.IsWaitingForUpload);
        Assert.True(uploadedItem.HasFile);
        Assert.False(uploadedItem.IsEffectivelyExpired);
        Assert.Equal("29 Days Left", uploadedItem.EffectiveCountdownLabel);

        var expiredByDateItem = new CustomerDropboxFolderItem
        {
            DropboxStatus = "Uploaded",
            OriginalFileName = "gallery.zip",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5)
        };
        Assert.True(expiredByDateItem.IsEffectivelyExpired);
        Assert.False(expiredByDateItem.HasFile);
        Assert.False(expiredByDateItem.IsWaitingForUpload);
        Assert.Equal("Unavailable", expiredByDateItem.EffectiveCountdownLabel);

        var expiredByStatusItem = new CustomerDropboxFolderItem
        {
            DropboxStatus = "Expired",
            IsExpired = true
        };
        Assert.True(expiredByStatusItem.IsEffectivelyExpired);
        Assert.False(expiredByStatusItem.HasFile);
        Assert.Equal("Unavailable", expiredByStatusItem.EffectiveCountdownLabel);
    }

    [Fact]
    public void CustomerDropbox_MarkupIncludesDeliveryWindowEmptyState_AndGreyedExpiredState()
    {
        var root = GetRepoRoot();
        var razorPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "CustomerDropbox.razor");
        var cssPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "CustomerDropbox.razor.css");
        var uploadRazorPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "UploadEventCustomer.razor");
        var uploadCssPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "UploadEventCustomer.razor.css");

        var razor = File.ReadAllText(razorPath);
        var css = File.ReadAllText(cssPath);
        var uploadRazor = File.ReadAllText(uploadRazorPath);
        var uploadCss = File.ReadAllText(uploadCssPath);

        Assert.Contains("Image Delivery Window", razor);
        Assert.Contains("30-Day Retention Window", razor);
        Assert.Contains("Dropbox Unavailable", razor);
        Assert.Contains("aev-dropbox-folder--expired", razor);
        Assert.Contains("is-unavailable", razor);

        Assert.Contains(".aev-dropbox-folder--expired", css);
        Assert.Contains("grayscale(100%)", css);
        Assert.Contains(".aev-dropbox-download-btn.is-unavailable", css);

        Assert.Contains("aev-pill-badge--expired", uploadRazor);
        Assert.Contains(".aev-event-pillbox.is-expired", uploadCss);
    }
}
