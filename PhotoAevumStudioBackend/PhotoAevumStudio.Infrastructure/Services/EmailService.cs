using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;

namespace PhotoAevumStudio.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly SmtpSettings _smtpSettings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<SmtpSettings> smtpSettings, ILogger<EmailService> logger)
    {
        _smtpSettings = smtpSettings.Value;
        _logger = logger;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true)
    {
        // Gracefully handle unconfigured / dummy credentials so local development isn't blocked
        if (string.IsNullOrWhiteSpace(_smtpSettings.Host) ||
            string.IsNullOrWhiteSpace(_smtpSettings.Username) ||
            _smtpSettings.Username.Contains("YOUR_MAILTRAP", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("SMTP credentials not configured. Verification/notification email simulated for {ToEmail}. Subject: {Subject}", toEmail, subject);
            return;
        }

        try
        {
            using var client = new SmtpClient(_smtpSettings.Host, _smtpSettings.Port)
            {
                Credentials = new NetworkCredential(_smtpSettings.Username, _smtpSettings.Password),
                EnableSsl = true
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(
                    string.IsNullOrWhiteSpace(_smtpSettings.FromEmail) ? "noreply@photoaevum.com" : _smtpSettings.FromEmail,
                    string.IsNullOrWhiteSpace(_smtpSettings.FromName) ? "Photo Aevum Studio" : _smtpSettings.FromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = isHtml
            };
            
            mailMessage.To.Add(toEmail);

            await client.SendMailAsync(mailMessage);
            
            _logger.LogInformation("Email sent successfully to {ToEmail}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {ToEmail}. Continuing without breaking request flow.", toEmail);
            // Do not rethrow in order to keep application flow alive if SMTP sandbox is unreachable
        }
    }
}
