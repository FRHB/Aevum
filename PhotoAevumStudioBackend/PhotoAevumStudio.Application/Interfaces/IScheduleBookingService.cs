using PhotoAevumStudio.Application.Scheduling;

namespace PhotoAevumStudio.Application.Interfaces;

public interface IScheduleBookingService
{
    Task<IReadOnlyList<ScheduleServiceDto>> GetServicesAsync(CancellationToken cancellationToken = default);

    Task<MonthAvailabilityDto> GetMonthAvailabilityAsync(int year, int month, CancellationToken cancellationToken = default);

    /// <summary>Mock link shown on the Confirm step; reused on booking if still valid.</summary>
    Task<MeetingLinkDto> PreviewMeetingLinkAsync(MeetingPlatform platform, DateOnly date, TimeOnly slotStart, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically books a slot, updates dbo.Appointment / dbo.AppointmentService / dbo.BookingOverview,
    /// then sends the confirmation email.
    /// Throws <see cref="SlotUnavailableException"/> on a double-booking race and
    /// <see cref="ScheduleValidationException"/> on invalid input.
    /// </summary>
    Task<ScheduleBookingResultDto> BookAsync(
        ScheduleBookingRequest request,
        BookingCustomer customer,
        CancellationToken cancellationToken = default);

    Task<DayAvailabilityDto> GetDayAvailabilityAsync(DateOnly date, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MyBookingDto>> GetMyBookingsAsync(string customerId, CancellationToken cancellationToken = default);
}

/// <summary>Creates online meeting links. Swap the mock for a real Zoom / Graph / Calendar API client.</summary>
public interface IMeetingLinkService
{
    Task<string> CreateMeetingLinkAsync(MeetingPlatform platform, DateOnly date, TimeOnly start, TimeOnly end, CancellationToken cancellationToken = default);

    /// <summary>True when <paramref name="link"/> looks like a link this service issued for <paramref name="platform"/>.</summary>
    bool IsIssuedLink(MeetingPlatform platform, string? link);
}

public sealed record BookingConfirmation(
    int AppointmentId,
    BookingCustomer Customer,
    DateOnly Date,
    TimeOnly SlotStart,
    TimeOnly SlotEnd,
    IReadOnlyList<ScheduleServiceDto> Services,
    decimal TotalPrice,
    MeetingPlatform Platform,
    string MeetingLink,
    string? EventLocation);

public interface IBookingNotificationService
{
    /// <summary>Emails the booking summary and meeting link to the customer.</summary>
    Task SendBookingConfirmationAsync(BookingConfirmation confirmation, CancellationToken cancellationToken = default);
}
