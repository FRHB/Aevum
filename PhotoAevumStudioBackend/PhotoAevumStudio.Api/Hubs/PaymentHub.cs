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
    public const string EventStatusChangedEvent = "EventStatusChanged";
    public const string LiveMonitorGroup = "live-monitor";

    public static string GroupName(string appointmentId) => $"booking-{appointmentId}";
    public static string CustomerGroupName(string customerId) => $"customer-{customerId}";
    public static string StaffGroupName(string employeeId) => $"staff-{employeeId}";

    private readonly IPendingPaymentService _payments;

    public PaymentHub(IPendingPaymentService payments) => _payments = payments;

    /// <summary>Joins the studio-wide live execution monitor group (staff, clerks, and admins only).</summary>
    public Task JoinLiveMonitor()
    {
        var user = Context.User;
        if (user == null || !IsStudioStaff(user))
            throw new HubException("Not authorized for live monitor updates.");

        return Groups.AddToGroupAsync(Context.ConnectionId, LiveMonitorGroup);
    }

    public Task LeaveLiveMonitor() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, LiveMonitorGroup);

    /// <summary>Joins an employee-scoped group to receive real-time roster and Team Leader assignment updates.</summary>
    public Task JoinStaff(string employeeId)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            throw new HubException("EmployeeId is required.");

        var user = Context.User;
        if (user == null || !IsStudioStaff(user))
            throw new HubException("Not authorized for staff updates.");

        var normalizedEmployeeId = employeeId.Trim();
        var isClerkOrAdmin = user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.SystemAdmin) || user.IsInRole(AppRoles.OfficeClerk);
        if (!isClerkOrAdmin && !MatchesUserIdentifier(user, normalizedEmployeeId))
            throw new HubException("Not authorized for this employee channel.");

        return Groups.AddToGroupAsync(Context.ConnectionId, StaffGroupName(normalizedEmployeeId));
    }

    public Task LeaveStaff(string employeeId)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return Task.CompletedTask;
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, StaffGroupName(employeeId.Trim()));
    }

    /// <summary>Joins a customer-scoped group to receive real-time status updates across all bookings.</summary>
    public Task JoinCustomer(string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            throw new HubException("CustomerId is required.");

        var user = Context.User!;
        if (!IsStudioStaff(user) && !MatchesUserIdentifier(user, customerId))
            throw new HubException("Not authorized for this customer channel.");

        return Groups.AddToGroupAsync(Context.ConnectionId, CustomerGroupName(customerId.Trim()));
    }

    public Task LeaveCustomer(string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            return Task.CompletedTask;
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, CustomerGroupName(customerId.Trim()));
    }

    /// <summary>Joins the group for one booking. Only the owning customer (or staff) may subscribe.</summary>
    public async Task JoinBooking(string appointmentId)
    {
        var info = await _payments.GetQrPaymentAsync(appointmentId, Context.ConnectionAborted);
        if (info is null)
            throw new HubException("Booking not found.");

        var user = Context.User!;
        if (!IsStudioStaff(user) && !MatchesUserIdentifier(user, info.CustomerId))
            throw new HubException("Not authorized for this booking.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(appointmentId));
    }

    public Task LeaveBooking(string appointmentId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(appointmentId));

    private static bool IsStudioStaff(ClaimsPrincipal user) =>
        user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.SystemAdmin) ||
        user.IsInRole(AppRoles.OfficeClerk) || user.IsInRole(AppRoles.Employee);

    private static bool MatchesUserIdentifier(ClaimsPrincipal user, string targetId)
    {
        var ids = new[]
        {
            user.FindFirstValue(ClaimTypes.NameIdentifier),
            user.FindFirstValue("studio_id"),
            user.FindFirstValue("sub")
        };
        return ids.Any(i => string.Equals(i, targetId, StringComparison.OrdinalIgnoreCase));
    }
}
