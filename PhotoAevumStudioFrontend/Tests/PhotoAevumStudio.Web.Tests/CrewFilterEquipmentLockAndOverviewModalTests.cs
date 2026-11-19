using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoAevumStudio.Api.Controllers;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Models;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class CrewFilterEquipmentLockAndOverviewModalTests
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
    public void BookingWizardEngine_ExcludesOfficeClerk_FromSelectableCrewDispatch()
    {
        var clerkByRole = new EmployeeViewModel
        {
            EmployeeId = "EMP-101",
            FirstName = "Clara",
            LastName = "Reyes",
            Role = "Office Clerk",
            Specializations = new List<string> { "Customer Relations" },
            Status = "Active"
        };

        var clerkBySpecialization = new EmployeeViewModel
        {
            EmployeeId = "EMP-102",
            FirstName = "Marco",
            LastName = "Diaz",
            Role = "Employee",
            Specializations = new List<string> { "Office Clerk" },
            Status = "Active"
        };

        var clerkByPrefix = new EmployeeViewModel
        {
            EmployeeId = "CLK-001",
            FirstName = "Sofia",
            LastName = "Lim",
            Role = "Employee",
            Specializations = new List<string> { "Consultation" },
            Status = "Active"
        };

        var photographer = new EmployeeViewModel
        {
            EmployeeId = "EMP-201",
            FirstName = "Liam",
            LastName = "Santos",
            Role = "Employee",
            Specializations = new List<string> { "Lead Photographer" },
            DaysOff = "Sunday",
            Status = "Active"
        };

        var videographer = new EmployeeViewModel
        {
            EmployeeId = "EMP-202",
            FirstName = "Aria",
            LastName = "Cruz",
            Role = "Employee",
            Specializations = new List<string> { "Videographer" },
            DaysOff = "Sunday",
            Status = "Active"
        };

        var allStaff = new[] { clerkByRole, clerkBySpecialization, clerkByPrefix, photographer, videographer };

        Assert.True(BookingWizardEngine.IsOfficeClerk(clerkByRole));
        Assert.True(BookingWizardEngine.IsOfficeClerk(clerkBySpecialization));
        Assert.True(BookingWizardEngine.IsOfficeClerk(clerkByPrefix));
        Assert.False(BookingWizardEngine.IsOfficeClerk(photographer));
        Assert.False(BookingWizardEngine.IsOfficeClerk(videographer));

        var dispatchable = BookingWizardEngine.FilterDispatchableCrew(allStaff).ToList();
        Assert.Equal(2, dispatchable.Count);
        Assert.DoesNotContain(dispatchable, e => e.EmployeeId is "EMP-101" or "EMP-102" or "CLK-001");

        // Wednesday shoot window: 10:00 AM - 12:00 PM
        var eventDate = new DateTime(2026, 11, 25);
        var availableCrews = BookingWizardEngine.GetAvailableCrews(
            allStaff,
            eventDate,
            TimeSpan.FromHours(10),
            TimeSpan.FromHours(12),
            Array.Empty<EventBookingModel>(),
            null);

        Assert.Equal(2, availableCrews.Count);
        Assert.Contains(availableCrews, c => c.Employee.EmployeeId == "EMP-201");
        Assert.Contains(availableCrews, c => c.Employee.EmployeeId == "EMP-202");
        Assert.DoesNotContain(availableCrews, c => BookingWizardEngine.IsOfficeClerk(c.Employee));
    }

    [Fact]
    public void BookingWizardEngine_SameDayEquipmentLock_BlocksEntireCalendarDay_IgnoresCancelledEvents()
    {
        var targetDate = new DateTime(2026, 11, 26);

        var morningConfirmedBooking = new EventBookingModel
        {
            Id = "EVT-1001",
            ClientName = "Morning Client",
            Date = targetDate,
            StartTime = TimeSpan.FromHours(8),
            EndTime = TimeSpan.FromHours(10),
            Status = "Confirmed",
            AssignedEquipmentIds = new List<string> { "EQ-CAM-01" }
        };

        var cancelledSameDayBooking = new EventBookingModel
        {
            Id = "EVT-1002",
            ClientName = "Cancelled Client",
            Date = targetDate,
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(11),
            Status = "Cancelled",
            AssignedEquipmentIds = new List<string> { "EQ-LENS-99" }
        };

        var bookings = new[] { morningConfirmedBooking, cancelledSameDayBooking };

        // Evening shoot (7:00 PM - 9:00 PM) on the same calendar date must still see EQ-CAM-01 locked
        var eveningConflicts = BookingWizardEngine.GetConflictingEquipmentIds(
            targetDate,
            TimeSpan.FromHours(19),
            TimeSpan.FromHours(21),
            bookings,
            Array.Empty<EquipmentReservationModel>());

        Assert.Contains("EQ-CAM-01", eveningConflicts);
        Assert.DoesNotContain("EQ-LENS-99", eveningConflicts);

        // Editing EVT-1001 itself excludes its own lock
        var selfEditConflicts = BookingWizardEngine.GetBookedEquipmentIdsForDate(
            targetDate,
            bookings,
            Array.Empty<EquipmentReservationModel>(),
            null,
            excludeAppointmentId: "EVT-1001");
        Assert.DoesNotContain("EQ-CAM-01", selfEditConflicts);

        // Next calendar day is unlocked
        var nextDayConflicts = BookingWizardEngine.GetBookedEquipmentIdsForDate(
            targetDate.AddDays(1),
            bookings,
            Array.Empty<EquipmentReservationModel>());
        Assert.Empty(nextDayConflicts);
    }

    [Fact]
    public void ScheduleModels_OverlapsWith_FlagsSameDayEquipmentConflictAcrossDifferentTimes()
    {
        var shootDate = new DateTime(2026, 11, 27);

        var morningEvent = new EventBookingModel
        {
            Id = "EVT-2001",
            Date = shootDate,
            StartTime = TimeSpan.FromHours(8),
            EndTime = TimeSpan.FromHours(9),
            Status = "Confirmed",
            AssignedCrewIds = new List<string> { "EMP-201" },
            AssignedEquipmentIds = new List<string> { "EQ-BODY-01" }
        };

        var eveningEventSameEquipment = new EventBookingModel
        {
            Id = "EVT-2002",
            Date = shootDate,
            StartTime = TimeSpan.FromHours(18),
            EndTime = TimeSpan.FromHours(20),
            Status = "Confirmed",
            AssignedCrewIds = new List<string> { "EMP-202" },
            AssignedEquipmentIds = new List<string> { "EQ-BODY-01" }
        };

        var eveningEventDifferentEquipment = new EventBookingModel
        {
            Id = "EVT-2003",
            Date = shootDate,
            StartTime = TimeSpan.FromHours(18),
            EndTime = TimeSpan.FromHours(20),
            Status = "Confirmed",
            AssignedCrewIds = new List<string> { "EMP-202" },
            AssignedEquipmentIds = new List<string> { "EQ-BODY-02" }
        };

        Assert.True(morningEvent.OverlapsWith(eveningEventSameEquipment));
        Assert.False(morningEvent.OverlapsWith(eveningEventDifferentEquipment));

        var morningRes = new EquipmentReservationModel
        {
            Id = "EVT-2001-EQ-BODY-01",
            EquipmentId = "EQ-BODY-01",
            Date = shootDate,
            StartTime = TimeSpan.FromHours(8),
            EndTime = TimeSpan.FromHours(9)
        };

        var eveningRes = new EquipmentReservationModel
        {
            Id = "EVT-2002-EQ-BODY-01",
            EquipmentId = "EQ-BODY-01",
            Date = shootDate,
            StartTime = TimeSpan.FromHours(18),
            EndTime = TimeSpan.FromHours(20)
        };

        Assert.True(morningRes.OverlapsWith(eveningRes));
    }

    [Fact]
    public async Task BookedEventOverviewModal_RendersAllFields_AndRoleGatesInlineControls()
    {
        var consultationMock = new Mock<IConsultationClient>();
        var empMock = new Mock<IEmployeeService>();
        var eqMock = new Mock<IEquipmentService>();
        var schedMock = new Mock<IScheduleService>();

        var booking = new EventBookingModel
        {
            Id = "EVT-3001",
            ClientName = "Isabella Montgomery",
            ClientEmail = "isabella@example.com",
            EventLocation = "Grand Botanical Conservatory",
            ServiceType = "Full Day Nuptial",
            AssignedServices = new List<string> { "Full Day Nuptial", "4K Drone Aerials" },
            Date = new DateTime(2026, 11, 28),
            StartTime = TimeSpan.FromHours(13),
            EndTime = TimeSpan.FromHours(16),
            Status = "Confirmed",
            EstimatedPrice = 45000m,
            AssignedCrewIds = new List<string> { "EMP-201" },
            AssignedCrew = new List<EventCrewSummary>
            {
                new("EMP-201", "Liam Santos", "Lead Photographer", true)
            },
            AssignedEquipmentIds = new List<string> { "EQ-CAM-01" },
            AssignedEquipmentNames = new List<string> { "Sony Alpha A7R V" },
            AssignedEquipment = new List<EventEquipmentSummary>
            {
                new("EQ-CAM-01", "Sony Alpha A7R V", "Camera Body")
            }
        };

        consultationMock
            .Setup(c => c.GetAppointmentByIdAsync("EVT-3001"))
            .ReturnsAsync(ApiResult<EventAppointmentDto>.Fail(ApiErrorKind.Server, "Offline test fallback"));

        empMock
            .Setup(e => e.GetEmployeesAsync())
            .ReturnsAsync(new List<EmployeeViewModel>
            {
                new()
                {
                    EmployeeId = "EMP-201",
                    FirstName = "Liam",
                    LastName = "Santos",
                    Role = "Employee",
                    Specializations = new List<string> { "Lead Photographer" },
                    Status = "Active"
                },
                new()
                {
                    EmployeeId = "EMP-202",
                    FirstName = "Aria",
                    LastName = "Cruz",
                    Role = "Employee",
                    Specializations = new List<string> { "Videographer" },
                    Status = "Active"
                },
                new()
                {
                    EmployeeId = "CLK-001",
                    FirstName = "Clara",
                    LastName = "Reyes",
                    Role = "Office Clerk",
                    Specializations = new List<string> { "Office Clerk" },
                    Status = "Active"
                }
            });

        empMock
            .Setup(e => e.GetAllLeavesAsync(It.IsAny<string?>()))
            .ReturnsAsync(new List<LeaveItemViewModel>());

        eqMock
            .Setup(e => e.GetEquipmentsAsync())
            .ReturnsAsync(new List<EquipmentItemModel>
            {
                new()
                {
                    EquipmentID = "EQ-CAM-01",
                    EquipmentName = "Sony Alpha A7R V",
                    EquipmentTypeName = "Camera Body",
                    EquipmentStatus = "Active"
                },
                new()
                {
                    EquipmentID = "EQ-LENS-02",
                    EquipmentName = "FE 24-70mm GM II",
                    EquipmentTypeName = "Lens",
                    EquipmentStatus = "Active"
                }
            });

        var services = new ServiceCollection();
        services.AddSingleton(consultationMock.Object);
        services.AddSingleton(empMock.Object);
        services.AddSingleton(eqMock.Object);
        services.AddSingleton(schedMock.Object);

        await using var sp = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);

        // 1. Read-only role (CanManageRoster = false)
        var readOnlyHtml = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                { nameof(BookedEventOverviewModal.Booking), booking },
                { nameof(BookedEventOverviewModal.CanManageRoster), false }
            });
            var output = await renderer.RenderComponentAsync<BookedEventOverviewModal>(parameters);
            return output.ToHtmlString();
        });

        Assert.Contains("Isabella Montgomery", readOnlyHtml);
        Assert.Contains("Grand Botanical Conservatory", readOnlyHtml);
        Assert.Contains("Full Day Nuptial", readOnlyHtml);
        Assert.Contains("4K Drone Aerials", readOnlyHtml);
        Assert.Contains("Liam Santos", readOnlyHtml);
        Assert.Contains("Sony Alpha A7R V", readOnlyHtml);
        Assert.DoesNotContain("+ Add Crew", readOnlyHtml);
        Assert.DoesNotContain("+ Add Equipment", readOnlyHtml);
        Assert.DoesNotContain("Save Roster Changes", readOnlyHtml);

        // 2. Office Clerk / Admin role (CanManageRoster = true)
        var editableHtml = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                { nameof(BookedEventOverviewModal.Booking), booking },
                { nameof(BookedEventOverviewModal.CanManageRoster), true }
            });
            var output = await renderer.RenderComponentAsync<BookedEventOverviewModal>(parameters);
            return output.ToHtmlString();
        });

        Assert.Contains("Isabella Montgomery", editableHtml);
        Assert.Contains("Grand Botanical Conservatory", editableHtml);
        Assert.Contains("+ Add Crew", editableHtml);
        Assert.Contains("+ Add Equipment", editableHtml);
        Assert.Contains("Replace", editableHtml);
        Assert.Contains("Remove", editableHtml);
        Assert.Contains("Save Roster Changes", editableHtml);
        // Selectable crew dropdown includes Aria Cruz (Videographer) but excludes Clara Reyes (Office Clerk)
        Assert.Contains("Aria Cruz", editableHtml);
        Assert.DoesNotContain("Clara Reyes", editableHtml);
    }

    [Fact]
    public async Task BackendEnforcement_ConsultationsControllerAndService_EnforceClerkOrAdmin_NoOfficeClerk_SameDayLock_AndHistoryLog()
    {
        var method = typeof(ConsultationsController).GetMethod(nameof(ConsultationsController.UpdateRoster));
        Assert.NotNull(method);

        var authorizeAttr = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttr);
        Assert.Equal("ClerkOrAdmin", authorizeAttr!.Policy);

        // Verify authenticated claims override spoofed client ActorId / ActorRole
        var serviceMock = new Mock<IConsultationNegotiationService>();
        PhotoAevumStudio.Application.UpdateAppointmentRosterRequest? capturedReq = null;
        serviceMock
            .Setup(s => s.UpdateAppointmentRosterAsync(
                "EVT-9001",
                It.IsAny<PhotoAevumStudio.Application.UpdateAppointmentRosterRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, PhotoAevumStudio.Application.UpdateAppointmentRosterRequest, CancellationToken>((_, r, _) => capturedReq = r)
            .ReturnsAsync(true);

        var controller = new ConsultationsController(serviceMock.Object, NullLogger<ConsultationsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[]
                        {
                            new Claim("studio_id", "CLK-007"),
                            new Claim("full_name", "Verified Clerk"),
                            new Claim(ClaimTypes.Role, "Office Clerk")
                        },
                        authenticationType: "TestAuth"))
                }
            }
        };

        var spoofedRequest = new PhotoAevumStudio.Application.UpdateAppointmentRosterRequest
        {
            EmployeeIds = new List<string> { "EMP-201" },
            EquipmentIds = new List<string> { "EQ-CAM-01" },
            ActorId = "ADM-SPOOF",
            ActorName = "Spoofed Admin",
            ActorRole = "SystemAdmin"
        };

        var actionResult = await controller.UpdateRoster("EVT-9001", spoofedRequest, CancellationToken.None);
        Assert.IsType<OkObjectResult>(actionResult);
        Assert.NotNull(capturedReq);
        Assert.Equal("CLK-007", capturedReq!.ActorId);
        Assert.Equal("Verified Clerk", capturedReq.ActorName);
        Assert.Equal("Employee", capturedReq.ActorRole);

        var root = GetRepositoryRoot();
        var serviceCode = await File.ReadAllTextAsync(Path.Combine(
            root,
            "PhotoAevumStudioBackend",
            "PhotoAevumStudio.Infrastructure",
            "Services",
            "ConsultationNegotiationService.cs"));

        Assert.Contains("Office Clerks are disabled from event crew dispatch", serviceCode);
        Assert.Contains("ValidateEquipmentSameDayAvailableAsync", serviceCode);
        Assert.Contains("INSERT INTO dbo.HistoryLog", serviceCode);

        var masterSql = await File.ReadAllTextAsync(Path.Combine(
            root,
            "PhotoAevumStudioBackend",
            "database",
            "PhotoAevumStudio.sql"));
        var idempotentSql = await File.ReadAllTextAsync(Path.Combine(
            root,
            "PhotoAevumStudioBackend",
            "database",
            "PhotoAevumStudioIdempotent.sql"));
        var sprocsSql = await File.ReadAllTextAsync(Path.Combine(
            root,
            "PhotoAevumStudioBackend",
            "database",
            "PhotoAevumStudioStoredProcedures.sql"));

        Assert.Contains("EventLocation", masterSql);
        Assert.Contains("EventLocation", idempotentSql);
        Assert.Contains("THROW 50005", sprocsSql);
        Assert.Contains("THROW 50006", sprocsSql);
    }
}
