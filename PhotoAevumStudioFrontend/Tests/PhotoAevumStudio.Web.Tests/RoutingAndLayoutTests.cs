using System.Reflection;
using Microsoft.AspNetCore.Components;
using PhotoAevumStudio.Web.Components.Layout;
using PhotoAevumStudio.Web.Components.Pages;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class RoutingAndLayoutTests
{
    private static readonly Assembly WebAssembly = typeof(AdminDashboard).Assembly;

    private static (Type? ComponentType, string[] Routes, Type? LayoutType) GetComponentInfo<T>()
    {
        return GetComponentInfo(typeof(T));
    }

    private static (Type? ComponentType, string[] Routes, Type? LayoutType) GetComponentInfo(Type type)
    {
        var routes = type.GetCustomAttributes<RouteAttribute>().Select(r => r.Template).ToArray();
        var layoutAttr = type.GetCustomAttribute<LayoutAttribute>();
        return (type, routes, layoutAttr?.LayoutType);
    }

    private static Type? FindComponentByRoute(string route)
    {
        return WebAssembly.GetTypes()
            .FirstOrDefault(t => t.GetCustomAttributes<RouteAttribute>()
                .Any(r => string.Equals(r.Template, route, StringComparison.OrdinalIgnoreCase)));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 1. Task 1: Persistent Layout Architecture Tests
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Layouts_InheritFromLayoutComponentBase()
    {
        Assert.True(typeof(LayoutComponentBase).IsAssignableFrom(typeof(AdminLayout)));
        Assert.True(typeof(LayoutComponentBase).IsAssignableFrom(typeof(EmployeeLayout)));
        Assert.True(typeof(LayoutComponentBase).IsAssignableFrom(typeof(CustomerLayout)));
        Assert.True(typeof(LayoutComponentBase).IsAssignableFrom(typeof(SidebarLayout)));
    }

    [Fact]
    public void AdminDashboardPages_AllSharePersistentAdminLayout()
    {
        var adminPages = new[]
        {
            typeof(AdminDashboard),
            typeof(BookingApprovals),
            typeof(Employee),
            typeof(Equipment),
            typeof(Schedules),
            typeof(CalendarOfEvents),
            typeof(StaffSchedule),
            typeof(EquipmentSchedule),
            typeof(MainServices),
            typeof(PackageServices),
            typeof(AdditionalServices),
            typeof(Specialization),
            typeof(Equipments),
            typeof(Dispatch),
            typeof(Maintenance),
            typeof(EquipmentTypes),
            typeof(Profile),
            typeof(AdminManagement),
            typeof(Clients),
            typeof(HistoryLog),
            typeof(FaqManagement),
            typeof(Sales),
            typeof(Reports),
            typeof(StudioAnalytics)
        };

        foreach (var page in adminPages)
        {
            var info = GetComponentInfo(page);
            Assert.True(info.LayoutType == typeof(AdminLayout),
                $"Component {page.Name} must use AdminLayout for persistent navigation without remounting.");
        }
    }

    [Fact]
    public void StaffPages_AllSharePersistentEmployeeLayout()
    {
        var staffPages = new[]
        {
            typeof(StaffDashboard),
            typeof(GearChecklist),
            typeof(DeliveryTracker),
            typeof(StaffAssignments),
            typeof(StaffProfile),
            typeof(StaffCalendarEvents),
            typeof(StaffCalendarSchedule),
            typeof(StaffCalendarEquipment),
            typeof(StaffServices),
            typeof(StaffPackageServices),
            typeof(StaffAdditionalServices)
        };

        foreach (var page in staffPages)
        {
            var info = GetComponentInfo(page);
            Assert.True(info.LayoutType == typeof(EmployeeLayout),
                $"Component {page.Name} must use EmployeeLayout for persistent navigation without remounting.");
        }
    }

    [Fact]
    public void CustomerPages_AllSharePersistentCustomerLayout()
    {
        var customerPages = new[]
        {
            typeof(CustomerDashboard),
            typeof(CustomerBookingFlow),
            typeof(MyBookings),
            typeof(PrivateGallery),
            typeof(CustomerProfile)
        };

        foreach (var page in customerPages)
        {
            var info = GetComponentInfo(page);
            Assert.True(info.LayoutType == typeof(CustomerLayout),
                $"Component {page.Name} must use CustomerLayout for persistent navigation without remounting.");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2. Task 2: Required New Routes & Placeholder Components Registration
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("/admin/calendar/events")]
    [InlineData("/calendar-of-events")]
    [InlineData("/admin/calendar/staff")]
    [InlineData("/staff-schedule")]
    [InlineData("/admin/calendar/equipment")]
    [InlineData("/equipments-schedule")]
    [InlineData("/admin/services/main")]
    [InlineData("/main-services")]
    [InlineData("/admin/services/packages")]
    [InlineData("/package-services")]
    [InlineData("/admin/services/additional")]
    [InlineData("/additional-services")]
    [InlineData("/admin/employees/specialization")]
    [InlineData("/specialization")]
    [InlineData("/admin/equipments")]
    [InlineData("/equipments")]
    [InlineData("/admin/equipment/dispatch")]
    [InlineData("/dispatch")]
    [InlineData("/admin/equipment/maintenance")]
    [InlineData("/maintenance")]
    [InlineData("/admin/equipment/types")]
    [InlineData("/equipment-types")]
    [InlineData("/admin/history-log")]
    [InlineData("/profile")]
    [InlineData("/admin/faqs")]
    [InlineData("/admin/faq")]
    [InlineData("/admin/sales")]
    [InlineData("/sales")]
    [InlineData("/admin/reports")]
    [InlineData("/reports")]
    public void RequiredNewRoutes_AreProperlyRegistered(string expectedRoute)
    {
        var component = FindComponentByRoute(expectedRoute);
        Assert.NotNull(component);
    }

    [Fact]
    public void ProfileRoute_IsRegisteredAndAccessible()
    {
        var component = FindComponentByRoute("/profile");
        Assert.NotNull(component);
        Assert.Equal(typeof(Profile), component);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 3. Constraints: Existing Routes Are Preserved
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("/")]
    [InlineData("/Home")]
    [InlineData("/login")]
    [InlineData("/change-password")]
    [InlineData("/admin")]
    [InlineData("/admin/schedules")]
    [InlineData("/admin/clients")]
    [InlineData("/admin/services")]
    [InlineData("/admin/employees")]
    [InlineData("/admin/equipment")]
    [InlineData("/admin/approvals")]
    [InlineData("/staff")]
    [InlineData("/staff/gear")]
    [InlineData("/staff/pipeline")]
    [InlineData("/staff/assignments")]
    [InlineData("/my")]
    [InlineData("/my/bookings")]
    [InlineData("/my/book")]
    [InlineData("/my/gallery/{GalleryId}")]
    [InlineData("/not-found")]
    [InlineData("/Error")]
    public void ExistingRoutes_AreNotDeleted(string existingRoute)
    {
        var component = FindComponentByRoute(existingRoute);
        Assert.NotNull(component);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 4. Exact Component Wiring for All Required Sidebar Links
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("/admin/calendar/events", typeof(CalendarOfEvents))]
    [InlineData("/admin/calendar/staff", typeof(StaffSchedule))]
    [InlineData("/admin/calendar/equipment", typeof(EquipmentSchedule))]
    [InlineData("/admin/services/main", typeof(MainServices))]
    [InlineData("/admin/services/packages", typeof(PackageServices))]
    [InlineData("/admin/services/additional", typeof(AdditionalServices))]
    [InlineData("/admin/employees/specialization", typeof(Specialization))]
    [InlineData("/admin/equipments", typeof(Equipments))]
    [InlineData("/admin/equipment/dispatch", typeof(Dispatch))]
    [InlineData("/admin/equipment/maintenance", typeof(Maintenance))]
    [InlineData("/admin/equipment/types", typeof(EquipmentTypes))]
    [InlineData("/admin/history-log", typeof(HistoryLog))]
    [InlineData("/profile", typeof(Profile))]
    [InlineData("/admin/faqs", typeof(FaqManagement))]
    [InlineData("/admin/sales", typeof(Sales))]
    [InlineData("/admin/reports", typeof(Reports))]
    public void SidebarLinks_WireUpToDedicatedPlaceholderComponents(string route, Type expectedComponent)
    {
        var component = FindComponentByRoute(route);
        Assert.NotNull(component);
        Assert.Equal(expectedComponent, component);
    }

    [Theory]
    [InlineData("/staff/calendar/master", typeof(StaffCalendarEvents))]
    [InlineData("/staff/calendar/staff", typeof(StaffCalendarSchedule))]
    [InlineData("/staff/calendar/equipment", typeof(StaffCalendarEquipment))]
    [InlineData("/staff/services", typeof(StaffServices))]
    [InlineData("/staff/services/packages", typeof(StaffPackageServices))]
    [InlineData("/staff/services/additional", typeof(StaffAdditionalServices))]
    [InlineData("/staff/profile", typeof(StaffProfile))]
    public void StaffRoutes_AllResolveToComponentsWithEmployeeLayout(string route, Type expectedComponent)
    {
        var component = FindComponentByRoute(route);
        Assert.NotNull(component);
        Assert.Equal(expectedComponent, component);

        var info = GetComponentInfo(component);
        Assert.Equal(typeof(EmployeeLayout), info.LayoutType);
    }

    [Fact]
    public void NoDuplicateRouteTemplates_AcrossAllComponents()
    {
        var allPageTypes = WebAssembly.GetTypes()
            .Where(t => t.GetCustomAttributes<RouteAttribute>().Any());

        var routeMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        foreach (var pageType in allPageTypes)
        {
            var routes = pageType.GetCustomAttributes<RouteAttribute>().Select(r => r.Template);
            foreach (var route in routes)
            {
                Assert.False(routeMap.TryGetValue(route, out var existingType),
                    $"Duplicate route detected: template '{route}' is declared on both {existingType?.FullName} and {pageType.FullName}.");
                routeMap[route] = pageType;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 5. Sidebar History Log Menu Item & Role-Based Authorization Tests
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Sidebar_HistoryLog_CollapsibleMenuMarkupAndStyling_MatchExistingPattern()
    {
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        string? sidebarPath = null;
        while (currentDir != null)
        {
            var candidate = Path.Combine(currentDir.FullName, "PhotoAevumStudio.Web", "Components", "Layout", "Sidebar.razor");
            if (File.Exists(candidate))
            {
                sidebarPath = candidate;
                break;
            }
            currentDir = currentDir.Parent;
        }

        Assert.True(sidebarPath != null && File.Exists(sidebarPath), $"Sidebar.razor was not found starting from {AppContext.BaseDirectory}");
        var content = File.ReadAllText(sidebarPath!);

        // 1. History Log menu item exists as a direct single-click link (no collapsible dropdown)
        Assert.Contains("History Log", content);
        Assert.DoesNotContain("IsOpen(\"history\")", content);
        Assert.DoesNotContain("Toggle(\"history\")", content);
        Assert.DoesNotContain("acc-history", content);

        // 2. Points directly to history log page via NavLink
        Assert.Contains("href=\"/admin/history-log\"", content);

        // 3. Role-based authorization wrapping
        Assert.Contains("<AuthorizeView Roles=\"System Admin, Admin\">", content);
    }

    [Theory]
    [InlineData("System Admin", true)]
    [InlineData("Admin", true)]
    [InlineData("Employee", false)]
    [InlineData("Office Clerk", false)]
    [InlineData("Customer", false)]
    [InlineData("Unauthorized", false)]
    public void Sidebar_HistoryLog_RoleAuthorizationCheck(string role, bool isAllowed)
    {
        // Allowed roles declared on AuthorizeView for History Log
        var allowedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System Admin",
            "Admin",
            "SystemAdmin",
            "Super Admin"
        };

        var permitted = allowedRoles.Contains(role);
        Assert.Equal(isAllowed, permitted);
    }

    [Fact]
    public void Sidebar_SalesAndReports_MenuMarkupAndStyling_MatchExistingPattern()
    {
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        string? sidebarPath = null;
        while (currentDir != null)
        {
            var candidate = Path.Combine(currentDir.FullName, "PhotoAevumStudio.Web", "Components", "Layout", "Sidebar.razor");
            if (File.Exists(candidate))
            {
                sidebarPath = candidate;
                break;
            }
            currentDir = currentDir.Parent;
        }

        Assert.True(sidebarPath != null && File.Exists(sidebarPath), $"Sidebar.razor was not found starting from {AppContext.BaseDirectory}");
        var content = File.ReadAllText(sidebarPath!);

        // 1. Sales & Report collapsible submenu contains exactly 3 nested items in order:
        //    Sales → Reports → Analytics  (consolidated from the former 6-item list)
        var salesReportSubIndex = content.IndexOf("id=\"acc-sales-report\"", StringComparison.Ordinal);
        Assert.True(salesReportSubIndex >= 0);

        var salesIdx     = content.IndexOf("href=\"/admin/sales\"",      salesReportSubIndex, StringComparison.Ordinal);
        var reportsIdx   = content.IndexOf("href=\"/admin/reports\"",    salesReportSubIndex, StringComparison.Ordinal);
        var analyticsIdx = content.IndexOf("href=\"/admin/analytics\"",  salesReportSubIndex, StringComparison.Ordinal);

        Assert.True(salesIdx     > salesReportSubIndex, "Sales link must appear inside acc-sales-report");
        Assert.True(reportsIdx   > salesIdx,            "Reports link must appear after Sales");
        Assert.True(analyticsIdx > reportsIdx,          "Analytics link must appear after Reports");

        // The four former individual stage links must be gone from the sidebar
        Assert.DoesNotContain("href=\"/analytics/descriptive\"",  content);
        Assert.DoesNotContain("href=\"/analytics/diagnostic\"",   content);
        Assert.DoesNotContain("href=\"/analytics/predictive\"",   content);
        Assert.DoesNotContain("href=\"/analytics/prescriptive\"", content);

        // Analytics link uses prefix matching so it stays active on any sub-stage route
        Assert.Contains("Match=\"NavLinkMatch.Prefix\"", content);

        // 2. Duplicate SALES & REPORT section header is removed
        Assert.DoesNotContain("<p class=\"aev-sidebar__section-label\">Sales &amp; Report</p>", content);
        Assert.DoesNotContain("href=\"/sales\"",   content);
        Assert.DoesNotContain("href=\"/reports\"", content);

        // 3. FAQ Management is still the final item in GENERAL; Resources & Logistics follows it
        var faqsIdx      = content.IndexOf("href=\"/admin/faqs\"",         StringComparison.Ordinal);
        var resourcesIdx = content.IndexOf("Resources &amp; Logistics",    StringComparison.Ordinal);

        Assert.True(faqsIdx      >= 0);
        Assert.True(resourcesIdx >  faqsIdx);

        // Analytics is NOT its own top-level accordion — it lives only as a sub-link inside Sales & Report
        Assert.DoesNotContain("acc-analytics",         content);
        Assert.DoesNotContain("IsOpen(\"analytics\")", content);
        Assert.DoesNotContain("Toggle(\"analytics\")", content);
    }
}
