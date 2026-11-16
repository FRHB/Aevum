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
}
