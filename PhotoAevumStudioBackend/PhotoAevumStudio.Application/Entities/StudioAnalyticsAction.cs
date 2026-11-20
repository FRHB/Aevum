namespace PhotoAevumStudio.Application.Entities;

/// <summary>
/// Persisted operational action applied by studio staff/admins from Prescriptive Analytics.
/// </summary>
public class StudioAnalyticsAction
{
    public int Id { get; set; }
    public string ActionKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string EffortTag { get; set; } = string.Empty;
    public string EstimatedImpact { get; set; } = string.Empty;
    public string? TriggerSignal { get; set; }
    public string? RelatedReferences { get; set; }
    public string? Notes { get; set; }
    public string AppliedByUserId { get; set; } = string.Empty;
    public string AppliedByName { get; set; } = string.Empty;
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Applied";
}
