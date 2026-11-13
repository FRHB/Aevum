using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoAevumStudio.Api.Controllers;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;

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
}
