using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;
using PhotoAevumStudio.Application.Scheduling;
using PhotoAevumStudio.Infrastructure.Services;
using Xunit;

namespace PhotoAevumStudio.Infrastructure.Tests;

public class EmailServiceFallbackTests
{
    private static EmailConfig CreateTestConfig()
    {
        return new EmailConfig
        {
            Primary = new EmailServerSettings
            {
                Host = "primary-smtp.example.com",
                Port = 587,
                Username = "primary-user",
                Password = "primary-password",
                FromName = "Photo Aevum Studio",
                FromEmail = "primary@photoaevum.com",
                EnableSsl = true
            },
            Fallback = new EmailServerSettings
            {
                Host = "sandbox.smtp.mailtrap.io",
                Port = 587,
                Username = "mailtrap-user",
                Password = "mailtrap-password",
                FromName = "Photo Aevum Studio",
                FromEmail = "fallback@photoaevum.com",
                EnableSsl = true
            }
        };
    }

    [Fact]
    public async Task SendEmailAsync_WhenPrimarySucceeds_SendsViaPrimaryAndDoesNotCallFallback()
    {
        // Arrange
        var config = CreateTestConfig();
        var primaryMock = new Mock<ISmtpClient>();
        var fallbackMock = new Mock<ISmtpClient>();
        var clients = new Queue<ISmtpClient>(new[] { primaryMock.Object, fallbackMock.Object });

        var service = new EmailService(
            Options.Create(config),
            NullLogger<EmailService>.Instance,
            () => clients.Dequeue());

        // Act
        await service.SendEmailAsync("client@example.com", "Test Subject", "<p>Hello</p>", isHtml: true);

        // Assert
        primaryMock.Verify(c => c.ConnectAsync("primary-smtp.example.com", 587, SecureSocketOptions.Auto, It.IsAny<CancellationToken>()), Times.Once);
        primaryMock.Verify(c => c.AuthenticateAsync("primary-user", "primary-password", It.IsAny<CancellationToken>()), Times.Once);
        primaryMock.Verify(c => c.SendAsync(It.Is<MimeMessage>(m => m.Subject == "Test Subject"), It.IsAny<CancellationToken>()), Times.Once);
        primaryMock.Verify(c => c.DisconnectAsync(true, It.IsAny<CancellationToken>()), Times.Once);
        primaryMock.Verify(c => c.Dispose(), Times.Once);

        // Fallback should never be contacted
        fallbackMock.Verify(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<SecureSocketOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendEmailAsync_WhenPrimaryThrows429RateLimit_FallsBackToSecondaryWithExactPayload()
    {
        // Arrange
        var config = CreateTestConfig();
        var primaryMock = new Mock<ISmtpClient>();
        var fallbackMock = new Mock<ISmtpClient>();

        // Primary throws rate limit 429
        primaryMock.Setup(c => c.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, (SmtpStatusCode)429, "Rate limit exceeded (429)"));

        var clients = new Queue<ISmtpClient>(new[] { primaryMock.Object, fallbackMock.Object });

        var service = new EmailService(
            Options.Create(config),
            NullLogger<EmailService>.Instance,
            () => clients.Dequeue());

        // Act
        await service.SendEmailAsync("client@example.com", "Booking Appointment #1234", "<b>Confirmed</b>", isHtml: true);

        // Assert
        // Primary was attempted and disposed
        primaryMock.Verify(c => c.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        primaryMock.Verify(c => c.Dispose(), Times.Once);

        // Fallback (Mailtrap) was invoked with the exact same payload
        fallbackMock.Verify(c => c.ConnectAsync("sandbox.smtp.mailtrap.io", 587, SecureSocketOptions.Auto, It.IsAny<CancellationToken>()), Times.Once);
        fallbackMock.Verify(c => c.AuthenticateAsync("mailtrap-user", "mailtrap-password", It.IsAny<CancellationToken>()), Times.Once);
        fallbackMock.Verify(c => c.SendAsync(It.Is<MimeMessage>(m =>
            m.Subject == "Booking Appointment #1234" &&
            (m.HtmlBody != null && m.HtmlBody.Contains("<b>Confirmed</b>")) &&
            m.To.Mailboxes.Any(b => b.Address == "client@example.com")),
            It.IsAny<CancellationToken>()), Times.Once);
        fallbackMock.Verify(c => c.DisconnectAsync(true, It.IsAny<CancellationToken>()), Times.Once);
        fallbackMock.Verify(c => c.Dispose(), Times.Once);
    }

    [Fact]
    public async Task SendEmailAsync_WhenPrimaryThrows452StorageLimit_FallsBackToSecondary()
    {
        // Arrange
        var config = CreateTestConfig();
        var primaryMock = new Mock<ISmtpClient>();
        var fallbackMock = new Mock<ISmtpClient>();

        // Primary throws 452 InsufficientStorage
        primaryMock.Setup(c => c.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.InsufficientStorage, "Insufficient storage / quota limit"));

        var clients = new Queue<ISmtpClient>(new[] { primaryMock.Object, fallbackMock.Object });

        var service = new EmailService(
            Options.Create(config),
            NullLogger<EmailService>.Instance,
            () => clients.Dequeue());

        // Act
        await service.SendEmailAsync("client@example.com", "Studio Deal Proposal", "Deal terms", isHtml: false);

        // Assert
        fallbackMock.Verify(c => c.SendAsync(It.Is<MimeMessage>(m => m.Subject == "Studio Deal Proposal"), It.IsAny<CancellationToken>()), Times.Once);
        fallbackMock.Verify(c => c.Dispose(), Times.Once);
    }

    [Fact]
    public async Task SendEmailAsync_WhenPrimaryThrowsSocketException_FallsBackToSecondary()
    {
        // Arrange
        var config = CreateTestConfig();
        var primaryMock = new Mock<ISmtpClient>();
        var fallbackMock = new Mock<ISmtpClient>();

        // Primary fails at connection level
        primaryMock.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<SecureSocketOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SocketException((int)SocketError.ConnectionRefused));

        var clients = new Queue<ISmtpClient>(new[] { primaryMock.Object, fallbackMock.Object });

        var service = new EmailService(
            Options.Create(config),
            NullLogger<EmailService>.Instance,
            () => clients.Dequeue());

        // Act
        await service.SendEmailAsync("client@example.com", "Network Test", "Body", isHtml: true);

        // Assert
        fallbackMock.Verify(c => c.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        fallbackMock.Verify(c => c.Dispose(), Times.Once);
    }

    [Fact]
    public async Task SendEmailAsync_WhenBothPrimaryAndSecondaryFail_DoesNotThrowUnhandledException()
    {
        // Arrange
        var config = CreateTestConfig();
        var primaryMock = new Mock<ISmtpClient>();
        var fallbackMock = new Mock<ISmtpClient>();

        primaryMock.Setup(c => c.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, (SmtpStatusCode)429, "Rate limit exceeded"));

        fallbackMock.Setup(c => c.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.ServiceNotAvailable, "Mailtrap down"));

        var clients = new Queue<ISmtpClient>(new[] { primaryMock.Object, fallbackMock.Object });

        var service = new EmailService(
            Options.Create(config),
            NullLogger<EmailService>.Instance,
            () => clients.Dequeue());

        // Act & Assert (must complete cleanly without halting or throwing)
        var exception = await Record.ExceptionAsync(() => service.SendEmailAsync("client@example.com", "Subject", "Body", isHtml: true));
        Assert.Null(exception);

        primaryMock.Verify(c => c.Dispose(), Times.Once);
        fallbackMock.Verify(c => c.Dispose(), Times.Once);
    }

    [Fact]
    public async Task SendEmailAsync_WhenUnconfigured_SimulatesSendWithoutThrowing()
    {
        // Arrange: empty configurations
        var config = new EmailConfig();
        var service = new EmailService(Options.Create(config), NullLogger<EmailService>.Instance);

        // Act & Assert
        var exception = await Record.ExceptionAsync(() => service.SendEmailAsync("test@example.com", "Subject", "Body"));
        Assert.Null(exception);
    }

    [Fact]
    public async Task SendBookingConfirmationAsync_WhenPrimaryRateLimited_DeliversViaFallback()
    {
        // Arrange
        var config = CreateTestConfig();
        var primaryMock = new Mock<ISmtpClient>();
        var fallbackMock = new Mock<ISmtpClient>();

        primaryMock.Setup(c => c.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, (SmtpStatusCode)429, "Primary rate limit"));

        var clients = new Queue<ISmtpClient>(new[] { primaryMock.Object, fallbackMock.Object });

        var service = new EmailService(
            Options.Create(config),
            NullLogger<EmailService>.Instance,
            () => clients.Dequeue());

        var booking = new BookingConfirmation(
            AppointmentId: 42,
            Customer: new BookingCustomer("Jane Doe", "jane@example.com", "09123456789"),
            Date: new DateOnly(2026, 11, 20),
            SlotStart: new TimeOnly(10, 0),
            SlotEnd: new TimeOnly(12, 0),
            Services: new List<ScheduleServiceDto> { new ScheduleServiceDto("SRV-001", "Portrait", "Studio Portrait", 2500m) },
            TotalPrice: 2500m,
            Platform: MeetingPlatform.GoogleMeet,
            MeetingLink: "https://meet.google.com/abc-defg-hij",
            EventLocation: "Studio A");

        // Act: should not throw because fallback delivers successfully
        await service.SendBookingConfirmationAsync(booking);

        // Assert
        fallbackMock.Verify(c => c.SendAsync(It.Is<MimeMessage>(m =>
            m.Subject != null && m.Subject.Contains("Booking confirmed") &&
            m.To.Mailboxes.Any(b => b.Address == "jane@example.com")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendBookingConfirmationAsync_ExcludesPrices_AndIncludesNegotiationTerms()
    {
        // Arrange
        var config = CreateTestConfig();
        var primaryMock = new Mock<ISmtpClient>();
        var clients = new Queue<ISmtpClient>(new[] { primaryMock.Object });

        var service = new EmailService(
            Options.Create(config),
            NullLogger<EmailService>.Instance,
            () => clients.Dequeue());

        var booking = new BookingConfirmation(
            AppointmentId: 43,
            Customer: new BookingCustomer("John Doe", "john@example.com", "09123456789"),
            Date: new DateOnly(2026, 11, 25),
            SlotStart: new TimeOnly(14, 0),
            SlotEnd: new TimeOnly(15, 0),
            Services: new List<ScheduleServiceDto> { new ScheduleServiceDto("SRV-002", "Editorial Shoot", "Editorial Session", 4500m) },
            TotalPrice: 4500m,
            Platform: MeetingPlatform.GoogleMeet,
            MeetingLink: "https://meet.google.com/xyz-uvw-rst",
            EventLocation: "Studio B");

        MimeMessage? sentMessage = null;
        primaryMock.Setup(c => c.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>()))
            .Callback<MimeMessage, CancellationToken, ITransferProgress>((m, _, _) => sentMessage = m)
            .ReturnsAsync("OK");

        // Act
        await service.SendBookingConfirmationAsync(booking);

        // Assert
        Assert.NotNull(sentMessage);
        var htmlBody = sentMessage.HtmlBody;
        Assert.NotNull(htmlBody);

        // Must NOT contain price or currency symbols for initial consultation with booking negotiation
        Assert.DoesNotContain("₱", htmlBody);
        Assert.DoesNotContain("4500", htmlBody);

        // Must display negotiation terms instead
        Assert.Contains("For Negotiation", htmlBody);
        Assert.Contains("To be finalized during consultation", htmlBody);
    }

    [Fact]
    public async Task SendEmployeeWelcomeEmailAsync_WithConfiguredPrimary_DispatchesThroughPrimary()
    {
        // Arrange
        var config = new EmailConfig
        {
            Primary = new EmailServerSettings
            {
                Host = "primary-smtp.example.com",
                Port = 587,
                Username = "primary-user",
                Password = "primary-password",
                FromName = "Photo Aevum Studio",
                FromEmail = "primary@photoaevum.com",
                EnableSsl = true
            }
        };

        var clientMock = new Mock<ISmtpClient>();

        var service = new EmailService(
            Options.Create(config),
            NullLogger<EmailService>.Instance,
            () => clientMock.Object);

        // Act
        await service.SendEmployeeWelcomeEmailAsync("staff@example.com", "John Doe", "EMP-100", "TempPass!123");

        // Assert
        clientMock.Verify(c => c.ConnectAsync("primary-smtp.example.com", 587, SecureSocketOptions.Auto, It.IsAny<CancellationToken>()), Times.Once);
        clientMock.Verify(c => c.AuthenticateAsync("primary-user", "primary-password", It.IsAny<CancellationToken>()), Times.Once);
        clientMock.Verify(c => c.SendAsync(It.Is<MimeMessage>(m => m.Subject != null && m.Subject.Contains("Employee Credentials")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GmailPrimary_FailoverToMailtrapFallback_SuccessfullyDelivers()
    {
        // Arrange
        var config = new EmailConfig
        {
            Primary = new EmailServerSettings
            {
                Host = "smtp.gmail.com",
                Port = 587,
                Username = "studio.photoaevum@gmail.com",
                Password = "gmail-app-password",
                FromName = "Photo Aevum Studio",
                FromEmail = "noreply@photoaevum.com",
                EnableSsl = true
            },
            Fallback = new EmailServerSettings
            {
                Host = "sandbox.smtp.mailtrap.io",
                Port = 587,
                Username = "mailtrap-live-user",
                Password = "mailtrap-live-password",
                FromName = "Photo Aevum Studio",
                FromEmail = "fallback@photoaevum.com",
                EnableSsl = true
            }
        };

        var gmailMock = new Mock<ISmtpClient>();
        var mailtrapMock = new Mock<ISmtpClient>();

        // Gmail throws SmtpCommandException (rate limit 429)
        gmailMock.Setup(c => c.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, (SmtpStatusCode)429, "Gmail daily quota exceeded"));

        var queue = new Queue<ISmtpClient>(new[] { gmailMock.Object, mailtrapMock.Object });
        var service = new EmailService(Options.Create(config), NullLogger<EmailService>.Instance, () => queue.Dequeue());

        // Act
        await service.SendEmailAsync("customer@example.com", "Session Update", "<p>Your session details</p>", isHtml: true);

        // Assert: Gmail was attempted first, then Mailtrap was called with the exact payload
        gmailMock.Verify(c => c.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.Auto, It.IsAny<CancellationToken>()), Times.Once);
        gmailMock.Verify(c => c.AuthenticateAsync("studio.photoaevum@gmail.com", "gmail-app-password", It.IsAny<CancellationToken>()), Times.Once);
        mailtrapMock.Verify(c => c.ConnectAsync("sandbox.smtp.mailtrap.io", 587, SecureSocketOptions.Auto, It.IsAny<CancellationToken>()), Times.Once);
        mailtrapMock.Verify(c => c.AuthenticateAsync("mailtrap-live-user", "mailtrap-live-password", It.IsAny<CancellationToken>()), Times.Once);
        mailtrapMock.Verify(c => c.SendAsync(It.Is<MimeMessage>(m => m.Subject == "Session Update"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void EnvironmentVariables_WithDoubleUnderscores_OverrideConfigProperties()
    {
        // Set environment variables injected by Infisical
        Environment.SetEnvironmentVariable("EMAIL_CONFIG__PRIMARY__HOST", "custom.gmail.smtp.com");
        Environment.SetEnvironmentVariable("EMAIL_CONFIG__PRIMARY__PORT", "465");
        Environment.SetEnvironmentVariable("EMAIL_CONFIG__PRIMARY__USERNAME", "infisical-gmail-user@gmail.com");
        Environment.SetEnvironmentVariable("EMAIL_CONFIG__FALLBACK__HOST", "custom.mailtrap.host");
        Environment.SetEnvironmentVariable("EMAIL_CONFIG__FALLBACK__PORT", "2525");
        Environment.SetEnvironmentVariable("EMAIL_CONFIG__FALLBACK__USERNAME", "infisical-mailtrap-user");

        try
        {
            var config = new EmailConfig();
            var service = new EmailService(Options.Create(config), NullLogger<EmailService>.Instance);

            Assert.Equal("custom.gmail.smtp.com", service.Primary.Host);
            Assert.Equal(465, service.Primary.Port);
            Assert.Equal("infisical-gmail-user@gmail.com", service.Primary.Username);
            Assert.Equal("custom.mailtrap.host", service.Fallback.Host);
            Assert.Equal(2525, service.Fallback.Port);
            Assert.Equal("infisical-mailtrap-user", service.Fallback.Username);
        }
        finally
        {
            Environment.SetEnvironmentVariable("EMAIL_CONFIG__PRIMARY__HOST", null);
            Environment.SetEnvironmentVariable("EMAIL_CONFIG__PRIMARY__PORT", null);
            Environment.SetEnvironmentVariable("EMAIL_CONFIG__PRIMARY__USERNAME", null);
            Environment.SetEnvironmentVariable("EMAIL_CONFIG__FALLBACK__HOST", null);
            Environment.SetEnvironmentVariable("EMAIL_CONFIG__FALLBACK__PORT", null);
            Environment.SetEnvironmentVariable("EMAIL_CONFIG__FALLBACK__USERNAME", null);
        }
    }
}
