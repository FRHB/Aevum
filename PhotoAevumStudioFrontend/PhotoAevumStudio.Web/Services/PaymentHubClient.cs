using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace PhotoAevumStudio.Web.Services;

public sealed record EventStatusChangedMessage(
    string AppointmentId,
    string EventStatus,
    string? FinalPaymentStatus,
    string? CustomerId);

/// <summary>
/// Connects the Blazor circuit to the API's /hubs/payments SignalR hub (JWT-authenticated)
/// for real-time payment completion and studio-wide event execution status updates.
/// </summary>
public sealed class PaymentHubClient(
    IConfiguration configuration,
    ITokenStore tokenStore,
    ILogger<PaymentHubClient> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _joinedBookings = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _joinedCustomers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _joinedStaff = new(StringComparer.OrdinalIgnoreCase);
    private bool _joinedLiveMonitor;
    private HubConnection? _connection;
    private string? _cachedToken;

    public event Func<EventStatusChangedMessage, Task>? OnEventStatusChanged;
    public event Func<string, Task>? OnPaymentCompletedForBooking;
    public event Func<string, Task>? OnBookingExpiredForBooking;

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
        await _gate.WaitAsync(ct);
        try
        {
            var conn = await EnsureConnectedCoreAsync(ct);
            _joinedBookings.Add(appointmentId);
            OnPaymentCompletedForBooking += id =>
                string.Equals(id, appointmentId, StringComparison.OrdinalIgnoreCase)
                    ? onPaymentCompleted()
                    : Task.CompletedTask;
            OnBookingExpiredForBooking += id =>
                string.Equals(id, appointmentId, StringComparison.OrdinalIgnoreCase)
                    ? onBookingExpired()
                    : Task.CompletedTask;

            await conn.InvokeAsync("JoinBooking", appointmentId, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EnsureLiveMonitorStartedAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var conn = await EnsureConnectedCoreAsync(ct);
            if (!_joinedLiveMonitor)
            {
                await conn.InvokeAsync("JoinLiveMonitor", ct);
                _joinedLiveMonitor = true;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EnsureCustomerMonitorStartedAsync(string customerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            return;

        await _gate.WaitAsync(ct);
        try
        {
            var conn = await EnsureConnectedCoreAsync(ct);
            if (_joinedCustomers.Add(customerId.Trim()))
            {
                await conn.InvokeAsync("JoinCustomer", customerId.Trim(), ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EnsureStaffMonitorStartedAsync(string employeeId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return;

        await _gate.WaitAsync(ct);
        try
        {
            var conn = await EnsureConnectedCoreAsync(ct);
            if (_joinedStaff.Add(employeeId.Trim()))
            {
                await conn.InvokeAsync("JoinStaff", employeeId.Trim(), ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task JoinBookingGroupAsync(string appointmentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(appointmentId))
            return;

        await _gate.WaitAsync(ct);
        try
        {
            var conn = await EnsureConnectedCoreAsync(ct);
            if (_joinedBookings.Add(appointmentId.Trim()))
            {
                await conn.InvokeAsync("JoinBooking", appointmentId.Trim(), ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HubConnection> EnsureConnectedCoreAsync(CancellationToken ct)
    {
        if (_connection != null && _connection.State == HubConnectionState.Connected)
            return _connection;

        if (_connection != null)
        {
            try
            {
                await _connection.StartAsync(ct);
                return _connection;
            }
            catch
            {
                try { await _connection.DisposeAsync(); } catch { }
                _connection = null;
            }
        }

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

        connection.On<JsonElement>("PaymentCompleted", async payload =>
        {
            var apptId = TryReadString(payload, "appointmentId") ?? string.Empty;
            var handler = OnPaymentCompletedForBooking;
            if (handler != null)
                await handler(apptId);
        });

        connection.On<JsonElement>("BookingExpired", async payload =>
        {
            var apptId = TryReadString(payload, "appointmentId") ?? string.Empty;
            var handler = OnBookingExpiredForBooking;
            if (handler != null)
                await handler(apptId);
        });

        connection.On<JsonElement>("EventStatusChanged", async payload =>
        {
            var msg = new EventStatusChangedMessage(
                TryReadString(payload, "appointmentId") ?? string.Empty,
                TryReadString(payload, "eventStatus") ?? string.Empty,
                TryReadString(payload, "finalPaymentStatus"),
                TryReadString(payload, "customerId"));

            var handler = OnEventStatusChanged;
            if (handler != null)
                await handler(msg);
        });

        connection.Reconnected += async _ =>
        {
            if (_joinedLiveMonitor)
            {
                try { await connection.InvokeAsync("JoinLiveMonitor"); }
                catch (Exception ex) { logger.LogWarning(ex, "Failed to rejoin live-monitor group"); }
            }

            foreach (var custId in _joinedCustomers.ToArray())
            {
                try { await connection.InvokeAsync("JoinCustomer", custId); }
                catch (Exception ex) { logger.LogWarning(ex, "Failed to rejoin customer group {CustomerId}", custId); }
            }

            foreach (var empId in _joinedStaff.ToArray())
            {
                try { await connection.InvokeAsync("JoinStaff", empId); }
                catch (Exception ex) { logger.LogWarning(ex, "Failed to rejoin staff group {EmployeeId}", empId); }
            }

            foreach (var apptId in _joinedBookings.ToArray())
            {
                try { await connection.InvokeAsync("JoinBooking", apptId); }
                catch (Exception ex) { logger.LogWarning(ex, "Failed to rejoin booking group {AppointmentId}", apptId); }
            }
        };

        await connection.StartAsync(ct);
        _connection = connection;
        return connection;
    }

    private static string? TryReadString(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        var pascal = char.ToUpperInvariant(prop[0]) + prop[1..];
        if (el.TryGetProperty(pascal, out var vp) && vp.ValueKind == JsonValueKind.String)
            return vp.GetString();
        return null;
    }

    public async Task StopAsync()
    {
        var c = _connection;
        _connection = null;
        _joinedLiveMonitor = false;
        _joinedBookings.Clear();
        _joinedCustomers.Clear();
        _joinedStaff.Clear();
        if (c is null) return;
        try { await c.StopAsync(); } catch { /* best effort */ }
        await c.DisposeAsync();
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
