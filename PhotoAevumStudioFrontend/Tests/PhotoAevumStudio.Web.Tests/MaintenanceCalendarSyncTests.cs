using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Models;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class MaintenanceCalendarSyncTests
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
    public void EventBookingModel_NeedsEquipmentFix_FollowsEquipmentStatusAndEventLifecycle()
    {
        var booking = new EventBookingModel
        {
            Id = "EVT-100",
            Status = "Confirmed",
            AssignedEquipment = new List<EventEquipmentSummary>
            {
                new("EQP-0001", "Canon EOS R5", "Camera Body", "Available"),
                new("EQP-0002", "RF 24-70mm", "Lens", "Maintenance")
            }
        };

        Assert.True(booking.NeedsEquipmentFix);
        var broken = Assert.Single(booking.UnavailableAssignedEquipment);
        Assert.Equal("EQP-0002", broken.EquipmentId);

        // Completed or Cancelled events should not flag NeedsEquipmentFix
        booking.Status = "Completed";
        Assert.False(booking.NeedsEquipmentFix);

        booking.Status = "Confirmed";
        Assert.True(booking.NeedsEquipmentFix);

        // Replacing or resolving equipment back to Available clears NeedsEquipmentFix
        booking.AssignedEquipment = new List<EventEquipmentSummary>
        {
            new("EQP-0001", "Canon EOS R5", "Camera Body", "Available"),
            new("EQP-0002", "RF 24-70mm", "Lens", "Available")
        };
        Assert.False(booking.NeedsEquipmentFix);
        Assert.Empty(booking.UnavailableAssignedEquipment);
    }

    [Fact]
    public void ScheduleService_InMemory_ReflectsMaintenanceAndAllowsReplacementScheduling()
    {
        var scheduleService = new ScheduleService(configuration: null);
        var equipment = scheduleService.GetEquipmentResources();
        var bookings = scheduleService.GetEventBookings();
        var reservations = scheduleService.GetEquipmentReservations();

        // Seeded eq-01 is assigned to evt-105 & evt-106; eq-03 is reserved on res-401 & res-402
        var eq01 = equipment.First(e => e.Id == "eq-01");
        eq01.Status = "In-Maintenance";

        var updatedBookings = scheduleService.GetEventBookings();
        var affectedBooking = updatedBookings.First(b => b.Id == "evt-105");
        Assert.True(affectedBooking.NeedsEquipmentFix);
        Assert.Contains(affectedBooking.UnavailableAssignedEquipment, e => e.EquipmentId == "eq-01");

        // Attempting to reserve maintenance equipment must throw
        Assert.Throws<InvalidOperationException>(() =>
            scheduleService.AddEquipmentReservation(new EquipmentReservationModel
            {
                EquipmentId = "eq-01",
                Date = DateTime.Today.AddDays(10),
                StartTime = TimeSpan.FromHours(9),
                EndTime = TimeSpan.FromHours(12),
                ProjectOrClient = "Blocked Test"
            }));

        // Send eq-03 to maintenance and replace res-401 with eq-01 after restoring eq-01
        eq01.Status = "Available";
        var eq03 = equipment.First(e => e.Id == "eq-03");
        eq03.Status = "In-Maintenance";

        var maintRes = scheduleService.GetEquipmentReservations().First(r => r.Id == "res-401");
        Assert.True(maintRes.NeedsEquipmentFix);

        // Add an unbooked available gear to swap in
        equipment.Add(new EquipmentItemResource
        {
            Id = "eq-99",
            Name = "Profoto B10X Plus",
            SerialNumber = "SN-PF-99",
            Category = "Strobe Lighting",
            Status = "Ready"
        });

        var (ok, err) = scheduleService.ReplaceReservationEquipment("res-401", null, "eq-03", "eq-99", "ADM-001");
        Assert.True(ok, err);

        var refreshedRes = scheduleService.GetEquipmentReservations().First(r => r.Id == "res-401");
        Assert.Equal("eq-99", refreshedRes.EquipmentId);
        Assert.False(refreshedRes.NeedsEquipmentFix);
    }

    [Fact]
    public async Task EquipmentService_InMemory_MaintainsDispatchMaintenanceVisibilityAndResolvesCleanly()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new EquipmentService(config);

        var (typeOk, typeErr) = await service.AddEquipmentTypeAsync("ET-CAM", "Camera Body", "Blue");
        Assert.True(typeOk, typeErr);
        var (eqOk, eqErr, createdEq) = await service.CreateEquipmentAsync("Sony A7 IV", null, "ET-CAM", "SN-A7IV", null);
        Assert.True(eqOk, eqErr);
        Assert.NotNull(createdEq);

        // Assign to an upcoming event via in-memory dispatch list
        var assignField = typeof(EquipmentService).GetField("_events", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(assignField);
        var events = (List<EventAppointmentModel>)assignField!.GetValue(service)!;
        events.Add(new EventAppointmentModel
        {
            EventAppointmentID = "EVT-777",
            EventName = "Corporate Gala",
            CustomerID = "CUST-1",
            CustomerName = "Acme Corp",
            EventDate = DateTime.Today.AddDays(3),
            EventTime = new TimeSpan(10, 0, 0),
            EventStatus = "Confirmed"
        });

        var (assigned, assignErr) = await service.AssignEquipmentToEventAsync("EVT-777", createdEq!.EquipmentID, "ADM-001");
        Assert.True(assigned, assignErr);

        // Send equipment to maintenance while scheduled
        var (logged, logErr) = await service.LogMaintenanceAsync(
            createdEq.EquipmentID,
            "Repair",
            "Shutter sticking during test",
            "ADM-001",
            setUnderMaintenance: true,
            subStatus: MaintenanceScheduleStatuses.SubUnderRepair);
        Assert.True(logged, logErr);

        var dispatches = await service.GetDispatchesAsync();
        var d = Assert.Single(dispatches, x => x.EquipmentID == createdEq.EquipmentID);
        Assert.True(d.IsEquipmentMaintenanceOrUnavailable);
        Assert.Equal("Maintenance", d.EquipmentStatus);

        // Resolve maintenance back to Available
        var (resolved, resErr) = await service.ResolveMaintenanceAsync(createdEq.EquipmentID, "Shutter replaced & tested", "ADM-001");
        Assert.True(resolved, resErr);

        var dispatchesAfter = await service.GetDispatchesAsync();
        var dAfter = Assert.Single(dispatchesAfter, x => x.EquipmentID == createdEq.EquipmentID);
        Assert.False(dAfter.IsEquipmentMaintenanceOrUnavailable);
        Assert.Equal("Available", dAfter.EquipmentStatus);
    }

    [Fact]
    public async Task BookedEventOverviewModal_ShowsMaintenanceAlertAndScheduleReplacementButton()
    {
        var consultationMock = new Mock<IConsultationClient>();
        var empMock = new Mock<IEmployeeService>();
        var eqMock = new Mock<IEquipmentService>();
        var schedMock = new Mock<IScheduleService>();

        empMock.Setup(s => s.GetEmployeesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeViewModel>
            {
                new() { EmployeeId = "EMP-01", FirstName = "Liam", LastName = "Santos", Role = "Employee", Status = "Active" }
            });
        empMock.Setup(s => s.GetAllLeavesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LeaveItemViewModel>());
        eqMock.Setup(s => s.GetEquipmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EquipmentItemModel>
            {
                new() { EquipmentID = "EQP-0001", EquipmentName = "Canon EOS R5", EquipmentTypeName = "Camera", EquipmentStatus = "Maintenance" },
                new() { EquipmentID = "EQP-0002", EquipmentName = "Canon EOS R6 II", EquipmentTypeName = "Camera", EquipmentStatus = "Available" }
            });
        consultationMock.Setup(s => s.GetAppointmentByIdAsync("EVT-900", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<EventAppointmentDto>(new EventAppointmentDto
            {
                EventAppointmentId = "EVT-900",
                EventName = "Wedding Prep",
                CustomerName = "Maria Clara",
                EventDate = DateOnly.FromDateTime(DateTime.Today.AddDays(2)),
                EventTime = new TimeOnly(9, 0),
                EventEndTime = new TimeOnly(12, 0),
                EventStatus = "Confirmed",
                AssignedEmployees = new List<AssignedEmployeeDto>
                {
                    new() { EmployeeId = "EMP-01", EmployeeName = "Liam Santos", IsTeamLeader = true }
                },
                AssignedEquipment = new List<AssignedEquipmentDto>
                {
                    new() { EquipmentId = "EQP-0001", EquipmentName = "Canon EOS R5", EquipmentType = "Camera", EquipmentStatus = "Maintenance" }
                }
            }, null));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(consultationMock.Object);
        services.AddSingleton(empMock.Object);
        services.AddSingleton(eqMock.Object);
        services.AddSingleton(schedMock.Object);

        await using var sp = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);

        var booking = new EventBookingModel
        {
            Id = "EVT-900",
            ClientName = "Maria Clara",
            ServiceType = "Wedding Prep",
            Date = DateTime.Today.AddDays(2),
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(12),
            Status = "Confirmed",
            AssignedEquipment = new List<EventEquipmentSummary>
            {
                new("EQP-0001", "Canon EOS R5", "Camera", "Maintenance")
            },
            AssignedEquipmentIds = new List<string> { "EQP-0001" }
        };

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                { nameof(BookedEventOverviewModal.Booking), booking },
                { nameof(BookedEventOverviewModal.CanManageRoster), true },
                { nameof(BookedEventOverviewModal.AllBookings), new List<EventBookingModel> { booking } },
                { nameof(BookedEventOverviewModal.EquipmentReservations), new List<EquipmentReservationModel>() }
            });
            var output = await renderer.RenderComponentAsync<BookedEventOverviewModal>(parameters);
            return output.ToHtmlString();
        });

        Assert.Contains("Needs Gear Fix", html);
        Assert.Contains("Equipment Maintenance Alert", html);
        Assert.Contains("Schedule Replacement", html);
    }

    [Fact]
    public void CalendarAndEquipmentViews_ContainMaintenanceFixAndSyncWiring()
    {
        var root = GetRepositoryRoot();
        var pagesDir = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages");
        var sharedDir = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Shared");

        var calendarOfEvents = File.ReadAllText(Path.Combine(pagesDir, "CalendarOfEvents.razor"));
        Assert.Contains("EquipmentService.OnChange", calendarOfEvents);
        Assert.Contains("_upcomingEquipmentFixAlerts", calendarOfEvents);
        Assert.Contains("FixAndScheduleEquipmentAsync", calendarOfEvents);
        Assert.Contains("Needs Fix / Schedule Equipment", calendarOfEvents);

        var equipmentSchedule = File.ReadAllText(Path.Combine(pagesDir, "EquipmentSchedule.razor"));
        Assert.Contains("UpcomingNeedsFixReservations", equipmentSchedule);
        Assert.Contains("ScheduleReplacementForSelectedAsync", equipmentSchedule);
        Assert.Contains("ReplaceReservationEquipment", equipmentSchedule);

        var dispatch = File.ReadAllText(Path.Combine(pagesDir, "Dispatch.razor"));
        Assert.Contains("IsEquipmentMaintenanceOrUnavailable", dispatch);

        var maintenance = File.ReadAllText(Path.Combine(pagesDir, "Maintenance.razor"));
        Assert.Contains("Schedule Replacement", maintenance);

        var pill = File.ReadAllText(Path.Combine(sharedDir, "TeamLeaderActiveEventPill.razor"));
        Assert.Contains("IsUnavailableStatus", pill);
    }
}
