namespace PhotoAevumStudio.Application.Models;

public class SmtpSettings
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
        !Username.Contains("YOUR_BREVO", StringComparison.OrdinalIgnoreCase);
}
