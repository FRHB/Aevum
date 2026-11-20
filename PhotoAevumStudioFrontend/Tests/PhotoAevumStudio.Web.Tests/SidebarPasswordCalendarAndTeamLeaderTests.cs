using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoAevumStudio.Api.Controllers;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Web.Components.Shared;
using PhotoAevumStudio.Web.Models;
using PhotoAevumStudio.Web.Services;
using QRCoder;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class SidebarPasswordCalendarAndTeamLeaderTests
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

    private static async Task<string> RenderChecklistHtmlAsync(string? password)
    {
        var services = new ServiceCollection();
        await using var sp = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                { nameof(PasswordRequirementsChecklist.Password), password }
            });
            var output = await renderer.RenderComponentAsync<PasswordRequirementsChecklist>(parameters);
            return output.ToHtmlString();
        });
    }

    [Fact]
    public async Task PasswordRequirementsChecklist_RendersLiveRedAndGreenIndicatorsWhileTyping()
    {
        // Empty input -> all 3 unmet (red)
        var emptyHtml = await RenderChecklistHtmlAsync("");
        Assert.DoesNotContain("is-met", emptyHtml);
        Assert.Contains("is-unmet", emptyHtml);
        Assert.Contains("8", emptyHtml);
        Assert.Contains("30", emptyHtml);
        Assert.Contains("@, #, $, %, &amp;", emptyHtml);

        // Partial input ("Pass123") -> number is met (green), length and special unmet (red)
        var partialHtml = await RenderChecklistHtmlAsync("Pass123");
        Assert.Equal(1, partialHtml.Split("is-met").Length - 1);
        Assert.Equal(2, partialHtml.Split("is-unmet").Length - 1);

        // Forbidden special char ("Pass123!") -> special char remains unmet (red)
        var invalidSpecialHtml = await RenderChecklistHtmlAsync("Pass123!");
        Assert.Equal(2, invalidSpecialHtml.Split("is-met").Length - 1);
        Assert.Equal(1, invalidSpecialHtml.Split("is-unmet").Length - 1);

        // Compliant input ("Pass123@") -> all 3 met (green)
        var validHtml = await RenderChecklistHtmlAsync("Pass123@");
        Assert.Equal(3, validHtml.Split("is-met").Length - 1);
        Assert.DoesNotContain("is-unmet", validHtml);

        // Over 30 characters -> length flips back to unmet (red), number + special stay green
        var tooLongHtml = await RenderChecklistHtmlAsync(new string('a', 29) + "1@");
        Assert.Equal(2, tooLongHtml.Split("is-met").Length - 1);
        Assert.Equal(1, tooLongHtml.Split("is-unmet").Length - 1);
    }

    [Fact]
    public void PasswordChecklist_IsWiredAcrossAccountCreationAndPasswordChangeViews()
    {
        var root = GetRepositoryRoot();
        var webRoot = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web");

        var expectedFiles = new[]
        {
            Path.Combine(webRoot, "Components", "Layout", "AuthPortalModal.razor"),
            Path.Combine(webRoot, "Components", "Pages", "ChangePassword.razor"),
            Path.Combine(webRoot, "Components", "Pages", "ForgotPassword.razor"),
            Path.Combine(webRoot, "Components", "Pages", "Profile.razor"),
            Path.Combine(webRoot, "Components", "Pages", "CustomerProfile.razor"),
            Path.Combine(webRoot, "Components", "Pages", "StaffProfile.razor")
        };

        foreach (var file in expectedFiles)
        {
            Assert.True(File.Exists(file), $"Missing UI file: {file}");
            var text = File.ReadAllText(file);
            Assert.Contains("PasswordRequirementsChecklist", text);
        }

        var adminWizard = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "AddAdminWizard.razor"));
        Assert.Contains("PasswordPolicyHelper.AllowedSpecialCharacters", adminWizard);
    }

    [Fact]
    public void Sidebar_And_Styles_ProvideThreeLineMinimizeKeepingLogo_And_LogoutConfirmationModal()
    {
        var root = GetRepositoryRoot();
        var webRoot = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web");
        var sidebarText = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "Sidebar.razor"));
        var cssText = File.ReadAllText(Path.Combine(webRoot, "wwwroot", "app.css"));

        // 3-line minimize toggle & logo retention
        Assert.Contains("ToggleMinimize", sidebarText);
        Assert.Contains("is-minimized", sidebarText);
        Assert.Contains("images/AevumLogo.jpg", sidebarText);
        Assert.Contains("aev-sidebar__minimize-btn", sidebarText);
        Assert.Contains(".aev-sidebar.is-minimized", cssText);
        Assert.Contains("--sidebar-w: 76px", cssText);

        // Logout confirmation modal ("Are you sure you want to logout?" Yes/No)
        Assert.Contains("Are you sure you want to logout?", sidebarText);
        Assert.Contains("Yes", sidebarText);
        Assert.Contains("No", sidebarText);
        Assert.Contains("RequestLogout", sidebarText);
        Assert.Contains("HandleLogoutAsync", sidebarText);
        Assert.Contains("CancelLogout", sidebarText);
    }

    [Fact]
    public void CalendarOfEvents_RemovesAgendaFilter_WhileStaffAndEquipmentRetainAgenda()
    {
        var root = GetRepositoryRoot();
        var pagesDir = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages");

        var calendarText = File.ReadAllText(Path.Combine(pagesDir, "CalendarOfEvents.razor"));
        var staffText = File.ReadAllText(Path.Combine(pagesDir, "StaffSchedule.razor"));
        var equipmentText = File.ReadAllText(Path.Combine(pagesDir, "EquipmentSchedule.razor"));

        Assert.DoesNotContain("ScheduleViewMode.Agenda", calendarText);
        Assert.DoesNotContain(">Agenda<", calendarText);
        Assert.Contains("ScheduleViewMode.Agenda", staffText);
        Assert.Contains("ScheduleViewMode.Agenda", equipmentText);
    }

    [Fact]
    public void ScheduleService_InvalidateCache_And_UpsertConsultationContract()
    {
        var scheduleService = new ScheduleService(configuration: null);
        scheduleService.AddEventBooking(new EventBookingModel
        {
            StudioSpaceId = "studio-a",
            StudioSpaceName = "Studio A (Main Hall)",
            ClientName = "Contract Test Client",
            ServiceType = "Full Studio Session",
            Date = DateTime.Today.AddDays(10),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(13, 0, 0),
            Status = "Pending"
        });

        var countBefore = scheduleService.GetEventBookings().Count;
        Assert.True(countBefore > 0);
        scheduleService.InvalidateCache();
        Assert.Equal(countBefore, scheduleService.GetEventBookings().Count);

        // Verify backend upsert & frontend cache invalidation on deal actions
        var root = GetRepositoryRoot();
        var backendService = File.ReadAllText(Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Infrastructure", "Services", "ConsultationNegotiationService.cs"));
        var negRazor = File.ReadAllText(Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "BookingNegotiations.razor"));

        Assert.Contains("RefreshStudioScheduleCache", negRazor);
        Assert.Contains("ScheduleService.InvalidateCache()", negRazor);
        Assert.Contains("WHERE NegotiationID = @NegId", backendService);
        Assert.Contains("updateEvtSql", backendService);
    }

    [Fact]
    public void StaffDashboard_TeamLeaderOngoingEvent_SupportsAllStagesCashPendingVerificationAndLocalQrSvg()
    {
        var root = GetRepositoryRoot();
        var webRoot = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web");
        var staffDashboard = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "StaffDashboard.razor"));
        var monitorPage = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "TeamLeadMonitor.razor"));
        var pillRazor = File.ReadAllText(Path.Combine(webRoot, "Components", "Shared", "TeamLeaderActiveEventPill.razor"));
        var confirmPayment = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "ConfirmPayment.razor"));
        var paymentsPage = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Payments.razor"));

        // Top Pill Box handles the full Team Leader workflow stages, Cash & QR settlement, and QR SVG rendering
        foreach (var stage in new[] { "\"Preparation\"", "\"Ongoing\"", "\"Post Event\"" })
        {
            Assert.Contains(stage, pillRazor);
        }

        Assert.Contains("1. Preparation", pillRazor);
        Assert.Contains("2. Dispatched", pillRazor);
        Assert.Contains("3. On Going", pillRazor);
        Assert.Contains("4. Post Event", pillRazor);
        Assert.Contains("Payment Verification (Cash)", pillRazor);
        Assert.Contains("Payment Verification (QR:Pending Payment)", pillRazor);
        Assert.Contains("SvgQRCode", pillRazor);
        Assert.Contains("SimulateQrPaidAsync", pillRazor);

        // Duplicate legacy container has been removed from StaffDashboard and TeamLeadMonitor
        Assert.DoesNotContain("View in Top Pill", staffDashboard);
        Assert.DoesNotContain("View in Top Pill", monitorPage);
        Assert.DoesNotContain("Sequential Execution Controls", staffDashboard);
        Assert.DoesNotContain("Preparation Equipment Checklist", staffDashboard);
        Assert.DoesNotContain("Preparation Equipment Checklist", monitorPage);

        Assert.Contains("IsFinalPayment", confirmPayment);
        Assert.Contains("\"Completed\"", confirmPayment);
        Assert.Contains("\"Completed\"", paymentsPage);

        // Verify runtime QRCoder SVG generation for Team Leader final QR checkout URL
        using var qrGen = new QRCodeGenerator();
        using var qrData = qrGen.CreateQrCode("https://photoaevum.studio/confirm-payment/EVT-2026-FINAL", QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(qrData).GetGraphic(5);
        Assert.Contains("<svg", svg);
        Assert.Contains("</svg>", svg);

        // Verify PaymentsController exposes Team Leader endpoints
        var getMethod = typeof(PaymentsController).GetMethod(nameof(PaymentsController.GetTeamLeaderEvents));
        var updateMethod = typeof(PaymentsController).GetMethod(nameof(PaymentsController.UpdateTeamLeaderEventStatus));
        Assert.NotNull(getMethod);
        Assert.NotNull(updateMethod);
        Assert.Contains(getMethod!.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false).Cast<HttpGetAttribute>(),
            a => a.Template == "team-leader-events");
        Assert.Contains(updateMethod!.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Cast<HttpPostAttribute>(),
            a => a.Template == "team-leader-events/{appointmentId}/status");

        // Verify DTO final payment properties across backend Application & frontend Web models
        var tlDto = new PhotoAevumStudio.Web.Models.TeamLeaderEventDto
        {
            EventAppointmentId = "EVT-2026",
            FinalPaymentMethod = "Xendit_QR",
            FinalPaymentStatus = "Payment Verification (QR:Pending Payment)",
            FinalPaymentReferenceId = "EVT-2026-FINAL"
        };
        Assert.Equal("EVT-2026-FINAL", tlDto.FinalPaymentReferenceId);
        Assert.True(new PhotoAevumStudio.Web.Models.QrPaymentInfoDto { IsFinalPayment = true }.IsFinalPayment);
        Assert.True(new PhotoAevumStudio.Application.Interfaces.QrPaymentInfoDto { IsFinalPayment = true }.IsFinalPayment);
    }

    [Fact]
    public void EventExecutionWorkflow_TeamLeader_PreparationDispatchOngoingPostEvent_AndCashVerificationFlow()
    {
        var root = GetRepositoryRoot();
        var webRoot = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web");
        var staffDashboard = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "StaffDashboard.razor"));
        var pillRazor = File.ReadAllText(Path.Combine(webRoot, "Components", "Shared", "TeamLeaderActiveEventPill.razor"));
        var customerDashboard = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "CustomerDashboard.razor"));
        var myBookings = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "MyBookings.razor"));
        var adminDashboard = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "AdminDashboard.razor"));
        var livePanel = File.ReadAllText(Path.Combine(webRoot, "Components", "Shared", "LiveEventsOverviewPanel.razor"));
        var backendService = File.ReadAllText(Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Infrastructure", "Services", "ConsultationNegotiationService.cs"));
        var paymentsController = File.ReadAllText(Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Api", "Controllers", "PaymentsController.cs"));

        // 1. Single Top Pill Box workflow: Equipment Verification, Add More Equipment, Dispatched, Event Start, Post Event, Cash & QR
        Assert.Contains("Equipment Dispatch &amp; Verification", pillRazor);
        Assert.Contains("Verify Equipment", pillRazor);
        Assert.Contains("Add More Equipment", pillRazor);
        Assert.Contains("ConfirmDispatchedAsync", pillRazor);
        Assert.Contains("Event Start", pillRazor);
        Assert.Contains("Post Event", pillRazor);
        Assert.Contains("Cash", pillRazor);
        Assert.Contains("QR Payment", pillRazor);
        Assert.Contains("Crew Monitor (View Only)", pillRazor);

        // 2. Customer Dashboard & MyBookings immediately mark cash pending verification & QR paid as Completed
        Assert.Contains("GetCustomerDisplayStatus", customerDashboard);
        Assert.Contains("GetCustomerEventDisplayStatus", myBookings);
        Assert.Contains("Payment Verification (Cash)", customerDashboard);
        Assert.Contains("Payment Verification (QR:Paid)", customerDashboard);
        Assert.Contains("\"Completed\"", customerDashboard);
        Assert.Contains("\"Completed\"", myBookings);

        // 3. Office Clerk & Admin Live Event Monitor standardized payment labels (no legacy strings)
        Assert.Contains("LiveEventsOverviewPanel", adminDashboard);
        Assert.Contains("LiveEventsOverviewPanel", staffDashboard);
        Assert.Contains("Verify Cash", livePanel);
        Assert.Contains("VerifyCashAsync", livePanel);
        Assert.Contains("Payment Verification (Cash)", livePanel);
        Assert.Contains("Payment Verification (QR:Paid)", livePanel);
        Assert.Contains("Payment Verification (QR:Pending Payment)", livePanel);
        Assert.DoesNotContain("Pending Cash Verification", livePanel);
        Assert.DoesNotContain("Paid Cash Pending Verification", livePanel);

        // 4. Dual email confirmation hooks (Team Leader + Customer) on final QR payment completion
        Assert.Contains("TeamLeaderQrPaymentPaidNotification", backendService);
        Assert.Contains("CustomerFinalQrPaymentConfirmation", backendService);
        Assert.Contains("NotifyTeamLeaderFinalQrPaidAsync", paymentsController);
        Assert.Contains("HandlePostPaymentConfirmationAsync", paymentsController);

        // 5. API Controller endpoints & authorization
        var markTaken = typeof(PaymentsController).GetMethod(nameof(PaymentsController.MarkEquipmentTaken));
        var addEquip = typeof(PaymentsController).GetMethod(nameof(PaymentsController.AddAdditionalEquipment));
        var liveOverview = typeof(PaymentsController).GetMethod(nameof(PaymentsController.GetLiveEventsOverview));
        var verifyCash = typeof(PaymentsController).GetMethod(nameof(PaymentsController.VerifyCashSettlement));

        Assert.NotNull(markTaken);
        Assert.NotNull(addEquip);
        Assert.NotNull(liveOverview);
        Assert.NotNull(verifyCash);
    }

    [Fact]
    public void RegularEmployee_RestrictsEquipmentSettlementTimesheetAndNavToAllowedPages()
    {
        var root = GetRepositoryRoot();
        var webRoot = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web");
        var sidebar = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "Sidebar.razor"));
        var staffDashboard = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "StaffDashboard.razor"));
        var timesheet = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Timesheet.razor"));
        var featuredServicesPage = Path.Combine(webRoot, "Components", "Pages", "StaffFeaturedServices.razor");

        Assert.True(File.Exists(featuredServicesPage));
        Assert.Contains("/staff/featured-services", sidebar);
        Assert.Contains("My Schedule", sidebar);
        Assert.Contains("/portfolio", sidebar);
        Assert.Contains("@if (isClerk || teamLeaderEvents.Any(e => e.IsTeamLeader))", staffDashboard);
        Assert.Contains("My Schedule (Read-Only)", staffDashboard);
        Assert.Contains("string.Equals(h.EmployeeStudioId, employeeId, StringComparison.OrdinalIgnoreCase)", timesheet);

        // Backend TimesheetController RBAC checks
        var summaryMethod = typeof(TimesheetController).GetMethod(nameof(TimesheetController.GetTodaySummary));
        Assert.NotNull(summaryMethod);
        var authAttr = summaryMethod!.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .FirstOrDefault();
        Assert.NotNull(authAttr);
        Assert.Equal("ClerkOrAdmin", authAttr!.Policy);
    }

    [Fact]
    public void EmployeeRoleHelper_IsEventAssignedToEmployee_MatchesByCrewIdsAndAssignedCrew()
    {
        var booking = new EventBookingModel
        {
            Id = "EVT-901",
            AssignedPhotographer = "Unassigned",
            AssignedCrewIds = new List<string> { "EMP-010", "EMP-020" },
            AssignedCrew = new List<EventCrewSummary>
            {
                new("EMP-010", "Juan Dela Cruz", "Photographer", true),
                new("EMP-020", "Maria Clara", "Videographer", false)
            }
        };

        // Matches by EmployeeID even if display name differs or AssignedPhotographer is "Unassigned"
        Assert.True(EmployeeRoleHelper.IsEventAssignedToEmployee(booking, "Juan D. Cruz", "EMP-010"));
        Assert.True(EmployeeRoleHelper.IsEventAssignedToEmployee(booking, "", "EMP-020"));
        Assert.False(EmployeeRoleHelper.IsEventAssignedToEmployee(booking, "Other Person", "EMP-999"));
    }

    [Fact]
    public void TeamLeadMonitor_And_PaymentCalendarSync_AreWiredCorrectly()
    {
        var root = GetRepositoryRoot();
        var webRoot = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web");
        var pagesDir = Path.Combine(webRoot, "Components", "Pages");

        var monitorPage = File.ReadAllText(Path.Combine(pagesDir, "TeamLeadMonitor.razor"));
        var sidebar = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "Sidebar.razor"));
        var payments = File.ReadAllText(Path.Combine(pagesDir, "Payments.razor"));
        var confirmPayment = File.ReadAllText(Path.Combine(pagesDir, "ConfirmPayment.razor"));
        var simulateCheckout = File.ReadAllText(Path.Combine(pagesDir, "SimulateCheckout.razor"));
        var myBookings = File.ReadAllText(Path.Combine(pagesDir, "MyBookings.razor"));
        var customerDashboard = File.ReadAllText(Path.Combine(pagesDir, "CustomerDashboard.razor"));
        var staffDashboard = File.ReadAllText(Path.Combine(pagesDir, "StaffDashboard.razor"));

        Assert.Contains("@page \"/staff/team-lead-monitor\"", monitorPage);
        Assert.Contains("TeamLeaderMonitorSelector.SelectUpcomingAndOngoing", monitorPage);
        Assert.Contains("TeamLeaderActiveEventPill", monitorPage);
        Assert.Contains("Switch to Event", monitorPage);

        Assert.Contains("TeamLeaderMonitorSelector.SelectUpcomingAndOngoing", staffDashboard);
        Assert.Contains("TeamLeaderActiveEventPill", staffDashboard);
        Assert.Contains("/staff/team-lead-monitor", staffDashboard);

        Assert.Contains("/staff/team-lead-monitor", sidebar);
        Assert.Contains("_teamLeadEventCount > 0", sidebar);
        Assert.Contains(".Count(e => e.IsTeamLeader)", sidebar);

        Assert.Contains("ScheduleService.InvalidateCache()", payments);
        Assert.Contains("ScheduleService.InvalidateCache()", confirmPayment);
        Assert.Contains("ScheduleService.InvalidateCache()", simulateCheckout);
        Assert.Contains("ScheduleService.InvalidateCache()", myBookings);
        Assert.Contains("ScheduleService.InvalidateCache()", customerDashboard);

        // Frontend TeamLeaderMonitorSelector filters upcoming + ongoing and tracks 5 process steps
        var now = new DateTime(2026, 11, 20, 10, 0, 0);
        var today = DateOnly.FromDateTime(now.Date);
        var filtered = PhotoAevumStudio.Web.Models.TeamLeaderMonitorSelector.SelectUpcomingAndOngoing(new[]
        {
            new PhotoAevumStudio.Web.Models.TeamLeaderEventDto { EventAppointmentId = "EVT-1", EventStatus = "Pending Payment", DownpaymentStatus = "Pending", EventDate = today },
            new PhotoAevumStudio.Web.Models.TeamLeaderEventDto { EventAppointmentId = "EVT-2", EventStatus = "Completed", DownpaymentStatus = "Paid", EventDate = today },
            new PhotoAevumStudio.Web.Models.TeamLeaderEventDto { EventAppointmentId = "EVT-3", EventStatus = "Confirmed", DownpaymentStatus = "Paid", EventDate = today.AddDays(1), EventTime = new TimeOnly(10, 0) },
            new PhotoAevumStudio.Web.Models.TeamLeaderEventDto { EventAppointmentId = "EVT-4", EventStatus = "Ongoing", DownpaymentStatus = "Paid", EventDate = today, EventTime = new TimeOnly(9, 0) }
        }, now);

        Assert.Equal(2, filtered.Count);
        Assert.Equal("EVT-4", filtered[0].EventAppointmentId);
        Assert.Equal("EVT-3", filtered[1].EventAppointmentId);
        Assert.Equal(5, PhotoAevumStudio.Web.Models.TeamLeaderMonitorSelector.GetProcessSteps(filtered[0]).Count);
    }

    [Fact]
    public void TeamLeaderTopPillContainer_And_CrossPlatformLiveSync_AreWiredCorrectly()
    {
        var root = GetRepositoryRoot();
        var webRoot = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web");
        var sharedDir = Path.Combine(webRoot, "Components", "Shared");
        var pagesDir = Path.Combine(webRoot, "Components", "Pages");

        var pillRazor = File.ReadAllText(Path.Combine(sharedDir, "TeamLeaderActiveEventPill.razor"));
        var monitorPage = File.ReadAllText(Path.Combine(pagesDir, "TeamLeadMonitor.razor"));
        var staffDashboard = File.ReadAllText(Path.Combine(pagesDir, "StaffDashboard.razor"));
        var livePanel = File.ReadAllText(Path.Combine(sharedDir, "LiveEventsOverviewPanel.razor"));
        var adminDashboard = File.ReadAllText(Path.Combine(pagesDir, "AdminDashboard.razor"));
        var customerDashboard = File.ReadAllText(Path.Combine(pagesDir, "CustomerDashboard.razor"));
        var myBookings = File.ReadAllText(Path.Combine(pagesDir, "MyBookings.razor"));
        var calendar = File.ReadAllText(Path.Combine(pagesDir, "CalendarOfEvents.razor"));
        var hubClient = File.ReadAllText(Path.Combine(webRoot, "Services", "PaymentHubClient.cs"));

        // Top pill box container renders Customer, Event, Location, Services, Time, Equipment Verification, Add More Equipment, bottom-right Dispatched, Event Start, Post Event, Cash & QR
        Assert.Contains("Customer", pillRazor);
        Assert.Contains("Location", pillRazor);
        Assert.Contains("Services", pillRazor);
        Assert.Contains("Equipment Dispatch", pillRazor);
        Assert.Contains("Verify Equipment", pillRazor);
        Assert.Contains("Add More Equipment", pillRazor);
        Assert.Contains("Dispatched", pillRazor);
        Assert.Contains("Event Start", pillRazor);
        Assert.Contains("Post Event", pillRazor);
        Assert.Contains("Cash", pillRazor);
        Assert.Contains("QR Payment", pillRazor);

        // Team Leader monitor & StaffDashboard render the top pill container and Start Event button
        Assert.Contains("TeamLeaderActiveEventPill", monitorPage);
        Assert.Contains("Start Event", monitorPage);
        Assert.Contains("TeamLeaderActiveEventPill", staffDashboard);
        Assert.Contains("Start Event", staffDashboard);

        // Admin & Clerk Live Monitor has Confirm Cash Verification and SignalR live sync
        Assert.Contains("Confirm Cash Verification", livePanel);
        Assert.Contains("EnsureLiveMonitorStartedAsync", livePanel);
        Assert.Contains("OnEventStatusChanged", livePanel);

        // Real-time SignalR sync across Admin, Calendar, and Customer portals
        Assert.Contains("EnsureLiveMonitorStartedAsync", hubClient);
        Assert.Contains("EnsureCustomerMonitorStartedAsync", hubClient);
        Assert.Contains("OnEventStatusChanged", adminDashboard);
        Assert.Contains("OnEventStatusChanged", calendar);
        Assert.Contains("EnsureCustomerMonitorStartedAsync", customerDashboard);
        Assert.Contains("EnsureCustomerMonitorStartedAsync", myBookings);

        // StaffDashboard Upcoming Events row exposes Team Leader Start / Monitor controls and surfaces API errors
        Assert.Contains("IsTeamLeaderForBooking", staffDashboard);
        Assert.Contains("★ Team Leader", staffDashboard);
        Assert.Contains("RefreshTeamLeaderEventsInternalAsync(surfaceError: true)", staffDashboard);

        var sidebar = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "Sidebar.razor"));
        Assert.Contains("HandleSidebarEventStatusChangedAsync", sidebar);
    }

    private sealed class StubNavManager : NavigationManager
    {
        public StubNavManager() => Initialize("https://localhost/", "https://localhost/staff/team-lead-monitor");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private static async Task<string> RenderTopPillHtmlAsync(IReadOnlyList<PhotoAevumStudio.Web.Models.TeamLeaderEventDto> events)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IConsultationClient>().Object);
        services.AddSingleton(new Mock<IScheduleService>().Object);
        services.AddSingleton(new Mock<IEquipmentService>().Object);
        services.AddSingleton<NavigationManager>(new StubNavManager());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Development");
        services.AddSingleton(envMock.Object);

        await using var sp = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                { nameof(TeamLeaderActiveEventPill.ActiveEvents), events },
                { nameof(TeamLeaderActiveEventPill.EmployeeId), "EMP-010" },
                { nameof(TeamLeaderActiveEventPill.EmployeeName), "Lexx Martinez" }
            });
            var output = await renderer.RenderComponentAsync<TeamLeaderActiveEventPill>(parameters);
            return output.ToHtmlString();
        });
    }

    [Fact]
    public async Task TeamLeaderActiveEventPill_RendersSingleActiveEvent_AndEnforcesCrewReadOnlyMode()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var events = new List<PhotoAevumStudio.Web.Models.TeamLeaderEventDto>
        {
            new()
            {
                EventAppointmentId = "EVT-0015",
                EventName = "Dyan Brad - Photoshoot Event",
                CustomerName = "Dyan Brad",
                EventDate = today,
                EventTime = new TimeOnly(15, 0),
                EventStatus = "Preparing",
                DownpaymentStatus = "Paid",
                FinalBalanceAmount = 8500m,
                IsTeamLeader = true,
                AssignedEquipment = new List<PhotoAevumStudio.Web.Models.AssignedEquipmentDto>
                {
                    new() { EventEquipmentId = 1, EquipmentId = "EQ-1", EquipmentName = "Sony A7 IV", EquipmentType = "Camera", IsTaken = false }
                }
            },
            new()
            {
                EventAppointmentId = "EVT-0016",
                EventName = "Second Scheduled Event",
                CustomerName = "Second Client",
                EventDate = today.AddDays(1),
                EventTime = new TimeOnly(10, 0),
                EventStatus = "Confirmed",
                DownpaymentStatus = "Paid",
                FinalBalanceAmount = 5000m,
                IsTeamLeader = true
            }
        };

        // 1. Team Leader view: renders only 1 pill body (EVT-0015), provides header switcher for EVT-0016, and shows edit controls
        var leaderHtml = await RenderTopPillHtmlAsync(events);
        Assert.Contains("Dyan Brad - Photoshoot Event", leaderHtml);
        Assert.Contains("Verify Equipment", leaderHtml);
        Assert.Contains("Verify All", leaderHtml);
        Assert.Contains("Add More Equipment", leaderHtml);
        Assert.Contains("Dispatched", leaderHtml);
        Assert.Contains("EVT-0016", leaderHtml); // Switcher pill in header
        Assert.DoesNotContain("Second Client", leaderHtml); // Second event details are NOT stacked as a second box

        // 2. Non-Team-Leader crew view: monitor-only pill box without any edit buttons
        events[0].IsTeamLeader = false;
        var crewHtml = await RenderTopPillHtmlAsync(events);
        Assert.Contains("Crew Monitor (View Only)", crewHtml);
        Assert.Contains("Dyan Brad - Photoshoot Event", crewHtml);
        Assert.Contains("Sony A7 IV", crewHtml);
        Assert.DoesNotContain("Verify Equipment", crewHtml);
        Assert.DoesNotContain("Verify All", crewHtml);
        Assert.DoesNotContain("Add More Equipment", crewHtml);
        Assert.DoesNotContain("✓ Dispatched", crewHtml);
    }

    [Fact]
    public async Task TeamLeaderActiveEventPill_EnforcesCrewVerification_WhenTwoOrMoreEmployeesAssigned_AndSupportsReplacementRequests()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        // 1. Solo Team Leader (1 employee): multi-crew verification is NOT required
        var soloEvent = new List<PhotoAevumStudio.Web.Models.TeamLeaderEventDto>
        {
            new()
            {
                EventAppointmentId = "EVT-SOLO",
                EventName = "Solo Portrait Shoot",
                CustomerName = "Alex Rivera",
                EventDate = today,
                EventTime = new TimeOnly(10, 0),
                EventStatus = "Preparing",
                DownpaymentStatus = "Paid",
                IsTeamLeader = true,
                AssignedEmployees = new List<PhotoAevumStudio.Web.Models.AssignedEmployeeDto>
                {
                    new() { EmployeeId = "EMP-010", EmployeeName = "Lexx Martinez", Specialization = "Photographer", IsTeamLeader = true, IsVerified = false }
                },
                AssignedEquipment = new List<PhotoAevumStudio.Web.Models.AssignedEquipmentDto>
                {
                    new() { EventEquipmentId = 1, EquipmentId = "EQ-1", EquipmentName = "Sony A7 IV", EquipmentType = "Camera", IsTaken = true }
                }
            }
        };

        var soloHtml = await RenderTopPillHtmlAsync(soloEvent);
        Assert.Contains("Solo Team Lead", soloHtml);
        Assert.DoesNotContain("Verify All Crew", soloHtml);
        Assert.DoesNotContain("Verify 1 remaining crew member", soloHtml);

        // 2. Multi-employee event (2+ employees), unverified crew -> renders Crew Verification, Request New Employee, and gates Dispatched
        var multiUnverified = new List<PhotoAevumStudio.Web.Models.TeamLeaderEventDto>
        {
            new()
            {
                EventAppointmentId = "EVT-MULTI",
                EventName = "Grand Wedding",
                CustomerName = "Dyan Brad",
                EventDate = today,
                EventTime = new TimeOnly(14, 0),
                EventStatus = "Preparing",
                DownpaymentStatus = "Paid",
                IsTeamLeader = true,
                AssignedEmployees = new List<PhotoAevumStudio.Web.Models.AssignedEmployeeDto>
                {
                    new() { EmployeeId = "EMP-010", EmployeeName = "Lexx Martinez", Specialization = "Photographer", IsTeamLeader = true, IsVerified = true, DutyStatus = "On Duty" },
                    new() { EmployeeId = "EMP-020", EmployeeName = "Aria Cruz", Specialization = "Videographer", IsTeamLeader = false, IsVerified = false, DutyStatus = "Assigned" }
                },
                AssignedEquipment = new List<PhotoAevumStudio.Web.Models.AssignedEquipmentDto>
                {
                    new() { EventEquipmentId = 1, EquipmentId = "EQ-1", EquipmentName = "Sony A7 IV", EquipmentType = "Camera", IsTaken = true }
                }
            }
        };

        var unverifiedHtml = await RenderTopPillHtmlAsync(multiUnverified);
        Assert.Contains("Crew Verification &amp; Replacement Request", unverifiedHtml);
        Assert.Contains("1 / 2 Crew On Duty", unverifiedHtml);
        Assert.Contains("Verify Crew", unverifiedHtml);
        Assert.Contains("Verify All Crew", unverifiedHtml);
        Assert.Contains("Request New Employee", unverifiedHtml);
        Assert.Contains("Verify 1 remaining crew member(s) on duty to enable Dispatched confirmation", unverifiedHtml);

        // 3. Pending crew replacement request -> alerts and keeps Dispatched gated
        multiUnverified[0].CrewReplacementRequests.Add(new PhotoAevumStudio.Web.Models.EventCrewReplacementRequestDto
        {
            RequestId = 7,
            EventAppointmentId = "EVT-MULTI",
            AbsentEmployeeId = "EMP-020",
            AbsentEmployeeName = "Aria Cruz",
            Reason = "Did not report for duty",
            Status = "Pending"
        });

        var pendingReqHtml = await RenderTopPillHtmlAsync(multiUnverified);
        Assert.Contains("Replacement Requested", pendingReqHtml);
        Assert.Contains("Awaiting 1 crew replacement assignment(s)", pendingReqHtml);

        // 4. All 2+ crew verified & no pending requests -> Dispatched gate cleared
        multiUnverified[0].CrewReplacementRequests.Clear();
        multiUnverified[0].AssignedEmployees[1].IsVerified = true;
        multiUnverified[0].AssignedEmployees[1].DutyStatus = "On Duty";

        var verifiedHtml = await RenderTopPillHtmlAsync(multiUnverified);
        Assert.Contains("2 / 2 Crew On Duty", verifiedHtml);
        Assert.DoesNotContain("remaining crew member", verifiedHtml);
    }

    [Fact]
    public void SuddenAbsence_CrewReplacement_And_StaffPortalSync_AreWiredAcrossControllersAndPages()
    {
        var root = GetRepositoryRoot();
        var webRoot = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web");

        var livePanel = File.ReadAllText(Path.Combine(webRoot, "Components", "Shared", "LiveEventsOverviewPanel.razor"));
        var overviewModal = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "BookedEventOverviewModal.razor"));
        var timesheet = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Timesheet.razor"));
        var staffDashboard = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "StaffDashboard.razor"));
        var monitorPage = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "TeamLeadMonitor.razor"));
        var sidebar = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "Sidebar.razor"));

        Assert.Contains("Manage Crew / Team Lead", livePanel);
        Assert.Contains("FulfillReplacementAsync", livePanel);
        Assert.Contains("Assign Replacement", livePanel);

        Assert.Contains("MarkReplacedCrewAsSuddenAbsence", overviewModal.Replace("_markReplacedCrewAsSuddenAbsence", "MarkReplacedCrewAsSuddenAbsence"));
        Assert.Contains("Sudden Absence / Emergency-Sick", overviewModal);

        Assert.Contains("Mark Absent", timesheet);
        Assert.Contains("MarkSuddenAbsenceAsync", timesheet);

        Assert.Contains("EnsureStaffMonitorStartedAsync", staffDashboard);
        Assert.Contains("LoadDisplayedEventsFromSchedule", staffDashboard);
        Assert.Contains("EnsureStaffMonitorStartedAsync", monitorPage);
        Assert.Contains("EnsureStaffMonitorStartedAsync", sidebar);

        var verifyCrewMethod = typeof(PaymentsController).GetMethod(nameof(PaymentsController.VerifyCrewDuty));
        var reqReplaceMethod = typeof(PaymentsController).GetMethod(nameof(PaymentsController.RequestCrewReplacement));
        var fulfillMethod = typeof(ConsultationsController).GetMethod(nameof(ConsultationsController.FulfillCrewReplacement));
        var suddenAbsMethod = typeof(LeavesController).GetMethod(nameof(LeavesController.MarkSuddenAbsence));

        Assert.NotNull(verifyCrewMethod);
        Assert.NotNull(reqReplaceMethod);
        Assert.NotNull(fulfillMethod);
        Assert.NotNull(suddenAbsMethod);
    }
}
