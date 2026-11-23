using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using PhotoAevumStudio.Api.Controllers;
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

    [Fact]
    public void BookingWizardEngine_RateTypeLabelsAndPlaceholders_GeneralizeMultiShotAndKeepPerHour()
    {
        Assert.Equal("Hours Quantity *", BookingWizardEngine.GetRateQuantityLabel("per hour"));
        Assert.Contains("Billed per hour", BookingWizardEngine.GetRateBillingSummary("per hour", 2));
        Assert.Equal("hr(s)", BookingWizardEngine.GetRateUnitLabel("per hour"));

        Assert.Equal("Service Quantity *", BookingWizardEngine.GetRateQuantityLabel("multi-shot"));
        Assert.Equal("Qty (depends on service)", BookingWizardEngine.GetRateQuantityPlaceholder("multi-shot"));
        var multiShotSummary = BookingWizardEngine.GetRateBillingSummary("multi-shot", 1);
        Assert.Contains("based on the service provided", multiShotSummary);
        Assert.DoesNotContain("Billed per shot / picture", multiShotSummary);
        Assert.Equal("unit(s)", BookingWizardEngine.GetRateUnitLabel("multi-shot"));

        Assert.Equal("Pictures Quantity *", BookingWizardEngine.GetRateQuantityLabel("per picture"));
        Assert.Equal("picture(s)", BookingWizardEngine.GetRateUnitLabel("per picture"));

        Assert.Equal("Shots Quantity *", BookingWizardEngine.GetRateQuantityLabel("per single shot"));
        Assert.Equal("shot(s)", BookingWizardEngine.GetRateUnitLabel("per single shot"));
    }

    [Fact]
    public void MyBookings_OrderCustomerBookings_PlacesLatestActiveBookingOnTop()
    {
        var olderCancelledFutureSlot = new MyBooking(
            AppointmentId: 11,
            ServiceTitle: "Photo Enhancement, Professional Headshot",
            Date: new DateOnly(2026, 11, 24),
            SlotStart: new TimeOnly(16, 0),
            SlotEnd: new TimeOnly(17, 0),
            Status: "Cancelled",
            TotalPrice: 1250m,
            Platform: "Zoom",
            MeetingLink: null,
            EventLocation: null,
            CreatedAt: new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc));

        var oldestConfirmed = new MyBooking(
            AppointmentId: 10,
            ServiceTitle: "Individual Portrait",
            Date: new DateOnly(2026, 11, 20),
            SlotStart: new TimeOnly(13, 0),
            SlotEnd: new TimeOnly(14, 0),
            Status: "Confirmed",
            TotalPrice: 1500m,
            Platform: "InStudio",
            MeetingLink: null,
            EventLocation: "SM Fairview",
            CreatedAt: new DateTime(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc));

        var latestConfirmed = new MyBooking(
            AppointmentId: 12,
            ServiceTitle: "Individual Portrait",
            Date: new DateOnly(2026, 11, 23),
            SlotStart: new TimeOnly(15, 0),
            SlotEnd: new TimeOnly(16, 0),
            Status: "Confirmed",
            TotalPrice: 1500m,
            Platform: "GoogleMeet",
            MeetingLink: "https://meet.google.com/abc-defg-hij",
            EventLocation: null,
            CreatedAt: new DateTime(2026, 11, 22, 11, 0, 0, DateTimeKind.Utc));

        var ordered = PhotoAevumStudio.Web.Components.Pages.MyBookings.OrderCustomerBookings(
            new[] { olderCancelledFutureSlot, oldestConfirmed, latestConfirmed });

        Assert.Equal(12, ordered[0].AppointmentId);
        Assert.Equal(10, ordered[1].AppointmentId);
        Assert.Equal(11, ordered[2].AppointmentId);
    }

    [Fact]
    public void BookingWizardEngine_DayStatus_FreeVsYellowVsRed_HandlesConsultationsTightGapsAndZeroCrew()
    {
        var wednesday = new DateTime(2026, 11, 25);
        var sunday = new DateTime(2026, 11, 29);

        var emp1 = new EmployeeViewModel { EmployeeId = "EMP-001", FirstName = "Elena", LastName = "Rostova", Status = "Active", DaysOff = "Sunday" };
        var clerk = new EmployeeViewModel { EmployeeId = "CLK-001", FirstName = "Clara", LastName = "Reyes", Role = "Office Clerk", Status = "Active", DaysOff = "Monday" };
        var staff = new[] { emp1, clerk };

        // 1. Consultation-only day (IsConsultation = true) -> Free (no dot)
        var consultationOnly = new List<EventBookingModel>
        {
            new()
            {
                Id = "NEG-0001",
                Date = wednesday,
                StartTime = new TimeSpan(10, 0, 0),
                EndTime = new TimeSpan(10, 30, 0),
                Status = "Pending_Call",
                IsConsultation = true,
                AssignedCrewIds = new List<string> { "CLK-001" }
            }
        };
        var freeDay = BookingWizardEngine.EvaluateDayAvailability(wednesday, 60, staff, consultationOnly, null);
        Assert.Equal(ShootDayStatus.Free, freeDay.Status);
        Assert.False(freeDay.HasBookings);
        Assert.Equal(12, freeDay.QuickSlots.Count);

        // 2. Minute-level off-hour middle gap: 08:15-09:15 (post 10:15) + 16:15-18:00 (prep 14:15) -> exact 12:15 PM - 1:15 PM fits!
        var minuteGapBookings = new List<EventBookingModel>
        {
            new() { Id = "EVT-1", Date = wednesday, StartTime = new TimeSpan(8, 15, 0), EndTime = new TimeSpan(9, 15, 0), Status = "Confirmed", AssignedCrewIds = new List<string> { "EMP-001" } },
            new() { Id = "EVT-2", Date = wednesday, StartTime = new TimeSpan(16, 15, 0), EndTime = new TimeSpan(18, 0, 0), Status = "Confirmed", AssignedCrewIds = new List<string> { "EMP-001" } }
        };
        var yellowMinuteDay = BookingWizardEngine.EvaluateDayAvailability(wednesday, 60, staff, minuteGapBookings, null);
        Assert.Equal(ShootDayStatus.BookingsAvailable, yellowMinuteDay.Status);
        Assert.True(yellowMinuteDay.HasBookings);
        Assert.Contains(yellowMinuteDay.QuickSlots, s => s.Start == new TimeOnly(12, 15) && s.End == new TimeOnly(13, 15));

        // 3. Tight middle gap lacking buffer time: 08:00-09:30 (post 10:30) + 16:00-18:00 (prep 14:00) -> 3h30m gap < 4h -> Red
        var tightGapBookings = new List<EventBookingModel>
        {
            new() { Id = "EVT-1", Date = wednesday, StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(9, 30, 0), Status = "Confirmed", AssignedCrewIds = new List<string> { "EMP-001" } },
            new() { Id = "EVT-2", Date = wednesday, StartTime = new TimeSpan(16, 0, 0), EndTime = new TimeSpan(18, 0, 0), Status = "Confirmed", AssignedCrewIds = new List<string> { "EMP-001" } }
        };
        var redTightDay = BookingWizardEngine.EvaluateDayAvailability(wednesday, 60, staff, tightGapBookings, null);
        Assert.Equal(ShootDayStatus.FullyBooked, redTightDay.Status);
        Assert.Empty(redTightDay.QuickSlots);

        // 4. Zero-crew cases: Sunday Day Off & Approved Leave -> Red
        var sundayRed = BookingWizardEngine.EvaluateDayAvailability(sunday, 60, staff, null, null);
        Assert.Equal(ShootDayStatus.FullyBooked, sundayRed.Status);
        Assert.Equal(0, sundayRed.EligibleCrewCount);

        var leaves = new List<LeaveItemViewModel>
        {
            new() { EmployeeStudioId = "EMP-001", Status = "Approved", StartDate = DateOnly.FromDateTime(wednesday), EndDate = DateOnly.FromDateTime(wednesday) }
        };
        var leaveRed = BookingWizardEngine.EvaluateDayAvailability(wednesday, 60, staff, null, null, leaves);
        Assert.Equal(ShootDayStatus.FullyBooked, leaveRed.Status);
        Assert.Equal(0, leaveRed.EligibleCrewCount);
    }

    [Fact]
    public void BookingWizardEngine_ValidateScheduleSelection_PreventsTightMiddleGap_OutOfHours_AndInsufficientDuration()
    {
        var wednesday = new DateTime(2026, 11, 25);
        var emp1 = new EmployeeViewModel { EmployeeId = "EMP-001", FirstName = "Elena", LastName = "Rostova", Status = "Active", DaysOff = "Sunday" };
        var staff = new[] { emp1 };
        var bookings = new List<EventBookingModel>
        {
            new() { Id = "EVT-1", Date = wednesday, StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(9, 30, 0), Status = "Confirmed", AssignedCrewIds = new List<string> { "EMP-001" } },
            new() { Id = "EVT-2", Date = wednesday, StartTime = new TimeSpan(16, 0, 0), EndTime = new TimeSpan(18, 0, 0), Status = "Confirmed", AssignedCrewIds = new List<string> { "EMP-001" } }
        };

        // Middle gap 12:00 PM - 1:00 PM lacks 2h prep / 1h post buffers
        var gapCheck = BookingWizardEngine.ValidateScheduleSelection(
            wednesday, new TimeSpan(12, 0, 0), new TimeSpan(13, 0, 0), 60, staff, bookings, null);
        Assert.False(gapCheck.IsValid);
        Assert.Contains("buffer", gapCheck.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Outside operating hours (8 AM - 8 PM) even on an empty day
        var earlyCheck = BookingWizardEngine.ValidateScheduleSelection(
            wednesday, new TimeSpan(7, 30, 0), new TimeSpan(8, 30, 0), 60, staff, null, null);
        Assert.False(earlyCheck.IsValid);
        Assert.Contains("8:00 AM", earlyCheck.ErrorMessage);

        var lateCheck = BookingWizardEngine.ValidateScheduleSelection(
            wednesday, new TimeSpan(19, 15, 0), new TimeSpan(20, 15, 0), 60, staff, null, null);
        Assert.False(lateCheck.IsValid);
        Assert.Contains("8:00 PM", lateCheck.ErrorMessage);

        // Shorter than required service duration (e.g. 2h service selected, user enters 1h)
        var shortCheck = BookingWizardEngine.ValidateScheduleSelection(
            wednesday, new TimeSpan(10, 0, 0), new TimeSpan(11, 0, 0), 120, staff, null, null);
        Assert.False(shortCheck.IsValid);
        Assert.Contains("120 min", shortCheck.ErrorMessage);

        // Valid 8:00 AM - 9:00 AM and 7:00 PM - 8:00 PM boundaries on open day
        Assert.True(BookingWizardEngine.ValidateScheduleSelection(wednesday, new TimeSpan(8, 0, 0), new TimeSpan(9, 0, 0), 60, staff, null, null).IsValid);
        Assert.True(BookingWizardEngine.ValidateScheduleSelection(wednesday, new TimeSpan(19, 0, 0), new TimeSpan(20, 0, 0), 60, staff, null, null).IsValid);
    }

    [Fact]
    public void BookingNegotiations_SuccessWizard_UsesFreeYellowRedDayStatuses_AndBlocksInvalidMiddleGap()
    {
        var page = new BookingNegotiations();
        var baseDate = DateTime.Today.AddDays(7);
        // Ensure deterministic non-Sunday test dates
        if (baseDate.DayOfWeek == DayOfWeek.Sunday) baseDate = baseDate.AddDays(1);
        var freeDate = baseDate;
        var yellowDate = baseDate.AddDays(1);
        if (yellowDate.DayOfWeek == DayOfWeek.Sunday) yellowDate = yellowDate.AddDays(1);
        var redDate = yellowDate.AddDays(1);
        if (redDate.DayOfWeek == DayOfWeek.Sunday) redDate = redDate.AddDays(1);

        var employeesField = typeof(BookingNegotiations).GetField("_availableEmployees", BindingFlags.NonPublic | BindingFlags.Instance);
        var bookingsField = typeof(BookingNegotiations).GetField("_studioBookings", BindingFlags.NonPublic | BindingFlags.Instance);
        var servicesField = typeof(BookingNegotiations).GetField("_serviceSelections", BindingFlags.NonPublic | BindingFlags.Instance);
        var selectedServicesField = typeof(BookingNegotiations).GetField("_selectedServiceIds", BindingFlags.NonPublic | BindingFlags.Instance);
        var eventNameField = typeof(BookingNegotiations).GetField("_wizardEventName", BindingFlags.NonPublic | BindingFlags.Instance);
        var locationField = typeof(BookingNegotiations).GetField("_wizardEventLocation", BindingFlags.NonPublic | BindingFlags.Instance);
        var eventDateField = typeof(BookingNegotiations).GetField("_wizardEventDate", BindingFlags.NonPublic | BindingFlags.Instance);
        var eventTimeField = typeof(BookingNegotiations).GetField("_wizardEventTime", BindingFlags.NonPublic | BindingFlags.Instance);
        var eventEndTimeField = typeof(BookingNegotiations).GetField("_wizardEventEndTime", BindingFlags.NonPublic | BindingFlags.Instance);
        var wizardErrorField = typeof(BookingNegotiations).GetField("_wizardError", BindingFlags.NonPublic | BindingFlags.Instance);
        var wizardStepField = typeof(BookingNegotiations).GetField("_wizardStep", BindingFlags.NonPublic | BindingFlags.Instance);
        var nextStepMethod = typeof(BookingNegotiations).GetMethod("NextWizardStep", BindingFlags.NonPublic | BindingFlags.Instance);

        var crew = new List<EmployeeViewModel>
        {
            new() { EmployeeId = "EMP-001", FirstName = "Elena", LastName = "Rostova", Status = "Active", DaysOff = "Sunday" }
        };
        var bookings = new List<EventBookingModel>
        {
            // Yellow day: 1 booking 09:00-10:00 (still has 1:00 PM - 8:00 PM open)
            new() { Id = "EVT-Y1", Date = yellowDate, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(10, 0, 0), Status = "Confirmed", AssignedCrewIds = new List<string> { "EMP-001" } },
            // Red day: tight middle gap 10:30-14:00 (insufficient for 1h + 2h prep + 1h post)
            new() { Id = "EVT-R1", Date = redDate, StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(9, 30, 0), Status = "Confirmed", AssignedCrewIds = new List<string> { "EMP-001" } },
            new() { Id = "EVT-R2", Date = redDate, StartTime = new TimeSpan(16, 0, 0), EndTime = new TimeSpan(18, 0, 0), Status = "Confirmed", AssignedCrewIds = new List<string> { "EMP-001" } }
        };
        var selections = new List<ServiceCustomizationItem>
        {
            new() { ServicesId = "SRV-0001", Name = "Portrait", RateType = "Per Hour", UnitPrice = 2000m, Quantity = 1, DurationHours = 1, IsConfirmed = true }
        };

        employeesField?.SetValue(page, crew);
        bookingsField?.SetValue(page, bookings);
        servicesField?.SetValue(page, selections);
        selectedServicesField?.SetValue(page, new HashSet<string> { "SRV-0001" });

        Assert.Equal(ShootDayStatus.Free, page.GetWizardDayAvailability(freeDate).Status);
        Assert.Equal(ShootDayStatus.BookingsAvailable, page.GetWizardDayAvailability(yellowDate).Status);
        Assert.Equal(ShootDayStatus.FullyBooked, page.GetWizardDayAvailability(redDate).Status);

        // Attempting NextWizardStep in tight middle gap on redDate must fail and stay on step 1
        wizardStepField?.SetValue(page, 1);
        eventNameField?.SetValue(page, "Test Shoot");
        locationField?.SetValue(page, "Studio A");
        eventDateField?.SetValue(page, redDate);
        eventTimeField?.SetValue(page, redDate.Date.AddHours(12));
        eventEndTimeField?.SetValue(page, redDate.Date.AddHours(13));

        nextStepMethod?.Invoke(page, null);
        Assert.Equal(1, (int)wizardStepField!.GetValue(page)!);
        Assert.False(string.IsNullOrWhiteSpace(wizardErrorField?.GetValue(page) as string));

        // Picking a feasible quick-pick slot on yellowDate allows advancing to step 2
        var yellowAvail = page.GetWizardDayAvailability(yellowDate);
        Assert.NotEmpty(yellowAvail.QuickSlots);
        var selectQuickSlotMethod = typeof(BookingNegotiations).GetMethod("SelectWizardQuickSlot", BindingFlags.NonPublic | BindingFlags.Instance);
        // Set date to yellowDate and invoke SelectWizardQuickSlot on its first feasible slot (1:00 PM - 2:00 PM)
        eventDateField?.SetValue(page, yellowDate);
        eventTimeField?.SetValue(page, yellowDate.Date.Add(yellowAvail.QuickSlots[0].Start.ToTimeSpan()));
        eventEndTimeField?.SetValue(page, yellowDate.Date.Add(yellowAvail.QuickSlots[0].End.ToTimeSpan()));
        Assert.True(page.ValidateWizardScheduleSelection().IsValid);
        nextStepMethod?.Invoke(page, null);
        Assert.Equal(2, (int)wizardStepField.GetValue(page)!);

        // Verify ScheduleController endpoint policy and BookingNegotiations calendar legend/dots markup
        var controllerMethod = typeof(ScheduleController).GetMethod(nameof(ScheduleController.GetShootMonthAvailability));
        Assert.NotNull(controllerMethod);
        var authAttr = controllerMethod!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.Equal("ClerkOrAdmin", authAttr!.Policy);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var negRazor = File.ReadAllText(Path.Combine(dir!.FullName, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "BookingNegotiations.razor"));
        Assert.Contains("Bookings • Slots Available", negRazor);
        Assert.Contains("Fully Booked 8 AM–8 PM", negRazor);
        Assert.Contains("data-day-status=\"yellow\"", negRazor);
        Assert.Contains("data-day-status=\"red\"", negRazor);
    }
}
