using System.Text.Json.Serialization;

namespace PhotoAevumStudio.Application.Scheduling;

[JsonConverter(typeof(JsonStringEnumConverter<MeetingPlatform>))]
public enum MeetingPlatform { Zoom, GoogleMeet, MicrosoftTeams }

public sealed record SlotDefinition(TimeOnly Start, TimeOnly End);

/// <summary>
/// Bookable one-hour slots: 9:00–11:00 AM and 1:00–5:00 PM (studio local time).
/// One client per slot.
/// </summary>
public static class StudioSlots
{
    public static readonly IReadOnlyList<SlotDefinition> All =
    [
        new(new TimeOnly(9, 0),  new TimeOnly(10, 0)),
        new(new TimeOnly(10, 0), new TimeOnly(11, 0)),
        new(new TimeOnly(13, 0), new TimeOnly(14, 0)),
        new(new TimeOnly(14, 0), new TimeOnly(15, 0)),
        new(new TimeOnly(15, 0), new TimeOnly(16, 0)),
        new(new TimeOnly(16, 0), new TimeOnly(17, 0)),
    ];

    public static SlotDefinition? Find(TimeOnly start) => All.FirstOrDefault(s => s.Start == start);
}

public sealed record ScheduleServiceDto(string ServicesId, string Name, string Description, decimal Price);

public sealed record SlotAvailabilityDto(TimeOnly Start, TimeOnly End, bool IsAvailable);

public sealed record DayAvailabilityDto(DateOnly Date, IReadOnlyList<SlotAvailabilityDto> Slots);

public sealed record MonthAvailabilityDto(int Year, int Month, IReadOnlyList<DayAvailabilityDto> Days);

public sealed record MeetingLinkDto(MeetingPlatform Platform, string MeetingLink);

public sealed class ScheduleBookingRequest
{
    public List<string> ServiceIds { get; set; } = new();
    public DateOnly Date { get; set; }
    public TimeOnly SlotStart { get; set; }
    public string? EventLocation { get; set; }
    public MeetingPlatform Platform { get; set; }

    /// <summary>Link previewed on step 5. Re-generated server-side if it isn't one of ours.</summary>
    public string? MeetingLink { get; set; }
}

/// <summary>Customer identity taken from the JWT, never from the request body.</summary>
public sealed record BookingCustomer(string UserId, string Email, string FullName);

public sealed record ScheduleBookingResultDto(
    int AppointmentId,
    DateOnly Date,
    TimeOnly SlotStart,
    TimeOnly SlotEnd,
    IReadOnlyList<ScheduleServiceDto> Services,
    decimal TotalPrice,
    MeetingPlatform Platform,
    string MeetingLink,
    string? EventLocation,
    bool ConfirmationEmailSent);

/// <summary>Another client won the race for this slot, or no staff member is available.</summary>
public sealed class SlotUnavailableException : Exception
{
    public DateOnly Date { get; }
    public TimeOnly Start { get; }

    public SlotUnavailableException(DateOnly date, TimeOnly start)
        : base($"The {start:h:mm tt} slot on {date:MMM d, yyyy} has just been booked by another client.")
    {
        Date = date;
        Start = start;
    }

    public SlotUnavailableException(DateOnly date, TimeOnly start, string message)
        : base(message)
    {
        Date = date;
        Start = start;
    }
}

/// <summary>Request failed business validation. Message is safe to show to the user.</summary>
public sealed class ScheduleValidationException(string message) : Exception(message);
