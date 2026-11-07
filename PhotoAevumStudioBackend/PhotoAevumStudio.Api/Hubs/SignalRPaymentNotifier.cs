using Microsoft.AspNetCore.SignalR;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.Hubs;

public class SignalRPaymentNotifier : IPaymentNotifier
{
    private readonly IHubContext<PaymentHub> _hub;

    public SignalRPaymentNotifier(IHubContext<PaymentHub> hub) => _hub = hub;

    public Task PaymentCompletedAsync(string appointmentId, CancellationToken ct = default) =>
        _hub.Clients.Group(PaymentHub.GroupName(appointmentId))
            .SendAsync(PaymentHub.PaymentCompletedEvent, new { appointmentId, status = "Confirmed" }, ct);

    public Task BookingExpiredAsync(string appointmentId, CancellationToken ct = default) =>
        _hub.Clients.Group(PaymentHub.GroupName(appointmentId))
            .SendAsync(PaymentHub.BookingExpiredEvent, new { appointmentId, status = "Cancelled" }, ct);
}
