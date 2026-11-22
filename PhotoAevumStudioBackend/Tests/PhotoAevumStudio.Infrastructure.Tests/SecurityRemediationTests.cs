using System.Net;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;
using PhotoAevumStudio.Application.Utils;
using PhotoAevumStudio.Application.Validation;
using PhotoAevumStudio.Infrastructure.Services;
using Xunit;

namespace PhotoAevumStudio.Infrastructure.Tests;

public class SecurityRemediationTests
{
    [Fact]
    public void SqlAuthService_VerifyPasswordStrict_RejectsHashStringReplay_AndHardcodedPasswords()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("StrongStudioPass@2026");
        var method = typeof(SqlAuthService).GetMethod("VerifyPasswordStrict", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        Assert.True((bool)method!.Invoke(null, ["StrongStudioPass@2026", hash])!);
        Assert.False((bool)method.Invoke(null, [hash, hash])!);
        Assert.False((bool)method.Invoke(null, ["AxiomBit_2026", hash])!);
        Assert.False((bool)method.Invoke(null, ["AxiomBit@2026", hash])!);
        Assert.False((bool)method.Invoke(null, ["plaintext", "plaintext"])!);
    }

    [Fact]
    public void SqlAuthService_VerifyPasswordFlexible_NeverMatchesHashStringAsPlaintext()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("RealPass@123");
        var method = typeof(SqlAuthService).GetMethod("VerifyPasswordFlexible", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        Assert.True((bool)method!.Invoke(null, ["RealPass@123", hash, true])!);
        Assert.False((bool)method.Invoke(null, [hash, hash, true])!);
        Assert.True((bool)method.Invoke(null, ["LegacyPlain", "LegacyPlain", true])!);
        Assert.False((bool)method.Invoke(null, ["LegacyPlain", "LegacyPlain", false])!);
    }

    [Fact]
    public async Task LocalStorageProvider_BlocksPathTraversalOnDeleteAndRead()
    {
        var provider = new LocalStorageProvider();

        await Assert.ThrowsAsync<ArgumentException>(() => provider.DeleteFileAsync("../appsettings.json"));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.DeleteFileAsync("..\\appsettings.json"));
        var secureUrl = await provider.GetSecureUrlAsync("../appsettings.json");
        Assert.DoesNotContain("..", secureUrl);
        Assert.DoesNotContain("wwwroot", secureUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task XenditPaymentService_FailsClosed_InProductionAndOnMissingKeys()
    {
        var previousEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Production);
        try
        {
            var options = Options.Create(new XenditSettings
            {
                SecretKey = "xnd_development_mock_secret",
                InvoiceBaseUrl = "https://checkout.xendit.co/web/"
            });

            using var httpClient = new HttpClient(new FailingHandler());
            var service = new XenditPaymentService(options, httpClient, NullLogger<XenditPaymentService>.Instance);

            var statusById = await service.CheckPaymentStatusAsync("inv_mock_12345");
            var statusByExt = await service.CheckPaymentStatusByExternalIdAsync("EVT-0001");

            Assert.Equal("UNVERIFIED", statusById.Status);
            Assert.Equal("UNVERIFIED", statusByExt.Status);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateQrCodeAsync("EVT-0001", 1000m, DateTime.UtcNow.AddMinutes(30)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previousEnv);
        }
    }

    [Fact]
    public void EmployeeCredentialGenerator_RandomOverload_ProducesUniquePolicyCompliantPasswords()
    {
        var p1 = EmployeeCredentialGenerator.GenerateInitialPassword(12);
        var p2 = EmployeeCredentialGenerator.GenerateInitialPassword(12);

        Assert.NotEqual(p1, p2);
        Assert.Null(PasswordPolicy.Validate(p1));
        Assert.Null(PasswordPolicy.Validate(p2));
    }

    [Fact]
    public void DropboxService_RequiresDedicatedDropboxSigningKey_NotJwtKey()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "ThisIsOnlyAJwtKeyThatMustNotSignDropboxTokens12345"
            })
            .Build();

        var dbOptions = new DbContextOptionsBuilder<PhotoAevumStudio.Infrastructure.Data.ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=Dummy;Trusted_Connection=True;")
            .Options;
        using var db = new PhotoAevumStudio.Infrastructure.Data.ApplicationDbContext(dbOptions);
        var service = new CustomerDropboxService(
            db,
            new Mock<IR2StorageService>().Object,
            config,
            NullLogger<CustomerDropboxService>.Instance);

        Assert.Throws<InvalidOperationException>(() =>
            service.CreateSignedDownloadToken("DRP-0001", "CUST-0001", TimeSpan.FromMinutes(10)));
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }
}
