using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Infrastructure.Scheduling;
using PhotoAevumStudio.Infrastructure.Services;
using Xunit;

namespace PhotoAevumStudio.Infrastructure.Tests;

public class CustomerRescheduleAndStatusTests
{
    private static readonly DateOnly TargetDate = new(2026, 11, 25); // Wednesday

    [Fact]
    public void OfficeClerkAssignmentResolver_RetainsOriginalClerk_WhenAvailableOnNewDateAndTime()
    {
        var clerks = new[]
        {
            new ClerkCandidate("EMP-CLK-1", "Maria Santos", "Sunday, Saturday"),
            new ClerkCandidate("EMP-CLK-2", "Juan Dela Cruz", "Sunday, Monday")
        };

        var result = OfficeClerkAssignmentResolver.ResolveClerkForReschedule(
            originalClerkId: "EMP-CLK-1",
            activeClerks: clerks,
            leaves: Array.Empty<ClerkLeaveWindow>(),
            busyWindows: Array.Empty<ClerkBusyWindow>(),
            targetDate: TargetDate,
            targetStart: new TimeOnly(10, 0),
            targetEnd: new TimeOnly(11, 0));

        Assert.True(result.IsAvailable);
        Assert.True(result.RetainedOriginalClerk);
        Assert.Equal("EMP-CLK-1", result.ClerkId);
        Assert.Equal("Maria Santos", result.ClerkName);
    }

    [Fact]
    public void OfficeClerkAssignmentResolver_ReassignsAnotherClerk_WhenOriginalClerkOnDayOff()
    {
        var clerks = new[]
        {
            new ClerkCandidate("EMP-CLK-1", "Maria Santos", "Wednesday"),
            new ClerkCandidate("EMP-CLK-2", "Juan Dela Cruz", "Sunday")
        };

        var result = OfficeClerkAssignmentResolver.ResolveClerkForReschedule(
            originalClerkId: "EMP-CLK-1",
            activeClerks: clerks,
            leaves: Array.Empty<ClerkLeaveWindow>(),
            busyWindows: Array.Empty<ClerkBusyWindow>(),
            targetDate: TargetDate, // Wednesday
            targetStart: new TimeOnly(10, 0),
            targetEnd: new TimeOnly(11, 0));

        Assert.True(result.IsAvailable);
        Assert.False(result.RetainedOriginalClerk);
        Assert.Equal("EMP-CLK-2", result.ClerkId);
        Assert.Equal("Juan Dela Cruz", result.ClerkName);
    }

    [Fact]
    public void OfficeClerkAssignmentResolver_ReassignsAnotherClerk_WhenOriginalClerkOnApprovedLeave()
    {
        var clerks = new[]
        {
            new ClerkCandidate("EMP-CLK-1", "Maria Santos", "Sunday"),
            new ClerkCandidate("EMP-CLK-2", "Juan Dela Cruz", "Sunday")
        };

        var leaves = new[]
        {
            new ClerkLeaveWindow("EMP-CLK-1", new DateOnly(2026, 11, 24), new DateOnly(2026, 11, 26))
        };

        var result = OfficeClerkAssignmentResolver.ResolveClerkForReschedule(
            originalClerkId: "EMP-CLK-1",
            activeClerks: clerks,
            leaves: leaves,
            busyWindows: Array.Empty<ClerkBusyWindow>(),
            targetDate: TargetDate,
            targetStart: new TimeOnly(14, 0),
            targetEnd: new TimeOnly(15, 0));

        Assert.True(result.IsAvailable);
        Assert.False(result.RetainedOriginalClerk);
        Assert.Equal("EMP-CLK-2", result.ClerkId);
    }

    [Fact]
    public void OfficeClerkAssignmentResolver_ReassignsAnotherClerk_WhenOriginalClerkHasBufferedOverlap()
    {
        var clerks = new[]
        {
            new ClerkCandidate("EMP-CLK-1", "Maria Santos", "Sunday"),
            new ClerkCandidate("EMP-CLK-2", "Juan Dela Cruz", "Sunday")
        };

        var busy = new[]
        {
            new ClerkBusyWindow("EMP-CLK-1", TargetDate, new TimeOnly(10, 0), new TimeOnly(11, 0), "NEG-OTHER")
        };

        var result = OfficeClerkAssignmentResolver.ResolveClerkForReschedule(
            originalClerkId: "EMP-CLK-1",
            activeClerks: clerks,
            leaves: Array.Empty<ClerkLeaveWindow>(),
            busyWindows: busy,
            targetDate: TargetDate,
            targetStart: new TimeOnly(11, 10), // Within 15-min post buffer of 10:00-11:00
            targetEnd: new TimeOnly(12, 10),
            excludeSourceId: "NEG-SELF");

        Assert.True(result.IsAvailable);
        Assert.False(result.RetainedOriginalClerk);
        Assert.Equal("EMP-CLK-2", result.ClerkId);
    }

    [Fact]
    public void OfficeClerkAssignmentResolver_RetainsOriginalClerk_WhenOnlyOverlapIsSelfAppointment()
    {
        var clerks = new[]
        {
            new ClerkCandidate("EMP-CLK-1", "Maria Santos", "Sunday"),
            new ClerkCandidate("EMP-CLK-2", "Juan Dela Cruz", "Sunday")
        };

        var busy = new[]
        {
            new ClerkBusyWindow("EMP-CLK-1", TargetDate, new TimeOnly(10, 0), new TimeOnly(11, 0), "NEG-SELF")
        };

        var result = OfficeClerkAssignmentResolver.ResolveClerkForReschedule(
            originalClerkId: "EMP-CLK-1",
            activeClerks: clerks,
            leaves: Array.Empty<ClerkLeaveWindow>(),
            busyWindows: busy,
            targetDate: TargetDate,
            targetStart: new TimeOnly(10, 30),
            targetEnd: new TimeOnly(11, 30),
            excludeSourceId: "NEG-SELF");

        Assert.True(result.IsAvailable);
        Assert.True(result.RetainedOriginalClerk);
        Assert.Equal("EMP-CLK-1", result.ClerkId);
    }

    [Fact]
    public void OfficeClerkAssignmentResolver_ReturnsUnavailable_WhenAllClerksBusyOrOnLeave()
    {
        var clerks = new[]
        {
            new ClerkCandidate("EMP-CLK-1", "Maria Santos", "Wednesday"),
            new ClerkCandidate("EMP-CLK-2", "Juan Dela Cruz", "Sunday")
        };

        var busy = new[]
        {
            new ClerkBusyWindow("EMP-CLK-2", TargetDate, new TimeOnly(13, 0), new TimeOnly(14, 0), "NEG-OTHER")
        };

        var result = OfficeClerkAssignmentResolver.ResolveClerkForReschedule(
            originalClerkId: "EMP-CLK-1",
            activeClerks: clerks,
            leaves: Array.Empty<ClerkLeaveWindow>(),
            busyWindows: busy,
            targetDate: TargetDate,
            targetStart: new TimeOnly(13, 0),
            targetEnd: new TimeOnly(14, 0));

        Assert.False(result.IsAvailable);
        Assert.Null(result.ClerkId);
    }

    [Fact]
    public void LiveEventOverviewSelector_ReturnsAllExecutingEvents_WhenAtLeastOneIsActive()
    {
        var now = new DateTime(2026, 11, 19, 14, 0, 0);
        var events = new[]
        {
            new LiveEventOverviewDto
            {
                EventAppointmentId = "EVT-EXEC-1",
                EventName = "Wedding Shoot",
                EventStatus = "In Execution",
                DisplayStage = "In Execution",
                EventDate = new DateOnly(2026, 11, 19),
                EventTime = new TimeOnly(13, 0)
            },
            new LiveEventOverviewDto
            {
                EventAppointmentId = "EVT-READY-2",
                EventName = "Studio Portrait",
                EventStatus = "Ready",
                DisplayStage = "Ready",
                EventDate = new DateOnly(2026, 11, 19),
                EventTime = new TimeOnly(14, 30)
            },
            new LiveEventOverviewDto
            {
                EventAppointmentId = "EVT-UPCOMING-3",
                EventName = "Future Corporate Shoot",
                EventStatus = "Confirmed",
                DisplayStage = "Confirmed",
                EventDate = new DateOnly(2026, 11, 21),
                EventTime = new TimeOnly(9, 0)
            }
        };

        var selected = LiveEventOverviewSelector.SelectForOverview(events, now);

        Assert.Equal(2, selected.Count);
        Assert.Contains(selected, e => e.EventAppointmentId == "EVT-EXEC-1");
        Assert.Contains(selected, e => e.EventAppointmentId == "EVT-READY-2");
        Assert.DoesNotContain(selected, e => e.EventAppointmentId == "EVT-UPCOMING-3");
    }

    [Fact]
    public void LiveEventOverviewSelector_ReturnsOnlySingleClosestUpcomingEvent_WhenNoneExecuting()
    {
        var now = new DateTime(2026, 11, 19, 14, 0, 0);
        var events = new[]
        {
            new LiveEventOverviewDto
            {
                EventAppointmentId = "EVT-DONE-1",
                EventName = "Finished Morning Shoot",
                EventStatus = "Completed",
                DisplayStage = "Completed",
                EventDate = new DateOnly(2026, 11, 19),
                EventTime = new TimeOnly(9, 0)
            },
            new LiveEventOverviewDto
            {
                EventAppointmentId = "EVT-LATER-3",
                EventName = "Next Week Shoot",
                EventStatus = "Confirmed",
                DisplayStage = "Confirmed",
                EventDate = new DateOnly(2026, 11, 23),
                EventTime = new TimeOnly(10, 0)
            },
            new LiveEventOverviewDto
            {
                EventAppointmentId = "EVT-CLOSEST-2",
                EventName = "Tomorrow Shoot",
                EventStatus = "Confirmed",
                DisplayStage = "Confirmed",
                EventDate = new DateOnly(2026, 11, 20),
                EventTime = new TimeOnly(11, 0)
            }
        };

        var selected = LiveEventOverviewSelector.SelectForOverview(events, now);

        var single = Assert.Single(selected);
        Assert.Equal("EVT-CLOSEST-2", single.EventAppointmentId);
    }

    [Fact]
    public void SqlRuleOfThree_AndSchemaBatches_SupportReadyInExecutionCompletedAndAppointmentBooked()
    {
        var batches = typeof(PendingPaymentService)
            .GetField("SchemaBatches", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetValue(null) as string[];
        Assert.NotNull(batches);
        var schema = string.Join("\n", batches!);
        Assert.Contains("'Ready'", schema);
        Assert.Contains("'In Execution'", schema);
        Assert.Contains("'Completed'", schema);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        var dbDir = Path.Combine(dir!.FullName, "PhotoAevumStudioBackend", "database");
        var baseSql = File.ReadAllText(Path.Combine(dbDir, "PhotoAevumStudio.sql"));
        var idempotentSql = File.ReadAllText(Path.Combine(dbDir, "PhotoAevumStudioIdempotent.sql"));
        var spSql = File.ReadAllText(Path.Combine(dbDir, "PhotoAevumStudioStoredProcedures.sql"));

        Assert.Contains("'Ready'", baseSql);
        Assert.Contains("'In Execution'", baseSql);
        Assert.Contains("'Completed'", baseSql);
        Assert.Contains("'Appointment Booked'", baseSql);

        Assert.Contains("'Ready'", idempotentSql);
        Assert.Contains("'In Execution'", idempotentSql);
        Assert.Contains("'Completed'", idempotentSql);
        Assert.Contains("'Appointment Booked'", idempotentSql);

        Assert.Contains("SET EventStatus = 'Completed'", spSql);
    }
}
