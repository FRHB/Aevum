using System;
using System.Linq;
using PhotoAevumStudio.Web.Components.Layout;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Models;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class ScheduleManagementTests
{
    [Fact]
    public void ScheduleService_InitializesWithSeedDataAndIdentifiesConflicts()
    {
        var service = new ScheduleService();

        // Check Studio Spaces & Events
        var spaces = service.GetStudioSpaces();
        Assert.NotEmpty(spaces);
        Assert.Contains(spaces, s => s.Id == "studio-a");

        var events = service.GetEventBookings();
        Assert.NotEmpty(events);
        Assert.True(service.GetEventConflictCount() > 0, "Initial seed data must detect the intentional event conflict.");

        // Check Staff Roster & Shifts
        var staff = service.GetStaffMembers();
        Assert.NotEmpty(staff);
        Assert.Contains(staff, s => s.FullName == "Elena Rostova");

        var shifts = service.GetStaffShifts();
        Assert.NotEmpty(shifts);
        Assert.Contains(shifts, s => s.IsOpenShift);
        Assert.True(service.GetShiftConflictCount() > 0, "Initial seed data must detect the intentional staff shift conflict.");

        // Check Equipment & Reservations
        var equipment = service.GetEquipmentResources();
        Assert.NotEmpty(equipment);
        Assert.Contains(equipment, e => e.Category == "Camera Bodies");

        var reservations = service.GetEquipmentReservations();
        Assert.NotEmpty(reservations);
        Assert.True(service.GetEquipmentConflictCount() > 0, "Initial seed data must detect the intentional equipment reservation conflict.");
    }

    [Fact]
    public void EventBooking_ConflictDetection_DetectsOverlappingTimes()
    {
        var service = new ScheduleService();
        var date = DateTime.Today.AddDays(10);

        var booking1 = new EventBookingModel
        {
            Id = "test-evt-1",
            StudioSpaceId = "studio-a",
            ClientName = "Client A",
            Date = date,
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(13, 0, 0)
        };

        var booking2 = new EventBookingModel
        {
            Id = "test-evt-2",
            StudioSpaceId = "studio-a",
            ClientName = "Client B",
            Date = date,
            StartTime = new TimeSpan(12, 0, 0), // Overlaps 12:00 - 13:00
            EndTime = new TimeSpan(15, 0, 0)
        };

        service.AddEventBooking(booking1);
        service.AddEventBooking(booking2);

        var updated1 = service.GetEventBookings().First(b => b.Id == "test-evt-1");
        var updated2 = service.GetEventBookings().First(b => b.Id == "test-evt-2");

        Assert.True(updated1.HasConflict);
        Assert.True(updated2.HasConflict);
        Assert.Contains("Double-booked", updated1.ConflictMessage);

        // Delete one and verify conflict clears
        service.DeleteEventBooking("test-evt-2");
        updated1 = service.GetEventBookings().First(b => b.Id == "test-evt-1");
        Assert.False(updated1.HasConflict);
    }

    [Fact]
    public void StaffShift_ConflictDetection_DetectsDoubleRostering()
    {
        var service = new ScheduleService();
        var date = DateTime.Today.AddDays(12);

        var shift1 = new StaffShiftModel
        {
            Id = "test-shift-1",
            StaffId = "staff-01",
            StaffName = "Elena Rostova",
            Date = date,
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(14, 0, 0),
            Location = "Studio A"
        };

        var shift2 = new StaffShiftModel
        {
            Id = "test-shift-2",
            StaffId = "staff-01",
            StaffName = "Elena Rostova",
            Date = date,
            StartTime = new TimeSpan(13, 0, 0), // Overlaps 13:00 - 14:00
            EndTime = new TimeSpan(18, 0, 0),
            Location = "Outdoor Shoot"
        };

        service.AddStaffShift(shift1);
        service.AddStaffShift(shift2);

        var updated1 = service.GetStaffShifts().First(s => s.Id == "test-shift-1");
        var updated2 = service.GetStaffShifts().First(s => s.Id == "test-shift-2");

        Assert.True(updated1.HasConflict);
        Assert.True(updated2.HasConflict);
    }

    [Fact]
    public void EquipmentReservation_ConflictDetection_DetectsDoubleBooking()
    {
        var service = new ScheduleService();
        var date = DateTime.Today.AddDays(15);

        var res1 = new EquipmentReservationModel
        {
            Id = "test-res-1",
            EquipmentId = "eq-01",
            ProjectOrClient = "Project Alpha",
            Date = date,
            StartTime = new TimeSpan(8, 0, 0),
            EndTime = new TimeSpan(12, 0, 0)
        };

        var res2 = new EquipmentReservationModel
        {
            Id = "test-res-2",
            EquipmentId = "eq-01",
            ProjectOrClient = "Project Beta",
            Date = date,
            StartTime = new TimeSpan(11, 0, 0), // Overlaps 11:00 - 12:00
            EndTime = new TimeSpan(15, 0, 0)
        };

        service.AddEquipmentReservation(res1);
        service.AddEquipmentReservation(res2);

        var updated1 = service.GetEquipmentReservations().First(r => r.Id == "test-res-1");
        var updated2 = service.GetEquipmentReservations().First(r => r.Id == "test-res-2");

        Assert.True(updated1.HasConflict);
        Assert.True(updated2.HasConflict);
    }
}
