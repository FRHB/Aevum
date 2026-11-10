using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Services;

namespace PhotoAevumStudio.Api.BackgroundServices;

/// <summary>
/// Every minute releases 'Pending Payment' bookings whose LockExpiresAt has passed (status -> Cancelled, slot freed),
/// pushes a SignalR event and emails the customer, preventing permanent ghost bookings.
/// </summary>
public class PendingPaymentExpiryService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PendingPaymentExpiryService> _logger;

    public PendingPaymentExpiryService(IServiceScopeFactory scopes, ILogger<PendingPaymentExpiryService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pending Payment expiry sweep failed.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var payments = scope.ServiceProvider.GetRequiredService<IPendingPaymentService>();
        var notifier = scope.ServiceProvider.GetRequiredService<IPaymentNotifier>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var released = await payments.ReleaseExpiredAsync(ct);
        foreach (var b in released)
        {
            _logger.LogInformation("Released expired Pending Payment booking {AppointmentId}.", b.AppointmentId);

            try { await notifier.BookingExpiredAsync(b.AppointmentId, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "SignalR expiry push failed for {AppointmentId}", b.AppointmentId); }

            if (string.IsNullOrWhiteSpace(b.CustomerEmail)) continue;
            try
            {
                var name = string.IsNullOrWhiteSpace(b.CustomerName) ? "Valued Client" : b.CustomerName;
                var content =
                    $"<p>Hi {System.Net.WebUtility.HtmlEncode(name)},</p>" +
                    $"<p>We did not receive the reservation fee for <strong>{System.Net.WebUtility.HtmlEncode(b.EventName)}</strong> " +
                    $"(#{b.AppointmentId}) scheduled on {b.EventDate:MMMM dd, yyyy} at {DateTime.Today.Add(b.EventTime):hh:mm tt} within the 1-hour window, " +
                    "so the booking was cancelled and the time slot has been released.</p>" +
                    "<p>You are welcome to contact the studio to book again.</p>";
                var body = StudioEmailTemplates.WrapLayout(
                    $"Booking {b.AppointmentId} cancelled - reservation fee not received",
                    "Booking Cancelled", "#dc2626", "Reservation Time Slot Released", content);
                await email.SendEmailAsync(b.CustomerEmail, $"Booking Cancelled: {b.EventName} (#{b.AppointmentId})", body);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Expiry notification email failed for {AppointmentId}", b.AppointmentId);
            }
        }
    }
}
