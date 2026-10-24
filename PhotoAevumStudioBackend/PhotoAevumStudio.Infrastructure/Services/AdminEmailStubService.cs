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
    private readonly ConcurrentBag<DispatchedEmail> _sentEmails = new();

    public record DispatchedEmail(
        string ToEmail,
        string Subject,
        string Body,
        bool IsHtml,
        DateTime SentAtUtc);

    public IReadOnlyCollection<DispatchedEmail> SentEmails => _sentEmails.ToArray();

    public AdminEmailStubService(ILogger<AdminEmailStubService> logger)
    {
        _logger = logger;
    }

    public Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true)
    {
        var record = new DispatchedEmail(toEmail, subject, body, isHtml, DateTime.UtcNow);
        _sentEmails.Add(record);

        _logger.LogInformation(
            "[STUB EMAIL SERVICE] Simulating email delivery to {ToEmail}. Subject: '{Subject}'. Payload length: {Length} chars.",
            toEmail, subject, body?.Length ?? 0);

        return Task.CompletedTask;
    }

    public Task SendAdminWelcomeEmailAsync(
        string toEmail,
        string adminName,
        string adminId,
        string temporaryPassword,
        string role,
        CancellationToken cancellationToken = default)
    {
        var subject = "Welcome to Photo Aevum Studio – Administrator Credentials";
        var body = $"Hello {adminName},\n\nYour administrator account has been provisioned.\nRole: {role}\nAdmin ID: {adminId}\nTemporary Password: {temporaryPassword}\n\nPlease change your password on first login.";

        _logger.LogInformation(
            "[STUB EMAIL SERVICE] Welcome email dispatched to {ToEmail} for Admin {AdminId} ({Role}). Temporary password: {TempPassword}",
            toEmail, adminId, role, temporaryPassword);

        return SendEmailAsync(toEmail, subject, body, isHtml: false);
    }
}
