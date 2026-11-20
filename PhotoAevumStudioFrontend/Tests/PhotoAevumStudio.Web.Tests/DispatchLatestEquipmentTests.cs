using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoAevumStudio.Web.Components.Shared;
using PhotoAevumStudio.Web.Models;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class DispatchLatestEquipmentTests
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

    private sealed class StubNavManager : NavigationManager
    {
        public StubNavManager() => Initialize("https://localhost/", "https://localhost/");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    [Fact]
    public void GetLatestDispatchPerEquipment_ReturnsOneRowPerEquipment_PreferringLatestConformingToCurrentDate()
    {
        var today = new DateTime(2026, 11, 20);
        var dispatches = new List<DispatchItemModel>
        {
            new()
            {
                EventEquipmentID = 30,
                EventAppointmentID = "EVT-025",
                EventName = "Mark Raymundo - Photoshoot Event",
                EventDate = new DateTime(2026, 11, 25),
                EventTime = new TimeSpan(14, 0, 0),
                EquipmentID = "EQP-0001",
                EquipmentName = "Canon CX3590",
                DispatchStatus = "Scheduled",
                DispatchedAt = new DateTime(2026, 11, 25)
            },
            new()
            {
                EventEquipmentID = 29,
                EventAppointmentID = "EVT-024",
                EventName = "Mark Raymundo - Photoshoot Event",
                EventDate = new DateTime(2026, 11, 24),
                EventTime = new TimeSpan(14, 0, 0),
                EquipmentID = "EQP-0001",
                EquipmentName = "Canon CX3590",
                DispatchStatus = "Scheduled",
                DispatchedAt = new DateTime(2026, 11, 24)
            },
            new()
            {
                EventEquipmentID = 15,
                EventAppointmentID = "EVT-011",
                EventName = "Individual Portrait",
                EventDate = new DateTime(2026, 11, 11),
                EventTime = new TimeSpan(10, 0, 0),
                EquipmentID = "EQP-0001",
                EquipmentName = "Canon CX3590",
                DispatchStatus = "Dispatched",
                DispatchedAt = new DateTime(2026, 11, 11)
            }
        };

        var latest = EquipmentService.GetLatestDispatchPerEquipment(dispatches, today);

        var row = Assert.Single(latest);
        Assert.Equal("EQP-0001", row.EquipmentID);
        Assert.Equal("EVT-011", row.EventAppointmentID);
        Assert.Equal("Dispatched", row.DispatchStatus);
    }

    [Fact]
    public void GetLatestDispatchPerEquipment_MultiplePastOrTodayDispatches_PicksMostRecentUpToCurrentDate()
    {
        var today = new DateTime(2026, 11, 20);
        var dispatches = new List<DispatchItemModel>
        {
            new()
            {
                EventEquipmentID = 10,
                EventAppointmentID = "EVT-OLD",
                EquipmentID = "EQP-0002",
                EventDate = new DateTime(2026, 11, 5),
                EventTime = new TimeSpan(9, 0, 0),
                DispatchStatus = "Dispatched",
                DispatchedAt = new DateTime(2026, 11, 5)
            },
            new()
            {
                EventEquipmentID = 18,
                EventAppointmentID = "EVT-TODAY",
                EquipmentID = "EQP-0002",
                EventDate = new DateTime(2026, 11, 19),
                EventTime = new TimeSpan(13, 0, 0),
                DispatchStatus = "Dispatched",
                DispatchedAt = new DateTime(2026, 11, 19)
            },
            new()
            {
                EventEquipmentID = 25,
                EventAppointmentID = "EVT-FUTURE",
                EquipmentID = "EQP-0002",
                EventDate = new DateTime(2026, 11, 25),
                EventTime = new TimeSpan(14, 0, 0),
                DispatchStatus = "Scheduled",
                DispatchedAt = new DateTime(2026, 11, 25)
            }
        };

        var latest = EquipmentService.GetLatestDispatchPerEquipment(dispatches, today);

        var row = Assert.Single(latest);
        Assert.Equal("EVT-TODAY", row.EventAppointmentID);
        Assert.Equal("Dispatched", row.DispatchStatus);
    }

    [Fact]
    public void GetLatestDispatchPerEquipment_FutureOnlyOrReturnedPlusFuture_PicksNearestScheduled()
    {
        var today = new DateTime(2026, 11, 20);
        var dispatches = new List<DispatchItemModel>
        {
            new()
            {
                EventEquipmentID = 1,
                EventAppointmentID = "EVT-PAST-RET",
                EquipmentID = "EQP-0003",
                EventDate = new DateTime(2026, 11, 11),
                EventTime = new TimeSpan(10, 0, 0),
                DispatchStatus = "Returned",
                ReturnedAt = new DateTime(2026, 11, 11, 18, 0, 0)
            },
            new()
            {
                EventEquipmentID = 3,
                EventAppointmentID = "EVT-NOV25",
                EquipmentID = "EQP-0003",
                EventDate = new DateTime(2026, 11, 25),
                EventTime = new TimeSpan(14, 0, 0),
                DispatchStatus = "Scheduled"
            },
            new()
            {
                EventEquipmentID = 2,
                EventAppointmentID = "EVT-NOV24",
                EquipmentID = "EQP-0003",
                EventDate = new DateTime(2026, 11, 24),
                EventTime = new TimeSpan(14, 0, 0),
                DispatchStatus = "Scheduled"
            },
            new()
            {
                EventEquipmentID = 4,
                EventAppointmentID = "EVT-RET-1",
                EquipmentID = "EQP-0004",
                EventDate = new DateTime(2026, 11, 5),
                EventTime = new TimeSpan(9, 0, 0),
                DispatchStatus = "Returned",
                ReturnedAt = new DateTime(2026, 11, 5, 17, 0, 0)
            },
            new()
            {
                EventEquipmentID = 5,
                EventAppointmentID = "EVT-RET-2",
                EquipmentID = "EQP-0004",
                EventDate = new DateTime(2026, 11, 12),
                EventTime = new TimeSpan(9, 0, 0),
                DispatchStatus = "Returned",
                ReturnedAt = new DateTime(2026, 11, 12, 17, 0, 0)
            }
        };

        var latest = EquipmentService.GetLatestDispatchPerEquipment(dispatches, today);

        Assert.Equal(2, latest.Count);
        Assert.Equal("EQP-0003", latest[0].EquipmentID);
        Assert.Equal("EVT-NOV24", latest[0].EventAppointmentID);
        Assert.Equal("Scheduled", latest[0].DispatchStatus);

        Assert.Equal("EQP-0004", latest[1].EquipmentID);
        Assert.Equal("EVT-RET-2", latest[1].EventAppointmentID);
        Assert.Equal("Returned", latest[1].DispatchStatus);
    }

    [Fact]
    public void GetLatestDispatchPerEquipment_TodayOrUpcomingScheduledBeatsStalePastScheduled()
    {
        var today = new DateTime(2026, 11, 20);
        var dispatches = new List<DispatchItemModel>
        {
            new()
            {
                EventEquipmentID = 1,
                EventAppointmentID = "EVT-STALE",
                EquipmentID = "EQP-0005",
                EventDate = new DateTime(2026, 11, 5),
                EventTime = new TimeSpan(9, 0, 0),
                DispatchStatus = "Scheduled"
            },
            new()
            {
                EventEquipmentID = 2,
                EventAppointmentID = "EVT-TODAY-SCHED",
                EquipmentID = "EQP-0005",
                EventDate = new DateTime(2026, 11, 20),
                EventTime = new TimeSpan(10, 0, 0),
                DispatchStatus = "Scheduled"
            },
            new()
            {
                EventEquipmentID = 3,
                EventAppointmentID = "EVT-NEXT-WEEK",
                EquipmentID = "EQP-0005",
                EventDate = new DateTime(2026, 11, 27),
                EventTime = new TimeSpan(10, 0, 0),
                DispatchStatus = "Scheduled"
            }
        };

        var latest = EquipmentService.GetLatestDispatchPerEquipment(dispatches, today);

        var row = Assert.Single(latest);
        Assert.Equal("EVT-TODAY-SCHED", row.EventAppointmentID);
        Assert.Equal("Scheduled", row.DispatchStatus);
    }

    [Fact]
    public async Task EquipmentSubNav_CountsActiveDispatchesPerUniqueEquipment()
    {
        var mockService = new Mock<IEquipmentService>();
        mockService.Setup(s => s.GetEquipmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EquipmentItemModel>
            {
                new() { EquipmentID = "EQP-0001", EquipmentName = "Canon CX3590", EquipmentStatus = "Available" }
            });
        mockService.Setup(s => s.GetEquipmentTypesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EquipmentTypeModel>());
        mockService.Setup(s => s.GetDispatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DispatchItemModel>
            {
                new()
                {
                    EventEquipmentID = 1,
                    EquipmentID = "EQP-0001",
                    EventDate = DateTime.Today.AddDays(-5),
                    DispatchStatus = "Dispatched"
                },
                new()
                {
                    EventEquipmentID = 2,
                    EquipmentID = "EQP-0001",
                    EventDate = DateTime.Today,
                    DispatchStatus = "Dispatched"
                },
                new()
                {
                    EventEquipmentID = 3,
                    EquipmentID = "EQP-0001",
                    EventDate = DateTime.Today.AddDays(4),
                    DispatchStatus = "Scheduled"
                }
            });

        var services = new ServiceCollection();
        services.AddSingleton(mockService.Object);
        services.AddSingleton<NavigationManager, StubNavManager>();
        await using var sp = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(EquipmentSubNav.ActiveTabLabel)] = "Dispatch & Checkout"
            });
            var output = await renderer.RenderComponentAsync<EquipmentSubNav>(parameters);
            return output.ToHtmlString();
        });

        Assert.Contains("Dispatch &amp; Checkout", html);
        Assert.Contains(">1<", html);
    }

    [Fact]
    public void DispatchPage_UsesLatestDispatchesForDisplayAndAllDispatchesForSameDayLock()
    {
        var dispatchRazor = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "PhotoAevumStudioFrontend",
            "PhotoAevumStudio.Web",
            "Components",
            "Pages",
            "Dispatch.razor"));

        Assert.Contains("LatestDispatches.Count(d => d.IsActiveCheckout)", dispatchRazor);
        Assert.Contains("LatestDispatches.Count(d => d.IsScheduled)", dispatchRazor);
        Assert.Contains("@LatestDispatches.Count</strong> dispatch manifests", dispatchRazor);
        Assert.Contains("var query = LatestDispatches.AsEnumerable();", dispatchRazor);
        Assert.Contains("foreach (var d in dispatches.Where(d =>", dispatchRazor);
    }

    [Fact]
    public void EquipmentItemModel_EffectiveStatus_ReflectsReservedDispatchedAndCompletedStates()
    {
        var item = new EquipmentItemModel
        {
            EquipmentID = "EQP-0001",
            EquipmentName = "Canon CX3590",
            EquipmentStatus = "Available"
        };

        Assert.False(item.IsDispatched);
        Assert.False(item.IsReservedForEvent);
        Assert.Equal("Available", item.EffectiveStatusLabel);

        // Assigned to event in Preparation (not yet verified/taken)
        item.CurrentEventId = "EVT-0015";
        item.CurrentEventName = "Dyan Brad - Photoshoot Event";
        item.CurrentEventStatus = "Preparation";
        item.IsCurrentEventTaken = false;
        Assert.False(item.IsDispatched);
        Assert.True(item.IsReservedForEvent);
        Assert.Equal("Reserved / Preparing", item.EffectiveStatusLabel);

        // Equipment verified & event proceeds to Dispatch
        item.IsCurrentEventTaken = true;
        item.CurrentEventStatus = "Dispatch";
        Assert.True(item.IsDispatched);
        Assert.False(item.IsReservedForEvent);
        Assert.Equal("Dispatched / On Shoot", item.EffectiveStatusLabel);

        // Event completed -> returns to Available
        item.CurrentEventId = null;
        item.CurrentEventName = null;
        item.CurrentEventStatus = null;
        item.IsCurrentEventTaken = false;
        Assert.False(item.IsDispatched);
        Assert.Equal("Available", item.EffectiveStatusLabel);
    }

    [Fact]
    public void TeamLeaderActiveEventPill_VerifyEquipment_ProceedsToDispatchedAndNotifiesEquipmentState()
    {
        var pillRazor = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "PhotoAevumStudioFrontend",
            "PhotoAevumStudio.Web",
            "Components",
            "Shared",
            "TeamLeaderActiveEventPill.razor"));

        Assert.Contains("TransitionToDispatch = isTaken", pillRazor);
        Assert.Contains("TransitionToDispatch = true", pillRazor);
        Assert.Contains("EquipmentService.NotifyStateChanged()", pillRazor);
    }
}
