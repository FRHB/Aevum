using Microsoft.Data.SqlClient;

namespace PhotoAevumStudio.Infrastructure.Data;

/// <summary>
/// Centralizes SQL Server connection pooling parameters, transient retry logic providers,
/// and stale pooled connection recovery for remote shared SQL Server instances.
/// </summary>
public static class SqlResilienceHelper
{
    private static readonly int[] TransientErrorNumbers =
    [
        -2,    // Timeout expired
        -1,    // Connection broken
        0,     // Pre-login handshake / transport provider forcible close
        2,     // Network error
        19,    // Physical connection is not usable (Session Provider)
        20,    // Encryption / instance not supported transient
        53,    // Network path not found
        64,    // Specified network name is no longer available
        121,   // Semaphore timeout period has expired
        233,   // No process is on the other end of the pipe
        1205,  // Deadlock victim
        10053, // Software caused connection abort
        10054, // Connection forcibly closed by remote host
        10060, // Connection timed out
        40197, // Service error processing request
        40501, // Service is busy
        40613, // Database unavailable
        49918, // Not enough resources
        49919,
        49920
    ];

    public static readonly SqlRetryLogicBaseProvider RetryProvider =
        SqlConfigurableRetryFactory.CreateExponentialRetryProvider(new SqlRetryLogicOption
        {
            NumberOfTries = 4,
            DeltaTime = TimeSpan.FromSeconds(1),
            MaxTimeInterval = TimeSpan.FromSeconds(8),
            TransientErrors = TransientErrorNumbers
        });

    /// <summary>
    /// Normalizes a SQL Server connection string with resilient pool lifetimes,
    /// connection retry settings, and command timeouts suited for remote shared hosts.
    /// </summary>
    public static string NormalizeConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return string.Empty;

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);

            if (builder.ConnectTimeout < 60)
                builder.ConnectTimeout = 60;

            if (builder.CommandTimeout < 60)
                builder.CommandTimeout = 60;

            if (builder.ConnectRetryCount < 3)
                builder.ConnectRetryCount = 3;

            if (builder.ConnectRetryInterval < 5)
                builder.ConnectRetryInterval = 5;

            if (builder.MaxPoolSize > 30)
                builder.MaxPoolSize = 30;

            // Recycle pooled connections after 45 seconds so the shared host's ~60s idle TCP reaper
            // never leaves dead sockets in the pool (which otherwise surface as Error 19 or pre-login resets).
            if (builder.LoadBalanceTimeout <= 0 || builder.LoadBalanceTimeout > 45)
                builder.LoadBalanceTimeout = 45;

            return builder.ConnectionString;
        }
        catch
        {
            return connectionString;
        }
    }

    /// <summary>
    /// Creates a <see cref="SqlConnection"/> with normalized pool settings and transient retry provider attached.
    /// </summary>
    public static SqlConnection CreateConnection(string? connectionString)
    {
        var normalized = NormalizeConnectionString(connectionString);
        return new SqlConnection(normalized)
        {
            RetryLogicProvider = RetryProvider
        };
    }

    /// <summary>
    /// Opens a <see cref="SqlConnection"/> with automatic pool eviction and retry if a stale
    /// physical socket or pre-login handshake reset occurs.
    /// </summary>
    public static async Task<SqlConnection> OpenConnectionAsync(string? connectionString, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeConnectionString(connectionString);
        const int maxAttempts = 3;

        for (int attempt = 1; ; attempt++)
        {
            var conn = new SqlConnection(normalized)
            {
                RetryLogicProvider = RetryProvider
            };

            try
            {
                await conn.OpenAsync(cancellationToken);
                return conn;
            }
            catch (SqlException ex) when (attempt < maxAttempts && IsTransientConnectionFailure(ex) && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    SqlConnection.ClearPool(conn);
                }
                catch
                {
                    // Ignore pool clear errors
                }

                await conn.DisposeAsync();
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
            catch
            {
                await conn.DisposeAsync();
                throw;
            }
        }
    }

    public static bool IsTransientConnectionFailure(SqlException ex)
    {
        foreach (SqlError err in ex.Errors)
        {
            if (TransientErrorNumbers.Contains(err.Number))
                return true;
        }

        var msg = ex.Message;
        return msg.Contains("pre-login handshake", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Physical connection is not usable", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("forcibly closed by the remote host", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("transport-level error", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Timeout Expired", StringComparison.OrdinalIgnoreCase);
    }
}
