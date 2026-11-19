using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
        var confirmPayment = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "ConfirmPayment.razor"));
        var paymentsPage = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Payments.razor"));

        foreach (var stage in new[] { "\"Preparing\"", "\"Ongoing\"", "\"Event Done\"", "\"Pending Payment\"" })
        {
            Assert.Contains(stage, staffDashboard);
        }

        Assert.Contains("Early (Preparing)", staffDashboard);
        Assert.Contains("Late (Preparing)", staffDashboard);
        Assert.Contains("Event Started", staffDashboard);
        Assert.Contains("Event Done", staffDashboard);
        Assert.Contains("Payment Verification (Cash)", staffDashboard);
        Assert.Contains("Payment Verification (QR:Paid)", staffDashboard);
        Assert.Contains("Payment Verification (QR:Pending Payment)", staffDashboard);
        Assert.Contains("SvgQRCode", staffDashboard);
        Assert.Contains("SimulateTeamLeaderQrPaidAsync", staffDashboard);
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
        var customerDashboard = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "CustomerDashboard.razor"));
        var myBookings = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "MyBookings.razor"));
        var adminDashboard = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "AdminDashboard.razor"));
        var livePanel = File.ReadAllText(Path.Combine(webRoot, "Components", "Shared", "LiveEventsOverviewPanel.razor"));
        var backendService = File.ReadAllText(Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Infrastructure", "Services", "ConsultationNegotiationService.cs"));
        var paymentsController = File.ReadAllText(Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Api", "Controllers", "PaymentsController.cs"));

        // 1. Sequential Team Leader controls, equipment checklist, Event Done settlement gate, Cash & QR settlement
        Assert.Contains("Early", staffDashboard);
        Assert.Contains("Late", staffDashboard);
        Assert.Contains("Preparing", staffDashboard);
        Assert.Contains("Event Started", staffDashboard);
        Assert.Contains("Event Done", staffDashboard);
        Assert.Contains("to unlock <strong>Settlement</strong>", staffDashboard);
        Assert.Contains("Preparation Equipment Checklist", staffDashboard);
        Assert.Contains("MarkAllEquipmentTakenAsync", staffDashboard);
        Assert.Contains("AddAdditionalEquipmentAsync", staffDashboard);
        Assert.Contains("Cash Received", staffDashboard);
        Assert.Contains("QuickCashReceivedAsync", staffDashboard);
        Assert.Contains("QR Payment", staffDashboard);

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
        Assert.Contains("@if (isClerk || teamLeaderEvents.Any())", staffDashboard);
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
}
