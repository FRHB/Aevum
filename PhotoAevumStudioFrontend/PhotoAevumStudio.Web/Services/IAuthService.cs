using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string userId, string password, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    Task<bool> IsUserLoggedInAsync(CancellationToken cancellationToken = default);
    Task<bool> RegisterAsync(RegistrationRequest request, CancellationToken cancellationToken = default);
}