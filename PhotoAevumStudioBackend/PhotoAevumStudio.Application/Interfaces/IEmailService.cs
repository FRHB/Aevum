namespace PhotoAevumStudio.Application.Interfaces;

public interface IEmailService
{
    Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true);
    Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml, CancellationToken cancellationToken) =>
        SendEmailAsync(toEmail, subject, body, isHtml);
    Task<bool> TrySendEmailAsync(string toEmail, string subject, string body, bool isHtml = true, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
    Task SendAdminWelcomeEmailAsync(string toEmail, string adminName, string adminId, string temporaryPassword, string role, CancellationToken cancellationToken = default);
    Task SendEmployeeWelcomeEmailAsync(string toEmail, string employeeName, string employeeId, string initialPassword, CancellationToken cancellationToken = default, string assignedRole = "Employee");
}
