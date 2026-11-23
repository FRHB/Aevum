using PhotoAevumStudio.Application.Scheduling;
using PhotoAevumStudio.Infrastructure.Scheduling;
using Xunit;

namespace PhotoAevumStudio.Infrastructure.Tests;

public class AvailabilityCheckerTests
{
    private static readonly DateTime StudioNow = new(2026, 11, 16, 8, 0, 0);
    private static readonly DateOnly TargetDate = new(2026, 11, 18); // Wednesday

    private static readonly List<ScheduleBookingService.ClerkSummary> DefaultClerks =
    [
        new("CLK-1", "Sunday", "Office Clerk")
    ];

    private static readonly List<ScheduleBookingService.CrewMemberSummary> DefaultCrew =
    [
        new("EMP-P1", "Sunday", IsPhotographer: true, IsVideographer: false),
        new("EMP-V1", "Sunday", IsPhotographer: false, IsVideographer: true)
    ];

    [Fact]
    public void EvaluateAvailability_NoConflicts_ReturnsFullDayAvailable()
    {
        var result = ScheduleBookingService.EvaluateAvailability(
            TargetDate,
            null,
            StudioNow,
            bookingWindowDays: 90,
            takenConsultationSlots: new HashSet<TimeOnly>(),
            DefaultClerks,
            DefaultCrew,
            leaves: [],
            assignedEvents: [],
            bookedConsultations: []);

        Assert.True(result.IsFullDayAvailable);
        Assert.False(result.IsWholeDayUnavailable);
        Assert.Equal("The full day is available.", result.DayStatusMessage);
        Assert.Null(result.TimeStatusMessage);
    }

    [Fact]
    public void EvaluateAvailability_AllCrewOnApprovedLeave_ReturnsWholeDayNotAvailable()
    {
        var leaves = new List<ScheduleBookingService.LeaveSummary>
        {
            new("EMP-P1", TargetDate.AddDays(-1), TargetDate.AddDays(1)),
            new("EMP-V1", TargetDate, TargetDate)
        };

        var result = ScheduleBookingService.EvaluateAvailability(
            TargetDate,
            null,
            StudioNow,
            bookingWindowDays: 90,
            takenConsultationSlots: new HashSet<TimeOnly>(),
            DefaultClerks,
            DefaultCrew,
            leaves,
            assignedEvents: [],
            bookedConsultations: []);

        Assert.False(result.IsFullDayAvailable);
        Assert.True(result.IsWholeDayUnavailable);
        Assert.Equal("The whole day is not available.", result.DayStatusMessage);
    }

    [Fact]
    public void EvaluateAvailability_TargetTimeWithBothCrewFree_ReturnsBothAvailable()
    {
        var result = ScheduleBookingService.EvaluateAvailability(
            TargetDate,
            new TimeOnly(9, 0),
            StudioNow,
            bookingWindowDays: 90,
            takenConsultationSlots: new HashSet<TimeOnly>(),
            DefaultClerks,
            DefaultCrew,
            leaves: [],
            assignedEvents: [],
            bookedConsultations: []);

        Assert.True(result.IsPhotographyAvailableForTime);
        Assert.True(result.IsVideographyAvailableForTime);
        Assert.Equal("Both Photography and Videography available.", result.TimeStatusMessage);
    }

    [Fact]
    public void EvaluateAvailability_VideographerOnLeaveOrBooked_ReturnsPhotographyOnly()
    {
        var leaves = new List<ScheduleBookingService.LeaveSummary>
        {
            new("EMP-V1", TargetDate, TargetDate)
        };

        var result = ScheduleBookingService.EvaluateAvailability(
            TargetDate,
            new TimeOnly(10, 0),
            StudioNow,
            bookingWindowDays: 90,
            takenConsultationSlots: new HashSet<TimeOnly>(),
            DefaultClerks,
            DefaultCrew,
            leaves,
            assignedEvents: [],
            bookedConsultations: []);

        Assert.True(result.IsPhotographyAvailableForTime);
        Assert.False(result.IsVideographyAvailableForTime);
        Assert.Equal("Available for this time: Photography only", result.TimeStatusMessage);
    }

    [Fact]
    public void EvaluateAvailability_HalfHourBlockedByEventBuffer_ReturnsOnlySomeTimeSlotsForHour()
    {
        // Event at 15:30 has a 2-hour pre-buffer starting at 13:30.
        // For the 13:00-14:00 hour: 13:00-13:30 is free, 13:30-14:00 is blocked!
        var events = new List<ScheduleBookingService.EventSummary>
        {
            new("EMP-P1", TargetDate, new TimeOnly(15, 30)),
            new("EMP-V1", TargetDate, new TimeOnly(15, 30))
        };

        var result = ScheduleBookingService.EvaluateAvailability(
            TargetDate,
            new TimeOnly(13, 0),
            StudioNow,
            bookingWindowDays: 90,
            takenConsultationSlots: new HashSet<TimeOnly>(),
            DefaultClerks,
            DefaultCrew,
            leaves: [],
            assignedEvents: events,
            bookedConsultations: []);

        Assert.Equal("Only some time slots are available for this hour.", result.TimeStatusMessage);
    }

    private static readonly List<ScheduleBookingService.ShootCrewMemberSummary> DefaultShootCrew =
    [
        new("EMP-P1", "Sunday"),
        new("EMP-V1", "Sunday")
    ];

    [Fact]
    public void EvaluateShootDayAvailability_NoBookingsAndCrewAvailable_ReturnsFreeWithAllHourlyQuickSlots()
    {
        var day = ScheduleBookingService.EvaluateShootDayAvailability(
            TargetDate,
            durationMinutes: 60,
            DefaultShootCrew,
            leaves: [],
            shootEvents: []);

        Assert.Equal(ShootDayStatus.Free, day.Status);
        Assert.False(day.HasBookings);
        Assert.Equal(2, day.EligibleCrewCount);
        Assert.Single(day.FeasibleStartRanges);
        Assert.Equal(new TimeOnly(8, 0), day.FeasibleStartRanges[0].Start);
        Assert.Equal(new TimeOnly(19, 0), day.FeasibleStartRanges[0].End);
        Assert.Equal(12, day.QuickSlots.Count);
        Assert.All(day.QuickSlots, q => Assert.Equal(2, q.AvailableCrewCount));
    }

    [Fact]
    public void EvaluateShootDayAvailability_WithBookingsAndRemainingSlots_ReturnsYellowBookingsAvailable()
    {
        var singleCrew = new List<ScheduleBookingService.ShootCrewMemberSummary> { new("EMP-P1", "Sunday") };
        var events = new List<ScheduleBookingService.ShootEventSummary>
        {
            new("EVT-0001", TargetDate, new TimeSpan(9, 0, 0), new TimeSpan(10, 0, 0), ["EMP-P1"])
        };

        var day = ScheduleBookingService.EvaluateShootDayAvailability(
            TargetDate,
            durationMinutes: 60,
            singleCrew,
            leaves: [],
            events);

        Assert.Equal(ShootDayStatus.BookingsAvailable, day.Status);
        Assert.True(day.HasBookings);
        Assert.Single(day.FeasibleStartRanges);
        // 9-10 AM blocks 7-11 AM; next feasible shoot needs 2h prep -> starts at 1:00 PM (13:00) through 7:00 PM (19:00)
        Assert.Equal(new TimeOnly(13, 0), day.FeasibleStartRanges[0].Start);
        Assert.Equal(new TimeOnly(19, 0), day.FeasibleStartRanges[0].End);
        Assert.Equal(new TimeOnly(13, 0), day.QuickSlots[0].Start);
        Assert.Equal(new TimeOnly(19, 0), day.QuickSlots[^1].Start);
    }

    [Fact]
    public void EvaluateShootDayAvailability_TightMiddleGapLackingBufferTime_ReturnsRedFullyBooked()
    {
        var singleCrew = new List<ScheduleBookingService.ShootCrewMemberSummary> { new("EMP-P1", "Sunday") };
        // Morning 08:00-09:30 -> blocked [06:00, 10:30]
        // Afternoon 16:00-18:00 -> blocked [14:00, 19:00]
        // Middle gap [10:30, 14:00] = 3h 30m (insufficient for 1h shoot + 2h prep + 1h post = 4h)
        // Evening after 19:00 -> earliest start 21:00 > 20:00
        var events = new List<ScheduleBookingService.ShootEventSummary>
        {
            new("EVT-0001", TargetDate, new TimeSpan(8, 0, 0), new TimeSpan(9, 30, 0), ["EMP-P1"]),
            new("EVT-0002", TargetDate, new TimeSpan(16, 0, 0), new TimeSpan(18, 0, 0), ["EMP-P1"])
        };

        var day = ScheduleBookingService.EvaluateShootDayAvailability(
            TargetDate,
            durationMinutes: 60,
            singleCrew,
            leaves: [],
            events);

        Assert.Equal(ShootDayStatus.FullyBooked, day.Status);
        Assert.True(day.HasBookings);
        Assert.Empty(day.FeasibleStartRanges);
        Assert.Empty(day.QuickSlots);
    }

    [Fact]
    public void EvaluateShootDayAvailability_ExactFourHourMiddleGap_YellowFor60Min_RedFor120Min()
    {
        var singleCrew = new List<ScheduleBookingService.ShootCrewMemberSummary> { new("EMP-P1", "Sunday") };
        // Morning 08:00-09:00 -> blocked [06:00, 10:00]
        // Afternoon 16:00-17:00 -> blocked [14:00, 18:00]
        // Exact 4h gap [10:00, 14:00] fits 1h shoot at 12:00-13:00 (buffered [10:00, 14:00]), NOT a 2h shoot (needs 5h)
        var events = new List<ScheduleBookingService.ShootEventSummary>
        {
            new("EVT-0001", TargetDate, new TimeSpan(8, 0, 0), new TimeSpan(9, 0, 0), ["EMP-P1"]),
            new("EVT-0002", TargetDate, new TimeSpan(16, 0, 0), new TimeSpan(17, 0, 0), ["EMP-P1"])
        };

        var day60 = ScheduleBookingService.EvaluateShootDayAvailability(TargetDate, 60, singleCrew, [], events);
        Assert.Equal(ShootDayStatus.BookingsAvailable, day60.Status);
        Assert.Single(day60.FeasibleStartRanges);
        Assert.Equal(new TimeOnly(12, 0), day60.FeasibleStartRanges[0].Start);
        Assert.Equal(new TimeOnly(12, 0), day60.FeasibleStartRanges[0].End);
        Assert.Single(day60.QuickSlots);
        Assert.Equal(new TimeOnly(12, 0), day60.QuickSlots[0].Start);
        Assert.Equal(new TimeOnly(13, 0), day60.QuickSlots[0].End);

        var day120 = ScheduleBookingService.EvaluateShootDayAvailability(TargetDate, 120, singleCrew, [], events);
        Assert.Equal(ShootDayStatus.FullyBooked, day120.Status);
        Assert.Empty(day120.FeasibleStartRanges);
        Assert.Empty(day120.QuickSlots);
    }

    [Fact]
    public void EvaluateShootDayAvailability_ZeroCrew_DayOffOrAllOnLeaveOrClerkOnly_ReturnsRedFullyBooked()
    {
        var sunday = new DateOnly(2026, 11, 22); // Sunday
        var sundayResult = ScheduleBookingService.EvaluateShootDayAvailability(sunday, 60, DefaultShootCrew, [], []);
        Assert.Equal(ShootDayStatus.FullyBooked, sundayResult.Status);
        Assert.False(sundayResult.HasBookings);
        Assert.Equal(0, sundayResult.EligibleCrewCount);

        var allLeaves = new List<ScheduleBookingService.LeaveSummary>
        {
            new("EMP-P1", TargetDate, TargetDate),
            new("EMP-V1", TargetDate, TargetDate)
        };
        var leaveResult = ScheduleBookingService.EvaluateShootDayAvailability(TargetDate, 60, DefaultShootCrew, allLeaves, []);
        Assert.Equal(ShootDayStatus.FullyBooked, leaveResult.Status);
        Assert.Equal(0, leaveResult.EligibleCrewCount);

        var clerkOnly = new List<ScheduleBookingService.ShootCrewMemberSummary> { new("CLK-001", "Monday") };
        var clerkResult = ScheduleBookingService.EvaluateShootDayAvailability(TargetDate, 60, clerkOnly, [], []);
        Assert.Equal(ShootDayStatus.FullyBooked, clerkResult.Status);
        Assert.Equal(0, clerkResult.EligibleCrewCount);
    }

    [Fact]
    public void EvaluateShootMonthAvailability_MultiCrewAndStatuses_ComputesMonthAccurately()
    {
        var nov18 = new DateOnly(2026, 11, 18); // Free Wednesday
        var nov19 = new DateOnly(2026, 11, 19); // Yellow: EMP-P1 full day, EMP-V1 booked 8-12 -> free 15-20
        var nov20 = new DateOnly(2026, 11, 20); // Red: EMP-P1 tight gap, EMP-V1 on approved leave

        var leaves = new List<ScheduleBookingService.LeaveSummary>
        {
            new("EMP-V1", nov20, nov20)
        };
        var events = new List<ScheduleBookingService.ShootEventSummary>
        {
            new("EVT-1", nov19, new TimeSpan(8, 0, 0), new TimeSpan(19, 0, 0), ["EMP-P1"]),
            new("EVT-2", nov19, new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0), ["EMP-V1"]),
            new("EVT-3", nov20, new TimeSpan(8, 0, 0), new TimeSpan(9, 30, 0), ["EMP-P1"]),
            new("EVT-4", nov20, new TimeSpan(16, 0, 0), new TimeSpan(18, 0, 0), ["EMP-P1"])
        };

        var month = ScheduleBookingService.EvaluateShootMonthAvailability(2026, 11, 60, DefaultShootCrew, leaves, events);

        Assert.Equal(30, month.Days.Count);
        Assert.Equal(ShootDayStatus.Free, month.Days.First(d => d.Date == nov18).Status);
        var d19 = month.Days.First(d => d.Date == nov19);
        Assert.Equal(ShootDayStatus.BookingsAvailable, d19.Status);
        Assert.All(d19.QuickSlots, q => Assert.Equal(1, q.AvailableCrewCount));
        Assert.Equal(new TimeOnly(15, 0), d19.QuickSlots[0].Start);
        Assert.Equal(ShootDayStatus.FullyBooked, month.Days.First(d => d.Date == nov20).Status);
        Assert.Equal(ShootDayStatus.FullyBooked, month.Days.First(d => d.Date == new DateOnly(2026, 11, 22)).Status); // Sunday
    }
}
