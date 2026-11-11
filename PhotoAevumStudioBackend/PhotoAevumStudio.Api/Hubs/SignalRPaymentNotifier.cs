using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.Hubs;

public class SignalRPaymentNotifier : IPaymentNotifier
{
    private readonly IHubContext<PaymentHub> _hub;
    private readonly ILogger<SignalRPaymentNotifier> _logger;

    public SignalRPaymentNotifier(IHubContext<PaymentHub> hub, ILogger<SignalRPaymentNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task PaymentCompletedAsync(string appointmentId, CancellationToken ct = default)
    {
        _logger.LogInformation("SignalR Hub broadcasting {Event} event to group {Group} for appointment {AppointmentId}.",
            PaymentHub.PaymentCompletedEvent, PaymentHub.GroupName(appointmentId), appointmentId);
        await _hub.Clients.Group(PaymentHub.GroupName(appointmentId))
            .SendAsync(PaymentHub.PaymentCompletedEvent, new { appointmentId, status = "Confirmed" }, ct);
    }

    public async Task BookingExpiredAsync(string appointmentId, CancellationToken ct = default)
    {
        _logger.LogInformation("SignalR Hub broadcasting {Event} event to group {Group} for appointment {AppointmentId}.",
            PaymentHub.BookingExpiredEvent, PaymentHub.GroupName(appointmentId), appointmentId);
        await _hub.Clients.Group(PaymentHub.GroupName(appointmentId))
            .SendAsync(PaymentHub.BookingExpiredEvent, new { appointmentId, status = "Cancelled" }, ct);
    }
}
