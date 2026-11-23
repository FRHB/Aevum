using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public enum ServiceRateCategory
{
    PerHour,
    PerSession,
    PerShot
}

/// <summary>
/// Tracks per-service customization, quantity/duration inputs, and confirmation state inside the Booking Wizard.
/// </summary>
public sealed class ServiceCustomizationItem
{
    public string ServicesId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RateType { get; set; } = "Per Session";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; } = 1;
    public int DurationHours { get; set; } = 1;
    public bool IsConfirmed { get; set; }

    public ServiceRateCategory RateCategory => BookingWizardEngine.ClassifyRateType(RateType);

    public int EffectiveDurationHours => RateCategory switch
    {
        ServiceRateCategory.PerHour => Math.Max(1, Quantity),
        ServiceRateCategory.PerSession => Math.Max(1, DurationHours),
        _ => 1
    };

    public decimal LineTotal => UnitPrice * Math.Max(1, Quantity);
}

public sealed record AvailableCrewOption(EmployeeViewModel Employee, string AvailabilityText);

public sealed record BufferedTimeWindow(TimeSpan PrepStart, TimeSpan EventStart, TimeSpan EventEnd, TimeSpan PostEnd)
{
    public TimeSpan PrepDuration => EventStart - PrepStart;
    public TimeSpan EventDuration => EventEnd - EventStart;
    public TimeSpan PostDuration => PostEnd - EventEnd;
}

public sealed record ShootScheduleValidationResult(
    bool IsValid,
    string? ErrorMessage,
    int AvailableCrewCount,
    BufferedTimeWindow BufferedWindow);

public sealed class ScheduleServiceItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string RateType { get; set; } = "Per Session";
}

/// <summary>
/// Pure scheduling, rate calculation, buffer conflict, and crew availability engine for the Booking Wizard.
/// </summary>
public class BookingWizardEngine
{
    public static readonly TimeSpan PrepBuffer = TimeSpan.FromMinutes(ShootAvailabilityCore.PrepBufferMinutes);
    public static readonly TimeSpan PostBuffer = TimeSpan.FromMinutes(ShootAvailabilityCore.PostBufferMinutes);
    public static readonly TimeSpan OperatingStart = TimeSpan.FromMinutes(ShootAvailabilityCore.OperatingStartMinutes);
    public static readonly TimeSpan OperatingEnd = TimeSpan.FromMinutes(ShootAvailabilityCore.OperatingEndMinutes);
    private static readonly TimeSpan EndOfDayCap = new(23, 59, 59);

    public static ServiceRateCategory ClassifyRateType(string? rateType)
    {
        if (string.IsNullOrWhiteSpace(rateType))
            return ServiceRateCategory.PerSession;

        var norm = rateType.Trim();
        if (norm.Contains("hour", StringComparison.OrdinalIgnoreCase))
            return ServiceRateCategory.PerHour;

        if (norm.Contains("shot", StringComparison.OrdinalIgnoreCase) ||
            norm.Contains("picture", StringComparison.OrdinalIgnoreCase) ||
            norm.Contains("photo", StringComparison.OrdinalIgnoreCase))
            return ServiceRateCategory.PerShot;

        return ServiceRateCategory.PerSession;
    }

    public static string GetRateQuantityLabel(string? rateType)
    {
        var category = ClassifyRateType(rateType);
        if (category == ServiceRateCategory.PerHour)
            return "Hours Quantity *";
        if (category == ServiceRateCategory.PerSession)
            return "Session Quantity *";

        var norm = (rateType ?? string.Empty).Trim();
        if (norm.Contains("multi", StringComparison.OrdinalIgnoreCase))
            return "Service Quantity *";
        if (norm.Contains("picture", StringComparison.OrdinalIgnoreCase) || norm.Contains("photo", StringComparison.OrdinalIgnoreCase))
            return "Pictures Quantity *";
        if (norm.Contains("single", StringComparison.OrdinalIgnoreCase) || norm.Contains("shot", StringComparison.OrdinalIgnoreCase))
            return "Shots Quantity *";

        return "Service Quantity *";
    }

    public static string GetRateQuantityPlaceholder(string? rateType)
    {
        var category = ClassifyRateType(rateType);
        if (category == ServiceRateCategory.PerHour)
            return "Hours";
        if (category == ServiceRateCategory.PerSession)
            return "Sessions";

        var norm = (rateType ?? string.Empty).Trim();
        if (norm.Contains("multi", StringComparison.OrdinalIgnoreCase))
            return "Qty (depends on service)";
        if (norm.Contains("picture", StringComparison.OrdinalIgnoreCase) || norm.Contains("photo", StringComparison.OrdinalIgnoreCase))
            return "Pictures";
        if (norm.Contains("single", StringComparison.OrdinalIgnoreCase) || norm.Contains("shot", StringComparison.OrdinalIgnoreCase))
            return "Shots";

        return "Qty (depends on service)";
    }

    public static string GetRateBillingSummary(string? rateType, int quantity)
    {
        var qty = Math.Max(1, quantity);
        var category = ClassifyRateType(rateType);
        if (category == ServiceRateCategory.PerHour)
            return $"Billed per hour. Contributes {qty} hr(s) to shoot duration (concurrent max across hourly services).";
        if (category == ServiceRateCategory.PerSession)
            return "Per-session rate; duration determined in hours.";

        var norm = (rateType ?? string.Empty).Trim();
        if (norm.Contains("multi", StringComparison.OrdinalIgnoreCase))
            return $"Billed based on the service provided ({qty} unit(s)). Set exact Start & End time below.";
        if (norm.Contains("picture", StringComparison.OrdinalIgnoreCase) || norm.Contains("photo", StringComparison.OrdinalIgnoreCase))
            return $"Billed per picture ({qty} picture(s)). Set exact Start & End time below.";
        if (norm.Contains("single", StringComparison.OrdinalIgnoreCase) || norm.Contains("shot", StringComparison.OrdinalIgnoreCase))
            return $"Billed per single shot ({qty} shot(s)). Set exact Start & End time below.";

        return $"Billed based on the service provided ({qty} unit(s)). Set exact Start & End time below.";
    }

    public static string GetRateUnitLabel(string? rateType)
    {
        var category = ClassifyRateType(rateType);
        if (category == ServiceRateCategory.PerHour)
            return "hr(s)";
        if (category == ServiceRateCategory.PerSession)
            return "session(s)";

        var norm = (rateType ?? string.Empty).Trim();
        if (norm.Contains("multi", StringComparison.OrdinalIgnoreCase))
            return "unit(s)";
        if (norm.Contains("picture", StringComparison.OrdinalIgnoreCase) || norm.Contains("photo", StringComparison.OrdinalIgnoreCase))
            return "picture(s)";
        if (norm.Contains("single", StringComparison.OrdinalIgnoreCase) || norm.Contains("shot", StringComparison.OrdinalIgnoreCase))
            return "shot(s)";

        return "unit(s)";
    }

    public static ServiceCustomizationItem CreateCustomizationItem(ScheduleServiceOption svc)
    {
        var category = ClassifyRateType(svc.RateType);
        return new ServiceCustomizationItem
        {
            ServicesId = svc.ServicesId,
            Name = svc.Name,
            RateType = string.IsNullOrWhiteSpace(svc.RateType) ? "Per Session" : svc.RateType,
            UnitPrice = svc.Price,
            Quantity = 1,
            DurationHours = category == ServiceRateCategory.PerSession ? 2 : 1,
            IsConfirmed = false
        };
    }

    /// <summary>
    /// Combined shoot duration uses the maximum duration across Per-Hour / Per-Session services
    /// (e.g., two 3-hour services run concurrently in a 3-hour shoot, not 6 hours), defaulting to 60 minutes for Per-Shot.
    /// </summary>
    public static int ComputeCombinedShootDurationMinutes(IEnumerable<ServiceCustomizationItem> selections)
    {
        var list = selections?.ToList() ?? new List<ServiceCustomizationItem>();
        if (list.Count == 0) return 60;

        var timedHours = list
            .Where(s => s.RateCategory is ServiceRateCategory.PerHour or ServiceRateCategory.PerSession)
            .Select(s => s.EffectiveDurationHours)
            .ToList();

        var maxHours = timedHours.Count > 0 ? timedHours.Max() : 1;
        return Math.Max(60, maxHours * 60);
    }

    public static BufferedTimeWindow ComputeBufferedWindow(TimeSpan eventStart, TimeSpan eventEnd)
    {
        if (eventEnd <= eventStart)
            eventEnd = eventStart.Add(TimeSpan.FromHours(1));

        var prepStart = eventStart >= PrepBuffer ? eventStart.Subtract(PrepBuffer) : TimeSpan.Zero;
        var postEnd = eventEnd.Add(PostBuffer);
        if (postEnd > EndOfDayCap) postEnd = EndOfDayCap;

        return new BufferedTimeWindow(prepStart, eventStart, eventEnd, postEnd);
    }

    public static bool WindowsOverlap(TimeSpan aStart, TimeSpan aEnd, TimeSpan bStart, TimeSpan bEnd) =>
        aStart < bEnd && bStart < aEnd;

    public static bool IsEmployeeOnDayOff(EmployeeViewModel emp, DateTime date) =>
        ShootAvailabilityCore.IsDayOff(emp.DaysOff, emp.SelectedDaysOff, date.DayOfWeek);

    public static bool IsEmployeeAssignedToBooking(EmployeeViewModel emp, EventBookingModel booking)
    {
        if (booking.AssignedCrewIds != null && booking.AssignedCrewIds.Count > 0)
        {
            return booking.AssignedCrewIds.Any(id => string.Equals(id, emp.EmployeeId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(booking.AssignedPhotographer))
        {
            var tokens = booking.AssignedPhotographer
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (tokens.Any(t =>
                string.Equals(t, emp.FullName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t, emp.EmployeeId, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    public static List<(TimeSpan BlockedStart, TimeSpan BlockedEnd, string Label)> GetEmployeeBusyIntervals(
        EmployeeViewModel emp,
        DateTime date,
        IEnumerable<EventBookingModel>? bookings,
        IEnumerable<ConsultationNegotiationDto>? negotiations,
        string? excludeAppointmentId = null)
    {
        var raw = new List<(TimeSpan Start, TimeSpan End, string Label)>();

        if (bookings != null)
        {
            foreach (var b in bookings)
            {
                if (b.IsConsultation || b.Date.Date != date.Date) continue;
                if (!string.IsNullOrWhiteSpace(excludeAppointmentId) &&
                    string.Equals(b.Id, excludeAppointmentId, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsInactiveStatus(b.Status)) continue;
                if (!IsEmployeeAssignedToBooking(emp, b)) continue;

                var win = ComputeBufferedWindow(b.StartTime, b.EffectiveEndTime);
                raw.Add((win.PrepStart, win.PostEnd, b.ServiceType));
            }
        }

        if (negotiations != null)
        {
            foreach (var neg in negotiations)
            {
                if (neg.Appointment != null &&
                    neg.Appointment.EventDate.ToDateTime(TimeOnly.MinValue).Date == date.Date &&
                    !IsInactiveStatus(neg.Appointment.EventStatus) &&
                    !IsInactiveStatus(neg.NegotiationStatus))
                {
                    if (!string.IsNullOrWhiteSpace(excludeAppointmentId) &&
                        string.Equals(neg.Appointment.EventAppointmentId, excludeAppointmentId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var assigned = neg.Appointment.AssignedEmployees?
                        .Any(ae => string.Equals(ae.EmployeeId, emp.EmployeeId, StringComparison.OrdinalIgnoreCase)) == true;
                    if (!assigned) continue;

                    var start = neg.Appointment.EventTime.ToTimeSpan();
                    var end = neg.Appointment.EventEndTime.HasValue && neg.Appointment.EventEndTime.Value > neg.Appointment.EventTime
                        ? neg.Appointment.EventEndTime.Value.ToTimeSpan()
                        : start.Add(TimeSpan.FromMinutes(Math.Max(60, neg.Appointment.DurationMinutes)));
                    var win = ComputeBufferedWindow(start, end);
                    raw.Add((win.PrepStart, win.PostEnd, neg.Appointment.EventName));
                }
                else if (neg.ConsultationDate.ToDateTime(TimeOnly.MinValue).Date == date.Date &&
                         !IsInactiveStatus(neg.NegotiationStatus) &&
                         string.Equals(neg.EmployeeId, emp.EmployeeId, StringComparison.OrdinalIgnoreCase))
                {
                    var start = neg.ConsultationTime.ToTimeSpan();
                    var win = ComputeBufferedWindow(start, start.Add(TimeSpan.FromHours(1)));
                    raw.Add((win.PrepStart, win.PostEnd, $"Consultation: {neg.CustomerName}"));
                }
            }
        }

        if (raw.Count == 0) return raw;

        var sorted = raw.OrderBy(r => r.Start).ThenBy(r => r.End).ToList();
        var merged = new List<(TimeSpan BlockedStart, TimeSpan BlockedEnd, string Label)> { sorted[0] };
        for (int i = 1; i < sorted.Count; i++)
        {
            var last = merged[^1];
            var cur = sorted[i];
            if (cur.Start <= last.BlockedEnd)
            {
                merged[^1] = (last.BlockedStart, cur.End > last.BlockedEnd ? cur.End : last.BlockedEnd, last.Label);
            }
            else
            {
                merged.Add(cur);
            }
        }

        return merged;
    }

    public static string? GetEmployeeBlockReason(
        EmployeeViewModel emp,
        DateTime date,
        IEnumerable<LeaveItemViewModel>? approvedLeaves = null)
    {
        if (!emp.IsActive)
            return "Inactive staff member";

        if (IsEmployeeOnDayOff(emp, date))
            return $"Scheduled Day Off ({date:dddd})";

        var d = DateOnly.FromDateTime(date);
        if (approvedLeaves != null)
        {
            var leave = approvedLeaves.FirstOrDefault(l =>
                string.Equals(l.EmployeeStudioId, emp.EmployeeId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(l.Status, "Approved", StringComparison.OrdinalIgnoreCase) &&
                d >= l.StartDate && d <= l.EndDate);
            if (leave != null)
                return $"On approved {leave.LeaveType} ({leave.StartDate:MMM d} – {leave.EndDate:MMM d})";
        }

        if (date.Date == DateTime.Today && emp.IsOnLeaveToday)
            return $"On approved {emp.ActiveLeaveType ?? "Leave"} today";

        return null;
    }

    public static bool IsOfficeClerk(EmployeeViewModel? emp)
    {
        if (emp == null) return false;

        if (!string.IsNullOrWhiteSpace(emp.EmployeeId) &&
            emp.EmployeeId.StartsWith("CLK-", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(emp.Role) &&
            emp.Role.Contains("clerk", StringComparison.OrdinalIgnoreCase))
            return true;

        if (emp.Specializations != null &&
            emp.Specializations.Any(s => !string.IsNullOrWhiteSpace(s) && s.Contains("clerk", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    public static IEnumerable<EmployeeViewModel> FilterDispatchableCrew(IEnumerable<EmployeeViewModel>? employees) =>
        (employees ?? Enumerable.Empty<EmployeeViewModel>())
            .Where(emp => emp != null && !IsOfficeClerk(emp));

    public static bool IsCrewAvailableForWindow(
        EmployeeViewModel emp,
        DateTime date,
        TimeSpan eventStart,
        TimeSpan eventEnd,
        IEnumerable<EventBookingModel>? bookings,
        IEnumerable<ConsultationNegotiationDto>? negotiations,
        string? excludeAppointmentId = null,
        IEnumerable<LeaveItemViewModel>? approvedLeaves = null)
    {
        if (IsOfficeClerk(emp))
            return false;

        if (GetEmployeeBlockReason(emp, date, approvedLeaves) != null)
            return false;

        var candidate = ComputeBufferedWindow(eventStart, eventEnd);
        var busy = GetEmployeeBusyIntervals(emp, date, bookings, negotiations, excludeAppointmentId);

        return !busy.Any(b => WindowsOverlap(candidate.PrepStart, candidate.PostEnd, b.BlockedStart, b.BlockedEnd));
    }

    public static string GetCrewAvailabilityText(
        EmployeeViewModel emp,
        DateTime date,
        TimeSpan eventStart,
        TimeSpan eventEnd,
        IEnumerable<EventBookingModel>? bookings,
        IEnumerable<ConsultationNegotiationDto>? negotiations,
        string? excludeAppointmentId = null)
    {
        var candidate = ComputeBufferedWindow(eventStart, eventEnd);
        var busy = GetEmployeeBusyIntervals(emp, date, bookings, negotiations, excludeAppointmentId);
        if (busy.Count == 0)
            return "Available all day";

        var prevBusy = busy.LastOrDefault(b => b.BlockedEnd <= candidate.PrepStart);
        var nextBusy = busy.FirstOrDefault(b => b.BlockedStart >= candidate.PostEnd);

        var hasPrev = prevBusy != default;
        var hasNext = nextBusy != default;

        if (hasPrev && hasNext)
            return $"Available {FormatTime(prevBusy.BlockedEnd)} - {FormatTime(nextBusy.BlockedStart)}";

        if (hasPrev)
            return $"Available {FormatTime(prevBusy.BlockedEnd)} onward";

        if (hasNext)
            return $"Available until {FormatTime(nextBusy.BlockedStart)}";

        return "Available all day";
    }

    public static List<AvailableCrewOption> GetAvailableCrews(
        IEnumerable<EmployeeViewModel> employees,
        DateTime date,
        TimeSpan eventStart,
        TimeSpan eventEnd,
        IEnumerable<EventBookingModel>? bookings,
        IEnumerable<ConsultationNegotiationDto>? negotiations,
        string? excludeAppointmentId = null,
        IEnumerable<LeaveItemViewModel>? approvedLeaves = null)
    {
        var available = new List<AvailableCrewOption>();
        foreach (var emp in FilterDispatchableCrew(employees))
        {
            if (!IsCrewAvailableForWindow(emp, date, eventStart, eventEnd, bookings, negotiations, excludeAppointmentId, approvedLeaves))
                continue;

            var text = GetCrewAvailabilityText(emp, date, eventStart, eventEnd, bookings, negotiations, excludeAppointmentId);
            available.Add(new AvailableCrewOption(emp, text));
        }

        return available;
    }

    public static int CountShootBookingsOnDate(
        DateTime date,
        IEnumerable<EventBookingModel>? bookings,
        IEnumerable<ConsultationNegotiationDto>? negotiations,
        string? excludeAppointmentId = null)
    {
        var seenEventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int count = 0;

        if (bookings != null)
        {
            foreach (var b in bookings)
            {
                if (b.IsConsultation || b.Date.Date != date.Date || IsInactiveStatus(b.Status)) continue;
                if (!string.IsNullOrWhiteSpace(excludeAppointmentId) &&
                    string.Equals(b.Id, excludeAppointmentId, StringComparison.OrdinalIgnoreCase)) continue;

                count++;
                if (!string.IsNullOrWhiteSpace(b.Id))
                    seenEventIds.Add(b.Id);
            }
        }

        if (negotiations != null)
        {
            var targetDate = DateOnly.FromDateTime(date);
            foreach (var neg in negotiations)
            {
                var appt = neg.Appointment;
                if (appt == null || appt.EventDate != targetDate) continue;
                if (IsInactiveStatus(appt.EventStatus) || IsInactiveStatus(neg.NegotiationStatus)) continue;
                if (!string.IsNullOrWhiteSpace(excludeAppointmentId) &&
                    string.Equals(appt.EventAppointmentId, excludeAppointmentId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(appt.EventAppointmentId) && seenEventIds.Contains(appt.EventAppointmentId))
                    continue;

                count++;
                if (!string.IsNullOrWhiteSpace(appt.EventAppointmentId))
                    seenEventIds.Add(appt.EventAppointmentId);
            }
        }

        return count;
    }

    public static ShootDayAvailability EvaluateDayAvailability(
        DateTime date,
        int shootDurationMinutes,
        IEnumerable<EmployeeViewModel>? employees,
        IEnumerable<EventBookingModel>? bookings,
        IEnumerable<ConsultationNegotiationDto>? negotiations,
        IEnumerable<LeaveItemViewModel>? approvedLeaves = null,
        string? excludeAppointmentId = null)
    {
        var normalizedDuration = shootDurationMinutes <= 0
            ? ShootAvailabilityCore.MinimumShootDurationMinutes
            : Math.Max(ShootAvailabilityCore.MinimumShootDurationMinutes, shootDurationMinutes);

        var bookedCount = CountShootBookingsOnDate(date, bookings, negotiations, excludeAppointmentId);
        var crewBusyLists = BuildEligibleCrewBusyLists(date, employees, bookings, negotiations, approvedLeaves, excludeAppointmentId);

        var eval = ShootAvailabilityCore.EvaluateDay(
            normalizedDuration,
            hasBookings: bookedCount > 0,
            bookedEventCount: bookedCount,
            crewBusyLists);

        var status = eval.StatusCode switch
        {
            ShootAvailabilityCore.StatusBookingsAvailable => ShootDayStatus.BookingsAvailable,
            ShootAvailabilityCore.StatusFullyBooked => ShootDayStatus.FullyBooked,
            _ => ShootDayStatus.Free
        };

        var ranges = eval.FeasibleStartRanges
            .Select(r => new ShootTimeRange(
                TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(r.MinStartMin)),
                TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(r.MaxStartMin))))
            .ToList();

        var quickSlots = eval.QuickSlots
            .Select(q => new ShootQuickSlot(
                TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(q.StartMin)),
                TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(q.EndMin)),
                TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(q.PrepStartMin)),
                TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(q.PostEndMin)),
                q.AvailableCrewCount))
            .ToList();

        return new ShootDayAvailability(
            DateOnly.FromDateTime(date),
            status,
            eval.HasBookings,
            eval.BookedEventCount,
            eval.EligibleCrewCount,
            ranges,
            quickSlots);
    }

    public static ShootMonthAvailability EvaluateMonthAvailability(
        int year,
        int month,
        int shootDurationMinutes,
        IEnumerable<EmployeeViewModel>? employees,
        IEnumerable<EventBookingModel>? bookings,
        IEnumerable<ConsultationNegotiationDto>? negotiations,
        IEnumerable<LeaveItemViewModel>? approvedLeaves = null,
        string? excludeAppointmentId = null)
    {
        var normalizedDuration = shootDurationMinutes <= 0
            ? ShootAvailabilityCore.MinimumShootDurationMinutes
            : Math.Max(ShootAvailabilityCore.MinimumShootDurationMinutes, shootDurationMinutes);

        var daysInMonth = DateTime.DaysInMonth(year, month);
        var days = new List<ShootDayAvailability>(daysInMonth);
        for (int day = 1; day <= daysInMonth; day++)
        {
            var dt = new DateTime(year, month, day);
            days.Add(EvaluateDayAvailability(dt, normalizedDuration, employees, bookings, negotiations, approvedLeaves, excludeAppointmentId));
        }

        return new ShootMonthAvailability(year, month, normalizedDuration, days);
    }

    public static ShootScheduleValidationResult ValidateScheduleSelection(
        DateTime date,
        TimeSpan eventStart,
        TimeSpan eventEnd,
        int requiredDurationMinutes,
        IEnumerable<EmployeeViewModel>? employees,
        IEnumerable<EventBookingModel>? bookings,
        IEnumerable<ConsultationNegotiationDto>? negotiations,
        IEnumerable<LeaveItemViewModel>? approvedLeaves = null,
        string? excludeAppointmentId = null)
    {
        var buffered = ComputeBufferedWindow(eventStart, eventEnd);
        if (eventEnd <= eventStart)
        {
            return new ShootScheduleValidationResult(
                false,
                "Please choose a valid shoot date and minute-level start/end time (end time must be after start time).",
                0,
                buffered);
        }

        if (eventStart < OperatingStart || eventEnd > OperatingEnd)
        {
            return new ShootScheduleValidationResult(
                false,
                "Shoot time must be within studio operating hours (8:00 AM – 8:00 PM).",
                0,
                buffered);
        }

        var startMin = ShootAvailabilityCore.ToStartMinutes(eventStart);
        var endMin = ShootAvailabilityCore.ToEndMinutes(eventEnd);
        var crewBusyLists = BuildEligibleCrewBusyLists(date, employees, bookings, negotiations, approvedLeaves, excludeAppointmentId);

        var outcome = ShootAvailabilityCore.ValidateWindow(
            startMin,
            endMin,
            requiredDurationMinutes,
            crewBusyLists.Count,
            crewBusyLists);

        return new ShootScheduleValidationResult(outcome.IsValid, outcome.ErrorMessage, outcome.AvailableCrewCount, buffered);
    }

    public static string FormatFeasibleOperatingWindows(IReadOnlyList<ShootTimeRange> feasibleStartRanges, int shootDurationMinutes)
    {
        if (feasibleStartRanges == null || feasibleStartRanges.Count == 0)
            return "None available";

        var dur = Math.Max(ShootAvailabilityCore.MinimumShootDurationMinutes, shootDurationMinutes);
        return string.Join(" • ", feasibleStartRanges.Select(r =>
        {
            var startTs = r.Start.ToTimeSpan();
            var maxEndTs = r.End.ToTimeSpan().Add(TimeSpan.FromMinutes(dur));
            if (maxEndTs > OperatingEnd) maxEndTs = OperatingEnd;
            return $"{FormatTime(startTs)} – {FormatTime(maxEndTs)}";
        }));
    }

    private static List<IReadOnlyList<ShootAvailabilityCore.ShootTimeInterval>> BuildEligibleCrewBusyLists(
        DateTime date,
        IEnumerable<EmployeeViewModel>? employees,
        IEnumerable<EventBookingModel>? bookings,
        IEnumerable<ConsultationNegotiationDto>? negotiations,
        IEnumerable<LeaveItemViewModel>? approvedLeaves,
        string? excludeAppointmentId)
    {
        var result = new List<IReadOnlyList<ShootAvailabilityCore.ShootTimeInterval>>();
        foreach (var emp in FilterDispatchableCrew(employees))
        {
            if (GetEmployeeBlockReason(emp, date, approvedLeaves) != null)
                continue;

            var busy = GetEmployeeBusyIntervals(emp, date, bookings, negotiations, excludeAppointmentId)
                .Select(b => new ShootAvailabilityCore.ShootTimeInterval(
                    ShootAvailabilityCore.ToStartMinutes(b.BlockedStart),
                    ShootAvailabilityCore.ToEndMinutes(b.BlockedEnd)));
            result.Add(ShootAvailabilityCore.MergeBusyIntervals(busy));
        }

        return result;
    }

    public static HashSet<string> GetBookedEquipmentIdsForDate(
        DateTime date,
        IEnumerable<EventBookingModel>? bookings,
        IEnumerable<EquipmentReservationModel>? reservations,
        IEnumerable<ConsultationNegotiationDto>? negotiations = null,
        string? excludeAppointmentId = null) =>
        GetConflictingEquipmentIds(
            date,
            TimeSpan.Zero,
            new TimeSpan(23, 59, 0),
            bookings,
            reservations,
            excludeAppointmentId,
            negotiations);

    public static HashSet<string> GetConflictingEquipmentIds(
        DateTime date,
        TimeSpan eventStart,
        TimeSpan eventEnd,
        IEnumerable<EventBookingModel>? bookings,
        IEnumerable<EquipmentReservationModel>? reservations,
        string? excludeAppointmentId = null,
        IEnumerable<ConsultationNegotiationDto>? negotiations = null)
    {
        var conflicting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (bookings != null)
        {
            foreach (var b in bookings)
            {
                if (b.IsConsultation || b.Date.Date != date.Date || IsInactiveStatus(b.Status)) continue;
                if (!string.IsNullOrWhiteSpace(excludeAppointmentId) &&
                    string.Equals(b.Id, excludeAppointmentId, StringComparison.OrdinalIgnoreCase)) continue;
                if (b.AssignedEquipmentIds == null || b.AssignedEquipmentIds.Count == 0) continue;

                // Same-day calendar lock: booked equipment is unavailable for the entire calendar day.
                foreach (var eqId in b.AssignedEquipmentIds.Where(id => !string.IsNullOrWhiteSpace(id)))
                    conflicting.Add(eqId.Trim());
            }
        }

        if (reservations != null)
        {
            foreach (var r in reservations)
            {
                if (r.Date.Date != date.Date || string.IsNullOrWhiteSpace(r.EquipmentId) || IsInactiveStatus(r.EventStatus)) continue;
                if (!string.IsNullOrWhiteSpace(excludeAppointmentId) &&
                    (string.Equals(r.EventAppointmentId, excludeAppointmentId, StringComparison.OrdinalIgnoreCase) ||
                     r.Id.StartsWith(excludeAppointmentId + "-", StringComparison.OrdinalIgnoreCase))) continue;

                conflicting.Add(r.EquipmentId.Trim());
            }
        }

        if (negotiations != null)
        {
            foreach (var neg in negotiations)
            {
                var appt = neg.Appointment;
                if (appt == null || appt.EventDate.ToDateTime(TimeOnly.MinValue).Date != date.Date) continue;
                if (IsInactiveStatus(appt.EventStatus) || IsInactiveStatus(neg.NegotiationStatus)) continue;
                if (!string.IsNullOrWhiteSpace(excludeAppointmentId) &&
                    string.Equals(appt.EventAppointmentId, excludeAppointmentId, StringComparison.OrdinalIgnoreCase)) continue;

                if (appt.AssignedEquipment != null)
                {
                    foreach (var eq in appt.AssignedEquipment.Where(e => !string.IsNullOrWhiteSpace(e.EquipmentId)))
                        conflicting.Add(eq.EquipmentId.Trim());
                }
            }
        }

        return conflicting;
    }

    public static string FormatTime(TimeSpan ts) => DateTime.Today.Add(ts).ToString("h:mm tt", CultureInfo.InvariantCulture);

    private static bool IsInactiveStatus(string? status) =>
        string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "Expired", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "Deal_Lost", StringComparison.OrdinalIgnoreCase);

    private readonly Dictionary<string, ScheduleServiceItem> _catalog = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ServiceCustomizationItem> _selectedServices = new(StringComparer.OrdinalIgnoreCase);

    public void LoadCatalog(IEnumerable<ScheduleServiceItem>? items)
    {
        _catalog.Clear();
        _selectedServices.Clear();
        if (items == null) return;
        foreach (var item in items.Where(i => i != null && !string.IsNullOrWhiteSpace(i.Id)))
        {
            _catalog[item.Id] = item;
        }
    }

    public void ToggleService(string serviceId)
    {
        if (string.IsNullOrWhiteSpace(serviceId)) return;
        if (_selectedServices.Remove(serviceId))
            return;

        if (_catalog.TryGetValue(serviceId, out var cat))
        {
            var opt = new ScheduleServiceOption(cat.Id, cat.Name, cat.Description, cat.Price, cat.RateType);
            _selectedServices[serviceId] = CreateCustomizationItem(opt);
        }
    }

    public void SetServiceQuantity(string serviceId, int quantity)
    {
        if (_selectedServices.TryGetValue(serviceId, out var item))
        {
            item.Quantity = Math.Max(1, quantity);
        }
    }

    public void SetServiceDurationHours(string serviceId, int hours)
    {
        if (_selectedServices.TryGetValue(serviceId, out var item))
        {
            var h = Math.Max(1, hours);
            item.DurationHours = h;
            if (item.RateCategory == ServiceRateCategory.PerHour)
            {
                item.Quantity = h;
            }
        }
    }

    public decimal CalculatedSubtotal => _selectedServices.Values.Sum(s => s.LineTotal);

    public List<EventServiceLineInput> BuildServiceSelections() =>
        _selectedServices.Values
            .Select(s => new EventServiceLineInput
            {
                ServicesId = s.ServicesId,
                Quantity = s.Quantity,
                DurationHours = s.EffectiveDurationHours,
                RateType = s.RateType
            })
            .ToList();
}
