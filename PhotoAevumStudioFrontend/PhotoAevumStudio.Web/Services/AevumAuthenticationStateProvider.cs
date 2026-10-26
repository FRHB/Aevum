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

            var identity = CreateIdentity(jwt);
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
        var identity = CreateIdentity(jwt);
        var user = new ClaimsPrincipal(identity);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
    }

    private static ClaimsIdentity CreateIdentity(JwtSecurityToken jwt)
    {
        var claims = new List<Claim>();
        string? studioId = null;
        string? spec = null;
        string? roleVal = null;

        foreach (var c in jwt.Claims)
        {
            claims.Add(c);
            if (c.Type == "role" || c.Type == ClaimTypes.Role)
            {
                roleVal = c.Value;
                claims.Add(new Claim(ClaimTypes.Role, c.Value));
                claims.Add(new Claim("role", c.Value));
            }
            if (c.Type == "name" || c.Type == ClaimTypes.Name || c.Type == "unique_name")
            {
                claims.Add(new Claim(ClaimTypes.Name, c.Value));
            }
            if (c.Type == "studio_id" || c.Type == ClaimTypes.NameIdentifier || c.Type == JwtRegisteredClaimNames.Sub)
            {
                studioId ??= c.Value;
            }
            if (c.Type == "specialization" || c.Type == "position")
            {
                spec = c.Value;
            }
        }

        // If studioId starts with CLK- or role/spec contains clerk, ensure Office Clerk role claim is present
        bool isClerk = (!string.IsNullOrEmpty(studioId) && studioId.StartsWith("CLK-", StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrEmpty(roleVal) && roleVal.Contains("clerk", StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrEmpty(spec) && spec.Contains("clerk", StringComparison.OrdinalIgnoreCase));

        if (isClerk)
        {
            claims.Add(new Claim(ClaimTypes.Role, AppRoles.OfficeClerk));
            claims.Add(new Claim("role", AppRoles.OfficeClerk));
        }

        // If studioId starts with SADM- or role contains system admin, ensure System Admin and Admin role claims are present
        bool isSysAdmin = (!string.IsNullOrEmpty(studioId) && studioId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrEmpty(roleVal) && (roleVal.Equals("SystemAdmin", StringComparison.OrdinalIgnoreCase) || roleVal.Equals("System Admin", StringComparison.OrdinalIgnoreCase) || roleVal.Equals("Super Admin", StringComparison.OrdinalIgnoreCase)));

        if (isSysAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "System Admin"));
            claims.Add(new Claim("role", "System Admin"));
            claims.Add(new Claim(ClaimTypes.Role, "SystemAdmin"));
            claims.Add(new Claim("role", "SystemAdmin"));
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            claims.Add(new Claim("role", "Admin"));
        }

        return new ClaimsIdentity(claims.DistinctBy(c => (c.Type, c.Value)), "jwt", ClaimTypes.Name, ClaimTypes.Role);
    }

    /// <summary>Call on logout to clear the token and notify Blazor.</summary>
    public async Task NotifyUserLogoutAsync()
    {
        await _tokenStore.ClearAsync();
        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));
    }
}
