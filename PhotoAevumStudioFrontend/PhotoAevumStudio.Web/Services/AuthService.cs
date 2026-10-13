using System.Net.Http.Json;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public sealed class AuthService : IAuthService
{
    private readonly HttpClient _http;
    private readonly AevumAuthenticationStateProvider _authStateProvider;

    // Use a named/typed client that does NOT go through AuthHttpHandler
    // (we don't have a token yet when logging in)
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
                return new AuthResult(false, Error: problem?.Detail ?? "Invalid credentials.");
            }

            var loginResponse = await response.Content
                .ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken);

            if (loginResponse is null)
                return new AuthResult(false, Error: "Unexpected server response.");

            await _authStateProvider.NotifyUserLoginAsync(loginResponse.Token);
            return new AuthResult(true, Token: loginResponse.Token);
        }
        catch (HttpRequestException ex)
        {
            return new AuthResult(false, Error: $"Cannot reach the API server: {ex.Message}");
        }
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        var result = await LoginAsync(username, password, CancellationToken.None);
        return result.Success;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
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

    // Local record so we don't need a NuGet dep for ProblemDetails deserialization
    private sealed record ProblemDetails(string? Detail);
}
