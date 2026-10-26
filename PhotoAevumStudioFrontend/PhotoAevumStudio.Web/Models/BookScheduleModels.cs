using System.Text.Json.Serialization;

namespace PhotoAevumStudio.Web.Models;

[JsonConverter(typeof(JsonStringEnumConverter<MeetingPlatform>))]
public enum MeetingPlatform { Zoom, GoogleMeet, MicrosoftTeams }

public static class MeetingPlatformInfo
{
    public static readonly MeetingPlatform[] All = [MeetingPlatform.GoogleMeet, MeetingPlatform.Zoom, MeetingPlatform.MicrosoftTeams];

    public static string Label(MeetingPlatform p) => p switch
    {
        MeetingPlatform.GoogleMeet => "Google Meet",
        MeetingPlatform.Zoom => "Zoom",
        MeetingPlatform.MicrosoftTeams => "Microsoft Teams",
        _ => p.ToString()
    };
}

/// <summary>Studio session windows. Keep in sync with StudioSlots on the API side (the server is the authority).</summary>
public static class SessionWindows
{
    public static readonly TimeOnly MorningStart = new(9, 0);
    public static readonly TimeOnly MorningEnd = new(11, 0);
    public static readonly TimeOnly AfternoonStart = new(13, 0);
    public static readonly TimeOnly AfternoonEnd = new(17, 0);

    public static bool IsMorning(TimeOnly start, TimeOnly end) => start >= MorningStart && end <= MorningEnd;
    public static bool IsAfternoon(TimeOnly start, TimeOnly end) => start >= AfternoonStart && end <= AfternoonEnd;
    public static bool IsInWindow(TimeOnly start, TimeOnly end) => IsMorning(start, end) || IsAfternoon(start, end);
}

public sealed record ScheduleServiceOption(string ServicesId, string Name, string Description, decimal Price);

public sealed record SlotAvailability(TimeOnly Start, TimeOnly End, bool IsAvailable);

public sealed record DayAvailability(DateOnly Date, IReadOnlyList<SlotAvailability> Slots)
{
    public bool HasAvailability => Slots.Any(s => s.IsAvailable && SessionWindows.IsInWindow(s.Start, s.End));
}

public sealed record MonthAvailability(int Year, int Month, IReadOnlyList<DayAvailability> Days);

public sealed record MeetingLinkPreview(MeetingPlatform Platform, string MeetingLink);

public sealed class ScheduleBookingRequest
{
    public List<string> ServiceIds { get; set; } = new();
    public DateOnly Date { get; set; }
    public TimeOnly SlotStart { get; set; }
    public string? EventLocation { get; set; }
    public MeetingPlatform Platform { get; set; }
    public string? MeetingLink { get; set; }
}

public sealed record ScheduleBookingResult(
    int AppointmentId,
    DateOnly Date,
    TimeOnly SlotStart,
    TimeOnly SlotEnd,
    IReadOnlyList<ScheduleServiceOption> Services,
    decimal TotalPrice,
    MeetingPlatform Platform,
    string MeetingLink,
    string? EventLocation,
    bool ConfirmationEmailSent);

/// <summary>One row for MyBookings / Dashboard, from GET api/schedule/my-bookings.</summary>
public sealed record MyBooking(
    int AppointmentId,
    string ServiceTitle,
    DateOnly Date,
    TimeOnly SlotStart,
    TimeOnly SlotEnd,
    string Status,
    decimal TotalPrice,
    string Platform,
    string? MeetingLink,
    string? EventLocation)
{
    public DateTime Start => Date.ToDateTime(SlotStart);
}

public enum ApiErrorKind { SlotTaken, Validation, Unauthorized, Network, Server }

public sealed record ApiError(ApiErrorKind Kind, string Message);

public sealed record ApiResult<T>(T? Value, ApiError? Error)
{
    public bool IsSuccess => Error is null;
    public static ApiResult<T> Ok(T value) => new(value, null);
    public static ApiResult<T> Fail(ApiErrorKind kind, string message) => new(default, new ApiError(kind, message));
}

/// <summary>
/// Wizard progress, registered as a scoped service (one per Blazor circuit), so selections
/// survive moving between steps. Steps: 1 Service, 2 Date &amp; Time, 3 Event Details, 4 Teleconference, 5 Summary.
/// </summary>
public sealed class BookScheduleState
{
    public const int LastStep = 5;

    public int Step { get; set; } = 1;
    public HashSet<string> ServiceIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public DateOnly VisibleMonth { get; set; } = FirstOfMonth(DateOnly.FromDateTime(DateTime.Now));
    public DateOnly? Date { get; private set; }
    public TimeOnly? SlotStart { get; private set; }
    public TimeOnly? SlotEnd { get; private set; }
    public bool DateTimeConfirmed { get; set; }
    public string? EventLocation { get; set; }
    public MeetingPlatform? Platform { get; private set; }
    public string? MeetingLink { get; set; }

    public void SelectDate(DateOnly date)
    {
        if (Date == date) return;
        Date = date;
        ClearSlot();
    }

    public void SelectSlot(TimeOnly start, TimeOnly end)
    {
        SlotStart = start;
        SlotEnd = end;
        DateTimeConfirmed = false; // a new time must be re-confirmed on step 3
        MeetingLink = null;        // link is tied to the time, so regenerate it
    }

    public void ClearSlot()
    {
        SlotStart = null;
        SlotEnd = null;
        DateTimeConfirmed = false;
        MeetingLink = null;
    }

    public void SelectPlatform(MeetingPlatform platform)
    {
        if (Platform == platform) return;
        Platform = platform;
        MeetingLink = null;
    }

    public void Reset()
    {
        Step = 1;
        ServiceIds.Clear();
        VisibleMonth = FirstOfMonth(DateOnly.FromDateTime(DateTime.Now));
        Date = null;
        ClearSlot();
        EventLocation = null;
        Platform = null;
    }

    public static DateOnly FirstOfMonth(DateOnly d) => new(d.Year, d.Month, 1);
}
