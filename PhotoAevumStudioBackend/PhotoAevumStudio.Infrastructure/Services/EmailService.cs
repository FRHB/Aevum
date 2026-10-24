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
        // Gracefully handle unconfigured / dummy credentials so local development isn't blocked
        if (string.IsNullOrWhiteSpace(_smtpSettings.Host) ||
            string.IsNullOrWhiteSpace(_smtpSettings.Username) ||
            _smtpSettings.Username.Contains("YOUR_MAILTRAP", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("SMTP credentials not configured. Email simulated for {ToEmail}. Subject: {Subject}", toEmail, subject);
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

    public async Task SendAdminWelcomeEmailAsync(
        string toEmail,
        string adminName,
        string adminId,
        string temporaryPassword,
        string role,
        CancellationToken cancellationToken = default)
    {
        var subject = "Welcome to Photo Aevum Studio – Administrator Credentials";

        var body = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <style>
    body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f8fafc; color: #1e293b; margin: 0; padding: 24px; }}
    .card {{ max-width: 600px; margin: 0 auto; background: #ffffff; border-radius: 12px; border: 1px solid #e2e8f0; overflow: hidden; box-shadow: 0 4px 6px -1px rgba(0,0,0,0.05); }}
    .header {{ background: #0f172a; padding: 28px 24px; text-align: center; color: #ffffff; }}
    .header h1 {{ margin: 0; font-size: 20px; font-weight: 700; letter-spacing: 0.05em; text-transform: uppercase; }}
    .header p {{ margin: 6px 0 0; font-size: 13px; color: #94a3b8; }}
    .content {{ padding: 28px 24px; }}
    .greeting {{ font-size: 16px; font-weight: 600; margin-bottom: 12px; }}
    .info-box {{ background: #f1f5f9; border-radius: 8px; border: 1px solid #cbd5e1; padding: 18px; margin: 20px 0; }}
    .row {{ display: flex; justify-content: space-between; padding: 8px 0; border-bottom: 1px solid #e2e8f0; font-size: 13px; }}
    .row:last-child {{ border-bottom: none; }}
    .label {{ color: #64748b; font-weight: 500; }}
    .val {{ color: #0f172a; font-weight: 700; font-family: monospace; }}
    .instructions {{ font-size: 13px; line-height: 1.6; color: #475569; }}
    .alert-pill {{ display: inline-block; background: #fef3c7; color: #92400e; padding: 6px 12px; border-radius: 6px; font-size: 12px; font-weight: 600; margin: 12px 0; }}
    .footer {{ background: #f8fafc; border-top: 1px solid #e2e8f0; padding: 16px 24px; text-align: center; font-size: 11px; color: #94a3b8; }}
  </style>
</head>
<body>
  <div class='card'>
    <div class='header'>
      <h1>Photo Aevum Studio</h1>
      <p>Management Portal &bull; System Onboarding</p>
    </div>
    <div class='content'>
      <div class='greeting'>Hello {WebUtility.HtmlEncode(adminName)},</div>
      <p class='instructions'>
        An administrator account has been provisioned for you at Photo Aevum Studio. Below are your assigned credentials and access details.
      </p>

      <div class='info-box'>
        <div class='row'>
          <span class='label'>Assigned Role:</span>
          <span class='val' style='font-family: inherit;'>{WebUtility.HtmlEncode(role)}</span>
        </div>
        <div class='row'>
          <span class='label'>Admin ID:</span>
          <span class='val'>{WebUtility.HtmlEncode(adminId)}</span>
        </div>
        <div class='row'>
          <span class='label'>Temporary Password:</span>
          <span class='val' style='color: #0369a1;'>{WebUtility.HtmlEncode(temporaryPassword)}</span>
        </div>
      </div>

      <div class='alert-pill'>
        Important: Initial Password Change Required
      </div>
      <p class='instructions'>
        For security compliance, you must change this temporary password upon your first login. You will not have access to studio data until this security step is completed.
      </p>
    </div>
    <div class='footer'>
      Photo Aevum Studio &bull; Confidential Administrative Notification &bull; Do not share your temporary credentials.
    </div>
  </div>
</body>
</html>";

        _logger.LogInformation("Dispatching onboarding credentials email to {Email} for {AdminId} ({Role}).", toEmail, adminId, role);
        await SendEmailAsync(toEmail, subject, body, isHtml: true);
    }
}
