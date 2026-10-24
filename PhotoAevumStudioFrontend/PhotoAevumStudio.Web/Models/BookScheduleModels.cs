using System.Text.Json.Serialization;

namespace PhotoAevumStudio.Web.Models;

[JsonConverter(typeof(JsonStringEnumConverter<MeetingPlatform>))]
public enum MeetingPlatform { Zoom, GoogleMeet, MicrosoftTeams }

public sealed record ScheduleServiceOption(string ServicesId, string Name, string Description, decimal Price);

public sealed record SlotAvailability(TimeOnly Start, TimeOnly End, bool IsAvailable);

public sealed record DayAvailability(DateOnly Date, IReadOnlyList<SlotAvailability> Slots)
{
    public bool HasAvailability => Slots.Any(s => s.IsAvailable);
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
/// survive moving between steps and closing/reopening the modal.
/// </summary>
public sealed class BookScheduleState
{
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
        MeetingLink = null;
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
