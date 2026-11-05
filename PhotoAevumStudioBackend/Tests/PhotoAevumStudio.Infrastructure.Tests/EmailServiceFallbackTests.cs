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
            Primary = new SmtpSettings
            {
                Host = "smtp-relay.brevo.com",
                Port = 587,
                Username = "brevo-user",
                Password = "brevo-password",
                FromName = "Photo Aevum Studio",
                FromEmail = "primary@photoaevum.com",
                EnableSsl = true
            },
            Fallback = new SmtpSettings
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
        primaryMock.Verify(c => c.ConnectAsync("smtp-relay.brevo.com", 587, SecureSocketOptions.Auto, It.IsAny<CancellationToken>()), Times.Once);
        primaryMock.Verify(c => c.AuthenticateAsync("brevo-user", "brevo-password", It.IsAny<CancellationToken>()), Times.Once);
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

        // Brevo throws rate limit 429
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

        // Brevo throws 452 InsufficientStorage
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

        // Brevo fails at connection level
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
            .ThrowsAsync(new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, (SmtpStatusCode)429, "Brevo rate limit"));

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
    public async Task LegacyConstructor_WithSmtpSettings_DispatchesThroughPrimary()
    {
        // Arrange
        var legacySettings = new SmtpSettings
        {
            Host = "legacy-smtp.example.com",
            Port = 587,
            Username = "legacy-user",
            Password = "legacy-password",
            FromName = "Legacy Studio",
            FromEmail = "legacy@photoaevum.com",
            EnableSsl = true
        };

        var clientMock = new Mock<ISmtpClient>();

        var service = new EmailService(
            Options.Create(legacySettings),
            NullLogger<EmailService>.Instance,
            () => clientMock.Object);

        // Act
        await service.SendEmployeeWelcomeEmailAsync("staff@example.com", "John Doe", "EMP-100", "TempPass!123");

        // Assert
        clientMock.Verify(c => c.ConnectAsync("legacy-smtp.example.com", 587, SecureSocketOptions.Auto, It.IsAny<CancellationToken>()), Times.Once);
        clientMock.Verify(c => c.AuthenticateAsync("legacy-user", "legacy-password", It.IsAny<CancellationToken>()), Times.Once);
        clientMock.Verify(c => c.SendAsync(It.Is<MimeMessage>(m => m.Subject != null && m.Subject.Contains("Employee Credentials")), It.IsAny<CancellationToken>()), Times.Once);
    }
}
