using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Models;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class NegotiationReadOnlyAndCalendarTypeFilterTests
{
    private sealed class TestableBookingNegotiations : BookingNegotiations
    {
        public void RenderToBuilder(RenderTreeBuilder builder) => BuildRenderTree(builder);
        public void SetLoadingForTest(bool loading)
        {
            var f = typeof(BookingNegotiations).GetField("_loading", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            f?.SetValue(this, loading);
        }
    }

    private sealed class TestableCalendarOfEvents : CalendarOfEvents
    {
        public void RenderToBuilder(RenderTreeBuilder builder) => BuildRenderTree(builder);
        public void SetLoadingForTest(bool loading)
        {
            var f = typeof(CalendarOfEvents).GetField("_isLoadingBookings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            f?.SetValue(this, loading);
        }
    }

    private sealed class RenderFragmentHost : ComponentBase
    {
        [Parameter, EditorRequired] public RenderFragment Content { get; set; } = default!;
        protected override void BuildRenderTree(RenderTreeBuilder builder) => Content(builder);
    }

    private static async Task<string> RenderFragmentHtmlAsync(ServiceProvider sp, RenderFragment fragment)
    {
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                { nameof(RenderFragmentHost.Content), fragment }
            });
            var output = await renderer.RenderComponentAsync<RenderFragmentHost>(parameters);
            return output.ToHtmlString();
        });
    }

    private sealed class RecordingNavManager : NavigationManager
    {
        public string? LastNavigatedUri { get; private set; }
        public int NavigateCount { get; private set; }

        public RecordingNavManager(string initialUri = "https://localhost/admin/calendar/events")
        {
            Initialize("https://localhost/", initialUri);
        }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            LastNavigatedUri = ToAbsoluteUri(uri).ToString();
            NavigateCount++;
            Uri = LastNavigatedUri;
        }
    }

    private sealed class StubAuthStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(user));
    }

    private static ClaimsPrincipal CreateAdminPrincipal() =>
        new(new ClaimsIdentity(
            new[]
            {
                new Claim("studio_id", "ADM-0001"),
                new Claim("full_name", "Admin Tester"),
                new Claim(ClaimTypes.Role, AppRoles.Admin)
            },
            authenticationType: "TestAuth"));

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

    private static ServiceProvider BuildBookingNegotiationsServiceProvider(
        Mock<IConsultationClient> consultationMock,
        Mock<IScheduleService>? scheduleMock = null,
        RecordingNavManager? nav = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(consultationMock.Object);

        var empMock = new Mock<IEmployeeService>();
        empMock.Setup(e => e.GetEmployeesAsync()).ReturnsAsync(new List<EmployeeViewModel>());
        empMock.Setup(e => e.GetAllLeavesAsync(It.IsAny<string?>())).ReturnsAsync(new List<LeaveItemViewModel>());
        services.AddSingleton(empMock.Object);

        var eqMock = new Mock<IEquipmentService>();
        eqMock.Setup(e => e.GetEquipmentsAsync()).ReturnsAsync(new List<EquipmentItemModel>());
        services.AddSingleton(eqMock.Object);

        services.AddSingleton(new Mock<IUserService>().Object);

        var schedApiMock = new Mock<IBookScheduleClient>();
        schedApiMock.Setup(s => s.GetServicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResult<IReadOnlyList<ScheduleServiceOption>>.Ok(Array.Empty<ScheduleServiceOption>()));
        services.AddSingleton(schedApiMock.Object);

        var schedSvc = scheduleMock ?? new Mock<IScheduleService>();
        if (scheduleMock == null)
        {
            schedSvc.Setup(s => s.GetEventBookings(It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
                .Returns(new List<EventBookingModel>());
            schedSvc.Setup(s => s.GetEquipmentReservations())
                .Returns(new List<EquipmentReservationModel>());
        }
        services.AddSingleton(schedSvc.Object);

        services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider(CreateAdminPrincipal()));
        services.AddSingleton<NavigationManager>(nav ?? new RecordingNavManager("https://localhost/admin/negotiations"));
        services.AddSingleton(Mock.Of<IJSRuntime>());

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildCalendarServiceProvider(
        IScheduleService scheduleService,
        RecordingNavManager? nav = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(scheduleService);

        var eqMock = new Mock<IEquipmentService>();
        eqMock.Setup(e => e.GetEquipmentsAsync()).ReturnsAsync(new List<EquipmentItemModel>());
        services.AddSingleton(eqMock.Object);

        var empMock = new Mock<IEmployeeService>();
        empMock.Setup(e => e.GetEmployeesAsync()).ReturnsAsync(new List<EmployeeViewModel>());
        empMock.Setup(e => e.GetAllLeavesAsync(It.IsAny<string?>())).ReturnsAsync(new List<LeaveItemViewModel>());
        services.AddSingleton(empMock.Object);

        var userMock = new Mock<IUserService>();
        userMock.Setup(u => u.GetMainServices()).Returns(new List<MainServiceItem>());
        services.AddSingleton(userMock.Object);

        services.AddSingleton(new Mock<IConsultationClient>().Object);
        services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider(CreateAdminPrincipal()));
        services.AddSingleton(Mock.Of<IJSRuntime>());
        services.AddSingleton(new PaymentHubClient(
            new ConfigurationBuilder().Build(),
            Mock.Of<ITokenStore>(),
            NullLogger<PaymentHubClient>.Instance));
        services.AddSingleton<NavigationManager>(nav ?? new RecordingNavManager());

        return services.BuildServiceProvider();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. BookingNegotiations Read-Only & "Booked Event" Modal Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Pending_Call", null, false, false)]
    [InlineData("Rescheduled", "", false, false)]
    [InlineData("Pending_Call", "EVT-101", true, true)]
    [InlineData("Pending Payment", "EVT-102", true, true)]
    [InlineData("Pending_Payment", null, true, true)]
    [InlineData("Appointment Booked", "EVT-103", true, true)]
    [InlineData("Deal_Won", null, true, true)]
    [InlineData("Deal_Lost", null, false, true)]
    [InlineData("Deal_Lost", "EVT-EXPIRED", false, true)]
    [InlineData("Cancelled", "EVT-CANCELLED", false, true)]
    public void BookingNegotiations_StatusAndEventConnection_ComputesBookedLockAndReadOnlyState(
        string status,
        string? eventAppointmentId,
        bool expectedEventBookedLocked,
        bool expectedReadOnly)
    {
        var neg = new ConsultationNegotiationDto
        {
            NegotiationId = "NEG-CHK-001",
            NegotiationStatus = status,
            EventAppointmentId = eventAppointmentId
        };

        Assert.Equal(expectedEventBookedLocked, BookingNegotiations.IsEventBookedLocked(neg));
        Assert.Equal(expectedReadOnly, BookingNegotiations.IsNegotiationReadOnly(neg));
    }

    [Fact]
    public async Task BookingNegotiations_RendersGreyedOutReadOnlyRows_WithBookedEventButtons_AndPreservesEditableRows()
    {
        var negotiations = new List<ConsultationNegotiationDto>
        {
            new()
            {
                NegotiationId = "NEG-OPEN-01",
                CustomerId = "CUST-01",
                CustomerName = "Active Inquirer",
                CustomerEmail = "active@example.com",
                EmployeeId = "CLK-01",
                EmployeeName = "Clara Reyes",
                ConsultationDate = new DateOnly(2026, 11, 24),
                ConsultationTime = new TimeOnly(10, 0),
                NegotiationStatus = "Pending_Call",
                EventAppointmentId = null
            },
            new()
            {
                NegotiationId = "NEG-PEND-02",
                CustomerId = "CUST-02",
                CustomerName = "Pending Deposit Client",
                CustomerEmail = "pending@example.com",
                EmployeeId = "CLK-01",
                EmployeeName = "Clara Reyes",
                ConsultationDate = new DateOnly(2026, 11, 25),
                ConsultationTime = new TimeOnly(11, 0),
                NegotiationStatus = "Pending Payment",
                EventAppointmentId = "EVT-202"
            },
            new()
            {
                NegotiationId = "NEG-BOOK-03",
                CustomerId = "CUST-03",
                CustomerName = "Booked Wedding Client",
                CustomerEmail = "booked@example.com",
                EmployeeId = "CLK-01",
                EmployeeName = "Clara Reyes",
                ConsultationDate = new DateOnly(2026, 11, 26),
                ConsultationTime = new TimeOnly(14, 0),
                NegotiationStatus = "Appointment Booked",
                EventAppointmentId = "EVT-303"
            },
            new()
            {
                NegotiationId = "NEG-WON-04",
                CustomerId = "CUST-04",
                CustomerName = "Deal Won Client",
                CustomerEmail = "won@example.com",
                EmployeeId = "CLK-01",
                EmployeeName = "Clara Reyes",
                ConsultationDate = new DateOnly(2026, 11, 27),
                ConsultationTime = new TimeOnly(15, 0),
                NegotiationStatus = "Deal_Won",
                EventAppointmentId = "EVT-404"
            },
            new()
            {
                NegotiationId = "NEG-LOST-05",
                CustomerId = "CUST-05",
                CustomerName = "Declined Client",
                CustomerEmail = "lost@example.com",
                EmployeeId = "CLK-01",
                EmployeeName = "Clara Reyes",
                ConsultationDate = new DateOnly(2026, 11, 28),
                ConsultationTime = new TimeOnly(16, 0),
                NegotiationStatus = "Deal_Lost",
                EventAppointmentId = null
            }
        };

        var consultationMock = new Mock<IConsultationClient>();
        consultationMock.Setup(c => c.GetNegotiationsAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResult<IReadOnlyList<ConsultationNegotiationDto>>.Ok(negotiations));
        consultationMock.Setup(c => c.GetNegotiationByIdAsync("NEG-BOOK-03", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResult<ConsultationNegotiationDto>.Ok(negotiations[2]));
        consultationMock.Setup(c => c.GetAppointmentByIdAsync("EVT-303", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResult<EventAppointmentDto>.Ok(new EventAppointmentDto
            {
                EventAppointmentId = "EVT-303",
                NegotiationId = "NEG-BOOK-03",
                CustomerName = "Booked Wedding Client",
                CustomerEmail = "booked@example.com",
                EventName = "Wedding Gala",
                EventLocation = "Studio A (Main Hall)",
                EventDate = new DateOnly(2026, 11, 29),
                EventTime = new TimeOnly(10, 0),
                EventEndTime = new TimeOnly(14, 0),
                EventStatus = "Confirmed",
                TotalAmount = 42000m
            }));

        await using var sp = BuildBookingNegotiationsServiceProvider(consultationMock);
        var page = new TestableBookingNegotiations();
        page.SetTestDependencies(consultationMock.Object, sp.GetRequiredService<IScheduleService>());
        page.Negotiations = negotiations;
        page.SetLoadingForTest(false);

        var html = await RenderFragmentHtmlAsync(sp, page.RenderToBuilder);

        // Locked/booked rows are greyed out, display explicit Read-Only badge, and offer active Booked Event buttons + calendar link
        Assert.Contains("bg-slate-100 text-slate-500 border-slate-300", html);
        Assert.Contains("Read-Only • Event Booked", html);
        Assert.Contains("Booked Event (#EVT-202)", html);
        Assert.Contains("Booked Event (#EVT-303)", html);
        Assert.Contains("Booked Event (#EVT-404)", html);
        Assert.Contains("/admin/calendar/events?eventId=EVT-303", html);
        Assert.Contains("View Details →", html);

        // Lost row is read-only without a Booked Event button
        Assert.Contains("Read-Only • Deal Lost", html);

        // Editable row remains active
        Assert.Contains("data-negotiation-id=\"NEG-OPEN-01\"", html);
        Assert.Contains("data-readonly=\"false\"", html);
        Assert.Contains("Open Details →", html);

        // Floating drawer for editable negotiation shows outcome buttons
        await page.OpenFloatingPanelAsync(negotiations[0]);
        var editableDrawerHtml = await RenderFragmentHtmlAsync(sp, page.RenderToBuilder);
        Assert.Contains("data-drawer-readonly=\"false\"", editableDrawerHtml);
        Assert.Contains("Failed with Deal", editableDrawerHtml);
        Assert.Contains("Reschedule", editableDrawerHtml);
        Assert.Contains("Success with Deal", editableDrawerHtml);

        // Floating drawer for booked/locked negotiation is greyed out, hides outcome buttons, and exposes Booked Event button + modal
        await page.OpenFloatingPanelAsync(negotiations[2]);
        var lockedDrawerHtml = await RenderFragmentHtmlAsync(sp, page.RenderToBuilder);
        Assert.Contains("data-drawer-readonly=\"true\"", lockedDrawerHtml);
        Assert.Contains("Read-Only • Event Booked", lockedDrawerHtml);
        Assert.Contains("Open in Calendar →", lockedDrawerHtml);
        Assert.DoesNotContain("Failed with Deal", lockedDrawerHtml);
        Assert.DoesNotContain(">Reschedule<", lockedDrawerHtml);
        Assert.DoesNotContain("Success with Deal", lockedDrawerHtml);

        // Clicking Booked Event renders BookedEventOverviewModal directly inside BookingNegotiations
        await page.OpenBookedEventModalAsync(negotiations[2]);
        var withModalHtml = await RenderFragmentHtmlAsync(sp, page.RenderToBuilder);
        Assert.Contains("booked-modal-backdrop", withModalHtml);
        Assert.Contains("#EVT-303", withModalHtml);
        Assert.Contains("Wedding Gala", withModalHtml);

        // Floating drawer for lost negotiation is read-only and hides outcome buttons
        page.CloseBookedEventModal();
        await page.OpenFloatingPanelAsync(negotiations[4]);
        var lostDrawerHtml = await RenderFragmentHtmlAsync(sp, page.RenderToBuilder);
        Assert.Contains("data-drawer-readonly=\"true\"", lostDrawerHtml);
        Assert.Contains("Read-Only • Deal Lost", lostDrawerHtml);
        Assert.DoesNotContain("Failed with Deal", lostDrawerHtml);
        Assert.DoesNotContain("Success with Deal", lostDrawerHtml);
    }

    [Fact]
    public async Task BookingNegotiations_ReadOnlyNegotiation_BlocksMutationActions_AndResolvesBookedEventFromAllSources()
    {
        var consultationMock = new Mock<IConsultationClient>();
        var scheduleMock = new Mock<IScheduleService>();

        var lockedNegFromCache = new ConsultationNegotiationDto
        {
            NegotiationId = "NEG-CACHE-01",
            EventAppointmentId = "EVT-CACHE-01",
            CustomerName = "Cached Bride",
            CustomerEmail = "bride@example.com",
            NegotiationStatus = "Appointment Booked",
            ConsultationDate = new DateOnly(2026, 12, 1),
            ConsultationTime = new TimeOnly(10, 0)
        };

        var lockedNegFromSchedule = new ConsultationNegotiationDto
        {
            NegotiationId = "NEG-SCHED-02",
            EventAppointmentId = "EVT-SCHED-02",
            CustomerName = "Schedule Groom",
            CustomerEmail = "groom@example.com",
            NegotiationStatus = "Deal_Won",
            ConsultationDate = new DateOnly(2026, 12, 2),
            ConsultationTime = new TimeOnly(13, 0)
        };

        var lockedNegFromApi = new ConsultationNegotiationDto
        {
            NegotiationId = "NEG-API-03",
            EventAppointmentId = "EVT-API-03",
            CustomerName = "API Corporate Client",
            CustomerEmail = "corp@example.com",
            CustomerPhone = "09171234567",
            NegotiationStatus = "Pending Payment",
            ConsultationDate = new DateOnly(2026, 12, 3),
            ConsultationTime = new TimeOnly(15, 0)
        };

        var scheduleBooking = new EventBookingModel
        {
            Id = "EVT-SCHED-02",
            LinkedNegotiationId = "NEG-SCHED-02",
            ClientName = "Schedule Groom",
            ServiceType = "Full Day Nuptial",
            EventLocation = "Grand Conservatory",
            Date = new DateTime(2026, 12, 2),
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(14, 0, 0),
            Status = "Confirmed",
            EstimatedPrice = 32000m,
            AssignedCrew = new List<EventCrewSummary> { new("EMP-10", "Alex Rivera", "Photographer", true) }
        };

        scheduleMock.Setup(s => s.GetEventBookings(It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .Returns(new List<EventBookingModel> { scheduleBooking });
        scheduleMock.Setup(s => s.GetEquipmentReservations())
            .Returns(new List<EquipmentReservationModel>());

        var apiAppointment = new EventAppointmentDto
        {
            EventAppointmentId = "EVT-API-03",
            NegotiationId = "NEG-API-03",
            CustomerName = "API Corporate Client",
            CustomerEmail = "corp@example.com",
            EventName = "Executive Summit Coverage",
            EventLocation = "Makati Ballroom",
            EventDate = new DateOnly(2026, 12, 3),
            EventTime = new TimeOnly(9, 0),
            EventEndTime = new TimeOnly(12, 0),
            EventStatus = "Pending Payment",
            TotalAmount = 25000m,
            SelectedServices = new List<EventServiceLineDto>
            {
                new() { ServicesId = "SRV-1", ServiceName = "Corporate Coverage", Quantity = 1, RateType = "Per Session" }
            },
            AssignedEmployees = new List<AssignedEmployeeDto>
            {
                new() { EmployeeId = "EMP-20", EmployeeName = "Maya Cruz", Specialization = "Lead Photographer", IsTeamLeader = true }
            },
            AssignedEquipment = new List<AssignedEquipmentDto>
            {
                new() { EquipmentId = "EQ-90", EquipmentName = "Sony A7R V", EquipmentType = "Camera", EquipmentStatus = "Active" }
            }
        };

        consultationMock.Setup(c => c.GetNegotiationByIdAsync("NEG-API-03", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResult<ConsultationNegotiationDto>.Ok(lockedNegFromApi));
        consultationMock.Setup(c => c.GetAppointmentByIdAsync("EVT-API-03", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResult<EventAppointmentDto>.Ok(apiAppointment));
        consultationMock.Setup(c => c.GetAppointmentByIdAsync("EVT-CACHE-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResult<EventAppointmentDto>.Fail(ApiErrorKind.Server, "Not needed"));
        consultationMock.Setup(c => c.GetAppointmentByIdAsync("EVT-SCHED-02", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResult<EventAppointmentDto>.Fail(ApiErrorKind.Server, "Not needed"));

        var page = new BookingNegotiations();
        page.SetTestDependencies(consultationMock.Object, scheduleMock.Object);
        page.Negotiations = new List<ConsultationNegotiationDto> { lockedNegFromCache, lockedNegFromSchedule, lockedNegFromApi };
        page.StudioBookings = new List<EventBookingModel>
        {
            new()
            {
                Id = "EVT-CACHE-01",
                LinkedNegotiationId = "NEG-CACHE-01",
                ClientName = "Cached Bride",
                ServiceType = "Bridal Portrait",
                Date = new DateTime(2026, 12, 1),
                StartTime = new TimeSpan(10, 0, 0),
                EndTime = new TimeSpan(12, 0, 0),
                Status = "Appointment Booked"
            }
        };

        // 1. Defensive outcome mutation block on read-only negotiation
        await page.OpenFloatingPanelAsync(lockedNegFromApi);
        Assert.True(page.IsPanelOpen);
        await page.MarkDealOutcomeAsync(lockedNegFromApi.NegotiationId, "Deal_Lost");
        await page.OpenRescheduleModal();
        page.OpenEventWizard();

        consultationMock.Verify(
            c => c.UpdateNegotiationOutcomeAsync(It.IsAny<string>(), It.IsAny<UpdateNegotiationOutcomeRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.False(page.IsRescheduleModalOpen);
        Assert.False(page.IsWizardOpen);

        // 2. Hydration Path A: resolved from _studioBookings
        await page.OpenBookedEventModalAsync(lockedNegFromCache);
        Assert.NotNull(page.BookedEventModalBooking);
        Assert.Equal("EVT-CACHE-01", page.BookedEventModalBooking!.Id);
        Assert.Equal("Cached Bride", page.BookedEventModalBooking.ClientName);

        // 3. Hydration Path B: resolved from ScheduleService when not in current _studioBookings
        page.StudioBookings = new List<EventBookingModel>();
        await page.OpenBookedEventModalAsync(lockedNegFromSchedule);
        Assert.NotNull(page.BookedEventModalBooking);
        Assert.Equal("EVT-SCHED-02", page.BookedEventModalBooking!.Id);
        Assert.Equal("Grand Conservatory", page.BookedEventModalBooking.EventLocation);
        scheduleMock.Verify(s => s.InvalidateCache(), Times.AtLeastOnce);

        // 4. Hydration Path C: resolved from ConsultationApi.GetAppointmentByIdAsync when absent in ScheduleService
        scheduleMock.Setup(s => s.GetEventBookings(It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .Returns(new List<EventBookingModel>());
        page.StudioBookings = new List<EventBookingModel>();

        await page.OpenBookedEventModalAsync(lockedNegFromApi);
        var hydratedFromApi = page.BookedEventModalBooking;
        Assert.NotNull(hydratedFromApi);
        Assert.Equal("EVT-API-03", hydratedFromApi!.Id);
        Assert.False(hydratedFromApi.IsConsultation);
        Assert.Equal("API Corporate Client", hydratedFromApi.ClientName);
        Assert.Equal("Makati Ballroom", hydratedFromApi.EventLocation);
        Assert.Equal(25000m, hydratedFromApi.EstimatedPrice);
        Assert.Contains("Corporate Coverage", hydratedFromApi.AssignedServices);
        Assert.Contains(hydratedFromApi.AssignedCrew, c => c.EmployeeId == "EMP-20" && c.IsTeamLeader);
        Assert.Contains(hydratedFromApi.AssignedEquipment, e => e.EquipmentId == "EQ-90");

        // Render BookedEventOverviewModal using the exact hydrated EventBookingModel
        await using var sp = BuildBookingNegotiationsServiceProvider(consultationMock, scheduleMock);
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);
        var modalHtml = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                { nameof(BookedEventOverviewModal.Booking), hydratedFromApi },
                { nameof(BookedEventOverviewModal.CanManageRoster), true },
                { nameof(BookedEventOverviewModal.AllBookings), new List<EventBookingModel> { hydratedFromApi } }
            });
            var output = await renderer.RenderComponentAsync<BookedEventOverviewModal>(parameters);
            return output.ToHtmlString();
        });

        Assert.Contains("#EVT-API-03", modalHtml);
        Assert.Contains("API Corporate Client", modalHtml);
        Assert.Contains("Makati Ballroom", modalHtml);
        Assert.Contains("Corporate Coverage", modalHtml);
        Assert.Contains("Maya Cruz", modalHtml);
        Assert.Contains("Sony A7R V", modalHtml);

        page.CloseBookedEventModal();
        Assert.Null(page.BookedEventModalBooking);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. CalendarOfEvents Entry Type Filter Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CalendarOfEvents_RendersEntryTypeFilter_AndAppliesQueryParameter()
    {
        var today = DateTime.Today;
        var bookings = new List<EventBookingModel>
        {
            new()
            {
                Id = "NEG-UI-1",
                ClientName = "Consult UI Alice",
                ServiceType = "Online Consultation",
                Date = today,
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(9, 30, 0),
                Status = "Pending_Call",
                IsConsultation = true
            },
            new()
            {
                Id = "EVT-UI-1",
                ClientName = "Event UI Bob",
                ServiceType = "Wedding Package",
                Date = today,
                StartTime = new TimeSpan(13, 0, 0),
                EndTime = new TimeSpan(16, 0, 0),
                Status = "Confirmed",
                IsConsultation = false
            }
        };

        var scheduleMock = new Mock<IScheduleService>();
        scheduleMock.Setup(s => s.GetStudioSpaces()).Returns(new List<StudioSpaceResource>
        {
            new() { Id = "studio-a", Name = "Studio A (Main Hall)" }
        });
        scheduleMock.Setup(s => s.GetEventBookings(It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .Returns(bookings);
        scheduleMock.Setup(s => s.GetEquipmentReservations())
            .Returns(new List<EquipmentReservationModel>());

        // 1. Default (All Entries) in Week, Day, and Month views
        var nav1 = new RecordingNavManager("https://localhost/admin/calendar/events");
        await using (var sp = BuildCalendarServiceProvider(scheduleMock.Object, nav1))
        {
            var cal = new TestableCalendarOfEvents();
            cal.SetTestDependencies(scheduleMock.Object, nav: nav1);
            cal._selectedDate = today;
            await cal.TriggerInitializedForTestAsync();
            cal.TriggerParametersSetForTest();
            await cal.LoadBookingsForCurrentViewAsync(forceReload: true);

            var weekAllHtml = await RenderFragmentHtmlAsync(sp, cal.RenderToBuilder);
            Assert.Contains("All Entries", weekAllHtml);
            Assert.Contains("Consultation / Negotiation", weekAllHtml);
            Assert.Contains(">Event</option>", weekAllHtml);
            Assert.Contains("Consult UI Alice", weekAllHtml);
            Assert.Contains("Event UI Bob", weekAllHtml);
            Assert.Contains(">2</strong> total bookings", weekAllHtml);
            Assert.DoesNotContain("clear-filters-link", weekAllHtml);

            // Switch to Consultation / Negotiation -> renders only consultations in Week, Day, and Month views
            cal.OnEntryTypeFilterChanged(new ChangeEventArgs { Value = "consultation" });
            var weekConsultHtml = await RenderFragmentHtmlAsync(sp, cal.RenderToBuilder);
            Assert.Contains("Consult UI Alice", weekConsultHtml);
            Assert.DoesNotContain("Event UI Bob", weekConsultHtml);
            Assert.Contains(">1</strong> total bookings", weekConsultHtml);
            Assert.Contains("clear-filters-link", weekConsultHtml);

            await cal.SetViewMode(ScheduleViewMode.Day);
            var dayConsultHtml = await RenderFragmentHtmlAsync(sp, cal.RenderToBuilder);
            Assert.Contains("Consult UI Alice", dayConsultHtml);
            Assert.DoesNotContain("Event UI Bob", dayConsultHtml);

            await cal.SetViewMode(ScheduleViewMode.Month);
            var monthConsultHtml = await RenderFragmentHtmlAsync(sp, cal.RenderToBuilder);
            Assert.Contains("Consult UI Alice", monthConsultHtml);
            Assert.DoesNotContain("Event UI Bob", monthConsultHtml);
        }

        // 2. Query param ?type=consultation activates filter & Reset button
        var nav2 = new RecordingNavManager("https://localhost/admin/calendar/events?type=consultation");
        await using (var sp = BuildCalendarServiceProvider(scheduleMock.Object, nav2))
        {
            var cal2 = new TestableCalendarOfEvents();
            cal2.SetTestDependencies(scheduleMock.Object, nav: nav2);
            cal2.QueryEntryType = "consultation";
            cal2._selectedDate = today;
            await cal2.TriggerInitializedForTestAsync();
            cal2.TriggerParametersSetForTest();
            await cal2.LoadBookingsForCurrentViewAsync(forceReload: true);

            var html = await RenderFragmentHtmlAsync(sp, cal2.RenderToBuilder);
            Assert.Contains("clear-filters-link", html);
            Assert.Contains("Reset", html);
            Assert.Contains("Consult UI Alice", html);
            Assert.DoesNotContain("Event UI Bob", html);
        }
    }

    [Fact]
    public async Task CalendarOfEvents_EntryTypeFilter_UpdatesLookupMetricsAndPersistsAcrossDayWeekMonthViews()
    {
        var today = DateTime.Today;
        int diffToMon = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
        var monday = today.AddDays(-diffToMon);
        var tuesday = monday.AddDays(1);
        var wednesday = monday.AddDays(2);

        var bookings = new List<EventBookingModel>
        {
            new()
            {
                Id = "NEG-101",
                ClientName = "Consult Alice",
                ServiceType = "Online Consultation",
                Date = monday,
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(9, 30, 0),
                Status = "Pending_Call",
                IsConsultation = true,
                HasConflict = false
            },
            new()
            {
                Id = "NEG-102",
                ClientName = "Consult Bob",
                ServiceType = "Online Consultation",
                Date = tuesday,
                StartTime = new TimeSpan(10, 0, 0),
                EndTime = new TimeSpan(10, 30, 0),
                Status = "Appointment Booked",
                IsConsultation = true,
                HasConflict = false
            },
            new()
            {
                Id = "EVT-201",
                ClientName = "Wedding Shoot",
                ServiceType = "Wedding Package",
                Date = monday,
                StartTime = new TimeSpan(13, 0, 0),
                EndTime = new TimeSpan(15, 0, 0),
                Status = "Confirmed",
                IsConsultation = false,
                HasConflict = true
            },
            new()
            {
                Id = "EVT-202",
                ClientName = "Editorial Shoot",
                ServiceType = "Editorial",
                Date = tuesday,
                StartTime = new TimeSpan(14, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
                Status = "Confirmed",
                IsConsultation = false,
                HasConflict = false
            },
            new()
            {
                Id = "EVT-203",
                ClientName = "Corporate Headshots",
                ServiceType = "Headshots",
                Date = wednesday,
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(13, 0, 0),
                Status = "Completed",
                IsConsultation = false,
                HasConflict = false
            }
        };

        var scheduleMock = new Mock<IScheduleService>();
        scheduleMock.Setup(s => s.GetStudioSpaces()).Returns(new List<StudioSpaceResource>
        {
            new() { Id = "studio-a", Name = "Studio A (Main Hall)" }
        });
        scheduleMock.Setup(s => s.GetEventBookings(It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .Returns(bookings);
        scheduleMock.Setup(s => s.GetEquipmentReservations())
            .Returns(new List<EquipmentReservationModel>());

        var nav = new RecordingNavManager("https://localhost/admin/calendar/events");
        var page = new CalendarOfEvents();
        page.SetTestDependencies(scheduleMock.Object, nav: nav);
        page._selectedDate = monday;

        await page.TriggerInitializedForTestAsync();
        page.TriggerParametersSetForTest();
        await page.LoadBookingsForCurrentViewAsync(forceReload: true);

        // Baseline: All Entries
        Assert.Equal(string.Empty, page.SelectedEntryTypeFilter);
        Assert.False(page.HasActiveFilters);
        Assert.Equal(5, page._filteredBookings.Count);
        Assert.Equal(5, page._totalBookingsCount);
        Assert.Equal(5, page._thisWeekBookingsCount);
        Assert.Equal(10.0, page._thisWeekHoursCount, precision: 2);
        Assert.Equal(1, page._conflictCount);
        Assert.Equal(2, page.GetBookingsForDate(monday).Count); // 1 consultation + 1 event

        // Filter: Consultation / Negotiation (b.IsConsultation == true)
        page.OnEntryTypeFilterChanged(new ChangeEventArgs { Value = "consultation" });
        Assert.Equal("consultation", page.SelectedEntryTypeFilter);
        Assert.True(page.HasActiveFilters);
        Assert.Equal(2, page._filteredBookings.Count);
        Assert.All(page._filteredBookings, b => Assert.True(b.IsConsultation));
        Assert.Equal(2, page._totalBookingsCount);
        Assert.Equal(2, page._thisWeekBookingsCount);
        Assert.Equal(1.0, page._thisWeekHoursCount, precision: 2);
        Assert.Equal(0, page._conflictCount);
        Assert.Single(page._bookingsByDateLookup[monday]);
        Assert.Single(page._bookingsByDateLookup[tuesday]);
        Assert.Empty(page._bookingsByDateLookup[wednesday]);
        Assert.Contains("type=consultation", nav.LastNavigatedUri);

        // Switch across Day, Week, and Month views: filter persists seamlessly
        await page.SetViewMode(ScheduleViewMode.Day);
        Assert.Equal("consultation", page.SelectedEntryTypeFilter);
        Assert.Single(page.GetBookingsForDate(monday));
        Assert.True(page.GetBookingsForDate(monday)[0].IsConsultation);

        await page.SetViewMode(ScheduleViewMode.Month);
        Assert.Equal("consultation", page.SelectedEntryTypeFilter);
        Assert.Single(page.GetBookingsForDate(tuesday));
        Assert.Empty(page.GetBookingsForDate(wednesday));

        await page.SetViewMode(ScheduleViewMode.Week);
        Assert.Equal("consultation", page.SelectedEntryTypeFilter);
        Assert.Equal(2, page._totalBookingsCount);

        // Filter: Event (b.IsConsultation == false)
        page.OnEntryTypeFilterChanged(new ChangeEventArgs { Value = "event" });
        Assert.Equal("event", page.SelectedEntryTypeFilter);
        Assert.True(page.HasActiveFilters);
        Assert.Equal(3, page._filteredBookings.Count);
        Assert.All(page._filteredBookings, b => Assert.False(b.IsConsultation));
        Assert.Equal(3, page._totalBookingsCount);
        Assert.Equal(3, page._thisWeekBookingsCount);
        Assert.Equal(9.0, page._thisWeekHoursCount, precision: 2);
        Assert.Equal(1, page._conflictCount);
        Assert.Single(page._bookingsByDateLookup[monday]);
        Assert.Single(page._bookingsByDateLookup[tuesday]);
        Assert.Single(page._bookingsByDateLookup[wednesday]);
        Assert.Contains("type=event", nav.LastNavigatedUri);

        // Combine Event filter with Status filter
        page.OnStatusFilterChanged(new ChangeEventArgs { Value = "Completed" });
        Assert.Single(page._filteredBookings);
        Assert.Equal("EVT-203", page._filteredBookings[0].Id);
        Assert.Equal(1, page._totalBookingsCount);

        // Reset via ClearFilters()
        page.ClearFilters();
        Assert.Equal(string.Empty, page.SelectedEntryTypeFilter);
        Assert.False(page.HasActiveFilters);
        Assert.Equal(5, page._filteredBookings.Count);
        Assert.Equal(5, page._totalBookingsCount);
        Assert.Equal(10.0, page._thisWeekHoursCount, precision: 2);
        Assert.Equal(1, page._conflictCount);
        Assert.DoesNotContain("type=", nav.LastNavigatedUri ?? string.Empty);

        // Hydrate from URL query parameter (?type=event) on child/page navigation
        var childNav = new RecordingNavManager("https://localhost/staff/calendar/events?type=event");
        var childPage = new CalendarOfEvents();
        childPage.SetTestDependencies(scheduleMock.Object, nav: childNav);
        await childPage.TriggerInitializedForTestAsync();
        childPage.TriggerParametersSetForTest();
        await childPage.LoadBookingsForCurrentViewAsync(forceReload: true);

        Assert.Equal("event", childPage.SelectedEntryTypeFilter);
        Assert.True(childPage.HasActiveFilters);
        Assert.Equal(3, childPage._filteredBookings.Count);
        Assert.All(childPage._filteredBookings, b => Assert.False(b.IsConsultation));
    }
}
