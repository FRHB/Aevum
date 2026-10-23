namespace PhotoAevumStudio.Application.Interfaces;

public record AuthUser(
    string Id,
    string Role,
    string FullName,
    string Email,
    string StoredPassword,
    bool IsFirstLogin);

public record AuthValidationResult(
    bool Success,
    bool RequiresPasswordChange = false,
    AuthUser? User = null,
    string? ErrorMessage = null,
    bool IsFirstLogin = false);

public interface ISqlAuthService
{
    Task<AuthValidationResult> AuthenticateAsync(string userIdOrEmail, string password, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> ChangeInitialPasswordAsync(string userIdOrEmail, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message, string? CustomerId)> RegisterCustomerAsync(string firstName, string lastName, string? middleName, string email, string? phone, string password, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> RegisterEmployeeAsync(string employeeId, string firstName, string lastName, string? middleName, string email, string password, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> RegisterAdminAsync(string adminId, string role, string firstName, string lastName, string? middleName, string? email, string password, string requestingUserId, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> ResetPasswordAsync(string email, string newPassword, CancellationToken cancellationToken = default);
    Task LogActivityAsync(string actionType, string entityName, string? entityId, string? details, string changedByType, string? actorId, CancellationToken cancellationToken = default);
}
