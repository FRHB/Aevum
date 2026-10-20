namespace PhotoAevumStudio.Web.Services;

/// <summary>
/// Stores the JWT token for the current Blazor Server circuit (user session).
/// Scoped lifetime = one instance per SignalR circuit.
/// </summary>
public interface ITokenStore
{
    Task<string?> GetTokenAsync(CancellationToken cancellationToken = default);
    Task SetTokenAsync(string token, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
