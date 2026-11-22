using System.Text;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;
using PhotoAevumStudio.Infrastructure.Services;
using Xunit;

namespace PhotoAevumStudio.Infrastructure.Tests;

public class BookingContractAndReceiptTests
{
    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static (BookingContractDto Contract, TermsVersionDto Terms) CreateSampleContractAndTerms(bool accepted = true)
    {
        var terms = new TermsVersionDto
        {
            TermsVersionId = 1,
            VersionCode = "v1.0",
            Title = "Standard Terms and Conditions",
            EffectiveDate = new DateOnly(2026, 1, 1),
            DeliveryBusinessDays = 7,
            OvertimeHourlyRate = 1500.00m,
            IsActive = true
        };
        terms.TermsBodyHtml = DocumentPdfService.BuildDefaultStandardTermsHtml(
            "Photo Aevum Studio", terms.VersionCode, terms.EffectiveDate, terms.DeliveryBusinessDays);

        var contract = new BookingContractDto
        {
            BookingContractId = 10,
            ContractNumber = "CTR-EVT-0042",
            EventAppointmentId = "EVT-0042",
            NegotiationId = "NEG-0018",
            CustomerId = "CUST-0009",
            TermsVersionId = 1,
            TermsVersionCode = "v1.0",
            DeliveryBusinessDays = 7,
            StudioName = "Photo Aevum Studio",
            StudioAddress = "Metro Manila, Philippines",
            StudioPhone = "(+63) 919-578-93124",
            StudioEmail = "photoaevumstudio@gmail.com",
            ClientName = "Maria Clara",
            ClientAddress = "Makati City",
            ClientPhone = "+639171234567",
            ClientEmail = "maria@example.com",
            SessionType = "Wedding Coverage",
            DateOfShoot = new DateOnly(2026, 12, 15),
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(15, 0),
            Venue = "Studio A (Main Hall)",
            PackageName = "Signature Full-Day Package",
            PackageInclusions = "6 hours coverage, edited digital gallery",
            TotalPackageAmount = 20000.00m,
            ReservationFeeAmount = 3000.00m,
            RemainingBalanceAmount = 17000.00m,
            RepresentativeName = "Elena Cruz",
            ContractStatus = accepted ? "Accepted" : "PendingAcceptance",
            AcceptedAtUtc = accepted ? new DateTime(2026, 11, 22, 4, 30, 0, DateTimeKind.Utc) : null,
            ClientIpAddress = accepted ? "203.0.113.42" : null,
            UserAgent = accepted ? "UnitTestBrowser/1.0" : null,
            ClickwrapReference = accepted ? "CW-EVT-0042" : null,
            CreatedAtUtc = new DateTime(2026, 11, 22, 4, 0, 0, DateTimeKind.Utc)
        };

        return (contract, terms);
    }

    [Fact]
    public void DocumentPdfService_GeneratesValidPdfs_ForAgreementTermsAndBothReceipts()
    {
        var pdfService = new DocumentPdfService(Options.Create(new StudioProfileSettings()));
        var (contract, terms) = CreateSampleContractAndTerms(accepted: true);

        var agreementPdf = pdfService.GenerateServicesAgreementPdf(contract, terms);
        var termsPdf = pdfService.GenerateStandardTermsPdf(terms);

        var stage1Receipt = new BookingReceiptDto
        {
            ReceiptNumber = "REC-S1-EVT-0042",
            EventAppointmentId = "EVT-0042",
            ReceiptType = "ReservationAcknowledgment",
            TotalContractCharges = 20000.00m,
            Stage1PaidAmount = 3000.00m,
            OvertimeAddOnAmount = 0m,
            Stage2PaidAmount = 0m,
            RemainingBalanceDue = 17000.00m,
            PaymentMethod = "Xendit QRPH",
            GatewayReferenceId = "qr_test_stage1",
            IssuedAtUtc = new DateTime(2026, 11, 22, 4, 35, 0, DateTimeKind.Utc)
        };
        var stage1Pdf = pdfService.GenerateReservationReceiptPdf(stage1Receipt, contract, terms);

        var finalReceipt = new BookingReceiptDto
        {
            ReceiptNumber = "REC-S2-EVT-0042",
            EventAppointmentId = "EVT-0042",
            ReceiptType = "FinalConsolidated",
            TotalContractCharges = 21500.00m,
            Stage1PaidAmount = 3000.00m,
            OvertimeAddOnAmount = 1500.00m,
            Stage2PaidAmount = 18500.00m,
            RemainingBalanceDue = 0.00m,
            PaymentMethod = "Xendit QRPH",
            GatewayReferenceId = "qr_test_stage2",
            IssuedAtUtc = new DateTime(2026, 12, 15, 8, 10, 0, DateTimeKind.Utc)
        };
        var finalPdf = pdfService.GenerateFinalConsolidatedReceiptPdf(
            finalReceipt, contract, terms, "2026-11-22 • qr_test_stage1", "2026-12-15 • qr_test_stage2", "1 hr reception overtime");

        foreach (var pdf in new[] { agreementPdf, termsPdf, stage1Pdf, finalPdf })
        {
            Assert.NotNull(pdf);
            Assert.True(pdf.Length > 1500, "Generated PDF should be non-trivial.");
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
        }

        var agreementHtml = pdfService.RenderServicesAgreementHtml(contract, terms);
        Assert.Contains("PHOTO STUDIO SERVICES AGREEMENT", agreementHtml);
        Assert.Contains("203.0.113.42", agreementHtml);
        Assert.Contains("CW-EVT-0042", agreementHtml);
        Assert.Contains("PHP 20,000.00", agreementHtml);

        var finalHtml = pdfService.RenderFinalConsolidatedReceiptHtml(
            finalReceipt, contract, terms, "2026-11-22 • qr_test_stage1", "2026-12-15 • qr_test_stage2", "1 hr reception overtime");
        Assert.Contains("FINAL CONSOLIDATED PAYMENT RECEIPT", finalHtml);
        Assert.Contains("STATUS: PAID IN FULL", finalHtml);
        Assert.Contains("1 hr reception overtime", finalHtml);
    }

    [Fact]
    public async Task EmailService_TrySendEmailWithAttachmentsAsync_AttachesPdfFiles()
    {
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

        MimeMessage? captured = null;
        var smtpMock = new Mock<ISmtpClient>();
        smtpMock.Setup(c => c.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>()))
            .Callback<MimeMessage, CancellationToken, ITransferProgress>((m, _, _) => captured = m)
            .ReturnsAsync("OK");

        var emailService = new EmailService(
            Options.Create(config),
            NullLogger<EmailService>.Instance,
            () => smtpMock.Object);

        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF");
        var sent = await emailService.TrySendEmailWithAttachmentsAsync(
            "client@example.com",
            "Agreement & Receipt",
            "<p>Attached</p>",
            new[] { new EmailAttachment("CTR-EVT-0042.pdf", pdfBytes) });

        Assert.True(sent);
        Assert.NotNull(captured);
        Assert.Contains(captured!.Attachments, a => a.ContentDisposition?.FileName == "CTR-EVT-0042.pdf");
    }

    [Fact]
    public void CanonicalSqlFiles_DefineTwoStageContractTermsPaymentsAndReceipts()
    {
        var root = GetRepoRoot();
        var dbDir = Path.Combine(root, "PhotoAevumStudioBackend", "database");

        var masterSql = File.ReadAllText(Path.Combine(dbDir, "PhotoAevumStudio.sql"));
        var idempotentSql = File.ReadAllText(Path.Combine(dbDir, "PhotoAevumStudioIdempotent.sql"));
        var procsSql = File.ReadAllText(Path.Combine(dbDir, "PhotoAevumStudioStoredProcedures.sql"));

        foreach (var sql in new[] { masterSql, idempotentSql })
        {
            Assert.Contains("CREATE TABLE dbo.TermsVersions", sql);
            Assert.Contains("CREATE TABLE dbo.BookingContracts", sql);
            Assert.Contains("CREATE TABLE dbo.BookingPayments", sql);
            Assert.Contains("CREATE TABLE dbo.BookingReceipts", sql);
            Assert.Contains("IsDeliverablesUnlocked", sql);
            Assert.Contains("RemainingBalance", sql);
            Assert.Contains("OvertimeAmount", sql);
            Assert.Contains("VersionCode = 'v1.0'", sql);
        }

        Assert.Contains("IsDeliverablesUnlocked = 1", procsSql);
        Assert.Contains("RemainingBalance = 0.00", procsSql);
    }
}
