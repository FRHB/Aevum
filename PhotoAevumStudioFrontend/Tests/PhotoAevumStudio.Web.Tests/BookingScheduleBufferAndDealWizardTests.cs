using System;
using System.Collections.Generic;
using System.Linq;
using PhotoAevumStudio.Infrastructure.Scheduling;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Models;
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
}
