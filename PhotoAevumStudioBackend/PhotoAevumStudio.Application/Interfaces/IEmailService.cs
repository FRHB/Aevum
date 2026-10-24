namespace PhotoAevumStudio.Application.Interfaces;

public interface IEmailService
{
    Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true);
    Task SendAdminWelcomeEmailAsync(string toEmail, string adminName, string adminId, string temporaryPassword, string role, CancellationToken cancellationToken = default);
}
