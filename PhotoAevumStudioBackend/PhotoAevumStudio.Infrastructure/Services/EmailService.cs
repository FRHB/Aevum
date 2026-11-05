using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;
using PhotoAevumStudio.Application.Scheduling;

namespace PhotoAevumStudio.Infrastructure.Services;

public class EmailService : IEmailService, IBookingNotificationService
{
    private readonly EmailConfig _emailConfig;
    private readonly ILogger<EmailService> _logger;
    private readonly Func<ISmtpClient> _smtpClientFactory;

    // Primary constructor used by Dependency Injection
    public EmailService(IOptions<EmailConfig> emailConfig, ILogger<EmailService> logger)
        : this(emailConfig, logger, () => new SmtpClient())
    {
    }

    // Factory-injected constructor for testing EmailConfig & failover behavior
    public EmailService(IOptions<EmailConfig> emailConfig, ILogger<EmailService> logger, Func<ISmtpClient> smtpClientFactory)
    {
        _emailConfig = emailConfig?.Value ?? new EmailConfig();
        _logger = logger;
        _smtpClientFactory = smtpClientFactory ?? (() => new SmtpClient());
    }

    // Backward-compatibility constructor for unit tests injecting mock SmtpClient with legacy SmtpSettings
    public EmailService(IOptions<SmtpSettings> smtpSettings, ILogger<EmailService> logger, Func<ISmtpClient> smtpClientFactory)
        : this(Options.Create(new EmailConfig { Primary = smtpSettings?.Value ?? new SmtpSettings() }), logger, smtpClientFactory)
    {
    }

    private enum SendOutcome { Sent, NotConfigured, Failed }

    public async Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true) =>
        await DeliverAsync(toEmail, subject, body, isHtml, CancellationToken.None);

    public async Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml, CancellationToken cancellationToken) =>
        await DeliverAsync(toEmail, subject, body, isHtml, cancellationToken);

    private static bool IsConfigured(SmtpSettings? settings)
    {
        if (settings == null) return false;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.Username))
            return false;
        if (settings.Username.Contains("YOUR_MAILTRAP", StringComparison.OrdinalIgnoreCase) ||
            settings.Username.Contains("YOUR_BREVO", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private async Task<SendOutcome> DeliverAsync(string toEmail, string subject, string body, bool isHtml, CancellationToken ct)
    {
        var primary = _emailConfig.Primary;
        var fallback = _emailConfig.Fallback;

        bool primaryConfigured = IsConfigured(primary);
        bool fallbackConfigured = IsConfigured(fallback);

        // Gracefully handle unconfigured / dummy credentials so local development isn't blocked
        if (!primaryConfigured && !fallbackConfigured)
        {
            _logger.LogWarning("SMTP credentials not configured for Primary (Brevo) or Fallback (Mailtrap). Email simulated for {ToEmail}. Subject: {Subject}", toEmail, subject);
            return SendOutcome.NotConfigured;
        }

        bool shouldFallback = false;

        // 1. Attempt delivery via Primary provider (Brevo)
        if (primaryConfigured)
        {
            try
            {
                await SendViaProviderAsync(primary, "Brevo", toEmail, subject, body, isHtml, ct);
                _logger.LogInformation("Email sent successfully via Primary SMTP (Brevo) to {ToEmail}", toEmail);
                return SendOutcome.Sent;
            }
            catch (SmtpCommandException ex)
            {
                var isRateLimit = (int)ex.StatusCode == 429 || (int)ex.StatusCode == 452;
                _logger.LogWarning(
                    ex,
                    "Primary SMTP (Brevo) command error (StatusCode: {StatusCode}, IsRateLimit: {IsRateLimit}). Initiating fallback to Secondary SMTP (Mailtrap) for recipient {ToEmail}.",
                    ex.StatusCode,
                    isRateLimit,
                    toEmail);
                shouldFallback = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Primary SMTP (Brevo) error ({ErrorType}: {ErrorMessage}). Initiating fallback to Secondary SMTP (Mailtrap) for recipient {ToEmail}.",
                    ex.GetType().Name,
                    ex.Message,
                    toEmail);
                shouldFallback = true;
            }
        }
        else
        {
            _logger.LogInformation("Primary SMTP (Brevo) is not configured. Falling back to Secondary SMTP (Mailtrap) for recipient {ToEmail}.", toEmail);
            shouldFallback = true;
        }

        // 2. Fallback delivery via Secondary provider (Mailtrap)
        if (shouldFallback && fallbackConfigured)
        {
            try
            {
                _logger.LogInformation("Dispatching fallback email via Secondary SMTP (Mailtrap) to {ToEmail}.", toEmail);
                await SendViaProviderAsync(fallback, "Mailtrap", toEmail, subject, body, isHtml, ct);
                _logger.LogInformation("Email sent successfully via Secondary SMTP (Mailtrap) to {ToEmail}", toEmail);
                return SendOutcome.Sent;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Fallback SMTP (Mailtrap) also failed to send email to {ToEmail}. Continuing without breaking request flow.",
                    toEmail);
                return SendOutcome.Failed;
            }
        }

        if (shouldFallback && !fallbackConfigured)
        {
            _logger.LogWarning("Primary SMTP failed and Fallback SMTP (Mailtrap) is not configured. Email delivery aborted for {ToEmail}.", toEmail);
        }

        return SendOutcome.Failed;
    }

    private async Task SendViaProviderAsync(
        SmtpSettings settings,
        string providerName,
        string toEmail,
        string subject,
        string body,
        bool isHtml,
        CancellationToken ct)
    {
        var message = new MimeMessage();
        var fromName = string.IsNullOrWhiteSpace(settings.FromName) ? "Photo Aevum Studio" : settings.FromName;
        var fromEmail = string.IsNullOrWhiteSpace(settings.FromEmail) ? "noreply@photoaevum.com" : settings.FromEmail;

        message.From.Add(new MailboxAddress(fromName, fromEmail));

        if (MailboxAddress.TryParse(toEmail.Trim(), out var mailbox))
        {
            message.To.Add(mailbox);
        }
        else
        {
            message.To.Add(new MailboxAddress(toEmail.Trim(), toEmail.Trim()));
        }

        message.Subject = subject;

        var bodyBuilder = new BodyBuilder();
        if (isHtml)
        {
            bodyBuilder.HtmlBody = body;
        }
        else
        {
            bodyBuilder.TextBody = body;
        }
        message.Body = bodyBuilder.ToMessageBody();

        using var client = _smtpClientFactory();
        client.Timeout = 15000;

        var secureSocketOptions = !settings.EnableSsl
            ? SecureSocketOptions.None
            : (settings.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.Auto);

        await client.ConnectAsync(settings.Host, settings.Port, secureSocketOptions, ct);

        if (!string.IsNullOrWhiteSpace(settings.Username) && !string.IsNullOrWhiteSpace(settings.Password))
        {
            await client.AuthenticateAsync(settings.Username, settings.Password, ct);
        }

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }

    /// <summary>
    /// Booking confirmation with the full session summary and teleconference link.
    /// Unlike SendEmailAsync this THROWS when the mail was not actually delivered, so
    /// ScheduleBookingService reports ConfirmationEmailSent = false instead of claiming success.
    /// </summary>
    public async Task SendBookingConfirmationAsync(BookingConfirmation c, CancellationToken cancellationToken = default)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        static string Row(string label, string valueHtml) =>
            $"<div class='row'><span class='label'>{label}</span><span class='val'>{valueHtml}</span></div>";

        var platformLabel = c.Platform switch
        {
            MeetingPlatform.GoogleMeet => "Google Meet",
            MeetingPlatform.Zoom => "Zoom",
            MeetingPlatform.MicrosoftTeams => "Microsoft Teams",
            _ => c.Platform.ToString()
        };

        var linkHtml = Uri.TryCreate(c.MeetingLink, UriKind.Absolute, out var uri) &&
                       (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? $"<a href='{E(c.MeetingLink)}'>{E(c.MeetingLink)}</a>"
            : E(c.MeetingLink);

        var services = string.Join(string.Empty, c.Services.Select(s => Row(E(s.Name), $"₱{s.Price:N2}")));

        var body = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <style>
    body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f8fafc; color: #1e293b; margin: 0; padding: 24px; }}
    .card {{ max-width: 600px; margin: 0 auto; background: #ffffff; border-radius: 12px; border: 1px solid #e2e8f0; overflow: hidden; }}
    .header {{ background: #0f172a; padding: 28px 24px; text-align: center; color: #ffffff; }}
    .header h1 {{ margin: 0; font-size: 20px; letter-spacing: 0.05em; text-transform: uppercase; }}
    .content {{ padding: 28px 24px; }}
    .info-box {{ background: #f1f5f9; border-radius: 8px; border: 1px solid #cbd5e1; padding: 14px 18px; margin: 16px 0; }}
    .row {{ display: flex; justify-content: space-between; gap: 16px; padding: 8px 0; border-bottom: 1px solid #e2e8f0; font-size: 13px; }}
    .row:last-child {{ border-bottom: none; }}
    .label {{ color: #64748b; font-weight: 500; }}
    .val {{ color: #0f172a; font-weight: 600; text-align: right; }}
    .footer {{ background: #f8fafc; border-top: 1px solid #e2e8f0; padding: 16px 24px; text-align: center; font-size: 11px; color: #94a3b8; }}
  </style>
</head>
<body>
  <div class='card'>
    <div class='header'><h1>Photo Aevum Studio</h1></div>
    <div class='content'>
      <p>Hello {E(c.Customer.FullName)}, your session is confirmed. Booking reference <strong>#{c.AppointmentId}</strong>.</p>
      <div class='info-box'>
        {Row("Date", E(c.Date.ToString("dddd, MMMM d, yyyy")))}
        {Row("Time", E($"{c.SlotStart:h:mm tt} – {c.SlotEnd:h:mm tt}"))}
        {(string.IsNullOrWhiteSpace(c.EventLocation) ? string.Empty : Row("Event place / venue", E(c.EventLocation)))}
        {Row("Teleconference", E(platformLabel))}
        {Row("Session link", linkHtml)}
      </div>
      <strong>Services</strong>
      <div class='info-box'>
        {services}
        {Row("Total", $"₱{c.TotalPrice:N2}")}
      </div>
      <p style='font-size:13px;color:#475569;'>Complimentary rescheduling is available up to 24 hours before your session.</p>
    </div>
    <div class='footer'>Photo Aevum Studio &bull; This is an automated confirmation.</div>
  </div>
</body>
</html>";

        var outcome = await DeliverAsync(c.Customer.Email, $"Booking confirmed – {c.Date:MMM d, yyyy} at {c.SlotStart:h:mm tt}", body, true, cancellationToken);
        if (outcome != SendOutcome.Sent)
            throw new InvalidOperationException($"Booking confirmation email was not delivered ({outcome}).");
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
        await SendEmailAsync(toEmail, subject, body, isHtml: true, cancellationToken);
    }

    public async Task SendEmployeeWelcomeEmailAsync(
        string toEmail,
        string employeeName,
        string employeeId,
        string initialPassword,
        CancellationToken cancellationToken = default)
    {
        var subject = "Welcome to Photo Aevum Studio – Employee Credentials";

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
      <p>Staff Portal &bull; Employee Onboarding</p>
    </div>
    <div class='content'>
      <div class='greeting'>Hello {WebUtility.HtmlEncode(employeeName)},</div>
      <p class='instructions'>
        Your employee account has been created at Photo Aevum Studio. Below are your assigned credentials and access details.
      </p>

      <div class='info-box'>
        <div class='row'>
          <span class='label'>Assigned Role:</span>
          <span class='val' style='font-family: inherit;'>Employee</span>
        </div>
        <div class='row'>
          <span class='label'>Employee ID:</span>
          <span class='val'>{WebUtility.HtmlEncode(employeeId)}</span>
        </div>
        <div class='row'>
          <span class='label'>Initial Password:</span>
          <span class='val' style='color: #0369a1;'>{WebUtility.HtmlEncode(initialPassword)}</span>
        </div>
      </div>

      <div class='alert-pill'>
        Important: Initial Password Change Required
      </div>
      <p class='instructions'>
        For security compliance, you must change this initial password upon your first login.
      </p>
    </div>
    <div class='footer'>
      Photo Aevum Studio &bull; Confidential Staff Notification &bull; Do not share your credentials.
    </div>
  </div>
</body>
</html>";

        _logger.LogInformation("Dispatching onboarding credentials email to {Email} for Employee {EmployeeId}.", toEmail, employeeId);
        await SendEmailAsync(toEmail, subject, body, isHtml: true, cancellationToken);
    }
}
