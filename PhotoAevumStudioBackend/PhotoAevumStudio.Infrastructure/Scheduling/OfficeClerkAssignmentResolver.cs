using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Infrastructure.Scheduling;

public sealed record ClerkCandidate(
    string EmployeeId,
    string FullName,
    string DaysOff,
    DateTime? LastAssignedAt = null);

public sealed record ClerkLeaveWindow(
    string EmployeeId,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record ClerkBusyWindow(
    string EmployeeId,
    DateOnly Date,
    TimeOnly Start,
    TimeOnly End,
    string? SourceId = null);

public sealed record ClerkRescheduleSelection(
    bool IsAvailable,
    string? ClerkId,
    string? ClerkName,
    bool RetainedOriginalClerk,
    string? FailureReason);

/// <summary>
/// Resolves Office Clerk availability during customer reschedules:
/// retains the originally assigned Office Clerk when free, or automatically
/// falls back to the least-recently-assigned available Office Clerk.
/// </summary>
public static class OfficeClerkAssignmentResolver
{
    public static bool IsClerkAvailable(
        ClerkCandidate clerk,
        IReadOnlyList<ClerkLeaveWindow> leaves,
        IReadOnlyList<ClerkBusyWindow> busyWindows,
        DateOnly targetDate,
        TimeOnly targetStart,
        TimeOnly targetEnd,
        string? excludeSourceId = null)
    {
        if (IsDayOff(clerk.DaysOff, targetDate))
            return false;

        if (leaves.Any(l =>
            string.Equals(l.EmployeeId, clerk.EmployeeId, StringComparison.OrdinalIgnoreCase) &&
            targetDate >= l.StartDate &&
            targetDate <= l.EndDate))
        {
            return false;
        }

        if (busyWindows.Any(w =>
            string.Equals(w.EmployeeId, clerk.EmployeeId, StringComparison.OrdinalIgnoreCase) &&
            w.Date == targetDate &&
            (string.IsNullOrWhiteSpace(excludeSourceId) ||
             !string.Equals(w.SourceId, excludeSourceId, StringComparison.OrdinalIgnoreCase)) &&
            ScheduleBookingService.DoesSlotOverlapWithBufferedBooking(targetStart, targetEnd, w.Start, w.End)))
        {
            return false;
        }

        return true;
    }

    public static ClerkRescheduleSelection ResolveClerkForReschedule(
        string? originalClerkId,
        IReadOnlyList<ClerkCandidate> activeClerks,
        IReadOnlyList<ClerkLeaveWindow> leaves,
        IReadOnlyList<ClerkBusyWindow> busyWindows,
        DateOnly targetDate,
        TimeOnly targetStart,
        TimeOnly targetEnd,
        string? excludeSourceId = null)
    {
        if (activeClerks == null || activeClerks.Count == 0)
        {
            return new ClerkRescheduleSelection(
                false,
                null,
                null,
                false,
                "No active Office Clerk is configured in the studio.");
        }

        ClerkCandidate? original = null;
        if (!string.IsNullOrWhiteSpace(originalClerkId))
        {
            original = activeClerks.FirstOrDefault(c =>
                string.Equals(c.EmployeeId, originalClerkId.Trim(), StringComparison.OrdinalIgnoreCase));

            if (original != null &&
                IsClerkAvailable(original, leaves, busyWindows, targetDate, targetStart, targetEnd, excludeSourceId))
            {
                return new ClerkRescheduleSelection(
                    true,
                    original.EmployeeId,
                    original.FullName,
                    RetainedOriginalClerk: true,
                    FailureReason: null);
            }
        }

        var fallback = activeClerks
            .Where(c => original == null || !string.Equals(c.EmployeeId, original.EmployeeId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.LastAssignedAt ?? DateTime.MinValue)
            .ThenBy(c => c.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(c => IsClerkAvailable(c, leaves, busyWindows, targetDate, targetStart, targetEnd, excludeSourceId));

        if (fallback != null)
        {
            return new ClerkRescheduleSelection(
                true,
                fallback.EmployeeId,
                fallback.FullName,
                RetainedOriginalClerk: false,
                FailureReason: null);
        }

        var allDayOff = activeClerks.All(c => IsDayOff(c.DaysOff, targetDate));
        var failure = allDayOff
            ? $"No Office Clerk is available on {targetDate:dddd, MMMM d, yyyy} because active Office Clerks have a scheduled Day Off."
            : $"No Office Clerk is available for {targetDate:MMM d, yyyy} at {targetStart:h:mm tt}. Please choose another date or time slot.";

        return new ClerkRescheduleSelection(false, null, null, false, failure);
    }

    private static bool IsDayOff(string? daysOff, DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(daysOff))
            return false;

        var dayName = date.DayOfWeek.ToString();
        return daysOff
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(d => string.Equals(d, dayName, StringComparison.OrdinalIgnoreCase));
    }
}
