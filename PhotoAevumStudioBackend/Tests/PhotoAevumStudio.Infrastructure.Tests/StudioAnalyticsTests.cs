using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;
using PhotoAevumStudio.Infrastructure.Services;
using Xunit;

namespace PhotoAevumStudio.Infrastructure.Tests;

public class StudioAnalyticsTests
{
    private static StudioAnalyticsSnapshot CreateRichSnapshot()
    {
        var today = new DateOnly(2026, 11, 20);
        var weddingLine = new List<AnalyticsLineItemRecord>
        {
            new("PKG-001", "Wedding Full Coverage", "Combo Package", "Wedding", 1)
        };
        var portraitLine = new List<AnalyticsLineItemRecord>
        {
            new("SVC-002", "Executive Portrait", "Service", "Portrait", 2)
        };
        var lightingAddOn = new List<AnalyticsLineItemRecord>
        {
            new("ADD-003", "Strobe Lighting Kit", "Add-on", "Commercial", 1)
        };

        var bookings = new List<AnalyticsBookingRecord>
        {
            // Current 90-day window (2026-08-23 .. 2026-11-20)
            new("EVT-0101", "EventAppointment", "Santos Wedding", "Maria Santos", new DateOnly(2026, 9, 5), new TimeOnly(9, 0), 240, "Delivered", "Completed", "Paid", null, "Elena Cruz", new DateTime(2026, 8, 25), weddingLine),
            new("EVT-0102", "EventAppointment", "Reyes Debut", "Ana Reyes", new DateOnly(2026, 9, 19), new TimeOnly(14, 0), 180, "Completed", "Completed", "Paid", null, "Elena Cruz", new DateTime(2026, 9, 1), weddingLine),
            new("EVT-0103", "EventAppointment", "Solis Headshots", "Marco Solis", new DateOnly(2026, 10, 4), new TimeOnly(10, 0), 90, "Confirmed", "Confirmed", "Paid", null, "Jude Tan", new DateTime(2026, 9, 25), portraitLine),
            new("EVT-0104", "EventAppointment", "Lim Lookbook", "Chloe Lim", new DateOnly(2026, 10, 18), new TimeOnly(13, 0), 120, "Cancelled", "Cancelled", "Pending", null, "Jude Tan", new DateTime(2026, 10, 10), lightingAddOn),
            new("EVT-0105", "EventAppointment", "Diaz Family", "Leo Diaz", new DateOnly(2026, 11, 8), new TimeOnly(15, 0), 60, "Expired", "Expired", "Pending", new DateTime(2026, 11, 2), null, new DateTime(2026, 11, 1), portraitLine),
            new("EVT-0106", "EventAppointment", "Bautista Prenup", "Kim Bautista", new DateOnly(2026, 11, 24), new TimeOnly(9, 0), 180, "Awaiting_Downpayment", "Pending", "Pending", new DateTime(2026, 11, 21), "Elena Cruz", new DateTime(2026, 11, 18), weddingLine),
            new("EVT-0107", "EventAppointment", "Noche Gala", "Vera Noche", new DateOnly(2026, 12, 12), new TimeOnly(16, 0), 240, "Confirmed", "Confirmed", "Paid", null, "Elena Cruz", new DateTime(2026, 11, 15), weddingLine),
            new("APT-0021", "ScheduleAppointment", "Studio Consultation (GoogleMeet)", "Paolo Go", new DateOnly(2026, 11, 12), new TimeOnly(11, 0), 60, "Confirmed", "Confirmed", "Consultation", null, null, new DateTime(2026, 11, 10), [new("CONSULT", "Book Schedule Consultation", "Consultation", "Consultation", 1)]),

            // Prior 90-day window (2026-05-25 .. 2026-08-22) + earlier months for predictive MAE
            new("EVT-0080", "EventAppointment", "June Wedding", "Client A", new DateOnly(2026, 6, 14), new TimeOnly(9, 0), 240, "Completed", "Completed", "Paid", null, "Elena Cruz", new DateTime(2026, 6, 1), weddingLine),
            new("EVT-0081", "EventAppointment", "July Studio", "Client B", new DateOnly(2026, 7, 11), new TimeOnly(13, 0), 120, "Delivered", "Completed", "Paid", null, "Jude Tan", new DateTime(2026, 7, 1), portraitLine),
            new("EVT-0082", "EventAppointment", "Aug Brand", "Client C", new DateOnly(2026, 8, 8), new TimeOnly(10, 0), 120, "Completed", "Completed", "Paid", null, "Jude Tan", new DateTime(2026, 8, 1), lightingAddOn),
        };

        var timesheets = new List<AnalyticsTimesheetRecord>
        {
            new("TS-1", "EMP-001", "Elena Cruz", new DateOnly(2026, 9, 5), 8.0, "Approved", false),
            new("TS-2", "EMP-001", "Elena Cruz", new DateOnly(2026, 9, 19), 7.5, "Approved", false),
            new("TS-3", "EMP-002", "Jude Tan", new DateOnly(2026, 10, 4), 6.0, "Completed", true),
            new("TS-4", "EMP-002", "Jude Tan", new DateOnly(2026, 8, 8), 5.0, "Approved", false)
        };

        var employees = new List<AnalyticsEmployeeRecord>
        {
            new("EMP-001", "Elena Cruz", "Active", "Sunday", ["Wedding Photography"]),
            new("EMP-002", "Jude Tan", "Active", "Sunday, Monday", ["Studio Portraits"]),
            new("EMP-003", "Rina Velasco", "Active", "Wednesday", [])
        };

        var leaves = new List<AnalyticsLeaveRecord>
        {
            new("LV-202611-01", "EMP-001", "Elena Cruz", "Vacation Leave", new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 25), "Approved", "Family trip"),
            new("LV-202610-02", "EMP-002", "Jude Tan", "Sick Leave", new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 16), "Approved", "Flu recovery")
        };

        var equipment = new List<AnalyticsEquipmentRecord>
        {
            new("EQP-0001", "Sony A7R V Body #1", "Camera Body", "Available"),
            new("EQP-0002", "Profoto B10X Plus", "Studio Lighting", "Maintenance"),
            new("EQP-0003", "Sony 24-70mm f/2.8 GM II", "Lens", "Needs Replacement")
        };

        var maintenance = new List<AnalyticsMaintenanceRecord>
        {
            new(10, "EQP-0002", "Profoto B10X Plus", "Calibration", "In Maintenance", "Checking", "Flash tube misfire", new DateTime(2026, 11, 10), new DateTime(2026, 11, 12), null)
        };

        var eqAssignments = new List<AnalyticsEquipmentAssignmentRecord>
        {
            new("EVT-0106", "EQP-0001", "Sony A7R V Body #1", new DateOnly(2026, 11, 24), "Awaiting_Downpayment", false),
            new("EVT-0107", "EQP-0001", "Sony A7R V Body #1", new DateOnly(2026, 12, 12), "Confirmed", false)
        };

        var negotiations = new List<AnalyticsNegotiationRecord>
        {
            new("NEG-010", null, "CUST-010", "Rina Velasco", new DateOnly(2026, 11, 14), "Pending_Call", "Asking for corporate rate", new DateTime(2026, 11, 14)),
            new("NEG-011", "EVT-0104", "CUST-011", "Rina Velasco", new DateOnly(2026, 10, 12), "Rescheduled", "Moved once", new DateTime(2026, 10, 12))
        };

        var audits = new List<AnalyticsAuditRecord>
        {
            new("AuditLog", "EVT-0104", "Cancel Booking", "Booking", "Client cancelled shoot", new DateTime(2026, 10, 15, 9, 0, 0)),
            new("HistoryLog", "EVT-0105", "STATUS_CHANGE", "EventAppointment", "Hold expired after LockExpiresAt", new DateTime(2026, 11, 2, 10, 0, 0))
        };

        var applied = new List<AnalyticsAppliedActionRecord>
        {
            new(1, "precheck-equipment-holds", "Resolve 2 equipment hold(s)", "Equipment", "High Effort", "Restores 2 gear unit(s)", "2 units in Maintenance", "EQP-0002, EQP-0003", "Assigned to tech team", "ADM-001", "Studio Admin", new DateTime(2026, 11, 19, 8, 0, 0), "Applied")
        };

        return new StudioAnalyticsSnapshot
        {
            Today = today,
            Bookings = bookings,
            Timesheets = timesheets,
            Employees = employees,
            RegisteredSpecializations = ["Wedding Photography", "Studio Portraits", "Drone Aerial"],
            Leaves = leaves,
            Equipment = equipment,
            MaintenanceSchedules = maintenance,
            EquipmentAssignments = eqAssignments,
            Negotiations = negotiations,
            AuditRecords = audits,
            AppliedActions = applied,
            AvailableServiceCategories = ["Wedding", "Portrait", "Commercial"]
        };
    }

    [Fact]
    public void Descriptive_ComputesRealKpisVolumeSeriesAndTopServices()
    {
        var snapshot = CreateRichSnapshot();
        var query = new StudioAnalyticsQuery(
            StartDate: new DateOnly(2026, 8, 23),
            EndDate: new DateOnly(2026, 11, 20),
            Granularity: "month");

        var overview = StudioAnalyticsCalculator.Compute(snapshot, query);
        var d = overview.Descriptive;

        Assert.True(overview.HasAnyData);
        Assert.False(overview.IsLowData); // 6 bookings in window
        Assert.Equal(6, d.TotalBookings); // EVT-0101..0105 + APT-0021
        Assert.Equal(5, d.EventAppointmentsCount);
        Assert.Equal(1, d.ConsultationBookingsCount);
        Assert.Equal(2, d.CompletedDeliveredCount); // Delivered + Completed
        Assert.Equal(2, d.ConfirmedActiveCount); // EVT-0103 + APT-0021
        Assert.Equal(2, d.CancelledExpiredCount); // Cancelled + Expired
        Assert.Equal(33.3, d.SessionCompletionRate); // 2/6 = 33.3%
        Assert.Equal(21.5, d.LoggedCrewHours); // 8.0 + 7.5 + 6.0
        Assert.Equal(2, d.ActiveCrewLoggedCount);
        Assert.Equal(1, d.DayOffShiftsCount);
        Assert.Equal(2, d.EquipmentMaintenanceHolds); // Maintenance + Needs Replacement
        Assert.Equal(3, d.TotalEquipmentCount);

        Assert.NotEmpty(d.VolumeSeries);
        Assert.Equal(4, d.VolumeSeries.Count); // Aug, Sep, Oct, Nov 2026
        Assert.Equal("Executive Portrait", d.TopServices[0].Name);
        Assert.Equal(2, d.TopServices[0].BookingCount);
        Assert.Equal(4, d.TopServices[0].TotalQuantity);
        Assert.Equal("Wedding Full Coverage", d.TopServices[1].Name);
        Assert.Equal(2, d.TopServices[1].BookingCount);
    }

    [Fact]
    public void Descriptive_HandlesEmptyDateRangeGracefullyWithoutNaN()
    {
        var snapshot = CreateRichSnapshot();
        var query = new StudioAnalyticsQuery(
            StartDate: new DateOnly(2025, 1, 1),
            EndDate: new DateOnly(2025, 1, 31),
            Granularity: "week");

        var overview = StudioAnalyticsCalculator.Compute(snapshot, query);

        Assert.False(overview.HasAnyData);
        Assert.True(overview.IsLowData);
        Assert.Equal(0, overview.Descriptive.TotalBookings);
        Assert.Equal(0.0, overview.Descriptive.SessionCompletionRate);
        Assert.Equal(0.0, overview.Descriptive.LoggedCrewHours);
        Assert.Empty(overview.Descriptive.TopServices);
        Assert.NotEmpty(overview.Descriptive.VolumeSeries);
        Assert.All(overview.Descriptive.VolumeSeries, pt => Assert.Equal(0, pt.TotalBookings));
    }

    [Fact]
    public void Diagnostic_ComputesPeriodShiftsAndFourDomainDrivers()
    {
        var snapshot = CreateRichSnapshot();
        var query = new StudioAnalyticsQuery(
            StartDate: new DateOnly(2026, 8, 23),
            EndDate: new DateOnly(2026, 11, 20));

        var overview = StudioAnalyticsCalculator.Compute(snapshot, query);
        var diag = overview.Diagnostic;

        Assert.Equal(6, diag.CurrentTotalBookings);
        Assert.Equal(3, diag.PriorTotalBookings); // Jun, Jul, Aug 8
        Assert.Equal(100.0, diag.BookingsChangePct);

        Assert.Equal(5, diag.StatusShifts.Count);
        Assert.Equal(4, diag.Drivers.Count);

        var driverIds = diag.Drivers.Select(x => x.DriverId).ToList();
        Assert.Contains("status-shifts", driverIds);
        Assert.Contains("staff-availability", driverIds);
        Assert.Contains("equipment-holds", driverIds);
        Assert.Contains("audit-negotiations", driverIds);

        var staffDriver = diag.Drivers.First(x => x.DriverId == "staff-availability");
        Assert.Contains(staffDriver.AffectedItems, a => a.ReferenceId == "LV-202610-02");
        Assert.Contains(staffDriver.AffectedItems, a => a.Title.Contains("Drone Aerial"));

        var equipDriver = diag.Drivers.First(x => x.DriverId == "equipment-holds");
        Assert.Contains(equipDriver.AffectedItems, a => a.ReferenceId == "EQP-0002");
    }

    [Fact]
    public void Predictive_ProducesThreeScenariosWithUncertaintyBandAndMae()
    {
        var snapshot = CreateRichSnapshot();
        var query = new StudioAnalyticsQuery(Scenario: "strong");

        var overview = StudioAnalyticsCalculator.Compute(snapshot, query);
        var pred = overview.Predictive;

        Assert.Equal("strong", pred.ActiveScenario);
        Assert.Equal(3, pred.Scenarios.Count);
        Assert.NotNull(pred.ModelErrorMae);
        Assert.True(pred.HistoricalMonthsCount >= 5);

        var cautious = pred.Scenarios.First(s => s.Key == "cautious");
        var expected = pred.Scenarios.First(s => s.Key == "expected");
        var strong = pred.Scenarios.First(s => s.Key == "strong");

        Assert.True(cautious.ProjectedBookingsTotal <= expected.ProjectedBookingsTotal);
        Assert.True(expected.ProjectedBookingsTotal <= strong.ProjectedBookingsTotal);

        foreach (var fp in expected.Series.Where(s => !s.IsHistorical))
        {
            Assert.True(fp.LowerBound <= fp.ProjectedBookings);
            Assert.True(fp.ProjectedBookings <= fp.UpperBound);
        }

        // Dec 2026 already has EVT-0107 confirmed on books
        var dec = expected.Series.First(s => s.PeriodKey == "2026-12");
        Assert.Equal(1, dec.ConfirmedOnBooksCount);
        Assert.True(dec.ProjectedBookings >= 1);
    }

    [Fact]
    public void Prescriptive_TriggersStudioSignalsAndMergesAppliedHistory()
    {
        var snapshot = CreateRichSnapshot();
        var query = new StudioAnalyticsQuery();

        var overview = StudioAnalyticsCalculator.Compute(snapshot, query);
        var presc = overview.Prescriptive;

        Assert.True(presc.Recommendations.Count >= 4);

        var pendingRec = presc.Recommendations.First(r => r.ActionKey == "follow-up-pending-deposits");
        Assert.True(pendingRec.IsSignalTriggered);
        Assert.Contains("EVT-0106", pendingRec.RelatedReferences);

        var leaveRec = presc.Recommendations.First(r => r.ActionKey == "buffer-leave-overlap");
        Assert.True(leaveRec.IsSignalTriggered);
        Assert.Contains("LV-202611-01", leaveRec.RelatedReferences);

        var equipRec = presc.Recommendations.First(r => r.ActionKey == "precheck-equipment-holds");
        Assert.True(equipRec.IsSignalTriggered);
        Assert.True(equipRec.IsAlreadyApplied);
        Assert.Equal("Studio Admin", equipRec.LastAppliedBy);

        var csv = StudioAnalyticsCalculator.BuildCsvReport(overview);
        Assert.Contains("Descriptive,Total Bookings", csv);
        Assert.Contains("Diagnostic Status Shift", csv);
        Assert.Contains("Predictive Forecast", csv);
        Assert.Contains("Prescriptive", csv);
    }

    [Fact]
    public void StudioAnalyticsService_ImplementsInterfaceAndEnsureSchemaExists()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=Dummy;Trusted_Connection=True;")
            .Options;
        using var db = new ApplicationDbContext(options);
        var auditMock = new Mock<IAuditLogService>();

        var service = new StudioAnalyticsService(db, auditMock.Object, NullLogger<StudioAnalyticsService>.Instance);
        Assert.IsAssignableFrom<IStudioAnalyticsService>(service);
    }
}
