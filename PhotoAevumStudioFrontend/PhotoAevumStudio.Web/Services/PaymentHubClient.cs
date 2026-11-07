using Microsoft.AspNetCore.SignalR.Client;

namespace PhotoAevumStudio.Web.Services;

/// <summary>
/// Connects the customer's Blazor circuit to the API's /hubs/payments SignalR hub (JWT-authenticated)
/// so the /payments/{id} page can flip to a success screen the moment the Xendit webhook confirms payment.
/// </summary>
public sealed class PaymentHubClient(
    IConfiguration configuration,
    ITokenStore tokenStore,
    ILogger<PaymentHubClient> logger) : IAsyncDisposable
{
    private HubConnection? _connection;
    private string? _cachedToken;

    private string HubUrl
    {
        get
        {
            var baseUrl = configuration["Api:BaseUrl"]
                ?? configuration["ApiBaseUrl"]
                ?? "http://api-photoaevum.tryasp.net/";
            return baseUrl.TrimEnd('/') + "/hubs/payments";
        }
    }

    public async Task StartAsync(
        string appointmentId,
        Func<Task> onPaymentCompleted,
        Func<Task> onBookingExpired,
        CancellationToken ct = default)
    {
        await StopAsync();

        _cachedToken = await tokenStore.GetTokenAsync(ct);

        var connection = new HubConnectionBuilder()
            .WithUrl(HubUrl, options =>
            {
                options.AccessTokenProvider = async () =>
                {
                    try { _cachedToken = await tokenStore.GetTokenAsync() ?? _cachedToken; }
                    catch { /* fall back to the token captured at start */ }
                    return _cachedToken;
                };
            })
            .WithAutomaticReconnect()
            .Build();

        connection.On<object>("PaymentCompleted", _ => onPaymentCompleted());
        connection.On<object>("BookingExpired", _ => onBookingExpired());
        connection.Reconnected += async _ =>
        {
            try { await connection.InvokeAsync("JoinBooking", appointmentId); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to rejoin booking group {AppointmentId}", appointmentId); }
        };

        await connection.StartAsync(ct);
        await connection.InvokeAsync("JoinBooking", appointmentId, ct);
        _connection = connection;
    }

    public async Task StopAsync()
    {
        var c = _connection;
        _connection = null;
        if (c is null) return;
        try { await c.StopAsync(); } catch { /* best effort */ }
        await c.DisposeAsync();
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
