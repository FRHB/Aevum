using System.Reflection;
using Microsoft.AspNetCore.Components;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Models;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class HomeAndHighlightEnhancementTests
{
    [Fact]
    public void HomeHighlightModel_DefaultValues_AreProperlyInitialized()
    {
        var model = new HomeHighlightModel();

        Assert.Equal(string.Empty, model.HighlightID);
        Assert.Equal(string.Empty, model.Title);
        Assert.Equal(string.Empty, model.ImageUrl);
        Assert.Equal(0, model.DisplayOrder);
        Assert.True(model.IsActive);
        Assert.True((DateTime.UtcNow - model.CreatedAt).TotalSeconds < 5);
    }

    [Fact]
    public void HomeHighlightModel_Ordering_SortsByDisplayOrderThenCreatedAt()
    {
        var now = DateTime.UtcNow;
        var list = new List<HomeHighlightModel>
        {
            new() { HighlightID = "HL-03", Title = "C", DisplayOrder = 3, CreatedAt = now.AddMinutes(1) },
            new() { HighlightID = "HL-01", Title = "A", DisplayOrder = 1, CreatedAt = now },
            new() { HighlightID = "HL-02", Title = "B", DisplayOrder = 2, CreatedAt = now.AddMinutes(2) },
            new() { HighlightID = "HL-04", Title = "A2", DisplayOrder = 1, CreatedAt = now.AddMinutes(5) }
        };

        var ordered = list
            .OrderBy(h => h.DisplayOrder)
            .ThenByDescending(h => h.CreatedAt)
            .ToList();

        Assert.Equal("HL-04", ordered[0].HighlightID);
        Assert.Equal("HL-01", ordered[1].HighlightID);
        Assert.Equal("HL-02", ordered[2].HighlightID);
        Assert.Equal("HL-03", ordered[3].HighlightID);
    }

    [Fact]
    public void HomeHighlightsManagement_HasConfiguredAdminRoutes()
    {
        var pageType = typeof(HomeHighlightsManagement);
        var routeAttrs = pageType.GetCustomAttributes<RouteAttribute>(inherit: false).ToList();

        Assert.NotEmpty(routeAttrs);
        var routes = routeAttrs.Select(r => r.Template).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("/admin/highlights", routes);
        Assert.Contains("/admin/portfolio/highlights", routes);
    }

    [Fact]
    public void MainLayout_ContactSection_UsesStandardCrispSvgsWithoutUnhydratedLucideTags()
    {
        var root = GetRepositoryRoot();
        var mainLayoutPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Layout", "MainLayout.razor");
        Assert.True(File.Exists(mainLayoutPath), $"File not found: {mainLayoutPath}");

        var content = File.ReadAllText(mainLayoutPath);

        // Verify standard SVGs are used for Contact section and social links
        Assert.Contains("mailto:photoaevumstudio@gmail.com", content);
        Assert.Contains("(+63) 919-578-93124", content);

        // Verify Lucide placeholders are NOT used for email and phone in Contact section
        Assert.DoesNotContain("data-lucide=\"mail\"", content);
        Assert.DoesNotContain("data-lucide=\"phone\"", content);
        Assert.DoesNotContain("data-lucide=\"instagram\"", content);
        Assert.DoesNotContain("data-lucide=\"twitter\"", content);

        // Verify inline SVG tags exist in the footer for email, phone, and socials
        Assert.Contains("<rect width=\"20\" height=\"16\" x=\"2\" y=\"4\" rx=\"2\"></rect>", content); // mail svg
        Assert.Contains("M22 16.92v3a2 2 0 0 1-2.18 2", content); // phone svg
        Assert.Contains("https://www.facebook.com/profile.php?id=100095100746143", content);
    }

    [Fact]
    public void HomeRazor_ContainsLuxuryLoader_AndFiltersFeaturedServicesOnly()
    {
        var root = GetRepositoryRoot();
        var homePath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "Home.razor");
        Assert.True(File.Exists(homePath), $"File not found: {homePath}");

        var content = File.ReadAllText(homePath);

        // 1. Loading screen markup & logic
        Assert.Contains("_showLoadingScreen", content);
        Assert.Contains("_loadingFadeOut", content);
        Assert.Contains("animate-spin", content);
        Assert.Contains("Photo Aevum Studio", content);
        Assert.Contains("Capturing Timeless Narratives", content);

        // 2. Featured Services filtering
        Assert.Contains("GetFeaturedCodesAsync(FeaturedEntityTypes.Main)", content);
        Assert.Contains("Where(s => s.IsFeatured)", content);
        Assert.Contains("No featured services currently highlighted", content);

        // 3. Hero highlights continuous slider
        Assert.Contains("_currentHighlightIndex", content);
        Assert.Contains("PauseHighlightTimer", content);
        Assert.Contains("ResumeHighlightTimer", content);
        Assert.Contains("StartHighlightTimer", content);
        Assert.Contains("SetHighlightSlide", content);
        Assert.Contains("Next Available Session", content);
    }

    [Fact]
    public void SidebarRazor_IncludesLinkToAdminHighlights()
    {
        var root = GetRepositoryRoot();
        var sidebarPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Layout", "Sidebar.razor");
        Assert.True(File.Exists(sidebarPath), $"File not found: {sidebarPath}");

        var content = File.ReadAllText(sidebarPath);

        Assert.Contains("/admin/highlights", content);
        Assert.Contains("Hero Highlights", content);
    }

    private static string GetRepositoryRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")) && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
