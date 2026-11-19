using System;
using System.Collections.Generic;
using System.Linq;
using PhotoAevumStudio.Infrastructure.Scheduling;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Models;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class BookingScheduleBufferAndDealWizardTests
{
    [Fact]
    public void BufferRule_ClerkBooked_9to10AM_Has1HourBuffer_CannotBeAssignedFor_10to11AM()
    {
        // When assigned clerk was booked for 9:00 AM - 10:00 AM:
        var bookingStart = new TimeOnly(9, 0);
        var bookingEnd = new TimeOnly(10, 0);

        // Check slot: 10:00 AM - 11:00 AM
        var slotStart = new TimeOnly(10, 0);
        var slotEnd = new TimeOnly(11, 0);

        bool overlaps = ScheduleBookingService.DoesSlotOverlapWithBufferedBooking(slotStart, slotEnd, bookingStart, bookingEnd);

        // Must overlap because the 1-hour post-event buffer restricts 10:00 AM - 11:00 AM
        Assert.True(overlaps, "Office clerk must have extra buffer time where they cannot be assigned for 10:00 AM - 11:00 AM.");
    }

    [Fact]
    public void BufferRule_ClerkBooked_11AMto12PM_Allows_1to2PM_NotRestricted()
    {
        // When booked schedule was from 11:00 AM - 12:00 PM:
        var bookingStart = new TimeOnly(11, 0);
        var bookingEnd = new TimeOnly(12, 0);

        // Check slot: 1:00 PM - 2:00 PM (13:00 - 14:00)
        var slotStart = new TimeOnly(13, 0);
        var slotEnd = new TimeOnly(14, 0);

        bool overlaps = ScheduleBookingService.DoesSlotOverlapWithBufferedBooking(slotStart, slotEnd, bookingStart, bookingEnd);

        // Must NOT overlap because 1 hour post-buffer ends at 1:00 PM (12:00 + 1hr = 13:00)
        Assert.False(overlaps, "When booked from 11:00 AM - 12:00 PM, the time for 1:00 PM - 2:00 PM should NOT be restricted and clerk can be booked.");
    }

    [Fact]
    public void BufferRule_ClerkBooked_11AMto12PM_Restricts_2HoursAdvancePreparation()
    {
        var bookingStart = new TimeOnly(11, 0);
        var bookingEnd = new TimeOnly(12, 0);

        // Check slot: 9:00 AM - 10:00 AM (2 hours prior)
        var slot9to10 = ScheduleBookingService.DoesSlotOverlapWithBufferedBooking(
            new TimeOnly(9, 0), new TimeOnly(10, 0), bookingStart, bookingEnd);

        // Check slot: 10:00 AM - 11:00 AM (1 hour prior)
        var slot10to11 = ScheduleBookingService.DoesSlotOverlapWithBufferedBooking(
            new TimeOnly(10, 0), new TimeOnly(11, 0), bookingStart, bookingEnd);

        // Both must be restricted due to the 2-hour advance preparation buffer
        Assert.True(slot9to10, "9:00 AM - 10:00 AM must be restricted as 2 hours advance preparation buffer.");
        Assert.True(slot10to11, "10:00 AM - 11:00 AM must be restricted as preparation buffer before 11:00 AM event.");
    }

    [Fact]
    public void SuccessWithDealWizard_GeneratesDaySlots_WithBookedAndBufferStates()
    {
        var page = new BookingNegotiations();
        var shootDate = DateTime.Today.AddDays(7);

        // Get slots for date with no bookings
        var freeSlots = page.GetWizardDaySlots(shootDate);
        Assert.NotEmpty(freeSlots);
        Assert.All(freeSlots, slot => Assert.True(slot.IsAvailable));

        // When a booked event exists on that date from 11:00 AM to 12:00 PM
        var bookingsField = typeof(BookingNegotiations).GetField("_studioBookings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var testBookings = new List<EventBookingModel>
        {
            new()
            {
                Id = "EVT-TEST-11AM",
                Date = shootDate,
                StartTime = new TimeSpan(11, 0, 0),
                EndTime = new TimeSpan(12, 0, 0),
                ServiceType = "Editorial Session",
                Status = "Confirmed"
            }
        };
        bookingsField?.SetValue(page, testBookings);

        var slotsWithEvent = page.GetWizardDaySlots(shootDate);

        // 1. 11:00 AM - 12:00 PM is Booked
        var bookedSlot = slotsWithEvent.First(s => s.Start == new TimeSpan(11, 0, 0));
        Assert.False(bookedSlot.IsAvailable);
        Assert.Equal(BookingNegotiations.SlotStatusType.Booked, bookedSlot.Status);
        Assert.Contains("Editorial Session", bookedSlot.Reason);

        // 2. 9:00 AM - 10:00 AM & 10:00 AM - 11:00 AM are 2h Prep Buffer
        var prepSlot1 = slotsWithEvent.First(s => s.Start == new TimeSpan(9, 0, 0));
        var prepSlot2 = slotsWithEvent.First(s => s.Start == new TimeSpan(10, 0, 0));
        Assert.False(prepSlot1.IsAvailable);
        Assert.Equal(BookingNegotiations.SlotStatusType.PrepBuffer, prepSlot1.Status);
        Assert.False(prepSlot2.IsAvailable);
        Assert.Equal(BookingNegotiations.SlotStatusType.PrepBuffer, prepSlot2.Status);

        // 3. 12:00 PM - 1:00 PM is 1h Post-Event Buffer
        var postSlot = slotsWithEvent.First(s => s.Start == new TimeSpan(12, 0, 0));
        Assert.False(postSlot.IsAvailable);
        Assert.Equal(BookingNegotiations.SlotStatusType.PostBuffer, postSlot.Status);

        // 4. 1:00 PM - 2:00 PM (13:00) is NOT restricted and is Available
        var availSlot = slotsWithEvent.First(s => s.Start == new TimeSpan(13, 0, 0));
        Assert.True(availSlot.IsAvailable);
        Assert.Equal(BookingNegotiations.SlotStatusType.Available, availSlot.Status);
    }

    [Fact]
    public void SuccessWithDealWizard_Booked9to10AM_Causes10to11AMBuffer()
    {
        var page = new BookingNegotiations();
        var shootDate = DateTime.Today.AddDays(7);

        var bookingsField = typeof(BookingNegotiations).GetField("_studioBookings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var testBookings = new List<EventBookingModel>
        {
            new()
            {
                Id = "EVT-TEST-9AM",
                Date = shootDate,
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(10, 0, 0),
                ServiceType = "Headshot Session",
                Status = "Confirmed"
            }
        };
        bookingsField?.SetValue(page, testBookings);

        var slots = page.GetWizardDaySlots(shootDate);

        // 9:00 AM - 10:00 AM is Booked
        var bookedSlot = slots.First(s => s.Start == new TimeSpan(9, 0, 0));
        Assert.False(bookedSlot.IsAvailable);
        Assert.Equal(BookingNegotiations.SlotStatusType.Booked, bookedSlot.Status);

        // 10:00 AM - 11:00 AM is 1h Buffer
        var bufferSlot = slots.First(s => s.Start == new TimeSpan(10, 0, 0));
        Assert.False(bufferSlot.IsAvailable);
        Assert.Equal(BookingNegotiations.SlotStatusType.PostBuffer, bufferSlot.Status);

        // 1:00 PM - 2:00 PM (13:00) is Available
        var afternoonSlot = slots.First(s => s.Start == new TimeSpan(13, 0, 0));
        Assert.True(afternoonSlot.IsAvailable);
    }

    [Fact]
    public void ServiceCustomization_PerHourConcurrentMaxDuration_AndDynamicReservationFee()
    {
        var svc1 = new ScheduleServiceOption("SRV-0001", "Portrait Photography", "Studio portrait", 2000m, "Per Hour");
        var svc2 = new ScheduleServiceOption("SRV-0002", "Event Videography", "Video coverage", 3000m, "Per Hour");
        var svc3 = new ScheduleServiceOption("SRV-0003", "Printed Portraits", "Edited shots", 250m, "Per Shot");

        var item1 = BookingWizardEngine.CreateCustomizationItem(svc1);
        item1.Quantity = 3; // 3 hours -> 6,000
        item1.IsConfirmed = true;

        var item2 = BookingWizardEngine.CreateCustomizationItem(svc2);
        item2.Quantity = 3; // 3 hours -> 9,000
        item2.IsConfirmed = true;

        var item3 = BookingWizardEngine.CreateCustomizationItem(svc3);
        item3.Quantity = 4; // 4 shots -> 1,000
        item3.IsConfirmed = true;

        var selections = new List<ServiceCustomizationItem> { item1, item2, item3 };

        // Combined duration of two 3-hour Per-Hour services is 3 hours (180 mins), not 6 hours
        var combinedMinutes = BookingWizardEngine.ComputeCombinedShootDurationMinutes(selections);
        Assert.Equal(180, combinedMinutes);

        var total = selections.Sum(s => s.LineTotal);
        Assert.Equal(16000m, total);

        var reservationFee = Math.Round(total * 0.15m, 2);
        Assert.Equal(2400m, reservationFee);
    }

    [Fact]
    public void MinuteLevelGranularity_AndBuffers_AllowDifferentCrewOverlap_BlockSameCrewOverlap()
    {
        var date = new DateTime(2026, 11, 20); // Friday

        var emp1 = new EmployeeViewModel { EmployeeId = "EMP-001", FirstName = "Elena", LastName = "Rostova", Status = "Active", DaysOff = "Sunday" };
        var emp2 = new EmployeeViewModel { EmployeeId = "EMP-002", FirstName = "Marcus", LastName = "Vance", Status = "Active", DaysOff = "Sunday" };

        // Existing event 10:15 AM - 11:35 AM assigned to Elena
        // Combined window for Elena: 8:15 AM (2h prep) to 12:35 PM (1h post)
        var existingBookings = new List<EventBookingModel>
        {
            new()
            {
                Id = "EVT-0001",
                StudioSpaceId = "studio-a",
                ClientName = "Client Alpha",
                ServiceType = "Editorial",
                Date = date,
                StartTime = new TimeSpan(10, 15, 0),
                EndTime = new TimeSpan(11, 35, 0),
                Status = "Pending Payment",
                AssignedPhotographer = "Elena Rostova",
                AssignedCrewIds = new List<string> { "EMP-001" },
                AssignedEquipmentIds = new List<string> { "EQP-0001" }
            }
        };

        // New event at overlapping minute-level time: 11:00 AM - 1:35 PM
        // New combined window: 9:00 AM - 2:35 PM
        var newStart = new TimeSpan(11, 0, 0);
        var newEnd = new TimeSpan(13, 35, 0);

        var available = BookingWizardEngine.GetAvailableCrews(
            new[] { emp1, emp2 },
            date,
            newStart,
            newEnd,
            existingBookings,
            null);

        // Elena is blocked due to combined Prep+Event+Post overlap; Marcus is available!
        Assert.Single(available);
        Assert.Equal("EMP-002", available[0].Employee.EmployeeId);
        Assert.Equal("Available all day", available[0].AvailabilityText);
    }

    [Fact]
    public void CrewAvailabilityText_FormatsOnwardAndBoundedWindowsAccurately()
    {
        var date = new DateTime(2026, 11, 21); // Saturday
        var emp = new EmployeeViewModel { EmployeeId = "EMP-010", FirstName = "Clara", LastName = "Oswald", Status = "Active", DaysOff = "Sunday" };

        // Morning event 9:00 AM - 11:00 AM -> Post buffer ends at 12:00 PM
        var morningBooking = new EventBookingModel
        {
            Id = "EVT-AM",
            Date = date,
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(11, 0, 0),
            Status = "Confirmed",
            AssignedCrewIds = new List<string> { "EMP-010" }
        };

        // Candidate event 2:00 PM - 3:00 PM -> Prep starts at 12:00 PM (no overlap!)
        var onwardText = BookingWizardEngine.GetCrewAvailabilityText(
            emp, date, new TimeSpan(14, 0, 0), new TimeSpan(15, 0, 0), new[] { morningBooking }, null);
        Assert.Equal("Available 12:00 PM onward", onwardText);

        // Add evening event 6:00 PM - 7:00 PM -> Prep starts at 4:00 PM
        var lateBooking = new EventBookingModel
        {
            Id = "EVT-LATE",
            Date = date,
            StartTime = new TimeSpan(18, 0, 0), // Prep starts at 4:00 PM
            EndTime = new TimeSpan(19, 0, 0),
            Status = "Confirmed",
            AssignedCrewIds = new List<string> { "EMP-010" }
        };
        var boundedText = BookingWizardEngine.GetCrewAvailabilityText(
            emp, date, new TimeSpan(14, 0, 0), new TimeSpan(15, 0, 0), new[] { morningBooking, lateBooking }, null);
        Assert.Equal("Available 12:00 PM - 4:00 PM", boundedText);
    }

    [Fact]
    public void ScheduleService_SameStudioOverlap_AllowedWithDifferentCrewAndEquipment_ConflictWhenCrewOrEquipmentOverlapsWithBuffers()
    {
        var service = new ScheduleService();
        var date = DateTime.Today.AddDays(20);

        // Event 1: Studio A, 10:00 AM - 12:00 PM, Crew EMP-1, Gear EQP-1
        var evt1 = new EventBookingModel
        {
            Id = "EVT-DIFF-1",
            StudioSpaceId = "studio-a",
            ClientName = "Client One",
            Date = date,
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(12, 0, 0),
            Status = "Pending Payment",
            AssignedPhotographer = "Elena Rostova",
            AssignedCrewIds = new List<string> { "EMP-1" },
            AssignedEquipmentIds = new List<string> { "EQP-1" }
        };

        // Event 2: Studio A (same studio space!), 10:30 AM - 12:30 PM, DIFFERENT Crew EMP-2 & DIFFERENT Gear EQP-2
        var evt2 = new EventBookingModel
        {
            Id = "EVT-DIFF-2",
            StudioSpaceId = "studio-a",
            ClientName = "Client Two",
            Date = date,
            StartTime = new TimeSpan(10, 30, 0),
            EndTime = new TimeSpan(12, 30, 0),
            Status = "Confirmed",
            AssignedPhotographer = "Marcus Vance",
            AssignedCrewIds = new List<string> { "EMP-2" },
            AssignedEquipmentIds = new List<string> { "EQP-2" }
        };

        service.AddEventBooking(evt1);
        service.AddEventBooking(evt2);

        var loaded1 = service.GetEventBookings().First(b => b.Id == "EVT-DIFF-1");
        var loaded2 = service.GetEventBookings().First(b => b.Id == "EVT-DIFF-2");
        Assert.False(loaded1.HasConflict);
        Assert.False(loaded2.HasConflict);
        Assert.Equal("Awaiting Reserve Fee Payment", loaded1.DisplayStatus);

        // Event 3: 12:30 PM - 1:30 PM with SAME Crew EMP-1 (conflicts with evt1's 1h post buffer until 1:00 PM!)
        var evt3 = new EventBookingModel
        {
            Id = "EVT-SAME-CREW-3",
            StudioSpaceId = "studio-a",
            ClientName = "Client Three",
            Date = date,
            StartTime = new TimeSpan(12, 30, 0),
            EndTime = new TimeSpan(13, 30, 0),
            Status = "Confirmed",
            AssignedPhotographer = "Elena Rostova",
            AssignedCrewIds = new List<string> { "EMP-1" },
            AssignedEquipmentIds = new List<string> { "EQP-3" }
        };

        service.AddEventBooking(evt3);
        var loaded1After = service.GetEventBookings().First(b => b.Id == "EVT-DIFF-1");
        var loaded3After = service.GetEventBookings().First(b => b.Id == "EVT-SAME-CREW-3");
        Assert.True(loaded1After.HasConflict);
        Assert.True(loaded3After.HasConflict);
    }

    [Fact]
    public void BookingWizardEngine_RespectsRateType_OnAdditionalAndPackageServices()
    {
        var addOnPic = new ScheduleServiceOption("ADD-0001", "Retouched High-Res Portrait", "Extra retouched portraits", 150m, "per picture");
        var addOnHourly = new ScheduleServiceOption("ADD-0002", "Extra Lighting Assistant", "Lighting crew add-on", 400m, "per hour");
        var pkgMulti = new ScheduleServiceOption("PKG-0001", "Lookbook Multi-Shot Bundle", "Full lookbook package", 2500m, "multi-shot");

        var item1 = BookingWizardEngine.CreateCustomizationItem(addOnPic);
        item1.Quantity = 4; // 4 pictures * 150 = 600
        item1.IsConfirmed = true;

        var item2 = BookingWizardEngine.CreateCustomizationItem(addOnHourly);
        item2.Quantity = 3; // 3 hours * 400 = 1200
        item2.IsConfirmed = true;

        var item3 = BookingWizardEngine.CreateCustomizationItem(pkgMulti);
        item3.Quantity = 2; // 2 multi-shot bundles * 2500 = 5000
        item3.IsConfirmed = true;

        var selections = new List<ServiceCustomizationItem> { item1, item2, item3 };

        Assert.Equal("per picture", item1.RateType);
        Assert.Equal(600m, item1.LineTotal);

        Assert.Equal("per hour", item2.RateType);
        Assert.Equal(1200m, item2.LineTotal);

        Assert.Equal("multi-shot", item3.RateType);
        Assert.Equal(5000m, item3.LineTotal);

        Assert.Equal(6800m, selections.Sum(s => s.LineTotal));
        // Only the 3-hour per-hour add-on contributes to shoot duration (180 mins), not the 4 pictures or 2 multi-shot bundles
        Assert.Equal(180, BookingWizardEngine.ComputeCombinedShootDurationMinutes(selections));
    }
}
