using System.Globalization;
using System.Text;
using PhotoAevumStudio.Application.DTOs;

namespace PhotoAevumStudio.Infrastructure.Services;

public static class StudioAnalyticsCalculator
{
    private static readonly string[] CanonicalStatusGroups =
    [
        "Pending",
        "Confirmed",
        "Completed",
        "Cancelled",
        "Expired"
    ];

    public static string NormalizeStatus(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
            return "Pending";

        return rawStatus.Trim() switch
        {
            var s when s.Equals("Completed", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("Complete", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("Delivered", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("Event Done", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("Post Event", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("Shot", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("Editing", StringComparison.OrdinalIgnoreCase)
                => "Completed",

            var s when s.Equals("Confirmed", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("Ready", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("In Execution", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("Preparation", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("Preparing", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("Dispatch", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("Ongoing", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("On Going", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("Pending Verification", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("Paid Cash Pending Verification", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("Payment Verification (Cash)", StringComparison.OrdinalIgnoreCase)
                => "Confirmed",

            var s when s.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("Canceled", StringComparison.OrdinalIgnoreCase)
                => "Cancelled",

            var s when s.Equals("Expired", StringComparison.OrdinalIgnoreCase)
                => "Expired",

            _ => "Pending"
        };
    }

    public static StudioAnalyticsOverviewDto Compute(StudioAnalyticsSnapshot snapshot, StudioAnalyticsQuery query)
    {
        var today = snapshot.Today;
        var endDate = query.EndDate ?? today;
        var startDate = query.StartDate ?? endDate.AddDays(-89);
        if (startDate > endDate)
        {
            (startDate, endDate) = (endDate, startDate);
        }

        var granularity = string.Equals(query.Granularity, "week", StringComparison.OrdinalIgnoreCase)
            ? "week"
            : "month";

        var scenario = NormalizeScenario(query.Scenario);

        var statusFilter = string.IsNullOrWhiteSpace(query.StatusFilter) ||
                           string.Equals(query.StatusFilter, "All", StringComparison.OrdinalIgnoreCase)
            ? null
            : query.StatusFilter.Trim();

        var categoryFilter = string.IsNullOrWhiteSpace(query.ServiceCategoryFilter) ||
                             string.Equals(query.ServiceCategoryFilter, "All", StringComparison.OrdinalIgnoreCase)
            ? null
            : query.ServiceCategoryFilter.Trim();

        var spanDays = endDate.DayNumber - startDate.DayNumber + 1;
        var priorEndDate = startDate.AddDays(-1);
        var priorStartDate = priorEndDate.AddDays(-(spanDays - 1));

        var filteredAllDates = FilterBookings(snapshot.Bookings, statusFilter, categoryFilter);
        var currentBookings = filteredAllDates
            .Where(b => b.EventDate >= startDate && b.EventDate <= endDate)
            .ToList();
        var priorBookings = filteredAllDates
            .Where(b => b.EventDate >= priorStartDate && b.EventDate <= priorEndDate)
            .ToList();

        var currentTimesheets = snapshot.Timesheets
            .Where(t => t.WorkDate >= startDate && t.WorkDate <= endDate)
            .ToList();
        var priorTimesheets = snapshot.Timesheets
            .Where(t => t.WorkDate >= priorStartDate && t.WorkDate <= priorEndDate)
            .ToList();

        var descriptive = ComputeDescriptive(
            snapshot,
            startDate,
            endDate,
            granularity,
            currentBookings,
            priorBookings,
            currentTimesheets,
            priorTimesheets);

        var diagnostic = ComputeDiagnostic(
            snapshot,
            startDate,
            endDate,
            priorStartDate,
            priorEndDate,
            currentBookings,
            priorBookings);

        var predictive = ComputePredictive(
            snapshot,
            today,
            scenario,
            categoryFilter);

        var prescriptive = ComputePrescriptive(
            snapshot,
            today,
            startDate,
            endDate);

        var availableCategories = snapshot.AvailableServiceCategories
            .Concat(snapshot.Bookings.SelectMany(b => b.LineItems.Select(li => li.CategoryName)))
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool hasAnyData = currentBookings.Count > 0 || currentTimesheets.Count > 0;
        bool isLowData = currentBookings.Count < 5;

        return new StudioAnalyticsOverviewDto(
            StartDate: startDate,
            EndDate: endDate,
            PriorStartDate: priorStartDate,
            PriorEndDate: priorEndDate,
            Granularity: granularity,
            AppliedStatusFilter: statusFilter,
            AppliedServiceCategoryFilter: categoryFilter,
            AvailableStatuses: CanonicalStatusGroups,
            AvailableServiceCategories: availableCategories,
            GeneratedAtUtc: DateTime.UtcNow,
            HasAnyData: hasAnyData,
            IsLowData: isLowData,
            Descriptive: descriptive,
            Diagnostic: diagnostic,
            Predictive: predictive,
            Prescriptive: prescriptive);
    }

    private static List<AnalyticsBookingRecord> FilterBookings(
        IReadOnlyList<AnalyticsBookingRecord> bookings,
        string? statusFilter,
        string? categoryFilter)
    {
        return bookings.Where(b =>
        {
            if (statusFilter is not null)
            {
                bool matchesNormalized = string.Equals(b.NormalizedStatus, statusFilter, StringComparison.OrdinalIgnoreCase);
                bool matchesRaw = string.Equals(b.RawStatus, statusFilter, StringComparison.OrdinalIgnoreCase);
                if (!matchesNormalized && !matchesRaw)
                    return false;
            }

            if (categoryFilter is not null)
            {
                if (b.LineItems.Count == 0)
                    return string.Equals(categoryFilter, "Studio Session", StringComparison.OrdinalIgnoreCase);

                bool matchesCategory = b.LineItems.Any(li =>
                    string.Equals(li.CategoryName, categoryFilter, StringComparison.OrdinalIgnoreCase));
                if (!matchesCategory)
                    return false;
            }

            return true;
        }).ToList();
    }

    private static DescriptiveAnalyticsDto ComputeDescriptive(
        StudioAnalyticsSnapshot snapshot,
        DateOnly startDate,
        DateOnly endDate,
        string granularity,
        List<AnalyticsBookingRecord> currentBookings,
        List<AnalyticsBookingRecord> priorBookings,
        List<AnalyticsTimesheetRecord> currentTimesheets,
        List<AnalyticsTimesheetRecord> priorTimesheets)
    {
        int totalBookings = currentBookings.Count;
        int eventAppointmentsCount = currentBookings.Count(b => b.Source == "EventAppointment");
        int consultationCount = currentBookings.Count(b => b.Source == "ScheduleAppointment");

        int completedCount = currentBookings.Count(b => b.NormalizedStatus == "Completed");
        int confirmedCount = currentBookings.Count(b => b.NormalizedStatus == "Confirmed");
        int pendingCount = currentBookings.Count(b => b.NormalizedStatus == "Pending");
        int cancelledOrExpiredCount = currentBookings.Count(b => b.NormalizedStatus is "Cancelled" or "Expired");

        double completionRate = totalBookings > 0
            ? Math.Round((double)completedCount / totalBookings * 100.0, 1)
            : 0.0;

        int priorTotalBookings = priorBookings.Count;
        double? bookingsDeltaPct = priorTotalBookings > 0
            ? Math.Round((double)(totalBookings - priorTotalBookings) / priorTotalBookings * 100.0, 1)
            : null;

        int priorCompleted = priorBookings.Count(b => b.NormalizedStatus == "Completed");
        double? priorCompletionRate = priorTotalBookings > 0
            ? (double)priorCompleted / priorTotalBookings * 100.0
            : null;
        double? completionDeltaPts = priorCompletionRate.HasValue && totalBookings > 0
            ? Math.Round(completionRate - priorCompletionRate.Value, 1)
            : null;

        double loggedHours = Math.Round(currentTimesheets.Sum(t => Math.Max(0, t.Hours)), 1);
        double priorHours = priorTimesheets.Sum(t => Math.Max(0, t.Hours));
        double? hoursDeltaPct = priorHours > 0
            ? Math.Round((loggedHours - priorHours) / priorHours * 100.0, 1)
            : null;
        int activeCrewLogged = currentTimesheets
            .Select(t => t.EmployeeStudioId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        int dayOffShifts = currentTimesheets.Count(t => t.IsDayOffWork);

        int equipInMaintenance = snapshot.Equipment.Count(e =>
            string.Equals(e.EquipmentStatus, "Maintenance", StringComparison.OrdinalIgnoreCase));
        int equipNeedsReplacement = snapshot.Equipment.Count(e =>
            string.Equals(e.EquipmentStatus, "Needs Replacement", StringComparison.OrdinalIgnoreCase));

        int activeMaintTicketsInPeriod = snapshot.MaintenanceSchedules.Count(m =>
        {
            var schedDate = DateOnly.FromDateTime(m.ScheduledDateUtc);
            var updDate = DateOnly.FromDateTime(m.UpdatedAtUtc);
            bool inWindow = (schedDate >= startDate && schedDate <= endDate) ||
                            (updDate >= startDate && updDate <= endDate);
            bool unresolved = string.Equals(m.Status, "In Maintenance", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(m.Status, "Needs Replacement", StringComparison.OrdinalIgnoreCase);
            return inWindow || unresolved;
        });

        int equipmentHolds = equipInMaintenance + equipNeedsReplacement;
        if (equipmentHolds == 0 && activeMaintTicketsInPeriod > 0)
        {
            equipmentHolds = activeMaintTicketsInPeriod;
        }

        int equipmentAssignmentsInPeriod = snapshot.EquipmentAssignments.Count(a =>
            a.EventDate >= startDate && a.EventDate <= endDate);

        var volumeSeries = BuildVolumeSeries(startDate, endDate, granularity, currentBookings, currentTimesheets);
        var topServices = BuildTopServices(currentBookings);

        return new DescriptiveAnalyticsDto(
            TotalBookings: totalBookings,
            EventAppointmentsCount: eventAppointmentsCount,
            ConsultationBookingsCount: consultationCount,
            BookingsDeltaPct: bookingsDeltaPct,
            SessionCompletionRate: completionRate,
            CompletionRateDeltaPts: completionDeltaPts,
            CompletedDeliveredCount: completedCount,
            ConfirmedActiveCount: confirmedCount,
            PendingHoldCount: pendingCount,
            CancelledExpiredCount: cancelledOrExpiredCount,
            LoggedCrewHours: loggedHours,
            CrewHoursDeltaPct: hoursDeltaPct,
            TimesheetEntriesCount: currentTimesheets.Count,
            ActiveCrewLoggedCount: activeCrewLogged,
            DayOffShiftsCount: dayOffShifts,
            EquipmentMaintenanceHolds: equipmentHolds,
            TotalEquipmentCount: snapshot.Equipment.Count,
            EquipmentInMaintenanceCount: equipInMaintenance,
            EquipmentNeedsReplacementCount: equipNeedsReplacement,
            ActiveMaintenanceTicketsInPeriod: activeMaintTicketsInPeriod,
            EquipmentAssignmentsInPeriod: equipmentAssignmentsInPeriod,
            VolumeSeries: volumeSeries,
            TopServices: topServices);
    }

    private static List<AnalyticsTimeSeriesPointDto> BuildVolumeSeries(
        DateOnly startDate,
        DateOnly endDate,
        string granularity,
        List<AnalyticsBookingRecord> bookings,
        List<AnalyticsTimesheetRecord> timesheets)
    {
        var points = new List<AnalyticsTimeSeriesPointDto>();

        if (granularity == "week")
        {
            var cursor = StartOfIsoWeek(startDate);
            var limit = StartOfIsoWeek(endDate);
            // Cap to 52 weeks if user picks "All History" spanning many years
            var earliestNeeded = limit.AddDays(-7 * 51);
            if (cursor < earliestNeeded) cursor = earliestNeeded;

            while (cursor <= limit)
            {
                var weekEnd = cursor.AddDays(6);
                var weekBookings = bookings.Where(b => b.EventDate >= cursor && b.EventDate <= weekEnd).ToList();
                var weekHours = Math.Round(timesheets.Where(t => t.WorkDate >= cursor && t.WorkDate <= weekEnd).Sum(t => Math.Max(0, t.Hours)), 1);

                var isoWeek = ISOWeek.GetWeekOfYear(cursor.ToDateTime(TimeOnly.MinValue));
                var isoYear = ISOWeek.GetYear(cursor.ToDateTime(TimeOnly.MinValue));
                string key = $"{isoYear}-W{isoWeek:00}";
                string label = $"{cursor:MMM d}";

                points.Add(new AnalyticsTimeSeriesPointDto(
                    Key: key,
                    Label: label,
                    TotalBookings: weekBookings.Count,
                    CompletedCount: weekBookings.Count(b => b.NormalizedStatus == "Completed"),
                    ConfirmedCount: weekBookings.Count(b => b.NormalizedStatus == "Confirmed"),
                    PendingCount: weekBookings.Count(b => b.NormalizedStatus == "Pending"),
                    CancelledOrExpiredCount: weekBookings.Count(b => b.NormalizedStatus is "Cancelled" or "Expired"),
                    LoggedCrewHours: weekHours));

                cursor = cursor.AddDays(7);
            }
        }
        else
        {
            var cursor = new DateOnly(startDate.Year, startDate.Month, 1);
            var limit = new DateOnly(endDate.Year, endDate.Month, 1);
            var earliestNeeded = limit.AddMonths(-23);
            if (cursor < earliestNeeded) cursor = earliestNeeded;

            while (cursor <= limit)
            {
                var monthEnd = cursor.AddMonths(1).AddDays(-1);
                var monthBookings = bookings.Where(b => b.EventDate >= cursor && b.EventDate <= monthEnd).ToList();
                var monthHours = Math.Round(timesheets.Where(t => t.WorkDate >= cursor && t.WorkDate <= monthEnd).Sum(t => Math.Max(0, t.Hours)), 1);

                points.Add(new AnalyticsTimeSeriesPointDto(
                    Key: cursor.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    Label: cursor.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                    TotalBookings: monthBookings.Count,
                    CompletedCount: monthBookings.Count(b => b.NormalizedStatus == "Completed"),
                    ConfirmedCount: monthBookings.Count(b => b.NormalizedStatus == "Confirmed"),
                    PendingCount: monthBookings.Count(b => b.NormalizedStatus == "Pending"),
                    CancelledOrExpiredCount: monthBookings.Count(b => b.NormalizedStatus is "Cancelled" or "Expired"),
                    LoggedCrewHours: monthHours));

                cursor = cursor.AddMonths(1);
            }
        }

        return points;
    }

    private static List<RankedServiceBreakdownDto> BuildTopServices(List<AnalyticsBookingRecord> bookings)
    {
        if (bookings.Count == 0)
            return [];

        var itemOccurrences = new List<(string ItemId, string Name, string Kind, string CategoryName, string BookingId, int Qty)>();
        foreach (var booking in bookings)
        {
            if (booking.LineItems.Count == 0)
            {
                itemOccurrences.Add((
                    "SESSION",
                    string.IsNullOrWhiteSpace(booking.Title) ? "Custom Studio Session" : booking.Title,
                    booking.Source == "ScheduleAppointment" ? "Consultation" : "Session",
                    "Studio Session",
                    booking.BookingId,
                    1));
            }
            else
            {
                foreach (var li in booking.LineItems)
                {
                    itemOccurrences.Add((
                        li.ItemId,
                        li.Name,
                        li.Kind,
                        string.IsNullOrWhiteSpace(li.CategoryName) ? "General" : li.CategoryName,
                        booking.BookingId,
                        Math.Max(1, li.Quantity)));
                }
            }
        }

        var grouped = itemOccurrences
            .GroupBy(x => (
                Name: x.Name.Trim().ToUpperInvariant(),
                Kind: x.Kind.Trim().ToUpperInvariant(),
                Category: x.CategoryName.Trim().ToUpperInvariant()))
            .Select(g =>
            {
                var first = g.First();
                int distinctBookings = g.Select(x => x.BookingId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                int totalQty = g.Sum(x => x.Qty);
                return new
                {
                    ServiceId = first.ItemId,
                    first.Name,
                    first.Kind,
                    first.CategoryName,
                    BookingCount = distinctBookings,
                    TotalQuantity = totalQty
                };
            })
            .OrderByDescending(x => x.BookingCount)
            .ThenByDescending(x => x.TotalQuantity)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        int denominator = Math.Max(1, bookings.Count);
        return grouped.Select(x => new RankedServiceBreakdownDto(
            ServiceId: x.ServiceId,
            Name: x.Name,
            Kind: x.Kind,
            CategoryName: x.CategoryName,
            BookingCount: x.BookingCount,
            TotalQuantity: x.TotalQuantity,
            SharePct: Math.Round(Math.Min(100.0, (double)x.BookingCount / denominator * 100.0), 1)))
            .ToList();
    }

    private static DiagnosticAnalyticsDto ComputeDiagnostic(
        StudioAnalyticsSnapshot snapshot,
        DateOnly startDate,
        DateOnly endDate,
        DateOnly priorStartDate,
        DateOnly priorEndDate,
        List<AnalyticsBookingRecord> currentBookings,
        List<AnalyticsBookingRecord> priorBookings)
    {
        string currentLabel = $"{startDate:MMM d, yyyy} – {endDate:MMM d, yyyy}";
        string priorLabel = $"{priorStartDate:MMM d, yyyy} – {priorEndDate:MMM d, yyyy}";

        int currTotal = currentBookings.Count;
        int priorTotal = priorBookings.Count;
        double? changePct = priorTotal > 0
            ? Math.Round((double)(currTotal - priorTotal) / priorTotal * 100.0, 1)
            : null;

        var statusShifts = CanonicalStatusGroups.Select(group =>
        {
            int c = currentBookings.Count(b => b.NormalizedStatus == group);
            int p = priorBookings.Count(b => b.NormalizedStatus == group);
            double share = currTotal > 0 ? Math.Round((double)c / currTotal * 100.0, 1) : 0.0;
            return new BookingStatusShiftDto(group, c, p, c - p, share);
        }).ToList();

        var drivers = new List<DiagnosticDriverDto>();

        // ── Driver 1: Booking Status Shifts ──────────────────────────────────
        int currCancelledExpired = currentBookings.Count(b => b.NormalizedStatus is "Cancelled" or "Expired");
        int priorCancelledExpired = priorBookings.Count(b => b.NormalizedStatus is "Cancelled" or "Expired");
        int currPending = currentBookings.Count(b => b.NormalizedStatus == "Pending");
        int priorPending = priorBookings.Count(b => b.NormalizedStatus == "Pending");
        int currCompleted = currentBookings.Count(b => b.NormalizedStatus == "Completed");
        int priorCompleted = priorBookings.Count(b => b.NormalizedStatus == "Completed");

        int dropDelta = currCancelledExpired - priorCancelledExpired;
        string statusSeverity = currTotal == 0
            ? "low"
            : (double)currCancelledExpired / currTotal >= 0.25 || dropDelta >= 3
                ? "high"
                : currPending > currCompleted && currPending > 0
                    ? "medium"
                    : "positive";

        var statusAffected = currentBookings
            .OrderByDescending(b => b.NormalizedStatus is "Expired" or "Cancelled" ? 3 : b.NormalizedStatus == "Pending" ? 2 : 1)
            .ThenByDescending(b => b.EventDate)
            .Take(12)
            .Select(b => new DiagnosticAffectedItemDto(
                ReferenceId: b.BookingId,
                Title: b.Title,
                DateLabel: $"{b.EventDate:MMM d, yyyy} {b.EventTime:HH\\:mm}",
                Status: b.RawStatus,
                ContextDetail: $"{b.CustomerLabel} · {b.DownpaymentStatus} deposit" +
                               (string.IsNullOrWhiteSpace(b.AssignedEmployeeName) ? "" : $" · Crew: {b.AssignedEmployeeName}")))
            .ToList();

        drivers.Add(new DiagnosticDriverDto(
            DriverId: "status-shifts",
            Category: "Status Shifts",
            Title: "Booking Status & Hold Conversion Shifts",
            Severity: statusSeverity,
            HeadlineMetric: currTotal == 0
                ? "0 bookings in window"
                : $"{currCompleted} Completed · {currPending} Pending · {currCancelledExpired} Cancelled/Expired",
            DeltaLabel: FormatSignedDelta(dropDelta, "dropouts vs prior"),
            Summary: currTotal == 0
                ? "No bookings matched the active filter window to compare status shifts."
                : $"Completed sessions shifted from {priorCompleted} to {currCompleted}, Pending/Awaiting-Deposit from {priorPending} to {currPending}, and Cancelled/Expired from {priorCancelledExpired} to {currCancelledExpired}.",
            DetailedFinding: currTotal == 0
                ? "Expand the date range or reset filters to inspect booking status movements between periods."
                : $"Across {currTotal} bookings in {currentLabel} (vs {priorTotal} in {priorLabel}), {currCancelledExpired} sessions ended as Cancelled/Expired ({FormatSignedDelta(dropDelta, "vs prior")}) and {currPending} remain in Pending/Awaiting Downpayment. " +
                  (currCancelledExpired > 0
                      ? "Expired/cancelled holds typically stem from unconfirmed deposit windows (LockExpiresAt) or client schedule changes."
                      : "Booking funnel stability is healthy with zero cancellations or hold expirations in this range."),
            AffectedItems: statusAffected));

        // ── Driver 2: Staff Leave, DaysOff & Specialization Availability ─────
        var overlappingLeaves = snapshot.Leaves
            .Where(l => l.StartDate <= endDate && l.EndDate >= startDate &&
                        !string.Equals(l.Status, "Rejected", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(l.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            .OrderBy(l => l.StartDate)
            .ToList();

        var priorLeavesCount = snapshot.Leaves
            .Count(l => l.StartDate <= priorEndDate && l.EndDate >= priorStartDate &&
                        !string.Equals(l.Status, "Rejected", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(l.Status, "Cancelled", StringComparison.OrdinalIgnoreCase));

        var activeEmployees = snapshot.Employees
            .Where(e => !string.Equals(e.Status, "Archive", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(e.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var daysOffGroups = activeEmployees
            .SelectMany(e => (e.DaysOff ?? "Sunday")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(d => !string.Equals(d, "None", StringComparison.OrdinalIgnoreCase))
            .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} ({g.Count()})")
            .Take(3)
            .ToList();

        var thinSpecs = snapshot.RegisteredSpecializations
            .Select(spec => new
            {
                Spec = spec,
                Count = activeEmployees.Count(e => e.Specializations.Any(s => string.Equals(s, spec, StringComparison.OrdinalIgnoreCase)))
            })
            .Where(x => x.Count <= 1)
            .ToList();

        int leaveDelta = overlappingLeaves.Count - priorLeavesCount;
        string staffSeverity = overlappingLeaves.Count >= 4 || thinSpecs.Count >= 3
            ? "high"
            : overlappingLeaves.Count > 0 || thinSpecs.Count > 0
                ? "medium"
                : "positive";

        var staffAffected = new List<DiagnosticAffectedItemDto>();
        foreach (var lv in overlappingLeaves.Take(8))
        {
            staffAffected.Add(new DiagnosticAffectedItemDto(
                ReferenceId: lv.LeaveId,
                Title: $"{lv.EmployeeName} — {lv.LeaveType}",
                DateLabel: $"{lv.StartDate:MMM d} – {lv.EndDate:MMM d, yyyy}",
                Status: lv.Status,
                ContextDetail: string.IsNullOrWhiteSpace(lv.Reason) ? "Staff leave overlap in selected window" : lv.Reason!));
        }
        foreach (var ts in thinSpecs.Take(4))
        {
            staffAffected.Add(new DiagnosticAffectedItemDto(
                ReferenceId: "SPEC-GAP",
                Title: $"Specialization coverage: {ts.Spec}",
                DateLabel: "Current Roster",
                Status: ts.Count == 0 ? "Unassigned" : "Single Specialist",
                ContextDetail: $"{ts.Count} active staff member(s) tagged with {ts.Spec}"));
        }

        drivers.Add(new DiagnosticDriverDto(
            DriverId: "staff-availability",
            Category: "Staff Availability",
            Title: "Crew Leaves, Days Off & Specialization Coverage",
            Severity: staffSeverity,
            HeadlineMetric: $"{overlappingLeaves.Count} Leave(s) · {activeEmployees.Count} Active Crew",
            DeltaLabel: FormatSignedDelta(leaveDelta, "leaves vs prior"),
            Summary: $"{overlappingLeaves.Count} leave request(s) overlap this window; top scheduled DaysOff: {(daysOffGroups.Count > 0 ? string.Join(", ", daysOffGroups) : "None")}.",
            DetailedFinding: $"During {currentLabel}, {overlappingLeaves.Count} active/pending leave records overlap studio dates (vs {priorLeavesCount} in prior period). " +
                             $"Recurring crew DaysOff concentrate on {(daysOffGroups.Count > 0 ? string.Join(", ", daysOffGroups) : "standard off-days")}. " +
                             (thinSpecs.Count > 0
                                 ? $"{thinSpecs.Count} specialization(s) ({string.Join(", ", thinSpecs.Select(x => x.Spec).Take(3))}) have ≤1 active crew member, creating single-point coverage risk when leaves occur."
                                 : "All registered specializations have multi-staff coverage."),
            AffectedItems: staffAffected));

        // ── Driver 3: Equipment Downtime & Maintenance Holds ─────────────────
        var periodMaint = snapshot.MaintenanceSchedules
            .Where(m =>
            {
                var d = DateOnly.FromDateTime(m.ScheduledDateUtc);
                var u = DateOnly.FromDateTime(m.UpdatedAtUtc);
                bool inCurr = (d >= startDate && d <= endDate) || (u >= startDate && u <= endDate);
                bool unresolved = string.Equals(m.Status, "In Maintenance", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(m.Status, "Needs Replacement", StringComparison.OrdinalIgnoreCase);
                return inCurr || unresolved;
            })
            .OrderByDescending(m => m.UpdatedAtUtc)
            .ToList();

        var priorMaintCount = snapshot.MaintenanceSchedules.Count(m =>
        {
            var d = DateOnly.FromDateTime(m.ScheduledDateUtc);
            return d >= priorStartDate && d <= priorEndDate;
        });

        var heldGear = snapshot.Equipment
            .Where(e => !string.Equals(e.EquipmentStatus, "Available", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(e.EquipmentStatus, "Retired", StringComparison.OrdinalIgnoreCase))
            .ToList();

        int maintDelta = periodMaint.Count - priorMaintCount;
        string equipSeverity = heldGear.Count >= 3 || heldGear.Any(g => string.Equals(g.EquipmentStatus, "Needs Replacement", StringComparison.OrdinalIgnoreCase))
            ? "high"
            : heldGear.Count > 0 || periodMaint.Count > 0
                ? "medium"
                : "positive";

        var equipAffected = periodMaint.Take(8).Select(m => new DiagnosticAffectedItemDto(
            ReferenceId: m.EquipmentId,
            Title: $"{m.EquipmentName} ({m.ServiceType})",
            DateLabel: $"{m.ScheduledDateUtc:MMM d, yyyy}",
            Status: string.IsNullOrWhiteSpace(m.SubStatus) ? m.Status : $"{m.Status} · {m.SubStatus}",
            ContextDetail: string.IsNullOrWhiteSpace(m.IssueDetails) ? "Scheduled maintenance / inspection" : m.IssueDetails!))
            .ToList();

        foreach (var eq in heldGear.Where(g => equipAffected.All(a => !string.Equals(a.ReferenceId, g.EquipmentIDString(), StringComparison.OrdinalIgnoreCase))).Take(4))
        {
            equipAffected.Add(new DiagnosticAffectedItemDto(
                ReferenceId: eq.EquipmentId,
                Title: eq.EquipmentName,
                DateLabel: "Current Inventory",
                Status: eq.EquipmentStatus,
                ContextDetail: $"Category: {eq.EquipmentTypeName}"));
        }

        drivers.Add(new DiagnosticDriverDto(
            DriverId: "equipment-holds",
            Category: "Equipment Downtime",
            Title: "Equipment Downtime & Maintenance Holds",
            Severity: equipSeverity,
            HeadlineMetric: $"{heldGear.Count} Held Gear · {periodMaint.Count} Ticket(s)",
            DeltaLabel: FormatSignedDelta(maintDelta, "tickets vs prior"),
            Summary: $"{heldGear.Count} of {snapshot.Equipment.Count} inventory items are currently on hold (Maintenance / Needs Replacement), with {periodMaint.Count} ticket(s) active in period.",
            DetailedFinding: $"Inventory checks show {heldGear.Count} non-available unit(s) out of {snapshot.Equipment.Count} total assets, and {periodMaint.Count} maintenance schedule entries in/affecting {currentLabel} (vs {priorMaintCount} in prior period). " +
                             (heldGear.Count > 0
                                 ? "Equipment holds tighten camera/lighting redundancy during multi-track or simultaneous outdoor shoots."
                                 : "All studio equipment assets are currently marked Available with no open downtime holds."),
            AffectedItems: equipAffected));

        // ── Driver 4: Audit Logs & BookingNegotiation Schedule Changes ───────
        var periodAudits = snapshot.AuditRecords
            .Where(a =>
            {
                var d = DateOnly.FromDateTime(a.TimestampUtc);
                return d >= startDate && d <= endDate;
            })
            .OrderByDescending(a => a.TimestampUtc)
            .ToList();

        var priorAuditsCount = snapshot.AuditRecords.Count(a =>
        {
            var d = DateOnly.FromDateTime(a.TimestampUtc);
            return d >= priorStartDate && d <= priorEndDate;
        });

        var periodNegotiations = snapshot.Negotiations
            .Where(n =>
            {
                var d = n.ConsultationDate ?? DateOnly.FromDateTime(n.CreatedAtUtc);
                return d >= startDate && d <= endDate;
            })
            .ToList();

        int reschedOrLost = periodNegotiations.Count(n =>
            n.NegotiationStatus.Equals("Rescheduled", StringComparison.OrdinalIgnoreCase) ||
            n.NegotiationStatus.Equals("Deal_Lost", StringComparison.OrdinalIgnoreCase) ||
            n.NegotiationStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase));

        int scheduleChangesInAudit = periodAudits.Count(a =>
            a.Resource.Contains("Booking", StringComparison.OrdinalIgnoreCase) ||
            a.Resource.Contains("Schedule", StringComparison.OrdinalIgnoreCase) ||
            a.Resource.Contains("Appointment", StringComparison.OrdinalIgnoreCase) ||
            a.Resource.Contains("Negotiation", StringComparison.OrdinalIgnoreCase) ||
            a.Action.Contains("STATUS", StringComparison.OrdinalIgnoreCase) ||
            a.Action.Contains("RESCHED", StringComparison.OrdinalIgnoreCase));

        int auditDelta = periodAudits.Count - priorAuditsCount;
        string auditSeverity = reschedOrLost >= 3
            ? "high"
            : reschedOrLost > 0 || scheduleChangesInAudit > 0
                ? "medium"
                : "low";

        var auditAffected = new List<DiagnosticAffectedItemDto>();
        foreach (var neg in periodNegotiations
                     .OrderByDescending(n => n.NegotiationStatus is "Deal_Lost" or "Cancelled" or "Rescheduled" ? 2 : 1)
                     .ThenByDescending(n => n.CreatedAtUtc)
                     .Take(6))
        {
            var negDate = neg.ConsultationDate ?? DateOnly.FromDateTime(neg.CreatedAtUtc);
            auditAffected.Add(new DiagnosticAffectedItemDto(
                ReferenceId: neg.NegotiationId,
                Title: $"Consultation {neg.NegotiationId}" + (string.IsNullOrWhiteSpace(neg.EventAppointmentId) ? "" : $" → {neg.EventAppointmentId}"),
                DateLabel: $"{negDate:MMM d, yyyy}",
                Status: neg.NegotiationStatus,
                ContextDetail: string.IsNullOrWhiteSpace(neg.InquiryNotes)
                    ? $"Clerk: {neg.AssignedEmployeeName ?? "Unassigned"}"
                    : neg.InquiryNotes!));
        }

        foreach (var aud in periodAudits.Take(6))
        {
            auditAffected.Add(new DiagnosticAffectedItemDto(
                ReferenceId: aud.ReferenceId,
                Title: $"{aud.Action} · {aud.Resource}",
                DateLabel: $"{aud.TimestampUtc:MMM d, yyyy HH\\:mm} UTC",
                Status: aud.Source,
                ContextDetail: aud.Details));
        }

        drivers.Add(new DiagnosticDriverDto(
            DriverId: "audit-negotiations",
            Category: "Schedule & Audit Changes",
            Title: "Audit Trail & Consultation Negotiations",
            Severity: auditSeverity,
            HeadlineMetric: $"{scheduleChangesInAudit} Schedule Audits · {reschedOrLost} Resched/Lost Deals",
            DeltaLabel: FormatSignedDelta(auditDelta, "audit events vs prior"),
            Summary: $"{periodNegotiations.Count} consultation negotiation(s) ({reschedOrLost} rescheduled/lost) and {periodAudits.Count} audit/history events logged.",
            DetailedFinding: $"Operational logs recorded {periodAudits.Count} total audit/history entries ({scheduleChangesInAudit} directly tied to bookings, schedules, or status transitions) alongside {periodNegotiations.Count} BookingNegotiation records ({reschedOrLost} marked Rescheduled, Deal_Lost, or Cancelled) during {currentLabel}.",
            AffectedItems: auditAffected));

        return new DiagnosticAnalyticsDto(
            CurrentPeriodLabel: currentLabel,
            PriorPeriodLabel: priorLabel,
            CurrentTotalBookings: currTotal,
            PriorTotalBookings: priorTotal,
            BookingsChangePct: changePct,
            StatusShifts: statusShifts,
            Drivers: drivers);
    }

    private static PredictiveAnalyticsDto ComputePredictive(
        StudioAnalyticsSnapshot snapshot,
        DateOnly today,
        string activeScenario,
        string? categoryFilter)
    {
        const int historicalWindowMonths = 6;
        const int horizonMonths = 3;

        var currentMonthStart = new DateOnly(today.Year, today.Month, 1);
        var historyStart = currentMonthStart.AddMonths(-(historicalWindowMonths - 1));

        var scopedBookings = FilterBookings(snapshot.Bookings, statusFilter: null, categoryFilter: categoryFilter);
        var demandBookings = scopedBookings
            .Where(b => b.NormalizedStatus is not ("Cancelled" or "Expired"))
            .ToList();

        // Fallback to all bookings if every historical record was cancelled/expired
        if (demandBookings.Count == 0 && scopedBookings.Count > 0)
            demandBookings = scopedBookings;

        var historicalBuckets = new List<(DateOnly MonthStart, string Key, string Label, int Count)>();
        for (int i = 0; i < historicalWindowMonths; i++)
        {
            var mStart = historyStart.AddMonths(i);
            var mEnd = mStart.AddMonths(1).AddDays(-1);
            int count = demandBookings.Count(b => b.EventDate >= mStart && b.EventDate <= mEnd);
            historicalBuckets.Add((
                mStart,
                mStart.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                mStart.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                count));
        }

        // Trim leading empty months so new studios don't get dragged down by empty pre-launch months
        int firstNonZeroIdx = historicalBuckets.FindIndex(b => b.Count > 0);
        var effectiveHistory = firstNonZeroIdx >= 0
            ? historicalBuckets.Skip(firstNonZeroIdx).ToList()
            : historicalBuckets.TakeLast(1).ToList();

        int effectiveMonthsCount = effectiveHistory.Count(b => b.Count > 0);
        int totalHistoricalBookings = effectiveHistory.Sum(b => b.Count);
        double? monthlyAvg = effectiveMonthsCount > 0
            ? Math.Round(effectiveHistory.Average(b => (double)b.Count), 1)
            : null;

        // Compute linear trend slope (damped) + walk-forward MAE
        double baseLevel = effectiveHistory.Count > 0 ? effectiveHistory.Average(b => (double)b.Count) : 0.0;
        double slope = ComputeDampedSlope(effectiveHistory.Select(b => (double)b.Count).ToList());
        double? mae = ComputeWalkForwardMae(effectiveHistory.Select(b => (double)b.Count).ToList());

        double avgHoursPerBooking = 4.0;
        if (snapshot.Timesheets.Count > 0 && totalHistoricalBookings > 0)
        {
            double histHours = snapshot.Timesheets
                .Where(t => t.WorkDate >= historyStart && t.WorkDate <= today)
                .Sum(t => Math.Max(0, t.Hours));
            if (histHours > 0)
            {
                avgHoursPerBooking = Math.Clamp(Math.Round(histHours / totalHistoricalBookings, 1), 1.5, 16.0);
            }
        }

        int activeStaffCount = Math.Max(2, snapshot.Employees.Count(e =>
            !string.Equals(e.Status, "Archive", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(e.Status, "Inactive", StringComparison.OrdinalIgnoreCase)));
        // Assume ~16 session slots per active staff member per month as studio reference capacity
        double monthlyCapacityBaseline = Math.Max(16.0, activeStaffCount * 8.0);

        var scenarioDefs = new (string Key, string Label, double Multiplier)[]
        {
            ("cautious", "Cautious", 0.85),
            ("expected", "Expected", 1.00),
            ("strong",   "Strong",   1.18)
        };

        double errorBase = mae ?? Math.Max(1.0, baseLevel * 0.25);
        var scenarios = new List<PredictiveScenarioDto>();

        foreach (var (key, label, multiplier) in scenarioDefs)
        {
            var series = new List<ForecastSeriesPointDto>();

            foreach (var h in historicalBuckets)
            {
                series.Add(new ForecastSeriesPointDto(
                    PeriodKey: h.Key,
                    Label: h.Label,
                    IsHistorical: true,
                    HistoricalBookings: h.Count,
                    ConfirmedOnBooksCount: null,
                    ProjectedBookings: h.Count,
                    LowerBound: h.Count,
                    UpperBound: h.Count,
                    EstimatedCapacityPct: Math.Round(Math.Min(100.0, h.Count / monthlyCapacityBaseline * 100.0), 1)));
            }

            var futurePoints = new List<ForecastSeriesPointDto>();
            double lastHist = effectiveHistory.Count > 0 ? effectiveHistory[^1].Count : 0.0;
            double anchor = totalHistoricalBookings == 0 ? 0.0 : (0.6 * baseLevel + 0.4 * lastHist);

            for (int step = 1; step <= horizonMonths; step++)
            {
                var fStart = currentMonthStart.AddMonths(step);
                var fEnd = fStart.AddMonths(1).AddDays(-1);
                int alreadyBooked = demandBookings.Count(b => b.EventDate >= fStart && b.EventDate <= fEnd);

                double rawProjection = totalHistoricalBookings == 0
                    ? alreadyBooked
                    : Math.Max(alreadyBooked, (anchor + slope * step) * multiplier);

                double projected = Math.Round(rawProjection, 1);
                double spread = totalHistoricalBookings == 0 && alreadyBooked == 0
                    ? 0.0
                    : Math.Max(1.0, errorBase * (1.0 + 0.18 * (step - 1)));

                double lower = Math.Round(Math.Max(alreadyBooked, projected - spread), 1);
                double upper = Math.Round(projected + spread, 1);

                var fp = new ForecastSeriesPointDto(
                    PeriodKey: fStart.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    Label: fStart.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                    IsHistorical: false,
                    HistoricalBookings: null,
                    ConfirmedOnBooksCount: alreadyBooked,
                    ProjectedBookings: projected,
                    LowerBound: lower,
                    UpperBound: upper,
                    EstimatedCapacityPct: Math.Round(Math.Min(100.0, projected / monthlyCapacityBaseline * 100.0), 1));

                futurePoints.Add(fp);
                series.Add(fp);
            }

            int totalProjected = (int)Math.Round(futurePoints.Sum(p => p.ProjectedBookings));
            var peak = futurePoints.OrderByDescending(p => p.ProjectedBookings).First();

            scenarios.Add(new PredictiveScenarioDto(
                Key: key,
                Label: label,
                Multiplier: multiplier,
                ProjectedBookingsTotal: totalProjected,
                PeakPeriodLabel: peak.Label,
                PeakPeriodProjectedBookings: (int)Math.Round(peak.ProjectedBookings),
                EstimatedCrewHoursTotal: Math.Round(totalProjected * avgHoursPerBooking, 1),
                Series: series));
        }

        string confidenceLevel = effectiveMonthsCount >= 5
            ? "Good"
            : effectiveMonthsCount >= 3
                ? "Moderate"
                : "Low";

        string confidenceNote = effectiveMonthsCount switch
        {
            0 => "No historical non-cancelled bookings found in the past 6 months. Projections reflect confirmed future bookings on the books only.",
            < 3 => $"Only {effectiveMonthsCount} active historical month(s) ({totalHistoricalBookings} bookings) available; walk-forward MAE requires ≥ 3 months, so confidence bands use a conservative ±25% baseline.",
            < 5 => $"Based on {effectiveMonthsCount} active historical months ({totalHistoricalBookings} bookings). Sample size is moderate; monitor weekly actuals against the uncertainty band.",
            _ => $"Grounded in {effectiveMonthsCount} active historical months ({totalHistoricalBookings} bookings) with walk-forward error validation."
        };

        string methodologyNote =
            "Transparent damped trend + moving-average projection anchored to historical non-cancelled EventAppointment & Schedule bookings and confirmed future reservations already on the books. " +
            "Scenario toggles apply Cautious (0.85×), Expected (1.00×), and Strong (1.18×) demand multipliers; no guaranteed financial outcomes are implied.";

        return new PredictiveAnalyticsDto(
            HorizonMonths: horizonMonths,
            ActiveScenario: activeScenario,
            HistoricalMonthsCount: effectiveMonthsCount,
            HistoricalBookingsCount: totalHistoricalBookings,
            HistoricalMonthlyAverage: monthlyAvg,
            ModelErrorMae: mae.HasValue ? Math.Round(mae.Value, 2) : null,
            ConfidenceLevel: confidenceLevel,
            ConfidenceNote: confidenceNote,
            MethodologyNote: methodologyNote,
            Scenarios: scenarios);
    }

    private static PrescriptiveAnalyticsDto ComputePrescriptive(
        StudioAnalyticsSnapshot snapshot,
        DateOnly today,
        DateOnly startDate,
        DateOnly endDate)
    {
        var horizonEnd = today.AddDays(30);
        var latestAppliedByKey = snapshot.AppliedActions
            .GroupBy(a => a.ActionKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.AppliedAtUtc).First(),
                StringComparer.OrdinalIgnoreCase);

        var recommendations = new List<PrescriptiveRecommendationDto>();

        // 1. Follow up on Pending / Awaiting_Downpayment bookings
        var pendingBookings = snapshot.Bookings
            .Where(b => b.NormalizedStatus == "Pending" && b.EventDate >= today.AddDays(-7))
            .OrderBy(b => b.EventDate)
            .ToList();

        latestAppliedByKey.TryGetValue("follow-up-pending-deposits", out var appliedPending);
        recommendations.Add(new PrescriptiveRecommendationDto(
            ActionKey: "follow-up-pending-deposits",
            Title: pendingBookings.Count > 0
                ? $"Follow up on {pendingBookings.Count} pending / awaiting-downpayment booking(s)"
                : "Audit pending payment hold expiry settings",
            Description: pendingBookings.Count > 0
                ? $"There are {pendingBookings.Count} reservation(s) in Pending or Awaiting_Downpayment status (nearest date: {pendingBookings[0].EventDate:MMM d, yyyy}). Reach out to confirm the 15% deposit before PendingPaymentExpiryService releases the slot."
                : "No unpaid booking holds are currently at risk of expiry. Keep automated 30-minute hold reminders active for incoming inquiries.",
            Category: "Bookings",
            EffortTag: "Low Effort",
            EstimatedImpact: pendingBookings.Count > 0
                ? $"Protects {pendingBookings.Count} upcoming slot(s) from hold expiry"
                : "Maintains clean deposit conversion queue",
            TriggerSignal: pendingBookings.Count > 0
                ? $"{pendingBookings.Count} active booking(s) awaiting deposit"
                : "Signal clear (0 pending holds)",
            Priority: pendingBookings.Count > 0 ? 1 : 4,
            IsSignalTriggered: pendingBookings.Count > 0,
            IsAlreadyApplied: appliedPending is not null,
            LastAppliedAtUtc: appliedPending?.AppliedAtUtc,
            LastAppliedBy: appliedPending?.AppliedByName,
            RelatedReferences: pendingBookings.Take(5).Select(b => b.BookingId).ToList()));

        // 2. Buffer staff leave overlaps & tight days-off coverage
        var upcomingLeaves = snapshot.Leaves
            .Where(l => l.EndDate >= today && l.StartDate <= horizonEnd &&
                        !string.Equals(l.Status, "Rejected", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(l.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            .OrderBy(l => l.StartDate)
            .ToList();

        var leaveOverlapBookings = snapshot.Bookings
            .Where(b => b.EventDate >= today && b.EventDate <= horizonEnd &&
                        b.NormalizedStatus is not ("Cancelled" or "Expired") &&
                        upcomingLeaves.Any(l => b.EventDate >= l.StartDate && b.EventDate <= l.EndDate))
            .ToList();

        bool staffSignal = upcomingLeaves.Count > 0;
        latestAppliedByKey.TryGetValue("buffer-leave-overlap", out var appliedLeave);
        recommendations.Add(new PrescriptiveRecommendationDto(
            ActionKey: "buffer-leave-overlap",
            Title: staffSignal
                ? $"Buffer {upcomingLeaves.Count} upcoming crew leave(s) across next 30 days"
                : "Review crew DaysOff & specialization backup rotation",
            Description: staffSignal
                ? $"{upcomingLeaves.Count} leave record(s) fall between {today:MMM d} and {horizonEnd:MMM d}, overlapping {leaveOverlapBookings.Count} scheduled session(s). Assign backup photographers/clerks and verify specialization coverage."
                : "No approved or pending staff leaves overlap the next 30 days. Verify backup team-leader assignments for multi-track weekends.",
            Category: "Crew",
            EffortTag: "Medium Effort",
            EstimatedImpact: staffSignal
                ? $"Secures crew coverage for {Math.Max(1, leaveOverlapBookings.Count)} session window(s)"
                : "Prevents last-minute crew roster bottlenecks",
            TriggerSignal: staffSignal
                ? $"{upcomingLeaves.Count} leave(s) · {leaveOverlapBookings.Count} overlapping booking(s)"
                : "Signal clear (0 upcoming leaves)",
            Priority: leaveOverlapBookings.Count > 0 ? 1 : (staffSignal ? 2 : 5),
            IsSignalTriggered: staffSignal,
            IsAlreadyApplied: appliedLeave is not null,
            LastAppliedAtUtc: appliedLeave?.AppliedAtUtc,
            LastAppliedBy: appliedLeave?.AppliedByName,
            RelatedReferences: upcomingLeaves.Take(3).Select(l => l.LeaveId)
                .Concat(leaveOverlapBookings.Take(3).Select(b => b.BookingId))
                .ToList()));

        // 3. Pre-check high-demand or maintenance-held equipment
        var heldEquipment = snapshot.Equipment
            .Where(e => string.Equals(e.EquipmentStatus, "Maintenance", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(e.EquipmentStatus, "Needs Replacement", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var upcomingHighDemandGear = snapshot.EquipmentAssignments
            .Where(a => a.EventDate >= today && a.EventDate <= horizonEnd &&
                        !string.Equals(a.EventStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
            .GroupBy(a => new { a.EquipmentId, a.EquipmentName })
            .OrderByDescending(g => g.Count())
            .Take(3)
            .ToList();

        bool equipSignal = heldEquipment.Count > 0 || upcomingHighDemandGear.Any(g => g.Count() >= 2);
        latestAppliedByKey.TryGetValue("precheck-equipment-holds", out var appliedEquip);
        recommendations.Add(new PrescriptiveRecommendationDto(
            ActionKey: "precheck-equipment-holds",
            Title: heldEquipment.Count > 0
                ? $"Resolve {heldEquipment.Count} equipment hold(s) & pre-check dispatch kits"
                : "Run pre-shoot calibration on top-booked camera & lighting gear",
            Description: heldEquipment.Count > 0
                ? $"{heldEquipment.Count} inventory item(s) are currently in Maintenance or Needs Replacement. Reassign redundant bodies/lenses for upcoming shoots and clear open MaintenanceSchedule tickets."
                : upcomingHighDemandGear.Count > 0
                    ? $"Top reserved gear over the next 30 days ({string.Join(", ", upcomingHighDemandGear.Select(g => $"{g.Key.EquipmentName} ({g.Count()}×)"))}) should undergo battery/sensor pre-checks prior to dispatch."
                    : "All studio equipment is Available with light dispatch load over the next 30 days.",
            Category: "Equipment",
            EffortTag: heldEquipment.Count > 0 ? "High Effort" : "Low Effort",
            EstimatedImpact: heldEquipment.Count > 0
                ? $"Restores {heldEquipment.Count} gear unit(s) to dispatch pool"
                : "Reduces on-set gear failure risk",
            TriggerSignal: heldEquipment.Count > 0
                ? $"{heldEquipment.Count} unit(s) in Maintenance / Needs Replacement"
                : $"{upcomingHighDemandGear.Count} high-demand gear items scheduled",
            Priority: heldEquipment.Count > 0 ? 1 : 3,
            IsSignalTriggered: equipSignal,
            IsAlreadyApplied: appliedEquip is not null,
            LastAppliedAtUtc: appliedEquip?.AppliedAtUtc,
            LastAppliedBy: appliedEquip?.AppliedByName,
            RelatedReferences: heldEquipment.Select(e => e.EquipmentId)
                .Concat(upcomingHighDemandGear.Select(g => g.Key.EquipmentId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList()));

        // 4. Open additional off-peak / weekend studio slots
        var windowBookings = snapshot.Bookings
            .Where(b => b.EventDate >= startDate && b.EventDate <= endDate &&
                        b.NormalizedStatus is not ("Cancelled" or "Expired"))
            .ToList();
        int weekendCount = windowBookings.Count(b => b.EventDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        int weekdayCount = windowBookings.Count - weekendCount;
        bool weekendHeavy = windowBookings.Count > 0 && (double)weekendCount / windowBookings.Count >= 0.55;

        latestAppliedByKey.TryGetValue("open-offpeak-slots", out var appliedSlots);
        recommendations.Add(new PrescriptiveRecommendationDto(
            ActionKey: "open-offpeak-slots",
            Title: weekendHeavy
                ? "Open off-peak weekday portrait/lookbook slots to relieve weekend load"
                : "Add extra weekend afternoon studio blocks for peak demand",
            Description: windowBookings.Count > 0
                ? $"{weekendCount} of {windowBookings.Count} active bookings ({Math.Round((double)weekendCount / windowBookings.Count * 100, 0)}%) fall on Sat/Sun vs {weekdayCount} on weekdays. " +
                  (weekendHeavy
                      ? "Promote Tue–Thu studio blocks in Book Schedule to smooth crew utilization."
                      : "Keep Saturday/Sunday 1–5 PM blocks staffed with a standby crew lead.")
                : "Insufficient bookings in the selected range to detect weekday/weekend skew; monitor 9–11 AM and 1–5 PM slot fill rates.",
            Category: "Schedule",
            EffortTag: "Medium Effort",
            EstimatedImpact: "Improves studio slot utilization & evens crew workload",
            TriggerSignal: windowBookings.Count > 0
                ? $"Weekend share: {weekendCount}/{windowBookings.Count} bookings"
                : "Baseline schedule capacity check",
            Priority: weekendHeavy ? 2 : 4,
            IsSignalTriggered: weekendHeavy,
            IsAlreadyApplied: appliedSlots is not null,
            LastAppliedAtUtc: appliedSlots?.AppliedAtUtc,
            LastAppliedBy: appliedSlots?.AppliedByName,
            RelatedReferences: []));

        // 5. Follow up on stalled BookingNegotiation consultations
        var openNegotiations = snapshot.Negotiations
            .Where(n => string.IsNullOrEmpty(n.EventAppointmentId) &&
                        (n.NegotiationStatus.Equals("Pending_Call", StringComparison.OrdinalIgnoreCase) ||
                         n.NegotiationStatus.Equals("Rescheduled", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(n => n.CreatedAtUtc)
            .ToList();

        latestAppliedByKey.TryGetValue("convert-pending-consultations", out var appliedNeg);
        recommendations.Add(new PrescriptiveRecommendationDto(
            ActionKey: "convert-pending-consultations",
            Title: openNegotiations.Count > 0
                ? $"Convert {openNegotiations.Count} open consultation(s) into scheduled shoots"
                : "Audit consultation-to-booking turnaround time",
            Description: openNegotiations.Count > 0
                ? $"{openNegotiations.Count} BookingNegotiation inquiry/inquiries remain in Pending_Call or Rescheduled without a linked EventAppointment. Have an Office Clerk send package proposals."
                : "All recent consultations have progressed past Pending_Call/Rescheduled.",
            Category: "Bookings",
            EffortTag: "Low Effort",
            EstimatedImpact: openNegotiations.Count > 0
                ? $"Moves up to {openNegotiations.Count} consultation(s) toward confirmed booking"
                : "Keeps inquiry pipeline responsive",
            TriggerSignal: openNegotiations.Count > 0
                ? $"{openNegotiations.Count} unlinked consultation(s)"
                : "Signal clear (0 stalled inquiries)",
            Priority: openNegotiations.Count > 0 ? 2 : 5,
            IsSignalTriggered: openNegotiations.Count > 0,
            IsAlreadyApplied: appliedNeg is not null,
            LastAppliedAtUtc: appliedNeg?.AppliedAtUtc,
            LastAppliedBy: appliedNeg?.AppliedByName,
            RelatedReferences: openNegotiations.Take(5).Select(n => n.NegotiationId).ToList()));

        var orderedRecommendations = recommendations
            .OrderByDescending(r => r.IsSignalTriggered)
            .ThenBy(r => r.Priority)
            .ToList();

        var history = snapshot.AppliedActions
            .OrderByDescending(a => a.AppliedAtUtc)
            .Take(20)
            .Select(a => new AppliedAnalyticsActionDto(
                Id: a.Id,
                ActionKey: a.ActionKey,
                Title: a.Title,
                Category: a.Category,
                EffortTag: a.EffortTag,
                EstimatedImpact: a.EstimatedImpact,
                TriggerSignal: a.TriggerSignal,
                RelatedReferences: a.RelatedReferences,
                Notes: a.Notes,
                AppliedByUserId: a.AppliedByUserId,
                AppliedByName: a.AppliedByName,
                AppliedAtUtc: a.AppliedAtUtc,
                Status: a.Status))
            .ToList();

        return new PrescriptiveAnalyticsDto(orderedRecommendations, history);
    }

    private static double ComputeDampedSlope(List<double> values)
    {
        int n = values.Count;
        if (n < 2) return 0.0;

        double meanX = (n - 1) / 2.0;
        double meanY = values.Average();
        double num = 0.0;
        double den = 0.0;
        for (int i = 0; i < n; i++)
        {
            double dx = i - meanX;
            num += dx * (values[i] - meanY);
            den += dx * dx;
        }
        if (den <= 0.0) return 0.0;
        // Damp slope by 0.5x so noisy small samples don't extrapolate wildly
        return (num / den) * 0.5;
    }

    private static double? ComputeWalkForwardMae(List<double> values)
    {
        if (values.Count < 3) return null;

        var errors = new List<double>();
        for (int t = 2; t < values.Count; t++)
        {
            var prefix = values.Take(t).ToList();
            double avg = prefix.Average();
            double slope = ComputeDampedSlope(prefix);
            double pred = Math.Max(0.0, avg + slope);
            errors.Add(Math.Abs(values[t] - pred));
        }
        return errors.Count > 0 ? errors.Average() : null;
    }

    private static string NormalizeScenario(string? scenario) =>
        scenario?.Trim().ToLowerInvariant() switch
        {
            "cautious" => "cautious",
            "strong" => "strong",
            _ => "expected"
        };

    private static DateOnly StartOfIsoWeek(DateOnly date)
    {
        int diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-diff);
    }

    private static string FormatSignedDelta(int delta, string suffix)
    {
        if (delta == 0) return $"0 {suffix}";
        return delta > 0 ? $"+{delta} {suffix}" : $"{delta} {suffix}";
    }

    private static string EquipmentIDString(this AnalyticsEquipmentRecord e) => e.EquipmentId;

    public static string BuildCsvReport(StudioAnalyticsOverviewDto overview)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Section,Metric,Dimension,Value,Notes");

        var d = overview.Descriptive;
        sb.AppendLine(CsvRow("Metadata", "Date Window", "Current", $"{overview.StartDate:yyyy-MM-dd} to {overview.EndDate:yyyy-MM-dd}", $"Prior: {overview.PriorStartDate:yyyy-MM-dd} to {overview.PriorEndDate:yyyy-MM-dd}"));
        sb.AppendLine(CsvRow("Descriptive", "Total Bookings", "All", d.TotalBookings.ToString(CultureInfo.InvariantCulture), $"Events: {d.EventAppointmentsCount}; Consultations: {d.ConsultationBookingsCount}"));
        sb.AppendLine(CsvRow("Descriptive", "Session Completion Rate", "%", d.SessionCompletionRate.ToString("0.0", CultureInfo.InvariantCulture), $"Completed/Delivered: {d.CompletedDeliveredCount}; Confirmed: {d.ConfirmedActiveCount}; Pending: {d.PendingHoldCount}; Cancelled/Expired: {d.CancelledExpiredCount}"));
        sb.AppendLine(CsvRow("Descriptive", "Logged Crew Hours", "Hours", d.LoggedCrewHours.ToString("0.0", CultureInfo.InvariantCulture), $"Entries: {d.TimesheetEntriesCount}; Active Crew: {d.ActiveCrewLoggedCount}"));
        sb.AppendLine(CsvRow("Descriptive", "Equipment Maintenance Holds", "Units", d.EquipmentMaintenanceHolds.ToString(CultureInfo.InvariantCulture), $"In Maintenance: {d.EquipmentInMaintenanceCount}; Needs Replacement: {d.EquipmentNeedsReplacementCount}; Total Gear: {d.TotalEquipmentCount}"));

        foreach (var pt in d.VolumeSeries)
        {
            sb.AppendLine(CsvRow("Descriptive Volume", pt.Label, pt.Key, pt.TotalBookings.ToString(CultureInfo.InvariantCulture), $"Completed={pt.CompletedCount}; Confirmed={pt.ConfirmedCount}; Pending={pt.PendingCount}; Cancelled/Expired={pt.CancelledOrExpiredCount}; CrewHours={pt.LoggedCrewHours:0.0}"));
        }

        foreach (var svc in d.TopServices)
        {
            sb.AppendLine(CsvRow("Top Services", svc.Name, svc.Kind, svc.BookingCount.ToString(CultureInfo.InvariantCulture), $"Category={svc.CategoryName}; Qty={svc.TotalQuantity}; Share={svc.SharePct:0.0}%"));
        }

        foreach (var shift in overview.Diagnostic.StatusShifts)
        {
            sb.AppendLine(CsvRow("Diagnostic Status Shift", shift.StatusGroup, "Current vs Prior", shift.CurrentCount.ToString(CultureInfo.InvariantCulture), $"Prior={shift.PriorCount}; Delta={shift.Delta}; Share={shift.ShareOfCurrentPct:0.0}%"));
        }

        foreach (var drv in overview.Diagnostic.Drivers)
        {
            sb.AppendLine(CsvRow("Diagnostic Driver", drv.Title, drv.Severity, drv.HeadlineMetric, $"{drv.DeltaLabel} | {drv.Summary}"));
        }

        var activeScenario = overview.Predictive.Scenarios.FirstOrDefault(s => s.Key == overview.Predictive.ActiveScenario)
                             ?? overview.Predictive.Scenarios.FirstOrDefault();
        if (activeScenario is not null)
        {
            sb.AppendLine(CsvRow("Predictive", $"Projected Bookings ({activeScenario.Label})", "Next 3 Months", activeScenario.ProjectedBookingsTotal.ToString(CultureInfo.InvariantCulture), $"Peak={activeScenario.PeakPeriodLabel} ({activeScenario.PeakPeriodProjectedBookings}); Confidence={overview.Predictive.ConfidenceLevel}; MAE={overview.Predictive.ModelErrorMae?.ToString("0.00", CultureInfo.InvariantCulture) ?? "N/A"}"));
            foreach (var fp in activeScenario.Series.Where(s => !s.IsHistorical))
            {
                sb.AppendLine(CsvRow("Predictive Forecast", fp.Label, activeScenario.Label, fp.ProjectedBookings.ToString("0.0", CultureInfo.InvariantCulture), $"Range={fp.LowerBound:0.0}..{fp.UpperBound:0.0}; ConfirmedOnBooks={fp.ConfirmedOnBooksCount ?? 0}"));
            }
        }

        foreach (var rec in overview.Prescriptive.Recommendations)
        {
            sb.AppendLine(CsvRow("Prescriptive", rec.Title, rec.EffortTag, rec.IsAlreadyApplied ? "Applied" : "Recommended", $"{rec.Category} | {rec.EstimatedImpact} | {rec.TriggerSignal}"));
        }

        return sb.ToString();
    }

    private static string CsvRow(params string[] cols) =>
        string.Join(",", cols.Select(EscapeCsv));

    private static string EscapeCsv(string? value)
    {
        var v = value ?? string.Empty;
        if (v.Contains('"') || v.Contains(',') || v.Contains('\n') || v.Contains('\r'))
            return $"\"{v.Replace("\"", "\"\"")}\"";
        return v;
    }
}
