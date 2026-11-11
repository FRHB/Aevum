namespace PhotoAevumStudio.Web.Services;

/// <summary>Builds and parses the payment-confirmation URL that the reservation QR code encodes.</summary>
public static class QrPaymentLinks
{
    public const string ConfirmPathPrefix = "/checkout/confirm/";

    /// <summary>
    /// Absolute URL a phone can open. Uses the configured <c>BaseUrl</c> (live domain / tunnel) and falls back to the
    /// host the page is currently served from, so it works on a live site, an ngrok tunnel or a LAN address.
    /// </summary>
    public static string BuildConfirmUrl(string? configuredBaseUrl, string currentBaseUri, string appointmentId)
    {
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl) ? currentBaseUri : configuredBaseUrl;
        return baseUrl.TrimEnd('/') + ConfirmPathPrefix + Uri.EscapeDataString(appointmentId);
    }

    /// <summary>
    /// Extracts the AppointmentId from a scanned QR value shaped like <c>https://host/checkout/confirm/EVT-0006</c>.
    /// Returns the relative app path so navigation stays on the host the laptop user is already using.
    /// </summary>
    public static bool TryGetConfirmPath(string? scanned, out string appointmentId, out string relativePath)
    {
        appointmentId = relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(scanned)) return false;

        var data = scanned.Trim();
        if (!Uri.TryCreate(data, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return false;

        var path = uri.AbsolutePath;
        var idx = path.IndexOf(ConfirmPathPrefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;

        var id = Uri.UnescapeDataString(path[(idx + ConfirmPathPrefix.Length)..].Trim('/'));
        if (string.IsNullOrWhiteSpace(id) || id.Contains('/')) return false;

        appointmentId = id;
        relativePath = ConfirmPathPrefix.TrimStart('/') + Uri.EscapeDataString(id);
        return true;
    }
}
