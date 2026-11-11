using Hangfire;
using PhotoAevumStudio.Api.Analytics.ETL;

namespace PhotoAevumStudio.Api.Analytics.Prescriptive;

/// <summary>Prescriptive analytics skeleton: turns a risk score into follow-up background work.</summary>
public class WorkflowActionEngine
{
    private const float HighRiskThreshold = 0.8f;

    // Rules evaluation (workflows loaded into the engine) will be wired in a later iteration.
    private readonly RulesEngine.RulesEngine _rulesEngine;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<WorkflowActionEngine> _logger;

    public WorkflowActionEngine(
        RulesEngine.RulesEngine rulesEngine,
        IBackgroundJobClient jobs,
        ILogger<WorkflowActionEngine> logger)
    {
        _rulesEngine = rulesEngine;
        _jobs = jobs;
        _logger = logger;
    }

    public void EvaluateBookingRisk(int bookingId, float riskScore)
    {
        _logger.LogInformation("Evaluating booking risk for booking {BookingId} with score {RiskScore}", bookingId, riskScore);

        if (riskScore > HighRiskThreshold)
        {
            // Placeholder follow-up job until dedicated intervention jobs exist.
            var jobId = _jobs.Enqueue<DailyAnalyticsSyncJob>(j => j.ExecuteSync());
            _logger.LogWarning("High risk booking {BookingId} ({RiskScore}); scheduled Hangfire job {JobId}", bookingId, riskScore, jobId);
        }
    }
}
