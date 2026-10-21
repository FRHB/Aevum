using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;

namespace PhotoAevumStudio.Web.Services;

public sealed class ProtectedSessionTokenStore : ITokenStore
{
    private readonly ProtectedSessionStorage _sessionStorage;
    private const string TokenKey = "jwt_token";

    // Cached per-circuit and in AsyncLocal so IHttpClientFactory handler scopes
    // and prerendering passes can access the active circuit's JWT without failing.
    private static readonly AsyncLocal<string?> AmbientToken = new();
    private string? _cachedToken;

    public ProtectedSessionTokenStore(ProtectedSessionStorage sessionStorage)
    {
        _sessionStorage = sessionStorage;
    }

    internal static string? GetAmbientToken() => AmbientToken.Value;

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_cachedToken))
        {
            AmbientToken.Value = _cachedToken;
            return _cachedToken;
        }

        if (!string.IsNullOrWhiteSpace(AmbientToken.Value))
        {
            _cachedToken = AmbientToken.Value;
            return _cachedToken;
        }

        try
        {
            var result = await _sessionStorage.GetAsync<string>(TokenKey);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Value))
            {
                _cachedToken = result.Value;
                AmbientToken.Value = result.Value;
                return result.Value;
            }

            return null;
        }
        catch (InvalidOperationException)
        {
            // Expected during Blazor Server static prerendering when JS interop is not yet available.
            return _cachedToken ?? AmbientToken.Value;
        }
        catch (JSDisconnectedException)
        {
            return _cachedToken ?? AmbientToken.Value;
        }
        catch (OperationCanceledException)
        {
            return _cachedToken ?? AmbientToken.Value;
        }
        catch
        {
            return _cachedToken ?? AmbientToken.Value;
        }
    }

    public async Task SetTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        _cachedToken = token;
        AmbientToken.Value = token;

        try
        {
            await _sessionStorage.SetAsync(TokenKey, token);
        }
        catch (InvalidOperationException)
        {
            // Prerendering guard: token remains cached in memory/AsyncLocal for the circuit.
        }
        catch (JSDisconnectedException)
        {
            // Circuit disconnected before storage write completed.
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _cachedToken = null;
        AmbientToken.Value = null;

        try
        {
            await _sessionStorage.DeleteAsync(TokenKey);
        }
        catch (InvalidOperationException)
        {
            // Prerendering guard: ignore JS interop unavailability during prerendering.
        }
        catch (JSDisconnectedException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }
}
