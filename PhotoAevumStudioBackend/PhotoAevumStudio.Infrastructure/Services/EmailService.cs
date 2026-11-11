using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public EmailServerSettings Primary => _emailConfig.Primary;
    public EmailServerSettings Fallback => _emailConfig.Fallback;
    public string BaseUrl => ResolveBaseUrl();

    // Primary constructor used by Dependency Injection
    [ActivatorUtilitiesConstructor]
    public EmailService(IOptions<EmailConfig> emailConfig, ILogger<EmailService> logger, IHttpContextAccessor? httpContextAccessor = null)
        : this(emailConfig, logger, () => new SmtpClient(), httpContextAccessor)
    {
    }

    // Factory-injected constructor for testing EmailConfig & failover behavior
    public EmailService(IOptions<EmailConfig> emailConfig, ILogger<EmailService> logger, Func<ISmtpClient> smtpClientFactory, IHttpContextAccessor? httpContextAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(emailConfig);
        _logger = logger ?? NullLogger<EmailService>.Instance;
        _smtpClientFactory = smtpClientFactory ?? (() => new SmtpClient());
        _httpContextAccessor = httpContextAccessor;

        var config = emailConfig.Value ?? new EmailConfig();
        _emailConfig = new EmailConfig
        {
            BaseUrl = config.BaseUrl ?? string.Empty,
            Primary = CloneSettings(config.Primary),
            Fallback = CloneSettings(config.Fallback)
        };

        ApplyEnvironmentVariables(_emailConfig);
    }

    public string ResolveBaseUrl()
    {
        var configured = _emailConfig.BaseUrl?.Trim();
        var ctx = _httpContextAccessor?.HttpContext;

        // If BaseUrl is explicitly set to a non-localhost domain, prefer it.
        if (!string.IsNullOrWhiteSpace(configured) &&
            !configured.Contains("localhost", StringComparison.OrdinalIgnoreCase) &&
            !configured.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return configured.TrimEnd('/');
        }

        // Otherwise, if an active HTTP request has a Host header (or Origin/Referer), resolve dynamically.
        if (ctx?.Request.Headers.TryGetValue("Origin", out var origin) == true &&
            Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var originUri) &&
            !originUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
            !originUri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return originUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }

        if (ctx?.Request.Host.HasValue == true &&
            !ctx.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
            !ctx.Request.Host.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}".TrimEnd('/');
        }

        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');

        if (ctx?.Request.Host.HasValue == true)
            return $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}".TrimEnd('/');

        return "https://photoaevumstudio.com";
    }

    private string ReplaceLocalhostUrls(string body)
    {
        if (string.IsNullOrEmpty(body)) return body;
        var baseUrl = ResolveBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl) || baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            return body;

        return System.Text.RegularExpressions.Regex.Replace(
            body,
            @"https?://(?:localhost|127\.0\.0\.1)(?::\d+)?",
            baseUrl.TrimEnd('/'),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static EmailServerSettings CloneSettings(EmailServerSettings? source)
    {
        if (source == null) return new EmailServerSettings();
        return new EmailServerSettings
        {
            Host = source.Host,
            Port = source.Port,
            Username = source.Username,
            Password = source.Password,
            FromName = source.FromName,
            FromEmail = source.FromEmail,
            EnableSsl = source.EnableSsl
        };
    }

    private static string? GetEnv(params string[] variableNames)
    {
        foreach (var name in variableNames)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();

            // Case-insensitive lookup across process environment variables
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (string.Equals(entry.Key?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    var val = entry.Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(val))
                        return val.Trim();
                }
            }
        }

        return null;
    }

    private static void ApplyEnvironmentVariables(EmailConfig config)
    {
        config.Primary ??= new EmailServerSettings();
        config.Fallback ??= new EmailServerSettings();

        var envBaseUrl = GetEnv("BASE_URL", "BaseUrl", "EMAIL_CONFIG__BASE_URL", "EmailConfig__BaseUrl");
        if (!string.IsNullOrWhiteSpace(envBaseUrl))
            config.BaseUrl = envBaseUrl.TrimEnd('/');

        // 1. Primary SMTP (Gmail)
        var primaryHost = GetEnv(
            "EMAIL_CONFIG__PRIMARY__HOST",
            "EMAIL_CONFIG:PRIMARY:HOST",
            "EmailConfig__Primary__Host",
            "GMAIL_SMTP_HOST",
            "GMAIL_HOST",
            "SMTP_HOST");
        if (!string.IsNullOrWhiteSpace(primaryHost))
            config.Primary.Host = primaryHost;
        else if (string.IsNullOrWhiteSpace(config.Primary.Host))
            config.Primary.Host = "smtp.gmail.com";

        var primaryPortStr = GetEnv(
            "EMAIL_CONFIG__PRIMARY__PORT",
            "EMAIL_CONFIG:PRIMARY:PORT",
            "EmailConfig__Primary__Port",
            "GMAIL_SMTP_PORT",
            "GMAIL_PORT",
            "SMTP_PORT");
        if (int.TryParse(primaryPortStr, out var primaryPort) && primaryPort > 0)
            config.Primary.Port = primaryPort;
        else if (config.Primary.Port <= 0)
            config.Primary.Port = 587;

        var primaryUser = GetEnv(
            "EMAIL_CONFIG__PRIMARY__USERNAME",
            "EMAIL_CONFIG:PRIMARY:USERNAME",
            "EmailConfig__Primary__Username",
            "EMAIL_CONFIG__PRIMARY__USER",
            "GMAIL_SMTP_USER",
            "GMAIL_SMTP_USERNAME",
            "GMAIL_USERNAME",
            "GMAIL_USER",
            "SMTP_USERNAME",
            "SMTP_USER");
        if (!string.IsNullOrWhiteSpace(primaryUser))
            config.Primary.Username = primaryUser;

        var primaryPass = GetEnv(
            "EMAIL_CONFIG__PRIMARY__PASSWORD",
            "EMAIL_CONFIG:PRIMARY:PASSWORD",
            "EmailConfig__Primary__Password",
            "EMAIL_CONFIG__PRIMARY__PASS",
            "GMAIL_SMTP_PASSWORD",
            "GMAIL_PASSWORD",
            "GMAIL_APP_PASSWORD",
            "SMTP_PASSWORD");
        if (!string.IsNullOrWhiteSpace(primaryPass))
            config.Primary.Password = primaryPass;

        var primaryFromName = GetEnv(
            "EMAIL_CONFIG__PRIMARY__FROM_NAME",
            "EMAIL_CONFIG_PRIMARY_FROM_NAME",
            "EMAIL_CONFIG__PRIMARY__FROMNAME",
            "EMAIL_CONFIG:PRIMARY:FROM_NAME",
            "EMAIL_CONFIG:PRIMARY:FROMNAME",
            "EmailConfig__Primary__FromName",
            "GMAIL_FROM_NAME",
            "SMTP_FROM_NAME");
        if (!string.IsNullOrWhiteSpace(primaryFromName))
            config.Primary.FromName = primaryFromName;

        var primaryFromEmail = GetEnv(
            "EMAIL_CONFIG__PRIMARY__FROM_EMAIL",
            "EMAIL_CONFIG_PRIMARY_FROM_EMAIL",
            "EMAIL_CONFIG__PRIMARY__FROMEMAIL",
            "EMAIL_CONFIG:PRIMARY:FROM_EMAIL",
            "EMAIL_CONFIG:PRIMARY:FROMEMAIL",
            "EmailConfig__Primary__FromEmail",
            "GMAIL_FROM_EMAIL",
            "SMTP_FROM_EMAIL");
        if (!string.IsNullOrWhiteSpace(primaryFromEmail))
            config.Primary.FromEmail = primaryFromEmail;
        else if (string.IsNullOrWhiteSpace(config.Primary.FromEmail) && !string.IsNullOrWhiteSpace(config.Primary.Username) && config.Primary.Username.Contains('@'))
            config.Primary.FromEmail = config.Primary.Username;
        else if (string.IsNullOrWhiteSpace(config.Primary.FromEmail))
            config.Primary.FromEmail = "photoaevumstudio@gmail.com";

        var primarySslStr = GetEnv(
            "EMAIL_CONFIG__PRIMARY__ENABLE_SSL",
            "EMAIL_CONFIG__PRIMARY__ENABLESSL",
            "EMAIL_CONFIG:PRIMARY:ENABLE_SSL",
            "EMAIL_CONFIG:PRIMARY:ENABLESSL",
            "EmailConfig__Primary__EnableSsl",
            "SMTP_ENABLE_SSL");
        if (bool.TryParse(primarySslStr, out var primarySsl))
            config.Primary.EnableSsl = primarySsl;

        // 2. Fallback SMTP (Mailtrap)
        var fallbackHost = GetEnv(
            "EMAIL_CONFIG__FALLBACK__HOST",
            "EMAIL_CONFIG:FALLBACK:HOST",
            "EmailConfig__Fallback__Host",
            "MAILTRAP_SMTP_HOST",
            "MAILTRAP_HOST");
        if (!string.IsNullOrWhiteSpace(fallbackHost))
            config.Fallback.Host = fallbackHost;
        else if (string.IsNullOrWhiteSpace(config.Fallback.Host))
            config.Fallback.Host = "sandbox.smtp.mailtrap.io";

        var fallbackPortStr = GetEnv(
            "EMAIL_CONFIG__FALLBACK__PORT",
            "EMAIL_CONFIG:FALLBACK:PORT",
            "EmailConfig__Fallback__Port",
            "MAILTRAP_SMTP_PORT",
            "MAILTRAP_PORT");
        if (int.TryParse(fallbackPortStr, out var fallbackPort) && fallbackPort > 0)
            config.Fallback.Port = fallbackPort;
        else if (config.Fallback.Port <= 0)
            config.Fallback.Port = 587;

        var fallbackUser = GetEnv(
            "EMAIL_CONFIG__FALLBACK__USERNAME",
            "EMAIL_CONFIG:FALLBACK:USERNAME",
            "EmailConfig__Fallback__Username",
            "EMAIL_CONFIG__FALLBACK__USER",
            "MAILTRAP_SMTP_USER",
            "MAILTRAP_SMTP_USERNAME",
            "MAILTRAP_USERNAME");
        if (!string.IsNullOrWhiteSpace(fallbackUser))
            config.Fallback.Username = fallbackUser;

        var fallbackPass = GetEnv(
            "EMAIL_CONFIG__FALLBACK__PASSWORD",
            "EMAIL_CONFIG:FALLBACK:PASSWORD",
            "EmailConfig__Fallback__Password",
            "EMAIL_CONFIG__FALLBACK__PASS",
            "MAILTRAP_SMTP_PASSWORD",
            "MAILTRAP_PASSWORD",
            "MAILTRAP_API_TOKEN");
        if (!string.IsNullOrWhiteSpace(fallbackPass))
            config.Fallback.Password = fallbackPass;

        var fallbackFromName = GetEnv(
            "EMAIL_CONFIG__FALLBACK__FROM_NAME",
            "EMAIL_CONFIG__FALLBACK__FROMNAME",
            "EMAIL_CONFIG:FALLBACK:FROM_NAME",
            "EMAIL_CONFIG:FALLBACK:FROMNAME",
            "EmailConfig__Fallback__FromName",
            "MAILTRAP_FROM_NAME");
        if (!string.IsNullOrWhiteSpace(fallbackFromName))
            config.Fallback.FromName = fallbackFromName;

        var fallbackFromEmail = GetEnv(
            "EMAIL_CONFIG__FALLBACK__FROM_EMAIL",
            "EMAIL_CONFIG__FALLBACK__FROMEMAIL",
            "EMAIL_CONFIG:FALLBACK:FROM_EMAIL",
            "EMAIL_CONFIG:FALLBACK:FROMEMAIL",
            "EmailConfig__Fallback__FromEmail",
            "MAILTRAP_FROM_EMAIL");
        if (!string.IsNullOrWhiteSpace(fallbackFromEmail))
            config.Fallback.FromEmail = fallbackFromEmail;
        else if (string.IsNullOrWhiteSpace(config.Fallback.FromEmail) && !string.IsNullOrWhiteSpace(config.Fallback.Username) && config.Fallback.Username.Contains('@'))
            config.Fallback.FromEmail = config.Fallback.Username;

        var fallbackSslStr = GetEnv(
            "EMAIL_CONFIG__FALLBACK__ENABLE_SSL",
            "EMAIL_CONFIG__FALLBACK__ENABLESSL",
            "EMAIL_CONFIG:FALLBACK:ENABLE_SSL",
            "EMAIL_CONFIG:FALLBACK:ENABLESSL",
            "EmailConfig__Fallback__EnableSsl");
        if (bool.TryParse(fallbackSslStr, out var fallbackSsl))
            config.Fallback.EnableSsl = fallbackSsl;
    }

    private enum SendOutcome { Sent, NotConfigured, Failed }

    public async Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true) =>
        await DeliverAsync(toEmail, subject, body, isHtml, CancellationToken.None);

    public async Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml, CancellationToken cancellationToken) =>
        await DeliverAsync(toEmail, subject, body, isHtml, cancellationToken);

    public async Task<bool> TrySendEmailAsync(string toEmail, string subject, string body, bool isHtml = true, CancellationToken cancellationToken = default)
    {
        var outcome = await DeliverAsync(toEmail, subject, body, isHtml, cancellationToken);
        return outcome == SendOutcome.Sent;
    }

    private static bool IsConfigured(EmailServerSettings? settings)
    {
        if (settings == null) return false;
        return settings.IsConfigured;
    }

    private async Task<SendOutcome> DeliverAsync(string toEmail, string subject, string body, bool isHtml, CancellationToken ct)
    {
        body = ReplaceLocalhostUrls(body);
        var primary = _emailConfig.Primary;
        var fallback = _emailConfig.Fallback;

        bool primaryConfigured = IsConfigured(primary);
        bool fallbackConfigured = IsConfigured(fallback);

        // Gracefully handle unconfigured / dummy credentials so local development and tests aren't blocked
        if (!primaryConfigured && !fallbackConfigured)
        {
            _logger.LogWarning("SMTP credentials not configured for Primary (Gmail) or Fallback (Mailtrap). Email simulated for {ToEmail}. Subject: {Subject}", toEmail, subject);
            return SendOutcome.NotConfigured;
        }

        bool shouldFallback = false;

        // 1. Attempt delivery via Primary provider (Gmail)
        if (primaryConfigured)
        {
            try
            {
                await SendViaProviderAsync(primary, "Gmail", toEmail, subject, body, isHtml, ct);
                _logger.LogInformation("Email sent successfully via Primary SMTP (Gmail) to {ToEmail}", toEmail);
                return SendOutcome.Sent;
            }
            catch (SmtpCommandException ex)
            {
                var isRateLimit = (int)ex.StatusCode == 429 || (int)ex.StatusCode == 452;
                _logger.LogWarning(
                    ex,
                    "Primary SMTP (Gmail) command error (StatusCode: {StatusCode}, IsRateLimit: {IsRateLimit}). Initiating fallback to Secondary SMTP (Mailtrap) for recipient {ToEmail}.",
                    ex.StatusCode,
                    isRateLimit,
                    toEmail);
                shouldFallback = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Primary SMTP (Gmail) error ({ErrorType}: {ErrorMessage}). Initiating fallback to Secondary SMTP (Mailtrap) for recipient {ToEmail}.",
                    ex.GetType().Name,
                    ex.Message,
                    toEmail);
                shouldFallback = true;
            }
        }
        else
        {
            _logger.LogInformation("Primary SMTP (Gmail) is not configured. Falling back to Secondary SMTP (Mailtrap) for recipient {ToEmail}.", toEmail);
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
        EmailServerSettings settings,
        string providerName,
        string toEmail,
        string subject,
        string body,
        bool isHtml,
        CancellationToken ct)
    {
        var message = new MimeMessage();
        var fromName = string.IsNullOrWhiteSpace(settings.FromName) ? "Photo Aevum Studio" : settings.FromName;
        var fromEmail = !string.IsNullOrWhiteSpace(settings.FromEmail)
            ? settings.FromEmail
            : (!string.IsNullOrWhiteSpace(settings.Username) && settings.Username.Contains('@') ? settings.Username : "photoaevumstudio@gmail.com");

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

        var services = string.Join(string.Empty, c.Services.Select(s => Row(E(s.Name), "For Negotiation")));

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
      <strong>Services Requested</strong>
      <div class='info-box'>
        {services}
        {Row("Pricing & Terms", "To be finalized during consultation")}
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
        CancellationToken cancellationToken = default,
        string assignedRole = "Employee")
    {
        var displayRole = string.IsNullOrWhiteSpace(assignedRole) ? "Employee" : assignedRole.Trim();
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
          <span class='val' style='font-family: inherit;'>{WebUtility.HtmlEncode(displayRole)}</span>
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

        _logger.LogInformation("Dispatching onboarding credentials email to {Email} for Employee {EmployeeId} ({AssignedRole}).", toEmail, employeeId, displayRole);
        await SendEmailAsync(toEmail, subject, body, isHtml: true, cancellationToken);
    }
}
