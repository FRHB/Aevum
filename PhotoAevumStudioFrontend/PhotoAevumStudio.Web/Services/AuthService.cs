using System.Net.Http.Json;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public sealed class AuthService : IAuthService
{
    private readonly HttpClient _http;
    private readonly AevumAuthenticationStateProvider _authStateProvider;

    public AuthService(IHttpClientFactory httpClientFactory,
                       AevumAuthenticationStateProvider authStateProvider)
    {
        _http = httpClientFactory.CreateClient("AevumApi.Anonymous");
        _authStateProvider = authStateProvider;
    }

    public async Task<AuthResult> LoginAsync(
        string userId, string password,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                "api/auth/login",
                new LoginRequest(userId, password),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var problem = await response.Content
                    .ReadFromJsonAsync<ProblemDetails>(cancellationToken: cancellationToken);
                return new AuthResult(false, Error: problem?.GetErrorMessage("Invalid credentials.") ?? "Invalid credentials.");
            }

            var loginResponse = await response.Content
                .ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken);

            if (loginResponse is null)
                return new AuthResult(false, Error: "Unexpected server response.");

            // First-time login: JWT is not issued; redirect to password change screen
            if (loginResponse.RequiresPasswordChange || loginResponse.IsFirstLogin)
            {
                return new AuthResult(
                    true,
                    RequiresPasswordChange: true,
                    UserId: loginResponse.UserId ?? userId,
                    Role: loginResponse.Role,
                    IsFirstLogin: true);
            }

            if (string.IsNullOrWhiteSpace(loginResponse.Token))
                return new AuthResult(false, Error: "Token missing from server response.");

            await _authStateProvider.NotifyUserLoginAsync(loginResponse.Token);
            return new AuthResult(true, Token: loginResponse.Token, UserId: loginResponse.UserId, Role: loginResponse.Role);
        }
        catch (HttpRequestException ex)
        {
            return new AuthResult(false, Error: $"Cannot reach the API server: {ex.Message}");
        }
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        var result = await LoginAsync(username, password, CancellationToken.None);
        return result.Success && !result.RequiresPasswordChange;
    }

    public async Task<AuthResult> ChangeInitialPasswordAsync(
        string userId, string currentPassword, string newPassword, string confirmPassword,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                "api/auth/initial-password-change",
                new InitialPasswordChangeRequest(userId, currentPassword, newPassword, confirmPassword),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var problem = await response.Content
                    .ReadFromJsonAsync<ProblemDetails>(cancellationToken: cancellationToken);
                return new AuthResult(false, Error: problem?.GetErrorMessage("Password change failed.") ?? "Password change failed.");
            }

            return new AuthResult(true);
        }
        catch (HttpRequestException ex)
        {
            return new AuthResult(false, Error: $"Cannot reach the API server: {ex.Message}");
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _http.PostAsync("api/auth/logout", null, cancellationToken);
        }
        catch
        {
            // Ignore API connectivity issues so the local user session is always guaranteed to be cleared
        }
        await _authStateProvider.NotifyUserLogoutAsync();
    }

    public async Task<bool> IsUserLoggedInAsync(CancellationToken cancellationToken = default)
    {
        var state = await _authStateProvider.GetAuthenticationStateAsync();
        return state.User.Identity?.IsAuthenticated ?? false;
    }

    public async Task<bool> RegisterCustomerAsync(object request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/auth/register-customer", request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SendVerificationCodeAsync(string email, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/auth/send-verification", new { Email = email }, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> VerifyCodeAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/auth/verify-code", new { Email = email, VerificationCode = code }, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/auth/forgot-password", new { Email = email }, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ResetPasswordAsync(string email, string verificationCode, string newPassword, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/auth/reset-password", new { Email = email, VerificationCode = verificationCode, NewPassword = newPassword }, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RegisterAsync(
        RegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                "api/auth/register",
                request,
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private sealed record ProblemDetails(
        string? Title = null,
        string? Detail = null,
        Dictionary<string, string[]>? Errors = null)
    {
        public string GetErrorMessage(string fallback)
        {
            if (Errors is { Count: > 0 })
            {
                var firstError = Errors.Values.SelectMany(v => v).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
                if (!string.IsNullOrWhiteSpace(firstError))
                    return firstError;
            }

            if (!string.IsNullOrWhiteSpace(Detail))
                return Detail;

            if (!string.IsNullOrWhiteSpace(Title) && Title != "One or more validation errors occurred.")
                return Title;

            return fallback;
        }
    }
}
