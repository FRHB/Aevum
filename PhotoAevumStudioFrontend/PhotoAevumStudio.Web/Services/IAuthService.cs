using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public interface IAuthService
{
    Task<bool> IsUserLoggedInAsync(CancellationToken cancellationToken = default);
    Task<bool> SendVerificationCodeAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> VerifyCodeAsync(string email, string code, CancellationToken cancellationToken = default);
    Task<bool> RegisterAsync(RegistrationRequest request, CancellationToken cancellationToken = default);
}