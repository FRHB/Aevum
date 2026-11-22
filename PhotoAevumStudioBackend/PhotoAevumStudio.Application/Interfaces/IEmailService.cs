namespace PhotoAevumStudio.Application.Interfaces;

public record WelcomeEmailDispatchResult(
    bool EmailSent,
    bool HandbookAttached,
    string? WarningMessage = null);

public record EmailAttachment(string FileName, byte[] Content, string ContentType = "application/pdf");

public interface IEmailService
{
    Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true);
    Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml, CancellationToken cancellationToken) =>
        SendEmailAsync(toEmail, subject, body, isHtml);
    Task<bool> TrySendEmailAsync(string toEmail, string subject, string body, bool isHtml = true, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
    Task<bool> TrySendEmailWithAttachmentsAsync(
        string toEmail,
        string subject,
        string body,
        IReadOnlyList<EmailAttachment> attachments,
        bool isHtml = true,
        CancellationToken cancellationToken = default) =>
        TrySendEmailAsync(toEmail, subject, body, isHtml, cancellationToken);
    Task SendAdminWelcomeEmailAsync(string toEmail, string adminName, string adminId, string temporaryPassword, string role, CancellationToken cancellationToken = default);
    Task SendEmployeeWelcomeEmailAsync(string toEmail, string employeeName, string employeeId, string initialPassword, CancellationToken cancellationToken = default, string assignedRole = "Employee");

    async Task<WelcomeEmailDispatchResult> TrySendAdminWelcomeEmailAsync(
        string toEmail,
        string adminName,
        string adminId,
        string temporaryPassword,
        string role,
        CancellationToken cancellationToken = default)
    {
        await SendAdminWelcomeEmailAsync(toEmail, adminName, adminId, temporaryPassword, role, cancellationToken);
        return new WelcomeEmailDispatchResult(EmailSent: true, HandbookAttached: false);
    }

    async Task<WelcomeEmailDispatchResult> TrySendEmployeeWelcomeEmailAsync(
        string toEmail,
        string employeeName,
        string employeeId,
        string initialPassword,
        CancellationToken cancellationToken = default,
        string assignedRole = "Employee")
    {
        await SendEmployeeWelcomeEmailAsync(toEmail, employeeName, employeeId, initialPassword, cancellationToken, assignedRole);
        return new WelcomeEmailDispatchResult(EmailSent: true, HandbookAttached: false);
    }
}
