using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

#if WEB_STANDALONE
namespace PhotoAevumStudio.Web.Services;
#else
namespace PhotoAevumStudio.Application.Scheduling;
#endif

/// <summary>
/// Single-source-of-truth scheduling core for Deal-Won shoot day availability (8:00 AM - 8:00 PM),
/// 2h prep / 1h post buffers, contiguous gap feasibility, quick-pick slot generation, and minute-level validation.
/// Compiled into PhotoAevumStudio.Application and linked into PhotoAevumStudio.Web under WEB_STANDALONE.
/// </summary>
#if WEB_STANDALONE
internal static class ShootAvailabilityCore
#else
public static class ShootAvailabilityCore
#endif
{
    public const int OperatingStartMinutes = 8 * 60;      // 08:00 (8:00 AM)
    public const int OperatingEndMinutes = 20 * 60;       // 20:00 (8:00 PM)
    public const int PrepBufferMinutes = 2 * 60;          // 2h advance prep buffer
    public const int PostBufferMinutes = 1 * 60;          // 1h post-event buffer
    public const int MinimumShootDurationMinutes = 60;    // 1h minimum shoot duration
    public const int MinutesPerDay = 24 * 60;             // 1440

    public const int StatusFree = 0;
    public const int StatusBookingsAvailable = 1;
    public const int StatusFullyBooked = 2;

    public readonly record struct ShootTimeInterval(int StartMin, int EndMin);

    public readonly record struct ShootStartWindow(int MinStartMin, int MaxStartMin);

    public readonly record struct ShootQuickSlotItem(
        int StartMin,
        int EndMin,
        int PrepStartMin,
        int PostEndMin,
        int AvailableCrewCount);

    public sealed record ShootDayEvaluationResult(
        int StatusCode,
        bool HasBookings,
        int BookedEventCount,
        int EligibleCrewCount,
        IReadOnlyList<ShootStartWindow> FeasibleStartRanges,
        IReadOnlyList<ShootQuickSlotItem> QuickSlots);

    public sealed record ShootScheduleValidationOutcome(
        bool IsValid,
        string? ErrorMessage,
        int AvailableCrewCount,
        int PrepStartMin,
        int PostEndMin);

    public static (int PrepStartMin, int PostEndMin) ComputeBufferedRange(int eventStartMin, int eventEndMin)
    {
        if (eventEndMin <= eventStartMin)
            eventEndMin = eventStartMin + MinimumShootDurationMinutes;

        var prepStart = Math.Max(0, eventStartMin - PrepBufferMinutes);
        var postEnd = Math.Min(MinutesPerDay, eventEndMin + PostBufferMinutes);
        return (prepStart, postEnd);
    }

    public static int ToStartMinutes(TimeSpan ts) =>
        Math.Clamp((int)Math.Floor(ts.TotalMinutes), 0, MinutesPerDay);

    public static int ToEndMinutes(TimeSpan ts) =>
        Math.Clamp((int)Math.Ceiling(ts.TotalMinutes), 0, MinutesPerDay);

    public static bool IsDayOff(
        string? daysOffCsv,
        IReadOnlyCollection<string>? selectedDaysOff,
        DayOfWeek dayOfWeek)
    {
        var dayName = dayOfWeek.ToString();

        if (!string.IsNullOrWhiteSpace(daysOffCsv))
        {
            var tokens = daysOffCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Any(t => string.Equals(t, dayName, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        if (selectedDaysOff != null &&
            selectedDaysOff.Any(d => string.Equals(d, dayName, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    public static List<ShootTimeInterval> BufferAndMergeEvents(IEnumerable<ShootTimeInterval> rawEvents)
    {
        var buffered = new List<ShootTimeInterval>();
        foreach (var ev in rawEvents)
        {
            var (prepStart, postEnd) = ComputeBufferedRange(ev.StartMin, ev.EndMin);
            if (postEnd > prepStart)
                buffered.Add(new ShootTimeInterval(prepStart, postEnd));
        }

        return MergeBusyIntervals(buffered);
    }

    public static List<ShootTimeInterval> MergeBusyIntervals(IEnumerable<ShootTimeInterval> raw)
    {
        var sorted = raw
            .Where(r => r.EndMin > r.StartMin)
            .Select(r => new ShootTimeInterval(Math.Clamp(r.StartMin, 0, MinutesPerDay), Math.Clamp(r.EndMin, 0, MinutesPerDay)))
            .Where(r => r.EndMin > r.StartMin)
            .OrderBy(r => r.StartMin)
            .ThenBy(r => r.EndMin)
            .ToList();

        if (sorted.Count == 0)
            return sorted;

        var merged = new List<ShootTimeInterval> { sorted[0] };
        for (int i = 1; i < sorted.Count; i++)
        {
            var last = merged[^1];
            var cur = sorted[i];
            if (cur.StartMin <= last.EndMin)
            {
                merged[^1] = new ShootTimeInterval(last.StartMin, Math.Max(last.EndMin, cur.EndMin));
            }
            else
            {
                merged.Add(cur);
            }
        }

        return merged;
    }

    public static List<ShootStartWindow> MergeStartWindows(IEnumerable<ShootStartWindow> raw)
    {
        var sorted = raw
            .Where(r => r.MaxStartMin >= r.MinStartMin)
            .OrderBy(r => r.MinStartMin)
            .ThenBy(r => r.MaxStartMin)
            .ToList();

        if (sorted.Count == 0)
            return sorted;

        var merged = new List<ShootStartWindow> { sorted[0] };
        for (int i = 1; i < sorted.Count; i++)
        {
            var last = merged[^1];
            var cur = sorted[i];
            if (cur.MinStartMin <= last.MaxStartMin + 1)
            {
                merged[^1] = new ShootStartWindow(last.MinStartMin, Math.Max(last.MaxStartMin, cur.MaxStartMin));
            }
            else
            {
                merged.Add(cur);
            }
        }

        return merged;
    }

    public static List<ShootStartWindow> ComputeCrewFeasibleStartRanges(
        IReadOnlyList<ShootTimeInterval> mergedBusyBuffered,
        int durationMinutes)
    {
        var duration = Math.Max(MinimumShootDurationMinutes, durationMinutes);
        var maxOperatingStart = OperatingEndMinutes - duration;
        if (maxOperatingStart < OperatingStartMinutes)
            return [];

        var ranges = new List<ShootStartWindow>();
        var cursor = 0;

        for (int i = 0; i < mergedBusyBuffered.Count; i++)
        {
            var busy = mergedBusyBuffered[i];
            if (busy.StartMin > cursor)
            {
                AddRangeIfFeasible(ranges, cursor, busy.StartMin, duration, maxOperatingStart);
            }
            if (busy.EndMin > cursor)
            {
                cursor = busy.EndMin;
            }
        }

        if (MinutesPerDay > cursor)
        {
            AddRangeIfFeasible(ranges, cursor, MinutesPerDay, duration, maxOperatingStart);
        }

        return ranges;
    }

    private static void AddRangeIfFeasible(
        List<ShootStartWindow> target,
        int gapStartMin,
        int gapEndMin,
        int duration,
        int maxOperatingStart)
    {
        var minStart = Math.Max(OperatingStartMinutes, gapStartMin + PrepBufferMinutes);
        var maxStart = Math.Min(maxOperatingStart, gapEndMin - duration - PostBufferMinutes);
        if (minStart <= maxStart)
        {
            target.Add(new ShootStartWindow(minStart, maxStart));
        }
    }

    public static ShootDayEvaluationResult EvaluateDay(
        int requestedDurationMinutes,
        bool hasBookings,
        int bookedEventCount,
        IReadOnlyList<IReadOnlyList<ShootTimeInterval>> crewMergedBusyPerEligibleMember)
    {
        var duration = Math.Max(MinimumShootDurationMinutes, requestedDurationMinutes);
        var eligibleCrewCount = crewMergedBusyPerEligibleMember.Count;

        if (eligibleCrewCount == 0)
        {
            return new ShootDayEvaluationResult(
                StatusFullyBooked,
                hasBookings,
                bookedEventCount,
                0,
                Array.Empty<ShootStartWindow>(),
                Array.Empty<ShootQuickSlotItem>());
        }

        var rawStartRanges = new List<ShootStartWindow>();
        foreach (var busy in crewMergedBusyPerEligibleMember)
        {
            rawStartRanges.AddRange(ComputeCrewFeasibleStartRanges(busy, duration));
        }

        var mergedStartRanges = MergeStartWindows(rawStartRanges);
        if (mergedStartRanges.Count == 0)
        {
            return new ShootDayEvaluationResult(
                StatusFullyBooked,
                hasBookings,
                bookedEventCount,
                eligibleCrewCount,
                Array.Empty<ShootStartWindow>(),
                Array.Empty<ShootQuickSlotItem>());
        }

        var quickSlots = GenerateQuickSlots(mergedStartRanges, duration, crewMergedBusyPerEligibleMember);
        var status = hasBookings ? StatusBookingsAvailable : StatusFree;

        return new ShootDayEvaluationResult(
            status,
            hasBookings,
            bookedEventCount,
            eligibleCrewCount,
            mergedStartRanges,
            quickSlots);
    }

    public static List<ShootQuickSlotItem> GenerateQuickSlots(
        IReadOnlyList<ShootStartWindow> mergedStartRanges,
        int durationMinutes,
        IReadOnlyList<IReadOnlyList<ShootTimeInterval>> crewMergedBusyPerEligibleMember)
    {
        var duration = Math.Max(MinimumShootDurationMinutes, durationMinutes);
        var candidates = new SortedSet<int>();

        foreach (var range in mergedStartRanges)
        {
            var addedHourly = 0;
            for (int h = OperatingStartMinutes; h <= OperatingEndMinutes - duration; h += 60)
            {
                if (h >= range.MinStartMin && h <= range.MaxStartMin)
                {
                    candidates.Add(h);
                    addedHourly++;
                }
            }

            // Include exact minute-level start boundary when a buffer ends off-hour (e.g., 1:15 PM)
            if (range.MinStartMin % 60 != 0)
            {
                candidates.Add(range.MinStartMin);
            }

            // If no hourly mark landed inside the gap (e.g., 1:15 PM - 1:45 PM), include 30-min marks inside it
            if (addedHourly == 0)
            {
                candidates.Add(range.MinStartMin);
                for (int m = ((range.MinStartMin + 29) / 30) * 30; m <= range.MaxStartMin; m += 30)
                {
                    candidates.Add(m);
                }
            }
        }

        var slots = new List<ShootQuickSlotItem>(candidates.Count);
        foreach (var startMin in candidates)
        {
            var endMin = startMin + duration;
            if (endMin > OperatingEndMinutes) continue;

            var (prepStart, postEnd) = ComputeBufferedRange(startMin, endMin);
            var availCount = CountAvailableCrew(crewMergedBusyPerEligibleMember, prepStart, postEnd);
            if (availCount > 0)
            {
                slots.Add(new ShootQuickSlotItem(startMin, endMin, prepStart, postEnd, availCount));
            }
        }

        return slots;
    }

    public static int CountAvailableCrew(
        IReadOnlyList<IReadOnlyList<ShootTimeInterval>> crewMergedBusyPerEligibleMember,
        int prepStartMin,
        int postEndMin)
    {
        int available = 0;
        for (int i = 0; i < crewMergedBusyPerEligibleMember.Count; i++)
        {
            var busy = crewMergedBusyPerEligibleMember[i];
            bool overlaps = false;
            for (int j = 0; j < busy.Count; j++)
            {
                if (prepStartMin < busy[j].EndMin && busy[j].StartMin < postEndMin)
                {
                    overlaps = true;
                    break;
                }
            }

            if (!overlaps)
                available++;
        }

        return available;
    }

    public static ShootScheduleValidationOutcome ValidateWindow(
        int eventStartMin,
        int eventEndMin,
        int requiredDurationMinutes,
        int eligibleCrewCount,
        IReadOnlyList<IReadOnlyList<ShootTimeInterval>> crewMergedBusyPerEligibleMember)
    {
        if (eventEndMin <= eventStartMin)
        {
            return new ShootScheduleValidationOutcome(
                false,
                "Please choose a valid shoot date and minute-level start/end time (end time must be after start time).",
                0,
                0,
                0);
        }

        if (eventStartMin < OperatingStartMinutes || eventEndMin > OperatingEndMinutes)
        {
            return new ShootScheduleValidationOutcome(
                false,
                "Shoot time must be within studio operating hours (8:00 AM – 8:00 PM).",
                0,
                0,
                0);
        }

        var minRequired = Math.Max(MinimumShootDurationMinutes, requiredDurationMinutes);
        var actualMinutes = eventEndMin - eventStartMin;
        if (actualMinutes < minRequired)
        {
            return new ShootScheduleValidationOutcome(
                false,
                $"Selected shoot duration ({actualMinutes} min) is shorter than the required {minRequired} min for the chosen service(s).",
                0,
                0,
                0);
        }

        var (prepStart, postEnd) = ComputeBufferedRange(eventStartMin, eventEndMin);
        if (eligibleCrewCount == 0 || crewMergedBusyPerEligibleMember.Count == 0)
        {
            return new ShootScheduleValidationOutcome(
                false,
                "No dispatchable shoot crew is available on this date (scheduled day off or approved leave). Please select another date.",
                0,
                prepStart,
                postEnd);
        }

        var availCount = CountAvailableCrew(crewMergedBusyPerEligibleMember, prepStart, postEnd);
        if (availCount == 0)
        {
            return new ShootScheduleValidationOutcome(
                false,
                $"No crew is available for {FormatMinutes(eventStartMin)} – {FormatMinutes(eventEndMin)} (requires {FormatMinutes(prepStart)} – {FormatMinutes(postEnd)} including 2h prep and 1h post-event buffers; remaining gap lacks buffer time or overlaps existing bookings).",
                0,
                prepStart,
                postEnd);
        }

        return new ShootScheduleValidationOutcome(true, null, availCount, prepStart, postEnd);
    }

    public static string FormatMinutes(int totalMinutes)
    {
        var clamped = Math.Clamp(totalMinutes, 0, MinutesPerDay - 1);
        var dt = DateTime.Today.AddMinutes(clamped);
        return dt.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }
}
