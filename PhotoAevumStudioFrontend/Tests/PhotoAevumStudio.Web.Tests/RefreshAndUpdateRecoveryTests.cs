using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.JSInterop;
using Moq;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class RefreshAndUpdateRecoveryTests
{
    private static string GetWebProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.Web", "PhotoAevumStudio.Web.csproj")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "PhotoAevumStudio.Web");
    }

    private static ProtectedSessionTokenStore CreateTokenStore()
    {
        var jsRuntime = new Mock<IJSRuntime>();
        var dataProtection = DataProtectionProvider.Create("PhotoAevumStudio.Web.Tests");
        var sessionStorage = new ProtectedSessionStorage(jsRuntime.Object, dataProtection);
        return new ProtectedSessionTokenStore(sessionStorage);
    }

    [Fact]
    public async Task ProtectedSessionTokenStore_IsolatesTokensAcrossCircuits()
    {
        var circuitA = CreateTokenStore();
        var circuitB = CreateTokenStore();

        await circuitA.SetTokenAsync("jwt-admin-token");
        await circuitB.SetTokenAsync("jwt-staff-token");

        using (ProtectedSessionTokenStore.BeginCircuitScope(circuitA))
        {
            Assert.Equal("jwt-admin-token", ProtectedSessionTokenStore.GetAmbientToken());
            Assert.Equal("jwt-admin-token", await circuitA.GetTokenAsync());
        }

        using (ProtectedSessionTokenStore.BeginCircuitScope(circuitB))
        {
            Assert.Equal("jwt-staff-token", ProtectedSessionTokenStore.GetAmbientToken());
            Assert.Equal("jwt-staff-token", await circuitB.GetTokenAsync());
        }

        // Clearing circuit B must not affect circuit A
        await circuitB.ClearAsync();

        using (ProtectedSessionTokenStore.BeginCircuitScope(circuitA))
        {
            Assert.Equal("jwt-admin-token", ProtectedSessionTokenStore.GetAmbientToken());
            Assert.Equal("jwt-admin-token", await circuitA.GetTokenAsync());
        }

        Assert.Null(await circuitB.GetTokenAsync());
    }

    [Fact]
    public void AppBootInfo_GeneratesDistinctBootIdsPerStartup()
    {
        var boot1 = new AppBootInfo();
        var boot2 = new AppBootInfo();

        Assert.False(string.IsNullOrWhiteSpace(boot1.BootId));
        Assert.False(string.IsNullOrWhiteSpace(boot2.BootId));
        Assert.NotEqual(boot1.BootId, boot2.BootId);
        Assert.False(string.IsNullOrWhiteSpace(boot1.BuildHash));
    }

    [Fact]
    public void AppRazor_IncludesFingerprintedAssets_ReconnectModal_AndNavigationRecovery()
    {
        var appPath = Path.Combine(GetWebProjectRoot(), "Components", "App.razor");
        Assert.True(File.Exists(appPath));
        var content = File.ReadAllText(appPath);

        Assert.Contains("<ImportMap />", content);
        Assert.Contains("@Assets[\"app.css\"]", content);
        Assert.Contains("@Assets[\"PhotoAevumStudio.Web.styles.css\"]", content);
        Assert.Contains("@Assets[\"_framework/blazor.web.js\"]", content);
        Assert.Contains("<ReconnectModal />", content);
        Assert.Contains("name=\"aevum-boot-id\"", content);
        Assert.Contains("window.__aevumNav", content);
        Assert.Contains("recoverNavigation", content);
        Assert.Contains("enhancedload", content);
    }

    [Fact]
    public void ReconnectModalJs_RecoversToPendingTargetInsteadOfUnconditionalReload()
    {
        var jsPath = Path.Combine(GetWebProjectRoot(), "Components", "Layout", "ReconnectModal.razor.js");
        Assert.True(File.Exists(jsPath));
        var content = File.ReadAllText(jsPath);

        Assert.Contains("recoverOrReload()", content);
        Assert.Contains("window.__aevumNav.recoverNavigation()", content);
        Assert.Contains("markCircuitBroken", content);
    }

    [Fact]
    public void ProgramCs_ConfiguresDataProtectionPersistence_NoCacheHtml_AndBootIdVersion()
    {
        var programPath = Path.Combine(GetWebProjectRoot(), "Program.cs");
        Assert.True(File.Exists(programPath));
        var content = File.ReadAllText(programPath);

        Assert.Contains("PersistKeysToFileSystem", content);
        Assert.Contains("AddScoped<CircuitHandler, TokenCircuitHandler>()", content);
        Assert.Contains("no-cache, no-store, must-revalidate", content);
        Assert.Contains("bootId = bootInfo.BootId", content);
    }
}
