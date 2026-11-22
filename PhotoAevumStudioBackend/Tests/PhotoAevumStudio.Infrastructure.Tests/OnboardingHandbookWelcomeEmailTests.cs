using System.Text;
using MailKit;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;
using PhotoAevumStudio.Infrastructure.Services;
using Xunit;

namespace PhotoAevumStudio.Infrastructure.Tests;

public class OnboardingHandbookWelcomeEmailTests
{
    private static string GetRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null &&
               !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")) &&
               !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static EmailConfig CreateConfiguredEmailConfig() => new()
    {
        Primary = new EmailServerSettings
        {
            Host = "smtp.gmail.com",
            Port = 587,
            Username = "studio@photoaevum.com",
            Password = "app-password",
            FromName = "Photo Aevum Studio",
            FromEmail = "studio@photoaevum.com",
            EnableSsl = true
        }
    };

    [Fact]
    public async Task OnboardingDocumentService_LoadsEmployeeHandbookPdf_FromConfiguredStoragePath()
    {
        var root = GetRepositoryRoot();
        var handbookPath = Path.Combine(
            root,
            "PhotoAevumStudioBackend",
            "PhotoAevumStudio.Api",
            "storage",
            "documents",
            "Employee_Handbook_and_Manual_PHOTO_AEVUM_STUDIO.pdf");

        Assert.True(File.Exists(handbookPath), $"Expected handbook PDF at {handbookPath}");

        var service = new OnboardingDocumentService(
            Options.Create(new OnboardingDocumentsSettings
            {
                StoragePath = handbookPath,
                HandbookFileName = "Employee_Handbook_and_Manual_PHOTO_AEVUM_STUDIO.pdf"
            }),
            NullLogger<OnboardingDocumentService>.Instance);

        var result = await service.GetEmployeeHandbookAsync();

        Assert.True(result.Found);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Attachment);
        Assert.Equal("Employee_Handbook_and_Manual_PHOTO_AEVUM_STUDIO.pdf", result.Attachment!.FileName);
        Assert.Equal("application/pdf", result.Attachment.ContentType);
        Assert.True(result.Attachment.Content.Length > 1000);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(result.Attachment.Content, 0, 5));
    }

    [Fact]
    public async Task OnboardingDocumentService_BlocksPathTraversal_AndFailsGracefullyWhenMissing()
    {
        var traversalService = new OnboardingDocumentService(
            Options.Create(new OnboardingDocumentsSettings
            {
                StoragePath = "../../appsettings.json"
            }),
            NullLogger<OnboardingDocumentService>.Instance);

        var traversalResult = await traversalService.GetEmployeeHandbookAsync();
        Assert.False(traversalResult.Found);
        Assert.Null(traversalResult.Attachment);
        Assert.NotNull(traversalResult.ErrorMessage);

        var missingService = new OnboardingDocumentService(
            Options.Create(new OnboardingDocumentsSettings
            {
                StoragePath = "storage/documents/nonexistent-handbook.pdf"
            }),
            NullLogger<OnboardingDocumentService>.Instance);

        var missingResult = await missingService.GetEmployeeHandbookAsync();
        Assert.False(missingResult.Found);
        Assert.Null(missingResult.Attachment);
        Assert.NotNull(missingResult.ErrorMessage);
    }

    [Fact]
    public async Task OnboardingDocumentService_UsesTimeLimitedR2Url_WhenPdfExceedsAttachmentLimit()
    {
        var root = GetRepositoryRoot();
        var handbookPath = Path.Combine(
            root,
            "PhotoAevumStudioBackend",
            "PhotoAevumStudio.Api",
            "storage",
            "documents",
            "Employee_Handbook_and_Manual_PHOTO_AEVUM_STUDIO.pdf");

        var r2Mock = new Mock<IR2StorageService>();
        r2Mock.SetupGet(r => r.IsConfigured).Returns(true);
        r2Mock.SetupGet(r => r.DocumentsBucket).Returns("documents");
        r2Mock.Setup(r => r.GetPresignedDownloadUrl("documents", "onboarding/Employee_Handbook_and_Manual_PHOTO_AEVUM_STUDIO.pdf", "Employee_Handbook_and_Manual_PHOTO_AEVUM_STUDIO.pdf", It.IsAny<TimeSpan?>()))
            .Returns("https://r2.example.com/documents/signed-handbook.pdf?X-Amz-Expires=604800");

        var service = new OnboardingDocumentService(
            Options.Create(new OnboardingDocumentsSettings
            {
                StoragePath = handbookPath,
                MaxAttachmentBytes = 1024 // Force attachment-limit fallback
            }),
            NullLogger<OnboardingDocumentService>.Instance,
            r2Mock.Object);

        var result = await service.GetEmployeeHandbookAsync();

        Assert.True(result.Found);
        Assert.Null(result.Attachment);
        Assert.Equal("https://r2.example.com/documents/signed-handbook.pdf?X-Amz-Expires=604800", result.DownloadUrl);
    }

    [Fact]
    public async Task TrySendEmployeeWelcomeEmailAsync_AttachesHandbookPdf_AndIncludesCredentialsAndOnboardingChecklist()
    {
        var root = GetRepositoryRoot();
        var handbookPath = Path.Combine(
            root,
            "PhotoAevumStudioBackend",
            "PhotoAevumStudio.Api",
            "storage",
            "documents",
            "Employee_Handbook_and_Manual_PHOTO_AEVUM_STUDIO.pdf");

        var docService = new OnboardingDocumentService(
            Options.Create(new OnboardingDocumentsSettings { StoragePath = handbookPath }),
            NullLogger<OnboardingDocumentService>.Instance);

        var smtpMock = new Mock<ISmtpClient>();
        MimeMessage? sentMessage = null;
        smtpMock.Setup(c => c.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>()))
            .Callback<MimeMessage, CancellationToken, ITransferProgress>((m, _, _) => sentMessage = m)
            .ReturnsAsync("OK");

        var emailService = new EmailService(
            Options.Create(CreateConfiguredEmailConfig()),
            NullLogger<EmailService>.Instance,
            () => smtpMock.Object,
            onboardingDocumentService: docService);

        var result = await emailService.TrySendEmployeeWelcomeEmailAsync(
            toEmail: "new.staff@photoaevum.studio",
            employeeName: "Elena Cruz",
            employeeId: "EMP-019",
            initialPassword: "Cruz#2026!",
            assignedRole: "Photographer");

        Assert.True(result.EmailSent);
        Assert.True(result.HandbookAttached);
        Assert.Null(result.WarningMessage);
        Assert.NotNull(sentMessage);

        var html = sentMessage!.HtmlBody;
        Assert.NotNull(html);
        Assert.Contains("new.staff@photoaevum.studio", html);
        Assert.Contains("Photographer", html);
        Assert.Contains("EMP-019", html);
        Assert.Contains("Cruz#2026!", html);
        Assert.Contains("change your password on first login", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Employee Handbook &amp; Operations Manual", html);
        Assert.Contains("Jay Anne D. Dingas", html);
        Assert.Contains("Workplace Policies", html);
        Assert.Contains("Confidentiality &amp; NDA Rules", html);
        Assert.Contains("Equipment Accountability", html);
        Assert.Contains("Employee Acknowledgment", html);

        var attachment = Assert.Single(sentMessage.Attachments);
        var mimePart = Assert.IsType<MimePart>(attachment);
        Assert.Equal("Employee_Handbook_and_Manual_PHOTO_AEVUM_STUDIO.pdf", mimePart.FileName);
        Assert.Equal("application/pdf", mimePart.ContentType.MimeType);
    }

    [Fact]
    public async Task TrySendAdminWelcomeEmailAsync_AttachesHandbookPdf_AndHandlesMissingFileGracefully()
    {
        var missingDocService = new OnboardingDocumentService(
            Options.Create(new OnboardingDocumentsSettings { StoragePath = "storage/documents/missing.pdf" }),
            NullLogger<OnboardingDocumentService>.Instance);

        var smtpMock = new Mock<ISmtpClient>();
        MimeMessage? sentMessage = null;
        smtpMock.Setup(c => c.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>()))
            .Callback<MimeMessage, CancellationToken, ITransferProgress>((m, _, _) => sentMessage = m)
            .ReturnsAsync("OK");

        var emailService = new EmailService(
            Options.Create(CreateConfiguredEmailConfig()),
            NullLogger<EmailService>.Instance,
            () => smtpMock.Object,
            onboardingDocumentService: missingDocService);

        var result = await emailService.TrySendAdminWelcomeEmailAsync(
            toEmail: "director@photoaevum.studio",
            adminName: "Jay Anne D. Dingas",
            adminId: "ADM-004",
            temporaryPassword: "Admin@2026!",
            role: "Admin");

        // Credentials email still sent even when PDF file retrieval fails, with explicit warning
        Assert.True(result.EmailSent);
        Assert.False(result.HandbookAttached);
        Assert.NotNull(result.WarningMessage);
        Assert.NotNull(sentMessage);
        Assert.Empty(sentMessage!.Attachments);
        Assert.Contains("ADM-004", sentMessage.HtmlBody);
        Assert.Contains("Admin@2026!", sentMessage.HtmlBody);
    }

    [Fact]
    public async Task TrySendAdminWelcomeEmailAsync_WhenSmtpFails_ReturnsFailureResultWithoutThrowing()
    {
        var root = GetRepositoryRoot();
        var handbookPath = Path.Combine(
            root,
            "PhotoAevumStudioBackend",
            "PhotoAevumStudio.Api",
            "storage",
            "documents",
            "Employee_Handbook_and_Manual_PHOTO_AEVUM_STUDIO.pdf");

        var docService = new OnboardingDocumentService(
            Options.Create(new OnboardingDocumentsSettings { StoragePath = handbookPath }),
            NullLogger<OnboardingDocumentService>.Instance);

        var failingSmtp = new Mock<ISmtpClient>();
        failingSmtp.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<MailKit.Security.SecureSocketOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP unreachable"));

        var emailService = new EmailService(
            Options.Create(CreateConfiguredEmailConfig()),
            NullLogger<EmailService>.Instance,
            () => failingSmtp.Object,
            onboardingDocumentService: docService);

        var result = await emailService.TrySendAdminWelcomeEmailAsync(
            toEmail: "admin@photoaevum.studio",
            adminName: "Studio Admin",
            adminId: "ADM-005",
            temporaryPassword: "Pass@2026!",
            role: "Admin");

        Assert.False(result.EmailSent);
        Assert.False(result.HandbookAttached);
        Assert.NotNull(result.WarningMessage);
    }
}
