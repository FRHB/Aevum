namespace PhotoAevumStudio.Application.Models;

public class GoogleMeetSettings
{
    public string Provider { get; set; } = "Scaffold";
    public string WorkspaceServiceAccountKeyPath { get; set; } = string.Empty;
    public string StudioFallbackBaseUrl { get; set; } = "https://meet.google.com/";
    public bool AutoGenerateMeetingCode { get; set; } = true;
}

public class XenditSettings
{
    public string SecretKey { get; set; } = "xnd_development_photoaevum_mock_secret_key_2026";
    public string WebhookToken { get; set; } = "xnd_webhook_token_photoaevum_2026";
    public decimal DownpaymentPercentage { get; set; } = 0.15m;
    public int HoldDurationMinutes { get; set; } = 30;
    /// <summary>Minutes a 'Pending Payment' booking keeps its time slot locked (LockExpiresAt).</summary>
    public int LockDurationMinutes { get; set; } = 60;
    /// <summary>Base URL of the Blazor customer portal, used for the /payments/{id} link in emails.</summary>
    /// <remarks>Overridden at startup by the top-level <c>BaseUrl</c> setting when present.</remarks>
    public string CustomerPortalBaseUrl { get; set; } = "http://photoaevum.tryasp.net";
    public string InvoiceBaseUrl { get; set; } = "https://checkout.xendit.co/web/";
}
