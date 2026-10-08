using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace PhotoAevumStudio.Web.Services;

/// <summary>
/// Reads the stored JWT and produces an AuthenticationState from its claims.
/// Blazor Server calls GetAuthenticationStateAsync on every render that needs auth.
/// </summary>
public sealed class AevumAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly ITokenStore _tokenStore;
    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    public AevumAuthenticationStateProvider(ITokenStore tokenStore)
    {
        _tokenStore = tokenStore;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await _tokenStore.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
            return Anonymous;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
                return Anonymous;

            var jwt = handler.ReadJwtToken(token);

            // Reject expired tokens before building identity
            if (jwt.ValidTo < DateTime.UtcNow)
            {
                await _tokenStore.ClearAsync();
                return Anonymous;
            }

            var identity = new ClaimsIdentity(jwt.Claims, "jwt");
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch
        {
            return Anonymous;
        }
    }

    /// <summary>Call after a successful login to notify Blazor components.</summary>
    public async Task NotifyUserLoginAsync(string token)
    {
        await _tokenStore.SetTokenAsync(token);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        var identity = new ClaimsIdentity(jwt.Claims, "jwt");
        var user = new ClaimsPrincipal(identity);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
    }

    /// <summary>Call on logout to clear the token and notify Blazor.</summary>
    public async Task NotifyUserLogoutAsync()
    {
        await _tokenStore.ClearAsync();
        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));
    }
}
