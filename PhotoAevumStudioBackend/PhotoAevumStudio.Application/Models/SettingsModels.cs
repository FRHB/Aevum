namespace PhotoAevumStudio.Application.Models;

public class GoogleMeetSettings
{
    public const string SectionName = "GoogleMeetSettings";

    public string Provider { get; set; } = "Scaffold";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string WorkspaceServiceAccountKeyPath { get; set; } = string.Empty;
    public string ImpersonatedUserEmail { get; set; } = string.Empty;
    public string CalendarId { get; set; } = "primary";
    public int DefaultDurationMinutes { get; set; } = 30;
    public string TimeZone { get; set; } = "Asia/Manila";
    public string StudioFallbackBaseUrl { get; set; } = "https://meet.google.com/";
    public bool AutoGenerateMeetingCode { get; set; } = true;

    public bool HasOAuthCredentials =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret) &&
        !string.IsNullOrWhiteSpace(RefreshToken);

    public bool HasServiceAccountKey =>
        !string.IsNullOrWhiteSpace(WorkspaceServiceAccountKeyPath) &&
        File.Exists(WorkspaceServiceAccountKeyPath);
}

public class XenditSettings
{
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookToken { get; set; } = string.Empty;
    public decimal DownpaymentPercentage { get; set; } = 0.15m;
    public int HoldDurationMinutes { get; set; } = 30;
    /// <summary>Minutes a 'Pending Payment' booking keeps its time slot locked (LockExpiresAt).</summary>
    public int LockDurationMinutes { get; set; } = 60;
    /// <summary>Base URL of the Blazor customer portal, used for the /payments/{id} link in emails.</summary>
    /// <remarks>Overridden at startup by the top-level <c>BaseUrl</c> setting when present.</remarks>
    public string CustomerPortalBaseUrl { get; set; } = "http://photoaevum.tryasp.net";
    public string InvoiceBaseUrl { get; set; } = "https://checkout.xendit.co/web/";
}
