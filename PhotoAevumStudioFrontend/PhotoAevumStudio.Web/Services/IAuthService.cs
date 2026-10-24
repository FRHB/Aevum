using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string userId, string password, CancellationToken cancellationToken = default);
    Task<bool> LoginAsync(string username, string password);
    Task<AuthResult> ChangeInitialPasswordAsync(string userId, string currentPassword, string newPassword, string confirmPassword, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    Task<bool> IsUserLoggedInAsync(CancellationToken cancellationToken = default);
    Task<bool> RegisterAsync(RegistrationRequest request, CancellationToken cancellationToken = default);
    Task<bool> RegisterCustomerAsync(object request, CancellationToken cancellationToken = default);
    Task<bool> SendVerificationCodeAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> VerifyCodeAsync(string email, string code, CancellationToken cancellationToken = default);
    Task<bool> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> ResetPasswordAsync(string email, string code, string newPassword, CancellationToken cancellationToken = default);
}
