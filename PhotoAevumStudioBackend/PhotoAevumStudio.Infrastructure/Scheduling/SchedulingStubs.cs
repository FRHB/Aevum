using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Scheduling;

namespace PhotoAevumStudio.Infrastructure.Scheduling;

/// <summary>
/// STUB: returns realistic-looking but fake meeting URLs.
/// Replace with Zoom API (POST /users/me/meetings), Google Calendar (conferenceData) or
/// Microsoft Graph (POST /me/onlineMeetings) clients for production.
/// </summary>
public sealed partial class MockMeetingLinkService : IMeetingLinkService
{
    public Task<string> CreateMeetingLinkAsync(MeetingPlatform platform, DateOnly date, TimeOnly start, TimeOnly end, CancellationToken cancellationToken = default)
    {
        var link = platform switch
        {
            MeetingPlatform.Zoom => $"https://zoom.us/j/{RandomDigits(11)}?pwd={RandomLetters(16, upper: true)}",
            MeetingPlatform.GoogleMeet => $"https://meet.google.com/{RandomLetters(3)}-{RandomLetters(4)}-{RandomLetters(3)}",
            MeetingPlatform.MicrosoftTeams => $"https://teams.microsoft.com/l/meetup-join/19%3ameeting_{Guid.NewGuid():N}%40thread.v2/0",
            _ => throw new ScheduleValidationException("Unsupported meeting platform.")
        };
        return Task.FromResult(link);
    }

    public bool IsIssuedLink(MeetingPlatform platform, string? link) => link is not null && platform switch
    {
        MeetingPlatform.Zoom => ZoomLink().IsMatch(link),
        MeetingPlatform.GoogleMeet => MeetLink().IsMatch(link),
        MeetingPlatform.MicrosoftTeams => TeamsLink().IsMatch(link),
        _ => false
    };

    private static string RandomDigits(int n) => string.Concat(Enumerable.Range(0, n).Select(_ => RandomNumberGenerator.GetInt32(10)));

    private static string RandomLetters(int n, bool upper = false) =>
        new(Enumerable.Range(0, n).Select(_ => (char)((upper && RandomNumberGenerator.GetInt32(2) == 0 ? 'A' : 'a') + RandomNumberGenerator.GetInt32(26))).ToArray());

    [GeneratedRegex(@"^https://zoom\.us/j/\d{11}\?pwd=[A-Za-z]{16}$")]
    private static partial Regex ZoomLink();

    [GeneratedRegex(@"^https://meet\.google\.com/[a-z]{3}-[a-z]{4}-[a-z]{3}$")]
    private static partial Regex MeetLink();

    [GeneratedRegex(@"^https://teams\.microsoft\.com/l/meetup-join/19%3ameeting_[0-9a-f]{32}%40thread\.v2/0$")]
    private static partial Regex TeamsLink();
}

/// <summary>
/// STUB email composer: builds the booking summary and hands it to the existing IEmailService
/// (which logs a simulated send when SMTP isn't configured).
/// </summary>
public sealed class BookingNotificationService(IEmailService email, ILogger<BookingNotificationService> logger) : IBookingNotificationService
{
    public async Task SendBookingConfirmationAsync(BookingConfirmation c, CancellationToken cancellationToken = default)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

        var platform = c.Platform switch
        {
            MeetingPlatform.GoogleMeet => "Google Meet",
            MeetingPlatform.MicrosoftTeams => "Microsoft Teams",
            _ => "Zoom"
        };
        var serviceRows = string.Concat(c.Services.Select(s =>
            $"<tr><td style=\"padding:4px 12px 4px 0\">{E(s.Name)}</td><td style=\"text-align:right\">₱{s.Price:N2}</td></tr>"));

        var body = $"""
            <div style="font-family:Arial,sans-serif;color:#111;max-width:560px">
              <h2 style="margin-bottom:4px">Your session is booked</h2>
              <p>Hi {E(c.Customer.FullName)}, here is your booking summary (reference #{c.AppointmentId}).</p>
              <p><strong>Date:</strong> {c.Date:dddd, MMMM d, yyyy}<br/>
                 <strong>Time:</strong> {c.SlotStart:h:mm tt} – {c.SlotEnd:h:mm tt}<br/>
                 <strong>Location:</strong> {(c.EventLocation is null ? "Online only" : E(c.EventLocation))}<br/>
                 <strong>Platform:</strong> {platform}</p>
              <table style="border-collapse:collapse">{serviceRows}
                <tr><td style="padding-top:8px"><strong>Total</strong></td><td style="text-align:right;padding-top:8px"><strong>₱{c.TotalPrice:N2}</strong></td></tr>
              </table>
              <p style="margin-top:16px"><a href="{E(c.MeetingLink)}" style="background:#000;color:#fff;padding:10px 16px;border-radius:6px;text-decoration:none">Join meeting</a></p>
              <p style="font-size:12px;color:#666">{E(c.MeetingLink)}</p>
            </div>
            """;

        await email.SendEmailAsync(c.Customer.Email, $"Booking confirmed – {c.Date:MMM d} at {c.SlotStart:h:mm tt}", body);
        logger.LogInformation("Booking confirmation for appointment {AppointmentId} dispatched to {Email}.", c.AppointmentId, c.Customer.Email);
    }
}
