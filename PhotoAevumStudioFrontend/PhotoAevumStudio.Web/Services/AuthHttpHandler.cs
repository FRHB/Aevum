using System.Net.Http.Headers;

namespace PhotoAevumStudio.Web.Services;

/// <summary>
/// DelegatingHandler that injects the stored JWT bearer token into every
/// outbound request made by the typed API HttpClients.
/// </summary>
public sealed class AuthHttpHandler : DelegatingHandler
{
    private readonly ITokenStore _tokenStore;

    public AuthHttpHandler(ITokenStore tokenStore)
    {
        _tokenStore = tokenStore;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _tokenStore.GetTokenAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }
}
