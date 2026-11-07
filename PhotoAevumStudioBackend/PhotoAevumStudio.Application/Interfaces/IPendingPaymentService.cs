namespace PhotoAevumStudio.Application.Interfaces;

/// <summary>Result of a validated Xendit QR payment confirmation.</summary>
public record PaymentConfirmation(string AppointmentId, string CustomerId, decimal Amount);

/// <summary>A 'Pending Payment' booking whose LockExpiresAt elapsed and whose slot was released.</summary>
public record ExpiredBooking(
    string AppointmentId,
    string CustomerId,
    string CustomerName,
    string CustomerEmail,
    string EventName,
    DateTime EventDate,
    TimeSpan EventTime);

/// <summary>Payload for the customer /payments/{id} page.</summary>
public class QrPaymentInfoDto
{
    public string AppointmentId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public DateTime EventDate { get; set; }
    public TimeSpan EventTime { get; set; }
    public string EventStatus { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? QrString { get; set; }
    public DateTime? LockExpiresAt { get; set; }
}

public interface IPendingPaymentService
{
    /// <summary>Adds LockExpiresAt/QrString columns, the 'Pending Payment' status and the release proc if missing.</summary>
    Task EnsureSchemaAsync(CancellationToken ct = default);

    /// <summary>
    /// Marks the booking Confirmed and nulls LockExpiresAt. Idempotent: returns null when the booking
    /// is not (or no longer) 'Pending Payment' (unknown reference, duplicate callback, or already released).
    /// </summary>
    Task<PaymentConfirmation?> ConfirmByReferenceAsync(
        string referenceId, string? qrId, decimal? amount, string? channel, CancellationToken ct = default);

    /// <summary>Calls dbo.usp_ReleaseExpiredPendingPayments; returns the bookings that were cancelled.</summary>
    Task<IReadOnlyList<ExpiredBooking>> ReleaseExpiredAsync(CancellationToken ct = default);

    Task<QrPaymentInfoDto?> GetQrPaymentAsync(string appointmentId, CancellationToken ct = default);
}

/// <summary>Pushes real-time payment events to the customer's Blazor UI (implemented with SignalR in the API).</summary>
public interface IPaymentNotifier
{
    Task PaymentCompletedAsync(string appointmentId, CancellationToken ct = default);
    Task BookingExpiredAsync(string appointmentId, CancellationToken ct = default);
}
