using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.BackgroundServices;

/// <summary>
/// Periodically sweeps expired Customer Dropbox records (30 days after event completion),
/// deletes the corresponding ZIP archives from Cloudflare R2, and removes their database rows.
/// </summary>
public class CustomerDropboxExpiryService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CustomerDropboxExpiryService> _logger;

    public CustomerDropboxExpiryService(
        IServiceScopeFactory scopes,
        ILogger<CustomerDropboxExpiryService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var dropboxService = scope.ServiceProvider.GetRequiredService<ICustomerDropboxService>();
                var purged = await dropboxService.PurgeExpiredDropboxesAsync(stoppingToken);
                if (purged.PurgedCount > 0)
                {
                    _logger.LogInformation("CustomerDropboxExpiryService purged {Count} expired Dropbox record(s).", purged.PurgedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Customer Dropbox 30-day expiry sweep failed.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
