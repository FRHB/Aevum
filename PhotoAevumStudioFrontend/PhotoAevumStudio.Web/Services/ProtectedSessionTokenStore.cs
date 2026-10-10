using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace PhotoAevumStudio.Web.Services;

public sealed class ProtectedSessionTokenStore : ITokenStore
{
    private readonly ProtectedSessionStorage _sessionStorage;
    private const string TokenKey = "jwt_token";

    public ProtectedSessionTokenStore(ProtectedSessionStorage sessionStorage)
    {
        _sessionStorage = sessionStorage;
    }

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        try 
        {
            var result = await _sessionStorage.GetAsync<string>(TokenKey);
            return result.Success ? result.Value : null;
        }
        catch 
        {
            return null;
        }
    }

    public async Task SetTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        await _sessionStorage.SetAsync(TokenKey, token);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _sessionStorage.DeleteAsync(TokenKey);
    }
}
