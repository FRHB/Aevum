using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using PhotoAevumStudio.Web.Services;

namespace PhotoAevumStudio.Web.Infrastructure;

public sealed class ProtectedSessionTokenStore : ITokenStore
{
    private readonly ProtectedSessionStorage _sessionStorage;

    public ProtectedSessionTokenStore(ProtectedSessionStorage sessionStorage)
    {
        _sessionStorage = sessionStorage;
    }

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _sessionStorage.GetAsync<string>("jwt_token");
            return result.Success ? result.Value : null;
        }
        catch
        {
            // Handles pre-rendering payload errors gracefully
            return null;
        }
    }

    public async Task SetTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        await _sessionStorage.SetAsync("jwt_token", token);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _sessionStorage.DeleteAsync("jwt_token");
    }
}