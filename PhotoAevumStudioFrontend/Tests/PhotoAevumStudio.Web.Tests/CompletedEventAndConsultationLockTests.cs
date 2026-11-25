using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Models;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class CompletedEventAndConsultationLockTests
{
    private sealed class FixedAuthStateProvider : AuthenticationStateProvider
    {
        private readonly ClaimsPrincipal _user;
        public FixedAuthStateProvider(string role = "Admin", string studioId = "EMP-001")
        {
            _user = new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, studioId),
                    new Claim("studio_id", studioId),
                    new Claim(ClaimTypes.Role, role)
                },
                "TestAuth"));
        }

        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(_user));
    }

    private static async Task<string> RenderBookedEventModalHtmlAsync(EventBookingModel booking, bool canManageRoster)
    {
        var services = new ServiceCollection();
        var consultMock = new Mock<IConsultationClient>();
        consultMock.Setup(c => c.GetAppointmentByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResult<EventAppointmentDto>.Fail(ApiErrorKind.Server, "none"));

        services.AddSingleton(consultMock.Object);
        services.AddSingleton(Mock.Of<IEmployeeService>());
        services.AddSingleton(Mock.Of<IEquipmentService>());
        services.AddSingleton(Mock.Of<IScheduleService>());
        services.AddSingleton<AuthenticationStateProvider>(new FixedAuthStateProvider());
        services.AddSingleton(Mock.Of<IJSRuntime>());

        await using var sp = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                { nameof(BookedEventOverviewModal.Booking), booking },
                { nameof(BookedEventOverviewModal.CanManageRoster), canManageRoster }
            });
            var output = await renderer.RenderComponentAsync<BookedEventOverviewModal>(parameters);
            return output.ToHtmlString();
        });
    }

    [Fact]
    public async Task BookedEventOverviewModal_DisablesAllEditing_WhenEventIsCompleted()
    {
        var completedBooking = new EventBookingModel
        {
            Id = "EVT-9001",
            ClientName = "Completed Client",
            ServiceType = "Wedding Coverage",
            Status = "Completed",
            IsConsultation = false,
            Date = DateTime.Today.AddDays(-2),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(14, 0, 0),
            AssignedCrew = new List<EventCrewSummary>
            {
                new("EMP-1", "Alex Lead", "Photographer", true),
                new("EMP-2", "Sam Second", "Videographer", false)
            },
            AssignedEquipment = new List<EventEquipmentSummary>
            {
                new("EQ-1", "Sony A7S III", "Camera", "Available")
            }
        };

        var html = await RenderBookedEventModalHtmlAsync(completedBooking, canManageRoster: true);

        Assert.Contains("Event Completed", html);
        Assert.DoesNotContain("Save Roster Changes", html);
        Assert.DoesNotContain("Set Lead", html);
        Assert.DoesNotContain("+ Add Crew", html);
        Assert.DoesNotContain("Add Equipment", html);
    }

    [Fact]
    public async Task BookedEventOverviewModal_AllowsEditing_WhenEventIsConfirmedAndNotCompleted()
    {
        var activeBooking = new EventBookingModel
        {
            Id = "EVT-9002",
            ClientName = "Active Client",
            ServiceType = "Wedding Coverage",
            Status = "Confirmed",
            IsConsultation = false,
            Date = DateTime.Today.AddDays(5),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(14, 0, 0),
            AssignedCrew = new List<EventCrewSummary>
            {
                new("EMP-1", "Alex Lead", "Photographer", true),
                new("EMP-2", "Sam Second", "Videographer", false)
            },
            AssignedEquipment = new List<EventEquipmentSummary>
            {
                new("EQ-1", "Sony A7S III", "Camera", "Available")
            }
        };

        var html = await RenderBookedEventModalHtmlAsync(activeBooking, canManageRoster: true);

        Assert.DoesNotContain("Event Completed", html);
        Assert.Contains("Save Roster Changes", html);
        Assert.Contains("Set Lead", html);
        Assert.Contains("+ Add Crew", html);
        Assert.Contains("Add Equipment", html);
    }

    [Fact]
    public void MyBookings_LocksConsultationWhenNegotiationSucceeded_AndAllowsOnlyNonCompletedConnectedEvent()
    {
        var page = new MyBookings();
        var canModifyBooking = typeof(MyBookings).GetMethod("CanCustomerModifyBooking", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var hasNegSucceeded = typeof(MyBookings).GetMethod("HasNegotiationSucceeded", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var canModifyEvent = typeof(MyBookings).GetMethod("CanCustomerModifyEvent", BindingFlags.NonPublic | BindingFlags.Static)!;

        var succeededConsultation = new MyBooking(
            AppointmentId: 101,
            ServiceTitle: "Wedding Consultation",
            Date: DateOnly.FromDateTime(DateTime.Today.AddDays(2)),
            SlotStart: new TimeOnly(10, 0),
            SlotEnd: new TimeOnly(11, 0),
            Status: "Confirmed",
            TotalPrice: 0m,
            Platform: "GoogleMeet",
            MeetingLink: "https://meet.google.com/locked-meet",
            EventLocation: "Online",
            NegotiationId: "NEG-0101",
            NegotiationStatus: "Deal_Won",
            LinkedEventAppointmentId: "EVT-0101");

        var pendingConsultation = new MyBooking(
            AppointmentId: 102,
            ServiceTitle: "Portrait Consultation",
            Date: DateOnly.FromDateTime(DateTime.Today.AddDays(3)),
            SlotStart: new TimeOnly(11, 0),
            SlotEnd: new TimeOnly(12, 0),
            Status: "Confirmed",
            TotalPrice: 0m,
            Platform: "GoogleMeet",
            MeetingLink: "https://meet.google.com/open-meet",
            EventLocation: "Online",
            NegotiationId: "NEG-0102",
            NegotiationStatus: "Pending_Call",
            LinkedEventAppointmentId: null);

        var activeConnectedEvent = new EventAppointmentDto
        {
            EventAppointmentId = "EVT-0101",
            NegotiationId = "NEG-0101",
            EventName = "Connected Active Wedding",
            EventDate = DateOnly.FromDateTime(DateTime.Today.AddDays(10)),
            EventTime = new TimeOnly(13, 0),
            DurationMinutes = 180,
            EventStatus = "Confirmed",
            DownpaymentStatus = "Paid",
            FinalPaymentStatus = "Pending",
            TotalAmount = 50000m
        };

        var completedConnectedEvent = new EventAppointmentDto
        {
            EventAppointmentId = "EVT-0102",
            NegotiationId = "NEG-0102",
            EventName = "Connected Completed Event",
            EventDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-5)),
            EventTime = new TimeOnly(13, 0),
            DurationMinutes = 180,
            EventStatus = "Completed",
            DownpaymentStatus = "Paid",
            FinalPaymentStatus = "Paid",
            TotalAmount = 40000m
        };

        Assert.True((bool)hasNegSucceeded.Invoke(page, new object[] { succeededConsultation })!);
        Assert.False((bool)canModifyBooking.Invoke(page, new object[] { succeededConsultation })!);

        Assert.False((bool)hasNegSucceeded.Invoke(page, new object[] { pendingConsultation })!);
        Assert.True((bool)canModifyBooking.Invoke(page, new object[] { pendingConsultation })!);

        Assert.True((bool)canModifyEvent.Invoke(null, new object[] { activeConnectedEvent })!);
        Assert.False((bool)canModifyEvent.Invoke(null, new object[] { completedConnectedEvent })!);
    }
}
