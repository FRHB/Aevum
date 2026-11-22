using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoAevumStudio.Api.Controllers;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Web.Tests;

public class OnboardingHandbookFlowTests
{
    private static AuthController CreateAuthController(
        ISqlAuthService sqlAuthService,
        IEmailService emailService,
        string requesterId = "SADM-001",
        string role = AppRoles.SystemAdmin)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "TestJwtKeyThatIsAtLeastThirtyTwoCharactersLong!",
                ["Jwt:Issuer"] = "PhotoAevumStudioApi",
                ["Jwt:Audience"] = "PhotoAevumStudioClients"
            })
            .Build();

        var controller = new AuthController(
            sqlAuthService,
            config,
            NullLogger<AuthController>.Instance,
            new MemoryCache(new MemoryCacheOptions()),
            emailService);

        var identity = new ClaimsIdentity(new[]
        {
            new Claim("studio_id", requesterId),
            new Claim(ClaimTypes.NameIdentifier, requesterId),
            new Claim(ClaimTypes.Role, role)
        }, "TestAuth");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };

        return controller;
    }

    [Fact]
    public async Task AuthController_RegisterEmployee_TriggersWelcomeEmailWithHandbook_AndPreservesAccountOnEmailFailure()
    {
        var sqlAuthMock = new Mock<ISqlAuthService>();
        sqlAuthMock.Setup(s => s.RegisterEmployeeAsync(
                "EMP-099", "Mia", "Santos", null, "mia.santos@photoaevum.studio", "Santos#2026!", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, "Employee registered successfully."));

        var emailMock = new Mock<IEmailService>();
        emailMock.Setup(e => e.TrySendEmployeeWelcomeEmailAsync(
                "mia.santos@photoaevum.studio",
                "Mia Santos",
                "EMP-099",
                "Santos#2026!",
                It.IsAny<CancellationToken>(),
                "Photographer"))
            .ReturnsAsync(new WelcomeEmailDispatchResult(
                EmailSent: false,
                HandbookAttached: false,
                WarningMessage: "SMTP connection timed out."));

        var controller = CreateAuthController(sqlAuthMock.Object, emailMock.Object, "ADM-001", AppRoles.Admin);

        var result = await controller.Register(new RegisterRequest(
            FirstName: "Mia",
            LastName: "Santos",
            MiddleName: null,
            Email: "mia.santos@photoaevum.studio",
            Password: "Santos#2026!",
            Role: AppRoles.Employee,
            StudioId: "EMP-099",
            AssignedRole: "Photographer"));

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);

        var json = JsonSerializer.SerializeToElement(created.Value);
        Assert.Equal("EMP-099", json.GetProperty("Id").GetString());
        Assert.False(json.GetProperty("WelcomeEmailSent").GetBoolean());
        Assert.False(json.GetProperty("HandbookAttached").GetBoolean());
        Assert.Equal("SMTP connection timed out.", json.GetProperty("EmailWarning").GetString());

        emailMock.VerifyAll();
    }

    [Fact]
    public async Task AuthController_RegisterAdmin_TriggersAdminWelcomeEmailWithHandbook()
    {
        var sqlAuthMock = new Mock<ISqlAuthService>();
        sqlAuthMock.Setup(s => s.RegisterAdminAsync(
                "ADM-015", AppRoles.Admin, "Carlos", "Reyes", null, "carlos.reyes@photoaevum.studio", "Reyes#2026!", "SADM-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, "Admin registered successfully."));

        var emailMock = new Mock<IEmailService>();
        emailMock.Setup(e => e.TrySendAdminWelcomeEmailAsync(
                "carlos.reyes@photoaevum.studio",
                "Carlos Reyes",
                "ADM-015",
                "Reyes#2026!",
                "Admin",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WelcomeEmailDispatchResult(EmailSent: true, HandbookAttached: true, WarningMessage: null));

        var controller = CreateAuthController(sqlAuthMock.Object, emailMock.Object, "SADM-001", AppRoles.SystemAdmin);

        var result = await controller.Register(new RegisterRequest(
            FirstName: "Carlos",
            LastName: "Reyes",
            MiddleName: null,
            Email: "carlos.reyes@photoaevum.studio",
            Password: "Reyes#2026!",
            Role: AppRoles.Admin,
            StudioId: "ADM-015"));

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);

        var json = JsonSerializer.SerializeToElement(created.Value);
        Assert.Equal("ADM-015", json.GetProperty("Id").GetString());
        Assert.True(json.GetProperty("WelcomeEmailSent").GetBoolean());
        Assert.True(json.GetProperty("HandbookAttached").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("EmailWarning").ValueKind);

        emailMock.VerifyAll();
    }

    [Fact]
    public void BackendApiProgram_ProtectsStorageRoute_FromPublicAccess()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var programPath = Path.Combine(dir!.FullName, "PhotoAevumStudioBackend", "PhotoAevumStudio.Api", "Program.cs");
        var source = File.ReadAllText(programPath);

        Assert.Contains("StartsWithSegments(\"/storage\"", source);
        Assert.Contains("IOnboardingDocumentService", source);
        Assert.Contains("OnboardingDocumentsSettings", source);
    }
}
