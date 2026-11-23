using System.Collections.Concurrent;

namespace PhotoAevumStudio.Web.Services;

public sealed record ActiveDropboxUploadInfo(
    string UploadSessionId,
    string EventAppointmentId,
    string UploadedByEmployeeId,
    string UploadedByName,
    string FileName,
    int Percent,
    string Status,
    DateTime StartedAtUtc,
    DateTime LastHeartbeatUtc);

/// <summary>
/// Application-wide singleton registry tracking active ZIP uploads across Blazor Server circuits
/// so other staff members see "Uploading by &lt;Name&gt;..." and cannot start conflicting uploads.
/// </summary>
public sealed class DropboxActiveUploadRegistry
{
    private static readonly TimeSpan StaleTimeout = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, ActiveDropboxUploadInfo> _active = new(StringComparer.OrdinalIgnoreCase);

    public event Action? OnChanged;

    public bool TryStartOrUpdate(
        string eventAppointmentId,
        string sessionId,
        string uploadedByEmployeeId,
        string uploadedByName,
        string fileName,
        int percent = 0,
        string status = "Uploading")
    {
        if (string.IsNullOrWhiteSpace(eventAppointmentId)) return false;
        PurgeStale();

        var key = eventAppointmentId.Trim().ToUpperInvariant();
        var cleanEmp = string.IsNullOrWhiteSpace(uploadedByEmployeeId) ? "STAFF" : uploadedByEmployeeId.Trim().ToUpperInvariant();
        var cleanName = string.IsNullOrWhiteSpace(uploadedByName) ? cleanEmp : uploadedByName.Trim();
        var now = DateTime.UtcNow;

        if (_active.TryGetValue(key, out var existing) &&
            !string.Equals(existing.UploadSessionId, sessionId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(existing.UploadedByEmployeeId, cleanEmp, StringComparison.OrdinalIgnoreCase) &&
            (now - existing.LastHeartbeatUtc) < StaleTimeout)
        {
            return false;
        }

        _active[key] = new ActiveDropboxUploadInfo(
            sessionId,
            key,
            cleanEmp,
            cleanName,
            fileName,
            Math.Clamp(percent, 0, 100),
            status,
            existing?.StartedAtUtc ?? now,
            now);

        OnChanged?.Invoke();
        return true;
    }

    public void UpdateProgress(string eventAppointmentId, string sessionId, int percent, string status)
    {
        if (string.IsNullOrWhiteSpace(eventAppointmentId)) return;
        var key = eventAppointmentId.Trim().ToUpperInvariant();
        if (_active.TryGetValue(key, out var existing) &&
            string.Equals(existing.UploadSessionId, sessionId, StringComparison.OrdinalIgnoreCase))
        {
            _active[key] = existing with
            {
                Percent = Math.Clamp(percent, 0, 100),
                Status = status,
                LastHeartbeatUtc = DateTime.UtcNow
            };
            OnChanged?.Invoke();
        }
    }

    public void CompleteOrRemove(string eventAppointmentId, string? sessionId = null)
    {
        if (string.IsNullOrWhiteSpace(eventAppointmentId)) return;
        var key = eventAppointmentId.Trim().ToUpperInvariant();
        if (_active.TryGetValue(key, out var existing))
        {
            if (string.IsNullOrWhiteSpace(sessionId) ||
                string.Equals(existing.UploadSessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            {
                if (_active.TryRemove(key, out _))
                    OnChanged?.Invoke();
            }
        }
    }

    public ActiveDropboxUploadInfo? GetActiveUpload(string eventAppointmentId)
    {
        if (string.IsNullOrWhiteSpace(eventAppointmentId)) return null;
        PurgeStale();
        return _active.TryGetValue(eventAppointmentId.Trim().ToUpperInvariant(), out var info) ? info : null;
    }

    public bool IsUploadingByOther(string eventAppointmentId, string? currentEmployeeId, out ActiveDropboxUploadInfo? active)
    {
        active = GetActiveUpload(eventAppointmentId);
        if (active == null) return false;
        if (string.IsNullOrWhiteSpace(currentEmployeeId)) return true;
        return !string.Equals(active.UploadedByEmployeeId, currentEmployeeId.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ActiveDropboxUploadInfo> GetAllActiveUploads()
    {
        PurgeStale();
        return _active.Values.ToList();
    }

    private void PurgeStale()
    {
        var cutoff = DateTime.UtcNow - StaleTimeout;
        var removed = false;
        foreach (var kv in _active)
        {
            if (kv.Value.LastHeartbeatUtc < cutoff && _active.TryRemove(kv.Key, out _))
                removed = true;
        }
        if (removed) OnChanged?.Invoke();
    }
}
