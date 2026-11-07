using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http.Connections;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.Hubs;

/// <summary>
/// Real-time channel for reservation-fee payments (/hubs/payments). JWT-authenticated; clients join a
/// per-booking group and receive "PaymentCompleted" / "BookingExpired" events.
/// </summary>
[Authorize]
public class PaymentHub : Hub
{
    public const string Route = "/hubs/payments";
    public const string PaymentCompletedEvent = "PaymentCompleted";
    public const string BookingExpiredEvent = "BookingExpired";

    public static string GroupName(string appointmentId) => $"booking-{appointmentId}";

    private readonly IPendingPaymentService _payments;

    public PaymentHub(IPendingPaymentService payments) => _payments = payments;

    /// <summary>Joins the group for one booking. Only the owning customer (or staff) may subscribe.</summary>
    public async Task JoinBooking(string appointmentId)
    {
        var info = await _payments.GetQrPaymentAsync(appointmentId, Context.ConnectionAborted);
        if (info is null)
            throw new HubException("Booking not found.");

        var user = Context.User!;
        var isStaff = user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.SystemAdmin) ||
                      user.IsInRole(AppRoles.OfficeClerk) || user.IsInRole(AppRoles.Employee);
        var ids = new[]
        {
            user.FindFirstValue(ClaimTypes.NameIdentifier),
            user.FindFirstValue("studio_id"),
            user.FindFirstValue("sub")
        };
        if (!isStaff && !ids.Any(i => string.Equals(i, info.CustomerId, StringComparison.OrdinalIgnoreCase)))
            throw new HubException("Not authorized for this booking.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(appointmentId));
    }

    public Task LeaveBooking(string appointmentId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(appointmentId));
}
