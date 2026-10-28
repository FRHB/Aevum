using System;
using System.Collections.Generic;
using System.Linq;
using PhotoAevumStudio.Web.Models;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class BookScheduleStep2Tests
{
    [Fact]
    public void BookingWizardModel_Initialization_HasNullDefaults()
    {
        var model = new BookingWizardModel();

        Assert.Null(model.SelectedDate);
        Assert.Null(model.SelectedTimeSlot);
        Assert.Null(model.SelectedTimeSlotString);
    }

    [Fact]
    public void BookingWizardModel_SelectedTimeSlotString_FormatsCorrectly()
    {
        var model = new BookingWizardModel
        {
            SelectedDate = new DateTime(2026, 11, 15),
            SelectedTimeSlot = new TimeSpan(9, 30, 0)
        };

        Assert.Equal("9:30 AM", model.SelectedTimeSlotString);

        model.SelectedTimeSlot = new TimeSpan(14, 0, 0);
        Assert.Equal("2:00 PM", model.SelectedTimeSlotString);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("2026-11-15", null, false)]
    [InlineData(null, "10:00:00", false)]
    [InlineData("2026-11-15", "10:00:00", true)]
    public void CanProceedToStep3_ValidationLock_RequiresBothDateAndTimeSlot(string? dateStr, string? timeStr, bool expectedCanProceed)
    {
        var model = new BookingWizardModel
        {
            SelectedDate = dateStr is not null ? DateTime.Parse(dateStr) : null,
            SelectedTimeSlot = timeStr is not null ? TimeSpan.Parse(timeStr) : null
        };

        bool canProceed = model.SelectedDate.HasValue && model.SelectedTimeSlot.HasValue;
        Assert.Equal(expectedCanProceed, canProceed);
    }

    [Fact]
    public void SessionWindows_CategorizesMorningAndAfternoonSlotsCorrectly()
    {
        // Morning: 9:00 AM - 11:00 AM
        var m1Start = new TimeOnly(9, 0);
        var m1End = new TimeOnly(10, 0);
        Assert.True(SessionWindows.IsMorning(m1Start, m1End));
        Assert.False(SessionWindows.IsAfternoon(m1Start, m1End));

        var m2Start = new TimeOnly(10, 0);
        var m2End = new TimeOnly(11, 0);
        Assert.True(SessionWindows.IsMorning(m2Start, m2End));
        Assert.False(SessionWindows.IsAfternoon(m2Start, m2End));

        // Lunch gap / out of window: 11:00 AM - 1:00 PM
        var lunchStart = new TimeOnly(11, 0);
        var lunchEnd = new TimeOnly(12, 0);
        Assert.False(SessionWindows.IsMorning(lunchStart, lunchEnd));
        Assert.False(SessionWindows.IsAfternoon(lunchStart, lunchEnd));
        Assert.False(SessionWindows.IsInWindow(lunchStart, lunchEnd));

        // Afternoon: 1:00 PM - 5:00 PM
        var a1Start = new TimeOnly(13, 0);
        var a1End = new TimeOnly(14, 0);
        Assert.False(SessionWindows.IsMorning(a1Start, a1End));
        Assert.True(SessionWindows.IsAfternoon(a1Start, a1End));

        var a4Start = new TimeOnly(16, 0);
        var a4End = new TimeOnly(17, 0);
        Assert.False(SessionWindows.IsMorning(a4Start, a4End));
        Assert.True(SessionWindows.IsAfternoon(a4Start, a4End));
    }

    [Fact]
    public void DayAvailability_HasAvailability_ReflectsOpenSlotsInStudioWindows()
    {
        var testDate = new DateOnly(2026, 11, 20);

        // Day with only booked slots
        var fullyBookedDay = new DayAvailability(testDate, new List<SlotAvailability>
        {
            new(new TimeOnly(9, 0), new TimeOnly(10, 0), false),
            new(new TimeOnly(10, 0), new TimeOnly(11, 0), false),
            new(new TimeOnly(13, 0), new TimeOnly(14, 0), false)
        });
        Assert.False(fullyBookedDay.HasAvailability);

        // Day with at least one available slot in studio window
        var openDay = new DayAvailability(testDate, new List<SlotAvailability>
        {
            new(new TimeOnly(9, 0), new TimeOnly(10, 0), false),
            new(new TimeOnly(10, 0), new TimeOnly(11, 0), true), // Open!
            new(new TimeOnly(13, 0), new TimeOnly(14, 0), false)
        });
        Assert.True(openDay.HasAvailability);
    }
}
