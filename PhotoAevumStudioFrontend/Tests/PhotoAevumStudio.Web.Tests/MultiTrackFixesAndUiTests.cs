using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoAevumStudio.Api.Controllers;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Components.Shared;
using PhotoAevumStudio.Web.Models;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class MultiTrackFixesAndUiTests
{
    private static string GetRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null &&
               !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")) &&
               !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static async Task<string> RenderComponentHtmlAsync<TComponent>(Dictionary<string, object?> parametersDict)
        where TComponent : IComponent
    {
        var services = new ServiceCollection();
        await using var sp = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(parametersDict);
            var output = await renderer.RenderComponentAsync<TComponent>(parameters);
            return output.ToHtmlString();
        });
    }

    [Fact]
    public async Task TablePagination_PaginatesMaxTenPerPageAndRendersControls()
    {
        Assert.Equal(10, TablePagination.DefaultPageSize);

        var dataset = Enumerable.Range(1, 25).ToList();
        var page1 = TablePagination.Paginate(dataset, 1);
        var page2 = TablePagination.Paginate(dataset, 2);
        var page3 = TablePagination.Paginate(dataset, 3);
        var clampedOver = TablePagination.Paginate(dataset, 99);
        var clampedUnder = TablePagination.Paginate(dataset, -2);

        Assert.Equal(10, page1.Count);
        Assert.Equal(1, page1.First());
        Assert.Equal(10, page1.Last());

        Assert.Equal(10, page2.Count);
        Assert.Equal(11, page2.First());
        Assert.Equal(20, page2.Last());

        Assert.Equal(5, page3.Count);
        Assert.Equal(21, page3.First());
        Assert.Equal(25, page3.Last());

        Assert.Equal(page3, clampedOver);
        Assert.Equal(page1, clampedUnder);

        var html = await RenderComponentHtmlAsync<TablePagination>(new()
        {
            { nameof(TablePagination.TotalItems), 25 },
            { nameof(TablePagination.PageSize), 10 },
            { nameof(TablePagination.CurrentPage), 1 },
            { nameof(TablePagination.ItemLabel), "clients" }
        });

        Assert.Contains("Previous", html);
        Assert.Contains("Next", html);
        Assert.Contains("1</span>", html);
        Assert.Contains("10</span>", html);
        Assert.Contains("25</span>", html);
        Assert.Contains("clients", html);
    }

    [Fact]
    public async Task SystemMessage_RendersBannerToastAndPopupModesConsistently()
    {
        var bannerHtml = await RenderComponentHtmlAsync<SystemMessage>(new()
        {
            { nameof(SystemMessage.Message), "Staff password reset email sent." },
            { nameof(SystemMessage.Title), "Success" },
            { nameof(SystemMessage.Severity), "success" },
            { nameof(SystemMessage.Mode), "banner" }
        });

        Assert.Contains("Staff password reset email sent.", bannerHtml);
        Assert.Contains("role=\"status\"", bannerHtml);

        var errorPopupHtml = await RenderComponentHtmlAsync<SystemMessage>(new()
        {
            { nameof(SystemMessage.Message), "Archive operation failed." },
            { nameof(SystemMessage.Severity), "error" },
            { nameof(SystemMessage.Mode), "popup" }
        });

        Assert.Contains("Archive operation failed.", errorPopupHtml);
        Assert.Contains("role=\"dialog\"", errorPopupHtml);
        Assert.Contains("OK", errorPopupHtml);

        var toastHtml = await RenderComponentHtmlAsync<SystemMessage>(new()
        {
            { nameof(SystemMessage.Message), "Changes saved." },
            { nameof(SystemMessage.Severity), "warning" },
            { nameof(SystemMessage.Mode), "toast" }
        });

        Assert.Contains("Changes saved.", toastHtml);
        Assert.Contains("fixed bottom-4 right-4", toastHtml);
    }

    [Fact]
    public async Task StaffOverviewModal_ShowsResetPasswordOnlyForAdmins()
    {
        var staff = new EmployeeViewModel
        {
            EmployeeId = "EMP-0042",
            FirstName = "Elena",
            LastName = "Reyes",
            Email = "elena@photoaevum.studio",
            Department = "Photography",
            Status = "Active",
            DaysOff = "Sunday"
        };

        var adminHtml = await RenderComponentHtmlAsync<StaffOverviewModal>(new()
        {
            { nameof(StaffOverviewModal.IsOpen), true },
            { nameof(StaffOverviewModal.Employee), staff },
            { nameof(StaffOverviewModal.CanResetPassword), true }
        });

        Assert.Contains("Elena Reyes", adminHtml);
        Assert.Contains("EMP-0042", adminHtml);
        Assert.Contains("Reset Password", adminHtml);

        var staffHtml = await RenderComponentHtmlAsync<StaffOverviewModal>(new()
        {
            { nameof(StaffOverviewModal.IsOpen), true },
            { nameof(StaffOverviewModal.Employee), staff },
            { nameof(StaffOverviewModal.CanResetPassword), false }
        });

        Assert.Contains("Elena Reyes", staffHtml);
        Assert.DoesNotContain("Reset Password", staffHtml);
    }

    [Fact]
    public void BackendControllers_EnforceAdminResetPasswordAndCatalogOutputCaching()
    {
        var resetMethod = typeof(ManagementController).GetMethod(nameof(ManagementController.ResetEmployeePassword));
        Assert.NotNull(resetMethod);

        var routeAttr = resetMethod!.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(routeAttr);
        Assert.Equal("employees/{employeeId}/reset-password", routeAttr!.Template);

        var authAttr = resetMethod.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.Equal("AdminOrAbove", authAttr!.Policy);

        foreach (var (controllerType, methodName) in new[]
        {
            (typeof(ServicesController), nameof(ServicesController.GetAll)),
            (typeof(PackagesController), nameof(PackagesController.GetCategories)),
            (typeof(PackagesController), nameof(PackagesController.GetAvailableServices)),
            (typeof(PackagesController), nameof(PackagesController.GetPackageSummary)),
            (typeof(AdditionalServicesController), nameof(AdditionalServicesController.GetAll)),
            (typeof(AdditionalServicesController), nameof(AdditionalServicesController.GetCategories)),
            (typeof(CategoriesController), nameof(CategoriesController.GetAll))
        })
        {
            var method = controllerType.GetMethod(methodName);
            Assert.NotNull(method);
            var cacheAttr = method!.GetCustomAttribute<OutputCacheAttribute>();
            Assert.NotNull(cacheAttr);
            Assert.Equal("CatalogCache", cacheAttr!.PolicyName);
        }
    }

    [Fact]
    public void Track2AndTrack3RazorPages_MeetNavigationConditionalDeleteAndCalendarSpecs()
    {
        var root = GetRepositoryRoot();
        var webRoot = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web");

        // HistoryLog single-click link & General -> Sales & Report icon placement
        var sidebar = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "Sidebar.razor"));
        Assert.Contains("<NavLink href=\"/admin/history-log\"", sidebar);
        Assert.DoesNotContain("id=\"acc-history\"", sidebar);
        Assert.Contains("href=\"/admin/sales\"", sidebar);
        Assert.Contains("href=\"/admin/reports\"", sidebar);

        // Conditional Delete Account button in Clients.razor
        var clients = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Clients.razor"));
        Assert.Contains("@if (client.HasRequestedDeletion)", clients);
        Assert.Contains("Delete Account", clients);
        Assert.Contains("<TablePagination", clients);

        // Month event pill opens details directly + Day timetable uses gantt-timeline-track
        var calendar = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "CalendarOfEvents.razor"));
        Assert.Contains("class=\"mini-event-pill", calendar);
        Assert.Contains("@onclick:stopPropagation=\"true\"", calendar);
        Assert.Contains("gantt-timeline-track", calendar);
        Assert.Contains("EffectiveEndTime", calendar);

        // Staff archiving HistoryLog inserts explicitly include ChangedByType
        var empService = File.ReadAllText(Path.Combine(webRoot, "Services", "EmployeeService.cs"));
        var userService = File.ReadAllText(Path.Combine(webRoot, "Services", "UserService.cs"));
        Assert.Contains("ChangedByType", empService);
        Assert.Contains("'STATUS_CHANGE'", empService);
        Assert.Contains("INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedByType", userService);
    }
}
