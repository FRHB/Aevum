namespace PhotoAevumStudio.Application.Models;

public class EmailConfig
{
    public EmailServerSettings Primary { get; set; } = new();
    public EmailServerSettings Fallback { get; set; } = new();
}

public class EmailServerSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public bool EnableSsl { get; set; } = true;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(Username) &&
        !Username.Contains("YOUR_MAILTRAP", StringComparison.OrdinalIgnoreCase) &&
        !Username.Contains("YOUR_GMAIL", StringComparison.OrdinalIgnoreCase) &&
        !Username.Contains("dummy", StringComparison.OrdinalIgnoreCase) &&
        !Username.Contains("mock", StringComparison.OrdinalIgnoreCase);
}
