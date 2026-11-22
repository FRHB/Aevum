using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace PhotoAevumStudio.Web.Services;

public sealed record DropboxUploadTicket(
    string TicketId,
    string JwtToken,
    string EventAppointmentId,
    string FileName,
    long FileSizeBytes,
    string ContentType,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Server-side in-memory store for short-lived opaque upload tickets so browser XHR
/// uploads can stream through the same-origin BFF without exposing raw JWTs to JS.
/// </summary>
public sealed class DropboxUploadTicketStore
{
    private static readonly TimeSpan TicketTtl = TimeSpan.FromHours(12);
    private readonly ConcurrentDictionary<string, DropboxUploadTicket> _tickets = new(StringComparer.Ordinal);

    public DropboxUploadTicket CreateTicket(
        string jwtToken,
        string eventAppointmentId,
        string fileName,
        long fileSizeBytes,
        string? contentType)
    {
        CleanupExpired();

        var ticketId = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var ticket = new DropboxUploadTicket(
            ticketId,
            jwtToken,
            eventAppointmentId.Trim(),
            fileName,
            fileSizeBytes,
            string.IsNullOrWhiteSpace(contentType) ? "application/zip" : contentType.Trim(),
            DateTimeOffset.UtcNow);

        _tickets[ticketId] = ticket;
        return ticket;
    }

    public bool TryGetValidTicket(string ticketId, out DropboxUploadTicket? ticket)
    {
        ticket = null;
        if (string.IsNullOrWhiteSpace(ticketId))
            return false;

        if (!_tickets.TryGetValue(ticketId.Trim(), out var found))
            return false;

        if (DateTimeOffset.UtcNow - found.CreatedAtUtc > TicketTtl)
        {
            _tickets.TryRemove(ticketId.Trim(), out _);
            return false;
        }

        ticket = found;
        return true;
    }

    public void RemoveTicket(string ticketId)
    {
        if (!string.IsNullOrWhiteSpace(ticketId))
            _tickets.TryRemove(ticketId.Trim(), out _);
    }

    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _tickets)
        {
            if (now - kvp.Value.CreatedAtUtc > TicketTtl)
                _tickets.TryRemove(kvp.Key, out _);
        }
    }
}
