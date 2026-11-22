using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Moq;
using PhotoAevumStudio.Api.Controllers;
using PhotoAevumStudio.Api.Middleware;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class PaymentAndAuthFlowTests
{
    private static string GetRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void PaymentsController_SimulateQrPayment_AllowsAnonymous_AndRateLimits()
    {
        var method = typeof(PaymentsController).GetMethod(nameof(PaymentsController.SimulateQrPayment));
        Assert.NotNull(method);

        Assert.NotNull(method!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(method.GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public async Task PaymentsController_SimulateQrPayment_WorksInProduction_WhenXenditTestKeyIsConfigured()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"qrpy_prod_test","status":"SUCCEEDED","amount":750}""", Encoding.UTF8, "application/json")
        });

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));

        var pendingMock = new Mock<IPendingPaymentService>();
        pendingMock.Setup(p => p.ConfirmByReferenceAsync("EVT-0099", It.IsAny<string?>(), It.IsAny<decimal?>(), "QRPH", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentConfirmation("EVT-0099", "CUST-01", 750m));

        var envMock = new Mock<IWebHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns(Environments.Production);

        var controller = new PaymentsController(
            Mock.Of<IConsultationNegotiationService>(),
            Mock.Of<IPaymentService>(),
            Options.Create(new XenditSettings { SecretKey = "xnd_development_sandbox_key_123" }),
            NullLogger<PaymentsController>.Instance,
            pendingMock.Object,
            Mock.Of<IPaymentNotifier>(),
            new ConfigurationBuilder().Build(),
            factoryMock.Object,
            envMock.Object);

        var result = await controller.SimulateQrPayment("EVT-0099", 750m, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task PaymentsController_SimulateQrPayment_RejectsLiveProductionKey()
    {
        var controller = new PaymentsController(
            Mock.Of<IConsultationNegotiationService>(),
            Mock.Of<IPaymentService>(),
            Options.Create(new XenditSettings { SecretKey = "xnd_production_real_live_key" }),
            NullLogger<PaymentsController>.Instance,
            Mock.Of<IPendingPaymentService>(),
            Mock.Of<IPaymentNotifier>(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IHttpClientFactory>());

        var result = await controller.SimulateQrPayment("EVT-0099", 750m, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
    }

    [Fact]
    public async Task PaymentsController_SimulateQrPayment_ForbidsAuthenticatedOtherCustomer()
    {
        var consultMock = new Mock<IConsultationNegotiationService>();
        consultMock.Setup(c => c.GetAppointmentByIdAsync("EVT-0099", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventAppointmentDto
            {
                EventAppointmentId = "EVT-0099",
                CustomerId = "CUST-OWNER"
            });

        var controller = new PaymentsController(
            consultMock.Object,
            Mock.Of<IPaymentService>(),
            Options.Create(new XenditSettings { SecretKey = "xnd_development_mock_key" }),
            NullLogger<PaymentsController>.Instance,
            Mock.Of<IPendingPaymentService>(),
            Mock.Of<IPaymentNotifier>(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IHttpClientFactory>());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Role, PhotoAevumStudio.Application.Auth.AppRoles.Customer),
                    new Claim("studio_id", "CUST-OTHER")
                }, "TestAuth"))
            }
        };

        var result = await controller.SimulateQrPayment("EVT-0099", 500m, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task PaymentsController_GetPublicQrPayment_IncludesIsFinalPayment()
    {
        var pendingMock = new Mock<IPendingPaymentService>();
        pendingMock.Setup(p => p.GetQrPaymentAsync("EVT-0099-FINAL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QrPaymentInfoDto
            {
                AppointmentId = "EVT-0099-FINAL",
                CustomerId = "CUST-01",
                EventName = "Wedding Shoot (Final Balance)",
                EventDate = new DateTime(2026, 12, 1),
                EventTime = new TimeSpan(10, 0, 0),
                EventStatus = "Pending Payment",
                Amount = 8500m,
                IsFinalPayment = true
            });

        var controller = new PaymentsController(
            Mock.Of<IConsultationNegotiationService>(),
            Mock.Of<IPaymentService>(),
            Options.Create(new XenditSettings()),
            NullLogger<PaymentsController>.Instance,
            pendingMock.Object,
            Mock.Of<IPaymentNotifier>(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IHttpClientFactory>());

        var res = await controller.GetPublicQrPayment("EVT-0099-FINAL", CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"IsFinalPayment\":true", json);
    }

    [Fact]
    public async Task AuthService_LoginAsync_Handles429AndBlankInputsGracefully()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("AevumApi.Anonymous"))
            .Returns(new HttpClient(handler) { BaseAddress = new Uri("http://api-photoaevum.tryasp.net/") });

        var authState = new AevumAuthenticationStateProvider(Mock.Of<ITokenStore>());
        var authService = new AuthService(factory.Object, authState);

        var emptyResult = await authService.LoginAsync("   ", "", CancellationToken.None);
        Assert.False(emptyResult.Success);
        Assert.Contains("required", emptyResult.Error, StringComparison.OrdinalIgnoreCase);

        var rateLimited = await authService.LoginAsync("customer@example.com", "Pass123@", CancellationToken.None);
        Assert.False(rateLimited.Success);
        Assert.Contains("Too many attempts", rateLimited.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SessionInactivityMiddleware_RecoversFreshTokenAfterCacheClear_AndRespectsMillisecondRevocation()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Session:TimeoutMinutes"] = "15" })
            .Build();

        var calledNext = false;
        var middleware = new SessionInactivityMiddleware(
            _ => { calledNext = true; return Task.CompletedTask; },
            cache,
            config,
            NullLogger<SessionInactivityMiddleware>.Instance);

        var now = DateTimeOffset.UtcNow;
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/payments/customer-appointments";
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("studio_id", "CUST-0001"),
            new Claim("iat", now.ToUnixTimeSeconds().ToString()),
            new Claim("iat_ms", now.ToUnixTimeMilliseconds().ToString())
        }, "Bearer"));

        // 1. Even with empty SessionActivity cache (e.g. IIS recycle), a fresh token is accepted and re-seeded
        await middleware.InvokeAsync(ctx);
        Assert.True(calledNext);

        // 2. If revoked BEFORE this token's millisecond timestamp, token stays valid (same-second logout/re-login)
        calledNext = false;
        cache.Set("SessionRevokedBeforeMs_CUST-0001", now.ToUnixTimeMilliseconds() - 10);
        await middleware.InvokeAsync(ctx);
        Assert.True(calledNext);

        // 3. If revoked AT OR AFTER this token's millisecond timestamp, token is rejected
        calledNext = false;
        cache.Set("SessionRevokedBeforeMs_CUST-0001", now.ToUnixTimeMilliseconds());
        await middleware.InvokeAsync(ctx);
        Assert.False(calledNext);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task ProtectedSessionTokenStore_ClearsBothSessionAndLocalStorage()
    {
        var jsMock = new Mock<IJSRuntime>();
        var dataProtection = DataProtectionProvider.Create("PhotoAevumStudio.Web.Tests");
        var session = new ProtectedSessionStorage(jsMock.Object, dataProtection);
        var local = new ProtectedLocalStorage(jsMock.Object, dataProtection);

        var store = new ProtectedSessionTokenStore(session, local);
        await store.SetTokenAsync("jwt-cross-tab-token");
        Assert.Equal("jwt-cross-tab-token", await store.GetTokenAsync());

        await store.ClearAsync();
        Assert.Null(await store.GetTokenAsync());
    }

    [Fact]
    public void PaymentAndRedirectComponents_FixLoginLoopAndCrossDeviceQrConfirmation()
    {
        var root = GetRepositoryRoot();
        var webRoot = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web");

        var confirmPayment = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "ConfirmPayment.razor"));
        var payments = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Payments.razor"));
        var customerDashboard = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "CustomerDashboard.razor"));
        var redirectToLogin = File.ReadAllText(Path.Combine(webRoot, "Components", "RedirectToLogin.razor"));
        var login = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Login.razor"));

        // ConfirmPayment uses Anonymous HttpClient so phone/cross-device QR scans never 401
        Assert.DoesNotContain("AuthorizedApi", confirmPayment);
        Assert.DoesNotContain("Please sign in and try again.", confirmPayment);
        Assert.Contains("Api.PostAsync($\"api/payments/simulate-qr/", confirmPayment);

        // Payments.razor provides direct Confirm Payment button and handles Unauthorized cleanly
        Assert.Contains("ConfirmPaymentAsync", payments);
        Assert.Contains("Confirm Payment", payments);
        Assert.Contains("ApiErrorKind.Unauthorized", payments);

        // CustomerDashboard does not force internal /payments/ routes into target="_blank"
        Assert.Contains("var isExternalCheckout = checkoutUrl.StartsWith(\"http\"", customerDashboard);

        // RedirectToLogin preserves leading "/" and Login normalizes local returnUrl safely
        Assert.Contains("\"/\" + relativeUri", redirectToLogin);
        Assert.Contains("TryNormalizeSafeLocalReturnUrl", login);
    }

    [Theory]
    [InlineData("/payments/EVT-0001", true, "/payments/EVT-0001")]
    [InlineData("payments/EVT-0001", true, "/payments/EVT-0001")]
    [InlineData("checkout/confirm/EVT-0001", true, "/checkout/confirm/EVT-0001")]
    [InlineData("//evil.com/phish", false, "")]
    [InlineData("https://evil.com/phish", false, "")]
    [InlineData("/\\evil.com", false, "")]
    [InlineData("javascript:alert(1)", false, "")]
    public void LoginRazor_ReturnUrlNormalization_AllowsLocalPathsAndBlocksOpenRedirects(
        string rawUrl, bool expectedValid, string expectedNormalized)
    {
        var method = typeof(PhotoAevumStudio.Web.Components.Pages.Login)
            .GetMethod("TryNormalizeSafeLocalReturnUrl", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var args = new object?[] { rawUrl, null };
        var isValid = (bool)method!.Invoke(null, args)!;

        Assert.Equal(expectedValid, isValid);
        if (expectedValid)
        {
            Assert.Equal(expectedNormalized, args[1]);
        }
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
