using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoAevumStudio.Api.Controllers;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Services;

namespace PhotoAevumStudio.Web.Tests;

public class CustomerForgotPasswordTests
{
    private static AuthController CreateController(
        Mock<ISqlAuthService> sqlAuthMock,
        Mock<IEmailService> emailMock,
        IMemoryCache cache)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "SuperSecretTestSigningKeyForJwtValidation123456",
                ["Jwt:Issuer"] = "PhotoAevumTest",
                ["Jwt:Audience"] = "PhotoAevumTest"
            })
            .Build();

        var controller = new AuthController(
            sqlAuthMock.Object,
            config,
            NullLogger<AuthController>.Instance,
            cache,
            emailMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    [Fact]
    public async Task ForgotPassword_RejectsNonCustomerAccount_AndDoesNotSendEmail()
    {
        var sqlAuth = new Mock<ISqlAuthService>();
        sqlAuth.Setup(s => s.CheckCustomerForgotPasswordEligibilityAsync("admin@studio.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, null, "Forgot Password is only available for customer accounts.", StatusCodes.Status403Forbidden));

        var emailSvc = new Mock<IEmailService>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = CreateController(sqlAuth, emailSvc, cache);

        var result = await controller.ForgotPassword(new SendVerificationRequest("admin@studio.com"));

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objResult.StatusCode);
        emailSvc.Verify(e => e.TrySendEmailAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForgotPassword_FullCustomerFlow_SendsCode_VerifiesCode_AndUpdatesPasswordOnce()
    {
        const string customerEmail = "maria.customer@example.com";
        string? capturedHtml = null;

        var sqlAuth = new Mock<ISqlAuthService>();
        sqlAuth.Setup(s => s.CheckCustomerForgotPasswordEligibilityAsync(customerEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, customerEmail, null, StatusCodes.Status200OK));
        sqlAuth.Setup(s => s.ResetPasswordAsync(customerEmail, "NewSecure#2026", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, "Password has been successfully reset."));

        var emailSvc = new Mock<IEmailService>();
        emailSvc.Setup(e => e.TrySendEmailAsync(
                customerEmail,
                It.IsAny<string>(),
                It.IsAny<string>(),
                true,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, bool, CancellationToken>((_, _, body, _, _) => capturedHtml = body)
            .ReturnsAsync(true);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = CreateController(sqlAuth, emailSvc, cache);

        // Step 1: Request reset code for customer email
        var forgotResult = await controller.ForgotPassword(new SendVerificationRequest("  Maria.Customer@Example.com  "));
        Assert.IsType<OkObjectResult>(forgotResult);
        Assert.NotNull(capturedHtml);

        var match = Regex.Match(capturedHtml!, @"\b(\d{6})\b");
        Assert.True(match.Success, "Expected a 6-digit verification code in the reset email body.");
        var code = match.Groups[1].Value;

        // Step 2a: Wrong verification code must fail
        var wrongVerify = await controller.VerifyResetCode(new VerifyCodeRequest(customerEmail, "000000"));
        Assert.IsType<BadRequestObjectResult>(wrongVerify);

        // Step 2b: Correct verification code succeeds
        var validVerify = await controller.VerifyResetCode(new VerifyCodeRequest(customerEmail, code));
        Assert.IsType<OkObjectResult>(validVerify);

        // Step 3: Update password using verified code
        var resetResult = await controller.ResetPassword(new ResetPasswordRequest(customerEmail, code, "NewSecure#2026"));
        Assert.IsType<OkObjectResult>(resetResult);
        sqlAuth.Verify(s => s.ResetPasswordAsync(customerEmail, "NewSecure#2026", It.IsAny<CancellationToken>()), Times.Once);

        // Step 4: Reusing the consumed verification code must fail
        var replayResult = await controller.ResetPassword(new ResetPasswordRequest(customerEmail, code, "AnotherPass#2026"));
        Assert.IsType<BadRequestObjectResult>(replayResult);
    }

    [Fact]
    public void LoginAndForgotPasswordUi_EnforceCustomerOnlyAvailabilityAndThreeStepFlow()
    {
        var root = GetRepositoryRoot();
        var loginPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "Login.razor");
        var forgotPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "ForgotPassword.razor");
        var sqlAuthPath = Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Infrastructure", "Services", "SqlAuthService.cs");

        var loginContent = File.ReadAllText(loginPath);
        var forgotContent = File.ReadAllText(forgotPath);
        var sqlAuthContent = File.ReadAllText(sqlAuthPath);

        // Login hides Forgot Password when staff/admin identifier is entered
        Assert.Contains("IsStaffOrAdminIdentifier", loginContent);
        Assert.Contains("ADM-", loginContent);
        Assert.Contains("EMP-", loginContent);
        Assert.Contains("SADM-", loginContent);
        Assert.Contains("CLK-", loginContent);

        // ForgotPassword page allows anonymous access and blocks authenticated non-customers
        Assert.Contains("@attribute [AllowAnonymous]", forgotContent);
        Assert.Contains("Customer Account Recovery", forgotContent);
        Assert.Contains("isRestrictedStaffSession", forgotContent);

        // ForgotPassword page wires the 3 customer reset endpoints (NOT registration send-verification/verify-code)
        Assert.Contains("RequestPasswordResetCodeAsync", forgotContent);
        Assert.Contains("VerifyPasswordResetCodeAsync", forgotContent);
        Assert.Contains("ResetCustomerPasswordAsync", forgotContent);
        Assert.DoesNotContain("SendVerificationCodeAsync", forgotContent);
        Assert.DoesNotContain("VerifyCodeAsync(", forgotContent);

        // SqlAuthService.ResetPasswordAsync strictly targets dbo.Customer only
        var resetMethodStart = sqlAuthContent.IndexOf("public async Task<(bool Success, string Message)> ResetPasswordAsync(");
        Assert.True(resetMethodStart >= 0);
        var resetMethodBody = sqlAuthContent.Substring(resetMethodStart, sqlAuthContent.IndexOf("public async Task LogActivityAsync(", resetMethodStart) - resetMethodStart);
        Assert.Contains("UPDATE dbo.Customer", resetMethodBody);
        Assert.DoesNotContain("UPDATE dbo.Admin", resetMethodBody);
        Assert.DoesNotContain("UPDATE dbo.Employees", resetMethodBody);
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_ReturnsOkWithGenericMessage_AndDoesNotSendEmail()
    {
        const string unknownEmail = "nonexistent.customer@example.com";
        var sqlAuth = new Mock<ISqlAuthService>();
        sqlAuth.Setup(s => s.CheckCustomerForgotPasswordEligibilityAsync(unknownEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, null, "No customer account found with that email address.", StatusCodes.Status404NotFound));

        var emailSvc = new Mock<IEmailService>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = CreateController(sqlAuth, emailSvc, cache);

        var result = await controller.ForgotPassword(new SendVerificationRequest(unknownEmail));

        var okResult = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(okResult.Value);
        Assert.Contains("If an active customer account is registered", json);
        Assert.DoesNotContain("not found", json, StringComparison.OrdinalIgnoreCase);

        emailSvc.Verify(e => e.TrySendEmailAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);

        Assert.False(cache.TryGetValue($"ResetCode_{unknownEmail}", out _));

        var verifyResult = await controller.VerifyResetCode(new VerifyCodeRequest(unknownEmail, "123456"));
        Assert.IsType<BadRequestObjectResult>(verifyResult);

        var resetResult = await controller.ResetPassword(new ResetPasswordRequest(unknownEmail, "123456", "NewSecure#2026"));
        Assert.IsType<BadRequestObjectResult>(resetResult);
    }

    [Fact]
    public async Task ForgotPassword_StoresSaltedHashedCodeInCache_NotPlaintext()
    {
        const string customerEmail = "hashed.customer@example.com";
        string? capturedHtml = null;

        var sqlAuth = new Mock<ISqlAuthService>();
        sqlAuth.Setup(s => s.CheckCustomerForgotPasswordEligibilityAsync(customerEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, customerEmail, null, StatusCodes.Status200OK));

        var emailSvc = new Mock<IEmailService>();
        emailSvc.Setup(e => e.TrySendEmailAsync(customerEmail, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Callback<string, string, string, bool, CancellationToken>((_, _, body, _, _) => capturedHtml = body)
            .ReturnsAsync(true);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = CreateController(sqlAuth, emailSvc, cache);

        var forgotResult = await controller.ForgotPassword(new SendVerificationRequest(customerEmail));
        Assert.IsType<OkObjectResult>(forgotResult);

        var code = Regex.Match(capturedHtml!, @"\b(\d{6})\b").Groups[1].Value;

        Assert.True(cache.TryGetValue($"ResetCode_{customerEmail}", out object? cachedValue));
        Assert.NotNull(cachedValue);
        Assert.IsNotType<string>(cachedValue);

        var serializedEntry = JsonSerializer.Serialize(cachedValue);
        Assert.DoesNotContain(code, serializedEntry);

        var verifyResult = await controller.VerifyResetCode(new VerifyCodeRequest(customerEmail, code));
        Assert.IsType<OkObjectResult>(verifyResult);
    }

    [Fact]
    public async Task VerifyResetCode_AndResetPassword_InvalidateCodeAfterFiveFailedAttempts()
    {
        const string customerEmail = "lockout.customer@example.com";
        string? capturedHtml = null;

        var sqlAuth = new Mock<ISqlAuthService>();
        sqlAuth.Setup(s => s.CheckCustomerForgotPasswordEligibilityAsync(customerEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, customerEmail, null, StatusCodes.Status200OK));

        var emailSvc = new Mock<IEmailService>();
        emailSvc.Setup(e => e.TrySendEmailAsync(customerEmail, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Callback<string, string, string, bool, CancellationToken>((_, _, body, _, _) => capturedHtml = body)
            .ReturnsAsync(true);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = CreateController(sqlAuth, emailSvc, cache);

        await controller.ForgotPassword(new SendVerificationRequest(customerEmail));
        var validCode = Regex.Match(capturedHtml!, @"\b(\d{6})\b").Groups[1].Value;
        var wrongCode = validCode == "000000" ? "999999" : "000000";

        // 4 failed attempts on verify-reset-code + 1 failed attempt on reset-password = 5 total failures
        for (int i = 0; i < 4; i++)
        {
            var attempt = await controller.VerifyResetCode(new VerifyCodeRequest(customerEmail, wrongCode));
            Assert.IsType<BadRequestObjectResult>(attempt);
        }

        var fifthAttempt = await controller.ResetPassword(new ResetPasswordRequest(customerEmail, wrongCode, "NewSecure#2026"));
        Assert.IsType<BadRequestObjectResult>(fifthAttempt);

        // Cached code must now be invalidated even when the previously valid code is provided
        Assert.False(cache.TryGetValue($"ResetCode_{customerEmail}", out _));

        var verifyAfterLockout = await controller.VerifyResetCode(new VerifyCodeRequest(customerEmail, validCode));
        Assert.IsType<BadRequestObjectResult>(verifyAfterLockout);

        var resetAfterLockout = await controller.ResetPassword(new ResetPasswordRequest(customerEmail, validCode, "NewSecure#2026"));
        Assert.IsType<BadRequestObjectResult>(resetAfterLockout);

        sqlAuth.Verify(s => s.ResetPasswordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForgotPassword_EnforcesPerEmailResendCooldown_WithoutInvalidatingExistingCode()
    {
        const string customerEmail = "cooldown.customer@example.com";
        const string otherCustomerEmail = "other.customer@example.com";
        const string unknownEmail = "unknown.cooldown@example.com";
        string? capturedHtml = null;

        var sqlAuth = new Mock<ISqlAuthService>();
        sqlAuth.Setup(s => s.CheckCustomerForgotPasswordEligibilityAsync(customerEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, customerEmail, null, StatusCodes.Status200OK));
        sqlAuth.Setup(s => s.CheckCustomerForgotPasswordEligibilityAsync(otherCustomerEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, otherCustomerEmail, null, StatusCodes.Status200OK));
        sqlAuth.Setup(s => s.CheckCustomerForgotPasswordEligibilityAsync(unknownEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, null, "No customer account found with that email address.", StatusCodes.Status404NotFound));
        sqlAuth.Setup(s => s.ResetPasswordAsync(customerEmail, "NewSecure#2026", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, "Password has been successfully reset."));

        var emailSvc = new Mock<IEmailService>();
        emailSvc.Setup(e => e.TrySendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Callback<string, string, string, bool, CancellationToken>((to, _, body, _, _) =>
            {
                if (to == customerEmail) capturedHtml = body;
            })
            .ReturnsAsync(true);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = CreateController(sqlAuth, emailSvc, cache);

        // First request succeeds
        Assert.IsType<OkObjectResult>(await controller.ForgotPassword(new SendVerificationRequest(customerEmail)));
        var firstCode = Regex.Match(capturedHtml!, @"\b(\d{6})\b").Groups[1].Value;

        // Immediate resend for same email is rate-limited (429)
        var resendResult = await controller.ForgotPassword(new SendVerificationRequest(customerEmail));
        var tooMany = Assert.IsType<ObjectResult>(resendResult);
        Assert.Equal(StatusCodes.Status429TooManyRequests, tooMany.StatusCode);

        // Different customer email is unaffected by first email's cooldown
        Assert.IsType<OkObjectResult>(await controller.ForgotPassword(new SendVerificationRequest(otherCustomerEmail)));

        // Unknown email also gets 200 OK first, then 429 on immediate resend without sending email
        Assert.IsType<OkObjectResult>(await controller.ForgotPassword(new SendVerificationRequest(unknownEmail)));
        var unknownResend = Assert.IsType<ObjectResult>(await controller.ForgotPassword(new SendVerificationRequest(unknownEmail)));
        Assert.Equal(StatusCodes.Status429TooManyRequests, unknownResend.StatusCode);

        emailSvc.Verify(e => e.TrySendEmailAsync(customerEmail, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()), Times.Once);
        emailSvc.Verify(e => e.TrySendEmailAsync(unknownEmail, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()), Times.Never);

        // Existing code from the first send still remains valid
        Assert.IsType<OkObjectResult>(await controller.VerifyResetCode(new VerifyCodeRequest(customerEmail, firstCode)));
        Assert.IsType<OkObjectResult>(await controller.ResetPassword(new ResetPasswordRequest(customerEmail, firstCode, "NewSecure#2026")));
    }

    [Fact]
    public async Task ResetPassword_RejectsWeakPassword_AndPreservesCodeUntilCompliantPassword()
    {
        const string customerEmail = "policy.customer@example.com";
        string? capturedHtml = null;

        var sqlAuth = new Mock<ISqlAuthService>();
        sqlAuth.Setup(s => s.CheckCustomerForgotPasswordEligibilityAsync(customerEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, customerEmail, null, StatusCodes.Status200OK));
        sqlAuth.Setup(s => s.ResetPasswordAsync(customerEmail, "NewSecure#2026", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, "Password has been successfully reset."));

        var emailSvc = new Mock<IEmailService>();
        emailSvc.Setup(e => e.TrySendEmailAsync(customerEmail, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Callback<string, string, string, bool, CancellationToken>((_, _, body, _, _) => capturedHtml = body)
            .ReturnsAsync(true);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = CreateController(sqlAuth, emailSvc, cache);

        await controller.ForgotPassword(new SendVerificationRequest(customerEmail));
        var code = Regex.Match(capturedHtml!, @"\b(\d{6})\b").Groups[1].Value;

        var weakResult = await controller.ResetPassword(new ResetPasswordRequest(customerEmail, code, "weak"));
        Assert.IsType<BadRequestObjectResult>(weakResult);
        sqlAuth.Verify(s => s.ResetPasswordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        var validResult = await controller.ResetPassword(new ResetPasswordRequest(customerEmail, code, "NewSecure#2026"));
        Assert.IsType<OkObjectResult>(validResult);
        sqlAuth.Verify(s => s.ResetPasswordAsync(customerEmail, "NewSecure#2026", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthService_CustomerForgotPasswordMethods_ParseProblemDetailsAnd429Gracefully()
    {
        var callIndex = 0;
        var handler = new StubHttpHandler(_ =>
        {
            callIndex++;
            return callIndex switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"message":"Sent"}""", Encoding.UTF8, "application/json")
                },
                2 => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent(
                        """{"title":"Please wait","detail":"Please wait 45 seconds before requesting a new verification code.","status":429}""",
                        Encoding.UTF8,
                        "application/problem+json")
                },
                3 => new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent(
                        """{"title":"Password recovery unavailable","detail":"Forgot Password is only available for customer accounts.","status":403}""",
                        Encoding.UTF8,
                        "application/problem+json")
                },
                _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        """{"title":"One or more validation errors occurred.","errors":{"NewPassword":["Password must contain at least one special character (@, #, $, %, &)."]}}""",
                        Encoding.UTF8,
                        "application/problem+json")
                }
            };
        });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("AevumApi.Anonymous"))
            .Returns(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });

        var authState = new AevumAuthenticationStateProvider(Mock.Of<ITokenStore>());
        var authService = new AuthService(factory.Object, authState);

        var ok = await authService.RequestPasswordResetCodeAsync("cust@example.com");
        Assert.True(ok.Success);
        Assert.Null(ok.Error);

        var rateLimited = await authService.RequestPasswordResetCodeAsync("cust@example.com");
        Assert.False(rateLimited.Success);
        Assert.Contains("45 seconds", rateLimited.Error);

        var forbidden = await authService.VerifyPasswordResetCodeAsync("admin@studio.com", "123456");
        Assert.False(forbidden.Success);
        Assert.Contains("customer accounts", forbidden.Error);

        var invalidPw = await authService.ResetCustomerPasswordAsync("cust@example.com", "123456", "NoSpecial1");
        Assert.False(invalidPw.Success);
        Assert.Contains("special character", invalidPw.Error);
    }

    [Fact]
    public async Task ForgotPasswordComponent_ThreeStepModalFlow_GuardsStepsAndRendersChecklistAndSuccess()
    {
        var authMock = new Mock<IAuthService>();
        authMock.Setup(a => a.RequestPasswordResetCodeAsync("staff@photoaevum.studio", It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "Forgot Password is only available for customer accounts."));
        authMock.Setup(a => a.RequestPasswordResetCodeAsync("maria.customer@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null));
        authMock.Setup(a => a.VerifyPasswordResetCodeAsync("maria.customer@example.com", "000000", It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "That verification code doesn't match or has expired."));
        authMock.Setup(a => a.VerifyPasswordResetCodeAsync("maria.customer@example.com", "654321", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null));
        authMock.Setup(a => a.ResetCustomerPasswordAsync("maria.customer@example.com", "654321", "NewSecure#2026", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null));

        var activator = new CapturingComponentActivator();
        var services = new ServiceCollection();
        services.AddSingleton(authMock.Object);
        services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider(new ClaimsPrincipal(new ClaimsIdentity())));
        services.AddSingleton<NavigationManager, StubNavManager>();
        services.AddSingleton<IComponentActivator>(activator);

        await using var sp = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);

        HtmlRootComponent rootComponent = default;
        var initialHtml = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            rootComponent = await renderer.RenderComponentAsync<ForgotPassword>();
            return rootComponent.ToHtmlString();
        });

        var page = activator.CapturedForgotPassword;
        Assert.NotNull(page);

        // Step 1 initially on page, modal closed
        Assert.Contains("Customer Account Recovery", initialHtml);
        Assert.Contains("Send Verification Code", initialHtml);
        Assert.Contains("recovery-email", initialHtml);
        Assert.DoesNotContain("role=\"dialog\"", initialHtml);

        // Staff email rejected on Step 1 -> stays on Step 1, modal does not open
        var staffHtml = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            page!.step1Model.Email = "staff@photoaevum.studio";
            await page.HandleSendVerificationCode();
            page.TriggerStateHasChanged();
            return rootComponent.ToHtmlString();
        });
        Assert.False(page!.isModalOpen);
        Assert.DoesNotContain("role=\"dialog\"", staffHtml);
        Assert.Contains("Forgot Password is only available for customer accounts.", staffHtml);

        // Valid customer email -> opens modal dialog at Step 2 with cooldown and Change Email
        var step2Html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            page.step1Model.Email = "  Maria.Customer@Example.com  ";
            await page.HandleSendVerificationCode();
            page.TriggerStateHasChanged();
            return rootComponent.ToHtmlString();
        });
        Assert.True(page.isModalOpen);
        Assert.Equal(2, page.modalStep);
        Assert.Contains("role=\"dialog\"", step2Html);
        Assert.Contains("aria-modal=\"true\"", step2Html);
        Assert.Contains("verification-code", step2Html);
        Assert.Contains("Change Email", step2Html);
        Assert.Contains("Resend code in", step2Html);
        Assert.DoesNotContain("pw-checklist", step2Html);

        // Wrong code -> stays on modal Step 2, does NOT reveal password form
        var wrongCodeHtml = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            page.step2Model.Code = "000000";
            await page.HandleVerifyCode();
            page.TriggerStateHasChanged();
            return rootComponent.ToHtmlString();
        });
        Assert.Equal(2, page.modalStep);
        Assert.Contains("doesn&#x27;t match or has expired", wrongCodeHtml);
        Assert.DoesNotContain("pw-checklist", wrongCodeHtml);

        // Valid code -> advances modal to Step 3 with PasswordRequirementsChecklist
        var step3Html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            page.step2Model.Code = "654321";
            await page.HandleVerifyCode();
            page.TriggerStateHasChanged();
            return rootComponent.ToHtmlString();
        });
        Assert.Equal(3, page.modalStep);
        Assert.Contains("Set New Password", step3Html);
        Assert.Contains("pw-checklist", step3Html);
        Assert.Contains("new-password", step3Html);
        Assert.Contains("confirm-password", step3Html);

        // Mismatched passwords blocked before API call
        var mismatchHtml = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            page.step3Model.NewPassword = "NewSecure#2026";
            page.step3Model.ConfirmPassword = "Different#2026";
            await page.HandleResetPassword();
            page.TriggerStateHasChanged();
            return rootComponent.ToHtmlString();
        });
        Assert.Contains("Passwords do not match.", mismatchHtml);
        authMock.Verify(a => a.ResetCustomerPasswordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // Compliant matching passwords -> shows clear success message and Back to Login button (/login)
        var successHtml = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            page.step3Model.NewPassword = "NewSecure#2026";
            page.step3Model.ConfirmPassword = "NewSecure#2026";
            await page.HandleResetPassword();
            page.TriggerStateHasChanged();
            return rootComponent.ToHtmlString();
        });
        Assert.True(page.passwordUpdated);
        Assert.Contains("Password Updated", successHtml);
        Assert.Contains("Back to Login", successHtml);
        Assert.Contains("href=\"/login\"", successHtml);
        authMock.Verify(a => a.ResetCustomerPasswordAsync("maria.customer@example.com", "654321", "NewSecure#2026", It.IsAny<CancellationToken>()), Times.Once);

        // Closing modal resets modal state cleanly
        var closedHtml = await renderer.Dispatcher.InvokeAsync(() =>
        {
            page.HandleCloseModal();
            page.TriggerStateHasChanged();
            return rootComponent.ToHtmlString();
        });
        Assert.False(page.isModalOpen);
        Assert.DoesNotContain("role=\"dialog\"", closedHtml);
    }

    [Fact]
    public async Task ForgotPasswordComponent_BlocksAuthenticatedStaffSession()
    {
        var staffPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "admin@photoaevum.studio"),
            new Claim(ClaimTypes.Role, "Admin")
        }, "TestAuth"));

        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IAuthService>());
        services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider(staffPrincipal));
        services.AddSingleton<NavigationManager, StubNavManager>();

        await using var sp = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<ForgotPassword>();
            return output.ToHtmlString();
        });

        Assert.Contains("Forgot Password is only available for customer accounts.", html);
        Assert.DoesNotContain("recovery-email", html);
        Assert.DoesNotContain("role=\"dialog\"", html);
    }

    [Fact]
    public async Task ForgotPasswordComponent_ModalResendEnforcesCooldown_AndSyncsServerCooldown()
    {
        var callCount = 0;
        var authMock = new Mock<IAuthService>();
        authMock.Setup(a => a.RequestPasswordResetCodeAsync("maria.customer@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount switch
                {
                    1 => (true, (string?)null),
                    2 => (false, "Please wait 42 seconds before requesting a new verification code."),
                    _ => (true, (string?)null)
                };
            });

        var activator = new CapturingComponentActivator();
        var services = new ServiceCollection();
        services.AddSingleton(authMock.Object);
        services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider(new ClaimsPrincipal(new ClaimsIdentity())));
        services.AddSingleton<NavigationManager, StubNavManager>();
        services.AddSingleton<IComponentActivator>(activator);

        await using var sp = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);

        HtmlRootComponent rootComponent = default;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            rootComponent = await renderer.RenderComponentAsync<ForgotPassword>();
        });

        var page = activator.CapturedForgotPassword!;

        // Initial send starts 60s cooldown; calling resend while cooldown > 0 does not hit API
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            page.step1Model.Email = "maria.customer@example.com";
            await page.HandleSendVerificationCode();
            await page.HandleResendCode();
            page.TriggerStateHasChanged();
        });
        Assert.Equal(60, page.resendCooldownSeconds);
        authMock.Verify(a => a.RequestPasswordResetCodeAsync("maria.customer@example.com", It.IsAny<CancellationToken>()), Times.Once);

        // Once local cooldown reaches 0, server 429 remaining seconds are synced and displayed
        var cooldownSyncHtml = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            page.resendCooldownSeconds = 0;
            await page.HandleResendCode();
            page.TriggerStateHasChanged();
            return rootComponent.ToHtmlString();
        });
        Assert.Equal(42, page.resendCooldownSeconds);
        Assert.Contains("Please wait 42 seconds", cooldownSyncHtml);
        Assert.Contains("Resend code in 42s", cooldownSyncHtml);

        // Subsequent successful resend clears code input and resets 60s cooldown
        var resendSuccessHtml = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            page.step2Model.Code = "999999";
            page.resendCooldownSeconds = 0;
            await page.HandleResendCode();
            page.TriggerStateHasChanged();
            return rootComponent.ToHtmlString();
        });
        Assert.Equal(60, page.resendCooldownSeconds);
        Assert.Equal(string.Empty, page.step2Model.Code);
        Assert.Contains("new 6-digit verification code", resendSuccessHtml);
    }

    private static string GetRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")) && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    private sealed class StubAuthStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(user));
    }

    private sealed class StubNavManager : NavigationManager
    {
        public StubNavManager() => Initialize("http://localhost/", "http://localhost/forgot-password");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class CapturingComponentActivator : IComponentActivator
    {
        public ForgotPassword? CapturedForgotPassword { get; private set; }

        public IComponent CreateInstance(Type componentType)
        {
            var instance = (IComponent)Activator.CreateInstance(componentType)!;
            if (instance is ForgotPassword fp)
            {
                CapturedForgotPassword = fp;
            }
            return instance;
        }
    }
}
