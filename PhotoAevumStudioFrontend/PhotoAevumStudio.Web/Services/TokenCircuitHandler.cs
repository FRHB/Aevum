using Microsoft.AspNetCore.Components.Server.Circuits;

namespace PhotoAevumStudio.Web.Services;

/// <summary>
/// Binds the current Blazor Server circuit's ITokenStore to the async execution flow
/// so IHttpClientFactory handlers (which execute in separate DI scopes) always resolve
/// the active circuit's JWT without sharing static state across users.
/// </summary>
public sealed class TokenCircuitHandler : CircuitHandler
{
    private readonly ITokenStore _tokenStore;

    public TokenCircuitHandler(ITokenStore tokenStore)
    {
        _tokenStore = tokenStore;
    }

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next)
    {
        return async context =>
        {
            using var scope = ProtectedSessionTokenStore.BeginCircuitScope(_tokenStore);
            await next(context);
        };
    }
}
