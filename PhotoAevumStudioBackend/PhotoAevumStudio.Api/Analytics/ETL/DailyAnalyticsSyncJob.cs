namespace PhotoAevumStudio.Api.Analytics.ETL;

/// <summary>Hangfire background job that synchronises operational data into the analytics store.</summary>
public class DailyAnalyticsSyncJob
{
    public void ExecuteSync()
    {
        Serilog.Log.Information("Analytics sync started");
    }
}
