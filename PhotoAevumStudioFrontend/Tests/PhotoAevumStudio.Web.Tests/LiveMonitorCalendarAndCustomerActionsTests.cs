using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoAevumStudio.Api.Controllers;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Components.Shared;
using PhotoAevumStudio.Web.Models;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class LiveMonitorCalendarAndCustomerActionsTests
{
    private static string GetRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    [Fact]
    public void LiveEventsOverviewPanel_SelectOverviewEvents_ShowsAllExecuting_OrSingleClosestUpcoming()
    {
        var now = new DateTime(2026, 11, 19, 12, 0, 0);
        var activeEvents = new[]
        {
            new LiveEventOverviewDto
            {
                EventAppointmentId = "EVT-101",
                EventName = "Active Ongoing Event",
                EventStatus = "Ongoing",
                DisplayStage = "Ongoing",
                EventDate = new DateOnly(2026, 11, 19),
                EventTime = new TimeOnly(11, 0)
            },
            new LiveEventOverviewDto
            {
                EventAppointmentId = "EVT-102",
                EventName = "Active Ready Event",
                EventStatus = "Ready",
                DisplayStage = "Ready",
                EventDate = new DateOnly(2026, 11, 19),
                EventTime = new TimeOnly(12, 30)
            },
            new LiveEventOverviewDto
            {
                EventAppointmentId = "EVT-103",
                EventName = "Future Confirmed Event",
                EventStatus = "Confirmed",
                DisplayStage = "Confirmed",
                EventDate = new DateOnly(2026, 11, 22),
                EventTime = new TimeOnly(9, 0)
            }
        };

        var executingResult = LiveEventsOverviewPanel.SelectOverviewEvents(activeEvents, now);
        Assert.Equal(2, executingResult.Count);
        Assert.All(executingResult, e => Assert.True(LiveEventsOverviewPanel.IsExecutingEvent(e)));

        var onlyUpcoming = new[]
        {
            new LiveEventOverviewDto
            {
                EventAppointmentId = "EVT-DONE",
                EventName = "Finished Shoot",
                EventStatus = "Completed",
                DisplayStage = "Completed",
                EventDate = new DateOnly(2026, 11, 19),
                EventTime = new TimeOnly(8, 0)
            },
            new LiveEventOverviewDto
            {
                EventAppointmentId = "EVT-FAR",
                EventName = "Next Month Shoot",
                EventStatus = "Confirmed",
                DisplayStage = "Confirmed",
                EventDate = new DateOnly(2026, 12, 1),
                EventTime = new TimeOnly(10, 0)
            },
            new LiveEventOverviewDto
            {
                EventAppointmentId = "EVT-NEAR",
                EventName = "Tomorrow Shoot",
                EventStatus = "Confirmed",
                DisplayStage = "Confirmed",
                EventDate = new DateOnly(2026, 11, 20),
                EventTime = new TimeOnly(9, 30)
            }
        };

        var fallbackResult = LiveEventsOverviewPanel.SelectOverviewEvents(onlyUpcoming, now);
        var closest = Assert.Single(fallbackResult);
        Assert.Equal("EVT-NEAR", closest.EventAppointmentId);
    }

    [Fact]
    public void EventBookingModel_StatusProperties_NormalizeCompletedPendingPaymentAndAppointmentBooked()
    {
        var completedEvent = new EventBookingModel { Status = "Complete" };
        Assert.True(completedEvent.IsCompleted);
        Assert.Equal("Completed", completedEvent.DisplayStatus);

        var pendingPaymentConsult = new EventBookingModel { Status = "Pending Payment", IsConsultation = true };
        Assert.True(pendingPaymentConsult.IsPendingPayment);
        Assert.Equal("Pending Payment", pendingPaymentConsult.DisplayStatus);

        var bookedConsult = new EventBookingModel
        {
            Status = "Deal_Won",
            IsConsultation = true,
            LinkedEventAppointmentId = "EVT-900"
        };
        Assert.True(bookedConsult.IsAppointmentBooked);
        Assert.Equal("Appointment Booked", bookedConsult.DisplayStatus);
        Assert.Equal("EVT-900", bookedConsult.LinkedEventAppointmentId);
    }

    [Fact]
    public async Task CalendarEventChip_AndDetailDrawer_RenderStatusBadgesAndLinkedEventSection()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        await using var htmlRenderer = new HtmlRenderer(services, NullLoggerFactory.Instance);

        var bookedConsultation = new EventBookingModel
        {
            Id = "NEG-777",
            ClientName = "Elena Cruz",
            ServiceType = "Consultation: Wedding Photography",
            AssignedPhotographer = "Maria Santos",
            Date = new DateTime(2026, 11, 20),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(11, 0, 0),
            Status = "Appointment Booked",
            IsConsultation = true,
            LinkedEventAppointmentId = "EVT-777"
        };

        var chipHtml = await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CalendarEventChip.Booking)] = bookedConsultation
            });
            var output = await htmlRenderer.RenderComponentAsync<CalendarEventChip>(parameters);
            return output.ToHtmlString();
        });

        Assert.Contains("Appointment Booked", chipHtml);
        Assert.Contains("#EVT-777", chipHtml);

        var drawerHtml = await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CalendarDetailDrawer.Booking)] = bookedConsultation
            });
            var output = await htmlRenderer.RenderComponentAsync<CalendarDetailDrawer>(parameters);
            return output.ToHtmlString();
        });

        Assert.Contains("Appointment Booked", drawerHtml);
        Assert.Contains("Resulting Event Appointment", drawerHtml);
        Assert.Contains("#EVT-777", drawerHtml);
        Assert.Contains("Open Event Appointment", drawerHtml);
    }

    [Fact]
    public void CustomerPortal_Calendar_BookingNegotiations_AndControllers_ExposeCancelRescheduleAndPreserveXendit()
    {
        var root = GetRepositoryRoot();
        var webRoot = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web");

        var myBookings = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "MyBookings.razor"));
        var customerDashboard = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "CustomerDashboard.razor"));
        var actionModal = File.ReadAllText(Path.Combine(webRoot, "Components", "Shared", "CustomerAppointmentActionModal.razor"));
        var calendarPage = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "CalendarOfEvents.razor"));
        var bookingNegotiations = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "BookingNegotiations.razor"));

        Assert.Contains("CustomerAppointmentActionModal", myBookings);
        Assert.Contains("OpenEventActionModal", myBookings);
        Assert.Contains("OpenBookingActionModal", myBookings);

        Assert.Contains("CustomerAppointmentActionModal", customerDashboard);
        Assert.Contains("OpenEventActionModal", customerDashboard);
        Assert.Contains("OpenBookingActionModal", customerDashboard);

        Assert.Contains("Smart Office Clerk Assignment", actionModal);
        Assert.Contains("CustomerRescheduleAppointmentAsync", actionModal);
        Assert.Contains("CustomerCancelAppointmentAsync", actionModal);
        Assert.Contains("RescheduleBookingAsync", actionModal);
        Assert.Contains("CancelBookingAsync", actionModal);

        Assert.Contains("Appointment Booked", calendarPage);
        Assert.Contains("OpenLinkedEventAsync", calendarPage);
        Assert.Contains("Appointment Booked", bookingNegotiations);
        Assert.Contains("/admin/calendar/events?eventId=", bookingNegotiations);

        // Verify ScheduleController and PaymentsController endpoints + 8-param Xendit constructor compatibility
        Assert.NotNull(typeof(ScheduleController).GetMethod(nameof(ScheduleController.CancelBooking)));
        Assert.NotNull(typeof(ScheduleController).GetMethod(nameof(ScheduleController.RescheduleBooking)));
        Assert.NotNull(typeof(PaymentsController).GetMethod(nameof(PaymentsController.CustomerCancelAppointment)));
        Assert.NotNull(typeof(PaymentsController).GetMethod(nameof(PaymentsController.CustomerRescheduleAppointment)));
        Assert.NotNull(typeof(PaymentsController).GetMethod(nameof(PaymentsController.XenditWebhook)));
        Assert.NotNull(typeof(PaymentsController).GetMethod(nameof(PaymentsController.XenditQrWebhook)));

        var ctors = typeof(PaymentsController).GetConstructors();
        Assert.Contains(ctors, c => c.GetParameters().Length == 8);
    }
}
