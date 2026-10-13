using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        if (string.IsNullOrWhiteSpace(_smtpSettings.Host))
        {
            _logger.LogWarning("SMTP host is not configured. Email to {ToEmail} skipped (Subject: {Subject}).", toEmail, subject);
            return;
        }

        try
        {
            var port = _smtpSettings.Port > 0 ? _smtpSettings.Port : 587;
            using var client = new SmtpClient(_smtpSettings.Host, port)
            {
                EnableSsl = _smtpSettings.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = _smtpSettings.UseDefaultCredentials
            };

            if (!_smtpSettings.UseDefaultCredentials && !string.IsNullOrWhiteSpace(_smtpSettings.Username))
            {
                client.Credentials = new NetworkCredential(_smtpSettings.Username, _smtpSettings.Password);
            }

            var fromAddress = string.IsNullOrWhiteSpace(_smtpSettings.FromEmail) ? "noreply@photoaevum.studio" : _smtpSettings.FromEmail;
            var fromDisplayName = string.IsNullOrWhiteSpace(_smtpSettings.FromName) ? "Photo Aevum Studio" : _smtpSettings.FromName;

            using var mailMessage = new MailMessage
            {
                From = new MailAddress(fromAddress, fromDisplayName),
                Subject = subject,
                Body = body,
                IsBodyHtml = isHtml
            };
            
            mailMessage.To.Add(toEmail);

            await client.SendMailAsync(mailMessage);
            _logger.LogInformation("Email dispatched via SMTP ({Host}:{Port}) to {ToEmail}.", _smtpSettings.Host, port, toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email via SMTP to {ToEmail}.", toEmail);
        }
    }
}
