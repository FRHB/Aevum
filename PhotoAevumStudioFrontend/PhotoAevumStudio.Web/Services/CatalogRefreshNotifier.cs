namespace PhotoAevumStudio.Web.Services;

/// <summary>
/// Application-wide singleton event bus that broadcasts immediate catalog / featured / portfolio refresh
/// signals across all open Blazor Server circuits whenever featured flags or highlights change.
/// </summary>
public sealed class CatalogRefreshNotifier
{
    public event Func<string, Task>? OnCatalogUpdatedAsync;

    public async Task NotifyCatalogUpdatedAsync(string category = "All")
    {
        UserService.InvalidateServiceCatalogCache();
        ServicesApiClient.InvalidateFeaturedAndPortfolioCaches();

        var handlers = OnCatalogUpdatedAsync;
        if (handlers == null) return;

        foreach (var d in handlers.GetInvocationList())
        {
            if (d is Func<string, Task> fn)
            {
                try { await fn(category); } catch { }
            }
        }
    }
}
