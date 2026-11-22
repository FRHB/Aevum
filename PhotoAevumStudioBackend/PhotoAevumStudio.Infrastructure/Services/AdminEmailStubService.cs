using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Infrastructure.Services;

/// <summary>
/// Production-grade test/development stub for IEmailService.
/// Captures all dispatched emails in-memory and emits structured log output.
/// </summary>
public class AdminEmailStubService : IEmailService
{
    private readonly ILogger<AdminEmailStubService> _logger;
    private readonly IOnboardingDocumentService? _onboardingDocumentService;
    private readonly ConcurrentBag<DispatchedEmail> _sentEmails = new();

    public record DispatchedEmail(
        string ToEmail,
        string Subject,
        string Body,
        bool IsHtml,
        DateTime SentAtUtc,
        IReadOnlyList<EmailAttachment>? Attachments = null);

    public IReadOnlyCollection<DispatchedEmail> SentEmails => _sentEmails.ToArray();

    public AdminEmailStubService(
        ILogger<AdminEmailStubService> logger,
        IOnboardingDocumentService? onboardingDocumentService = null)
    {
        _logger = logger;
        _onboardingDocumentService = onboardingDocumentService;
    }

    public Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true) =>
        SendEmailAsync(toEmail, subject, body, isHtml, CancellationToken.None);

    public Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml, CancellationToken cancellationToken)
    {
        var record = new DispatchedEmail(toEmail, subject, body, isHtml, DateTime.UtcNow);
        _sentEmails.Add(record);

        _logger.LogInformation(
            "[STUB EMAIL SERVICE] Simulating email delivery to {ToEmail}. Subject: '{Subject}'. Payload length: {Length} chars.",
            toEmail, subject, body?.Length ?? 0);

        return Task.CompletedTask;
    }

    public Task<bool> TrySendEmailAsync(string toEmail, string subject, string body, bool isHtml = true, CancellationToken cancellationToken = default)
    {
        SendEmailAsync(toEmail, subject, body, isHtml, cancellationToken);
        return Task.FromResult(true);
    }

    public Task<bool> TrySendEmailWithAttachmentsAsync(
        string toEmail,
        string subject,
        string body,
        IReadOnlyList<EmailAttachment> attachments,
        bool isHtml = true,
        CancellationToken cancellationToken = default)
    {
        var record = new DispatchedEmail(toEmail, subject, body, isHtml, DateTime.UtcNow, attachments);
        _sentEmails.Add(record);

        _logger.LogInformation(
            "[STUB EMAIL SERVICE] Simulating email delivery to {ToEmail}. Subject: '{Subject}'. Attachments: {AttachmentCount}.",
            toEmail, subject, attachments?.Count ?? 0);

        return Task.FromResult(true);
    }

    public Task SendAdminWelcomeEmailAsync(
        string toEmail,
        string adminName,
        string adminId,
        string temporaryPassword,
        string role,
        CancellationToken cancellationToken = default) =>
        TrySendAdminWelcomeEmailAsync(toEmail, adminName, adminId, temporaryPassword, role, cancellationToken);

    public async Task<WelcomeEmailDispatchResult> TrySendAdminWelcomeEmailAsync(
        string toEmail,
        string adminName,
        string adminId,
        string temporaryPassword,
        string role,
        CancellationToken cancellationToken = default)
    {
        var handbook = _onboardingDocumentService != null
            ? await _onboardingDocumentService.GetEmployeeHandbookAsync(cancellationToken)
            : new OnboardingDocumentResult(false, null, null, null);

        var attachments = handbook.Attachment != null ? new[] { handbook.Attachment } : Array.Empty<EmailAttachment>();
        var subject = "Welcome to Photo Aevum Studio – Administrator Credentials";
        var body =
            $"Hello {adminName},\n\nYour administrator account has been provisioned.\nEmail / Username: {toEmail}\nRole: {role}\nAdmin ID: {adminId}\nTemporary Password: {temporaryPassword}\n\nPlease change your password on first login.\n\n" +
            "Onboarding Reminder: Please review the attached PHOTO AEVUM-STUDIO Employee Handbook & Operations Manual (approved by Studio Director Jay Anne D. Dingas) covering workplace policies, confidentiality/NDA rules, equipment accountability, and sign/return the Employee Acknowledgment section.";

        _logger.LogInformation(
            "[STUB EMAIL SERVICE] Welcome email dispatched to {ToEmail} for Admin {AdminId} ({Role}). HandbookAttached={HandbookAttached}.",
            toEmail, adminId, role, attachments.Length > 0);

        await TrySendEmailWithAttachmentsAsync(toEmail, subject, body, attachments, isHtml: false, cancellationToken);
        return new WelcomeEmailDispatchResult(EmailSent: true, HandbookAttached: attachments.Length > 0);
    }

    public Task SendEmployeeWelcomeEmailAsync(
        string toEmail,
        string employeeName,
        string employeeId,
        string initialPassword,
        CancellationToken cancellationToken = default,
        string assignedRole = "Employee") =>
        TrySendEmployeeWelcomeEmailAsync(toEmail, employeeName, employeeId, initialPassword, cancellationToken, assignedRole);

    public async Task<WelcomeEmailDispatchResult> TrySendEmployeeWelcomeEmailAsync(
        string toEmail,
        string employeeName,
        string employeeId,
        string initialPassword,
        CancellationToken cancellationToken = default,
        string assignedRole = "Employee")
    {
        var displayRole = string.IsNullOrWhiteSpace(assignedRole) ? "Employee" : assignedRole.Trim();
        var handbook = _onboardingDocumentService != null
            ? await _onboardingDocumentService.GetEmployeeHandbookAsync(cancellationToken)
            : new OnboardingDocumentResult(false, null, null, null);

        var attachments = handbook.Attachment != null ? new[] { handbook.Attachment } : Array.Empty<EmailAttachment>();
        var subject = "Welcome to Photo Aevum Studio – Staff Account Credentials";
        var body =
            $"Hello {employeeName},\n\nYour staff account has been provisioned.\nEmail / Username: {toEmail}\nAssigned Role: {displayRole}\nStaff ID: {employeeId}\nTemporary Password: {initialPassword}\n\nPlease change your password on first login.\n\n" +
            "Onboarding Reminder: Please review the attached PHOTO AEVUM-STUDIO Employee Handbook & Operations Manual (approved by Studio Director Jay Anne D. Dingas) covering workplace policies, confidentiality/NDA rules, equipment accountability, and sign/return the Employee Acknowledgment section.";

        _logger.LogInformation(
            "[STUB EMAIL SERVICE] Staff welcome email dispatched to {ToEmail} for Staff {EmployeeId} ({AssignedRole}). HandbookAttached={HandbookAttached}.",
            toEmail, employeeId, displayRole, attachments.Length > 0);

        await TrySendEmailWithAttachmentsAsync(toEmail, subject, body, attachments, isHtml: false, cancellationToken);
        return new WelcomeEmailDispatchResult(EmailSent: true, HandbookAttached: attachments.Length > 0);
    }
}
