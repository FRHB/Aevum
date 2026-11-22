using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;

namespace PhotoAevumStudio.Web.Services;

public sealed class ProtectedSessionTokenStore : ITokenStore
{
    private readonly ProtectedSessionStorage _sessionStorage;
    private readonly ProtectedLocalStorage? _localStorage;
    private readonly SemaphoreSlim _readLock = new(1, 1);
    private const string TokenKey = "jwt_token";

    // Scoped to the active Blazor Server circuit (never shared statically across users).
    private string? _circuitToken;

    // AsyncLocal flows across await boundaries into IHttpClientFactory's handler DI scopes.
    private static readonly AsyncLocal<ITokenStore?> AmbientStore = new();
    private static readonly AsyncLocal<string?> AmbientToken = new();

    public ProtectedSessionTokenStore(ProtectedSessionStorage sessionStorage, ProtectedLocalStorage? localStorage = null)
    {
        _sessionStorage = sessionStorage;
        _localStorage = localStorage;
    }

    internal static IDisposable BeginCircuitScope(ITokenStore store)
    {
        var previousStore = AmbientStore.Value;
        var previousToken = AmbientToken.Value;
        AmbientStore.Value = store;
        if (store is ProtectedSessionTokenStore sessionStore && !string.IsNullOrWhiteSpace(sessionStore._circuitToken))
        {
            AmbientToken.Value = sessionStore._circuitToken;
        }

        return new CircuitScope(previousStore, previousToken);
    }

    internal static ITokenStore? GetAmbientStore() => AmbientStore.Value;

    internal static string? GetAmbientToken()
    {
        if (AmbientStore.Value is ProtectedSessionTokenStore sessionStore &&
            !string.IsNullOrWhiteSpace(sessionStore._circuitToken))
        {
            return sessionStore._circuitToken;
        }

        return AmbientToken.Value;
    }

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_circuitToken))
        {
            AmbientToken.Value = _circuitToken;
            return _circuitToken;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        await _readLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_circuitToken))
            {
                AmbientToken.Value = _circuitToken;
                return _circuitToken;
            }

            var result = await _sessionStorage.GetAsync<string>(TokenKey);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Value))
            {
                _circuitToken = result.Value;
                AmbientToken.Value = result.Value;
                return result.Value;
            }

            // Cross-tab fallback: allow links opened in a new tab (e.g. payment checkout / email links)
            // within the same browser to inherit the active signed-in session.
            if (_localStorage is not null)
            {
                try
                {
                    var localResult = await _localStorage.GetAsync<string>(TokenKey);
                    if (localResult.Success && !string.IsNullOrWhiteSpace(localResult.Value))
                    {
                        if (IsTokenExpired(localResult.Value))
                        {
                            try { await _localStorage.DeleteAsync(TokenKey); } catch { }
                            return null;
                        }

                        _circuitToken = localResult.Value;
                        AmbientToken.Value = localResult.Value;
                        try { await _sessionStorage.SetAsync(TokenKey, localResult.Value); } catch { }
                        return localResult.Value;
                    }
                }
                catch (CryptographicException)
                {
                    try { await _localStorage.DeleteAsync(TokenKey); } catch { }
                }
                catch
                {
                    // Fall through cleanly if localStorage is unavailable during prerender/disconnect.
                }
            }

            return null;
        }
        catch (CryptographicException)
        {
            // Data protection keys changed (e.g. legacy deployment without persisted keys); clear unreadable value.
            _circuitToken = null;
            AmbientToken.Value = null;
            try
            {
                await _sessionStorage.DeleteAsync(TokenKey);
            }
            catch
            {
                // Best effort cleanup
            }
            return null;
        }
        catch (InvalidOperationException)
        {
            // Expected during Blazor Server static prerendering or when outside circuit JS interop.
            return null;
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            _readLock.Release();
        }
    }

    public async Task SetTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        _circuitToken = token;
        AmbientToken.Value = token;

        if (cancellationToken.IsCancellationRequested) return;

        try
        {
            await _sessionStorage.SetAsync(TokenKey, token);
        }
        catch (InvalidOperationException)
        {
            // Prerendering guard: token remains cached in this circuit instance.
        }
        catch (JSDisconnectedException)
        {
            // Circuit disconnected before storage write completed.
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }

        if (_localStorage is not null)
        {
            try
            {
                await _localStorage.SetAsync(TokenKey, token);
            }
            catch
            {
                // Best effort cross-tab persistence
            }
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _circuitToken = null;
        AmbientToken.Value = null;

        if (cancellationToken.IsCancellationRequested) return;

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
        catch (Exception)
        {
        }

        if (_localStorage is not null)
        {
            try
            {
                await _localStorage.DeleteAsync(TokenKey);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    private static bool IsTokenExpired(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
                return false;
            var jwt = handler.ReadJwtToken(token);
            return jwt.ValidTo < DateTime.UtcNow;
        }
        catch
        {
            return false;
        }
    }

    private sealed class CircuitScope : IDisposable
    {
        private readonly ITokenStore? _previousStore;
        private readonly string? _previousToken;

        public CircuitScope(ITokenStore? previousStore, string? previousToken)
        {
            _previousStore = previousStore;
            _previousToken = previousToken;
        }

        public void Dispose()
        {
            AmbientStore.Value = _previousStore;
            AmbientToken.Value = _previousToken;
        }
    }
}
