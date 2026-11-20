using System.Net.Http.Headers;

namespace PhotoAevumStudio.Web.Services;

/// <summary>
/// DelegatingHandler that injects the stored JWT bearer token into every
/// outbound request made by the typed API HttpClients.
/// Uses the active circuit's ITokenStore bound via AsyncLocal so IHttpClientFactory's
/// separate handler DI scope always resolves the calling circuit's JWT without static cross-user leakage.
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
        if (request.Headers.Authorization is null)
        {
            try
            {
                var ambientStore = ProtectedSessionTokenStore.GetAmbientStore();
                var token = ProtectedSessionTokenStore.GetAmbientToken()
                            ?? (ambientStore is not null ? await ambientStore.GetTokenAsync(cancellationToken) : null)
                            ?? await _tokenStore.GetTokenAsync(cancellationToken);

                if (!string.IsNullOrWhiteSpace(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
            }
            catch (OperationCanceledException)
            {
                // Silently bypass token injection if cancellation was requested
            }
            catch
            {
                // Proceed without token if lookup fails
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
