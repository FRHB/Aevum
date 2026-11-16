using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using PhotoAevumStudio.Api.Controllers;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Components.Shared;
using PhotoAevumStudio.Web.Models;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class CalendarTabularAndLeaveManagementTests
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

    [Fact]
    public async Task LeaveRequestModal_RendersDropdownAndDescriptionFields()
    {
        var empMock = new Mock<IEmployeeService>();
        var services = new ServiceCollection();
        services.AddSingleton(empMock.Object);

        await using var sp = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                { nameof(LeaveRequestModal.IsOpen), true },
                { nameof(LeaveRequestModal.EmployeeId), "EMP-001" }
            });
            var output = await renderer.RenderComponentAsync<LeaveRequestModal>(parameters);
            return output.ToHtmlString();
        });

        Assert.Contains("Request for Leave", html);
        Assert.Contains("Leave Type", html);
        Assert.Contains("Sick Leave", html);
        Assert.Contains("Vacation Leave", html);
        Assert.Contains("Maternity Leave", html);
        Assert.Contains("Paternity Leave", html);
        Assert.Contains("Description", html);
        Assert.Contains("Submit Request", html);
    }

    [Fact]
    public async Task AdjustDaysOffModal_RendersAllWeekdaysAndPreselectedDaysOff()
    {
        var empMock = new Mock<IEmployeeService>();
        var services = new ServiceCollection();
        services.AddSingleton(empMock.Object);

        await using var sp = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);

        var emp = new EmployeeViewModel
        {
            EmployeeId = "EMP-002",
            FirstName = "Elena",
            LastName = "Rostova",
            DaysOff = "Sunday, Monday"
        };

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                { nameof(AdjustDaysOffModal.IsOpen), true },
                { nameof(AdjustDaysOffModal.Employee), emp }
            });
            var output = await renderer.RenderComponentAsync<AdjustDaysOffModal>(parameters);
            return output.ToHtmlString();
        });

        Assert.Contains("Adjust Staff Days Off", html);
        Assert.Contains("Elena Rostova", html);
        Assert.Contains("EMP-002", html);
        foreach (var day in new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" })
        {
            Assert.Contains(day, html);
        }
        Assert.Contains("Monday, Sunday", html);
        Assert.Contains("Save Days Off", html);
    }

    [Fact]
    public async Task CalendarOfEvents_Routes_And_CalendarEventChip_RenderCorrectly()
    {
        var routes = typeof(CalendarOfEvents)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.RouteAttribute), inherit: false)
            .Cast<Microsoft.AspNetCore.Components.RouteAttribute>()
            .Select(r => r.Template)
            .ToArray();
        Assert.Contains("/admin/calendar/events", routes);

        var staffCalRoutes = typeof(StaffCalendarEvents)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.RouteAttribute), inherit: false)
            .Cast<Microsoft.AspNetCore.Components.RouteAttribute>()
            .Select(r => r.Template)
            .ToArray();
        Assert.Contains("/staff/calendar/events", staffCalRoutes);

        var services = new ServiceCollection();
        await using var sp = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);

        var booking = new EventBookingModel
        {
            Id = "EVT-777",
            ClientName = "Studio Test Client",
            ServiceType = "Wedding Shoot",
            AssignedPhotographer = "Alex Rivera",
            Status = "Confirmed",
            Date = DateTime.Today,
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(13, 0, 0)
        };

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                { nameof(CalendarEventChip.Booking), booking }
            });
            var output = await renderer.RenderComponentAsync<CalendarEventChip>(parameters);
            return output.ToHtmlString();
        });

        Assert.Contains("Studio Test Client", html);
        Assert.Contains("Alex Rivera", html);
        Assert.Contains("week-event-chip", html);
    }

    [Fact]
    public void CalendarOfEvents_ImplementsTabularGanttDayView_WeekJsInteropHighlight_AndMonthRouting()
    {
        var root = GetRepositoryRoot();
        var webRoot = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web");

        var calendarRazor = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "CalendarOfEvents.razor"));
        var calendarCss = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "CalendarOfEvents.razor.css"));
        var appRazor = File.ReadAllText(Path.Combine(webRoot, "Components", "App.razor"));

        // 1. Day Filter: tabular Gantt / time-blocked timetable
        Assert.Contains("day-gantt-table", calendarRazor);
        Assert.Contains("gantt-time-block", calendarRazor);
        Assert.Contains("data-event-id=\"@b.Id\"", calendarRazor);
        Assert.Contains("GetDayTimelineBounds", calendarRazor);

        // 2. Week Filter: route to Day + 10-second JS Interop highlight
        Assert.Contains("HandleWeekEventClick", calendarRazor);
        Assert.Contains("aevumCalendar.highlightEventRow", calendarRazor);
        Assert.Contains("10000", calendarRazor);
        Assert.Contains("window.aevumCalendar", appRazor);
        Assert.Contains("highlightEventRow", appRazor);
        Assert.Contains("event-row-highlighted", calendarCss);
        Assert.Contains("event-row-highlighted", appRazor);

        // 3. Month Filter: day cell routes to Day tabular timetable
        Assert.Contains("NavigateToDayTimetable(day)", calendarRazor);

        // 4. Collapsible Staff Schedule section under Admin Calendar of Events
        Assert.Contains("Staff Schedule", calendarRazor);
        Assert.Contains("ToggleStaffScheduleSection", calendarRazor);
        Assert.Contains("Upcoming &amp; Ongoing Leaves", calendarRazor);
        Assert.Contains("Staff Days Off Roster", calendarRazor);
        Assert.Contains("ApproveLeaveRequestAsync", calendarRazor);
        Assert.Contains("ConfirmRejectLeaveAsync", calendarRazor);
        Assert.Contains("AdjustDaysOffModal", calendarRazor);
    }

    [Fact]
    public void EmployeeLeaveRequestUI_And_AdminDashboardPanel_AreWired()
    {
        var root = GetRepositoryRoot();
        var pagesDir = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages");

        var staffCal = File.ReadAllText(Path.Combine(pagesDir, "StaffCalendarEvents.razor"));
        var staffDash = File.ReadAllText(Path.Combine(pagesDir, "StaffDashboard.razor"));
        var adminDash = File.ReadAllText(Path.Combine(pagesDir, "AdminDashboard.razor"));

        Assert.Contains("Request for Leave", staffCal);
        Assert.Contains("LeaveRequestModal", staffCal);
        Assert.Contains("Request for Leave", staffDash);

        Assert.Contains("Staff Leave Requests", adminDash);
        Assert.Contains("HandleApproveLeaveAsync", adminDash);
        Assert.Contains("HandleRejectLeaveAsync", adminDash);
    }

    [Fact]
    public void BackendControllers_And_BookingEngine_EnforceLeaveBlockingAndDaysOffUpdates()
    {
        // 1. ManagementController DaysOff endpoint
        var daysOffMethod = typeof(ManagementController).GetMethod(nameof(ManagementController.UpdateEmployeeDaysOff));
        Assert.NotNull(daysOffMethod);
        Assert.Contains(daysOffMethod!.GetCustomAttributes(typeof(HttpPutAttribute), inherit: false).Cast<HttpPutAttribute>(),
            a => a.Template == "employees/{employeeId}/days-off");
        Assert.Contains(daysOffMethod.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false).Cast<AuthorizeAttribute>(),
            a => a.Policy == "ClerkOrAdmin");

        // 2. LeavesController endpoints
        Assert.NotNull(typeof(LeavesController).GetMethod(nameof(LeavesController.RequestLeave)));
        Assert.NotNull(typeof(LeavesController).GetMethod(nameof(LeavesController.GetPendingLeaves)));
        Assert.NotNull(typeof(LeavesController).GetMethod(nameof(LeavesController.GetAllLeaves)));
        Assert.NotNull(typeof(LeavesController).GetMethod(nameof(LeavesController.ApproveLeave)));
        Assert.NotNull(typeof(LeavesController).GetMethod(nameof(LeavesController.RejectLeave)));

        // 3. Booking engine leave & day-off enforcement across backend & frontend
        var root = GetRepositoryRoot();
        var negotiationSvc = File.ReadAllText(Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Infrastructure", "Services", "ConsultationNegotiationService.cs"));
        var negotiationsUi = File.ReadAllText(Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "BookingNegotiations.razor"));
        var scheduleSvc = File.ReadAllText(Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Services", "ScheduleService.cs"));
        var procsSql = File.ReadAllText(Path.Combine(root, "PhotoAevumStudioBackend", "database", "PhotoAevumStudioStoredProcedures.sql"));

        Assert.Contains("ValidateEmployeesAvailableAsync", negotiationSvc);
        Assert.Contains("dbo.EmployeeLeaves", negotiationSvc);
        Assert.Contains("GetEmployeeBlockReason", negotiationsUi);
        Assert.Contains("dbo.EmployeeLeaves", scheduleSvc);
        Assert.Contains("@ConsultationDate", procsSql);
        Assert.Contains("dbo.EmployeeLeaves", procsSql);
    }

    private sealed class TestAuthStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(user));
    }
}
