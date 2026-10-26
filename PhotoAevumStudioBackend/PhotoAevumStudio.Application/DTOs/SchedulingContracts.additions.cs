// ── Application/Scheduling: add this record next to your other scheduling DTOs ──
namespace PhotoAevumStudio.Application.Scheduling;

public sealed record MyBookingDto(
    int AppointmentId,
    string ServiceTitle,
    DateOnly Date,
    TimeOnly SlotStart,
    TimeOnly SlotEnd,
    string Status,
    decimal TotalPrice,
    string Platform,
    string? MeetingLink,
    string? EventLocation);

// ── Application/Interfaces/IScheduleBookingService.cs: add these two members ──
//
//     Task<DayAvailabilityDto> GetDayAvailabilityAsync(DateOnly date, CancellationToken cancellationToken = default);
//     Task<IReadOnlyList<MyBookingDto>> GetMyBookingsAsync(string customerId, CancellationToken cancellationToken = default);
