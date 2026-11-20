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
        await _hub.Clients.Group(PaymentHub.LiveMonitorGroup)
            .SendAsync(PaymentHub.EventStatusChangedEvent, new { appointmentId, eventStatus = "Cancelled", finalPaymentStatus = (string?)null, customerId = (string?)null }, ct);
    }

    public async Task EventStatusChangedAsync(
        string appointmentId,
        string eventStatus,
        string? finalPaymentStatus = null,
        string? customerId = null,
        CancellationToken ct = default)
    {
        var payload = new
        {
            appointmentId,
            eventStatus,
            finalPaymentStatus,
            customerId
        };

        var groups = new List<string> { PaymentHub.LiveMonitorGroup };
        if (!string.IsNullOrWhiteSpace(appointmentId) && appointmentId != "*")
        {
            groups.Add(PaymentHub.GroupName(appointmentId));
            groups.Add(PaymentHub.GroupName($"{appointmentId}-FINAL"));
        }
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            groups.Add(PaymentHub.CustomerGroupName(customerId));
        }

        _logger.LogInformation(
            "SignalR Hub broadcasting {Event} ({Status}) for {AppointmentId} to {GroupCount} groups.",
            PaymentHub.EventStatusChangedEvent, eventStatus, appointmentId, groups.Count);

        await _hub.Clients.Groups(groups)
            .SendAsync(PaymentHub.EventStatusChangedEvent, payload, ct);
    }

    public async Task RosterUpdatedAsync(
        string appointmentId,
        string? customerId = null,
        IEnumerable<string>? affectedEmployeeIds = null,
        CancellationToken ct = default)
    {
        var payload = new
        {
            appointmentId,
            eventStatus = "RosterUpdated",
            finalPaymentStatus = (string?)null,
            customerId
        };

        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (affectedEmployeeIds != null)
        {
            foreach (var empId in affectedEmployeeIds.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                groups.Add(PaymentHub.StaffGroupName(empId.Trim()));
            }
        }
        if (groups.Count == 0)
        {
            groups.Add(PaymentHub.LiveMonitorGroup);
            if (!string.IsNullOrWhiteSpace(appointmentId) && appointmentId != "*")
            {
                groups.Add(PaymentHub.GroupName(appointmentId));
            }
        }

        _logger.LogInformation(
            "SignalR Hub broadcasting RosterUpdated for {AppointmentId} to {GroupCount} staff groups.",
            appointmentId, groups.Count);

        await _hub.Clients.Groups(groups.ToList())
            .SendAsync(PaymentHub.EventStatusChangedEvent, payload, ct);
    }
}
