namespace PhotoAevumStudio.Web.Services;

/// <summary>
/// Scoped (per circuit) signal that the customer's bookings changed. The wizard raises it after a
/// successful booking; the dashboard and My Bookings subscribe and reload. Register:
///   builder.Services.AddScoped&lt;BookingEvents&gt;();
/// </summary>
public sealed class BookingEvents
{
    public event Action? Changed;
    public void NotifyBookingsChanged() => Changed?.Invoke();
}
