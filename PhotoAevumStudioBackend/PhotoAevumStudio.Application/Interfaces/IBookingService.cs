namespace PhotoAevumStudio.Application.Interfaces;

public interface IBookingService
{
    /// <summary>
    /// Books an event by calling the stored procedure dbo.usp_CreateEventAppointment.
    /// Throws BookingRuleException when the booking is rejected.
    /// </summary>
    Task<BookingResultDto> CreateEventAppointmentAsync(
        CreateBookingRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all event appointments from dbo.EventAppointment.
    /// </summary>
    Task<IReadOnlyList<BookingListItemDto>> GetBookingsAsync(
        CancellationToken cancellationToken = default);
}
