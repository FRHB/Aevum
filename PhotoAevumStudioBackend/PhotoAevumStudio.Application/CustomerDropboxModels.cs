namespace PhotoAevumStudio.Application;

public static class DropboxStatuses
{
    public const string Waiting = "Waiting";
    public const string Uploaded = "Uploaded";
    public const string Expired = "Expired";
}

public static class DropboxCountdownHelper
{
    public const int RetentionDays = 30;
    public const int DefaultDeliveryBusinessDays = 7;

    public static int ComputeDaysRemaining(DateTime? expiresAtUtc, DateTime? nowUtc = null)
    {
        if (!expiresAtUtc.HasValue)
            return 0;

        var now = nowUtc ?? DateTime.UtcNow;
        var remaining = expiresAtUtc.Value - now;
        if (remaining <= TimeSpan.Zero)
            return 0;

        // Immediately after upload (e.g., 29 days 23 hours left), Floor yields 29 ("29 Days Left").
        return Math.Clamp((int)Math.Floor(remaining.TotalDays), 0, RetentionDays);
    }

    public static string FormatCountdownLabel(DateTime? expiresAtUtc, DateTime? nowUtc = null)
    {
        if (!expiresAtUtc.HasValue)
            return "Awaiting Upload";

        var days = ComputeDaysRemaining(expiresAtUtc.Value, nowUtc);
        return days == 1 ? "1 Day Left" : $"{days} Days Left";
    }

    public static DateTime AddBusinessDays(DateTime startUtc, int businessDays)
    {
        var days = businessDays > 0 ? businessDays : DefaultDeliveryBusinessDays;
        var current = startUtc;
        var remaining = days;

        while (remaining > 0)
        {
            current = current.AddDays(1);
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
                remaining--;
        }

        return current;
    }

    public static int ComputeBusinessDaysRemaining(DateTime windowStartUtc, int deliveryBusinessDays, DateTime? nowUtc = null)
    {
        var totalDays = deliveryBusinessDays > 0 ? deliveryBusinessDays : DefaultDeliveryBusinessDays;
        var now = nowUtc ?? DateTime.UtcNow;
        var end = AddBusinessDays(windowStartUtc, totalDays);
        if (now >= end)
            return 0;

        double weekdayDays = 0;
        var cursorDate = now.Date;
        var endDate = end.Date;

        while (cursorDate <= endDate)
        {
            var dayStart = cursorDate;
            var dayEnd = cursorDate.AddDays(1);
            if (dayStart.DayOfWeek != DayOfWeek.Saturday && dayStart.DayOfWeek != DayOfWeek.Sunday)
            {
                var overlapStart = now > dayStart ? now : dayStart;
                var overlapEnd = end < dayEnd ? end : dayEnd;
                if (overlapEnd > overlapStart)
                    weekdayDays += (overlapEnd - overlapStart).TotalDays;
            }
            cursorDate = dayEnd;
        }

        return Math.Clamp((int)Math.Ceiling(weekdayDays), 0, totalDays);
    }

    public static string FormatDeliveryWindowLabel(DateTime windowStartUtc, int deliveryBusinessDays, DateTime? nowUtc = null)
    {
        var days = ComputeBusinessDaysRemaining(windowStartUtc, deliveryBusinessDays, nowUtc);
        return days == 1 ? "1 Business Day Left" : $"{days} Business Days Left";
    }
}

public class CustomerDropboxFolderDto
{
    public string DropboxId { get; set; } = string.Empty;
    public string EventAppointmentId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string EventDescription { get; set; } = string.Empty;
    public DateOnly EventDate { get; set; }
    public TimeOnly EventTime { get; set; }
    public string EventStatus { get; set; } = string.Empty;
    public string DropboxStatus { get; set; } = DropboxStatuses.Waiting;
    public string? OriginalFileName { get; set; }
    public long? FileSizeBytes { get; set; }
    public DateTime EventCompletedAt { get; set; }
    public DateTime? UploadedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int DeliveryBusinessDays { get; set; } = DropboxCountdownHelper.DefaultDeliveryBusinessDays;
    public DateTime? DeliveryWindowEndsAt { get; set; }
    public int DeliveryDaysRemaining { get; set; }
    public int DaysRemaining { get; set; }
    public string CountdownLabel { get; set; } = string.Empty;
    public bool IsExpired { get; set; }
    public decimal RemainingBalance { get; set; }
    public bool IsDeliverablesUnlocked { get; set; } = true;
    public string? LockReason { get; set; }
    public bool IsWaitingForUpload => string.Equals(DropboxStatus, DropboxStatuses.Waiting, StringComparison.OrdinalIgnoreCase) && !IsExpired;
    public bool HasFile => string.Equals(DropboxStatus, DropboxStatuses.Uploaded, StringComparison.OrdinalIgnoreCase)
                           && !IsExpired
                           && !string.IsNullOrWhiteSpace(OriginalFileName);
}

public class DropboxDownloadUrlDto
{
    public string DropboxId { get; set; } = string.Empty;
    public string EventAppointmentId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public class DropboxPurgeResultDto
{
    public int PurgedCount { get; set; }
    public List<string> PurgedDropboxIds { get; set; } = new();
    public List<string> DeletedR2Keys { get; set; } = new();
}
