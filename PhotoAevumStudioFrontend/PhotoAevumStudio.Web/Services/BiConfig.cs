namespace PhotoAevumStudio.Web.Services;

/// <summary>
/// Adaptable business intelligence configuration bound to appsettings "Bi" section.
/// Allows studio business wording, KPI definitions, and playbook labels to be customized
/// for different industry verticals without modifying component code.
/// </summary>
public sealed class BiConfig
{
    public const string SectionName = "Bi";

    public BiEntityLabels EntityLabels { get; set; } = new();
    public BiPageKpiConfig Kpis { get; set; } = new();
    public BiPlaybookConfig Playbook { get; set; } = new();
}

public sealed class BiEntityLabels
{
    public string Appointment { get; set; } = "Event Booking";
    public string Crew { get; set; } = "Photographer/Crew";
    public string Client { get; set; } = "Customer";
    public string Service { get; set; } = "Service/Package";
}

public sealed class BiPageKpiConfig
{
    public List<BiKpiDefinition> Sales { get; set; } =
    [
        new("sales-gross-revenue", "Total Revenue (Gross)", "TotalRevenue", "C", "positive"),
        new("sales-average-value", "Average Sale Value", "AverageSaleValue", "C", "neutral"),
        new("sales-outstanding-invoices", "Outstanding Invoices", "PendingAmount", "C", "warning"),
        new("sales-completed-transactions", "Completed Transactions", "CompletedCount", "N0", "neutral")
    ];

    public List<BiKpiDefinition> Analytics { get; set; } =
    [
        new("analytics-total-bookings", "Total Bookings", "TotalBookings", "N0", "primary"),
        new("analytics-completion-rate", "Completion Rate", "SessionCompletionRate", "P1", "positive"),
        new("analytics-logged-hours", "Logged Crew Hours", "LoggedCrewHours", "N1", "info"),
        new("analytics-equipment-holds", "Equipment Holds", "EquipmentMaintenanceHolds", "N0", "warning")
    ];

    public List<BiKpiDefinition> Reports { get; set; } =
    [
        new("report-gross-value", "Gross Booking Value", "GrossBookingValue", "C", "primary"),
        new("report-capacity", "Studio Capacity", "CapacityUtilizationPercent", "P1", "neutral"),
        new("report-efficiency", "Staff Efficiency", "TotalLoggedHours", "N1", "info"),
        new("report-retention", "Client Retention", "RetentionPercent", "P1", "positive")
    ];

    public string GetLabel(string page, string id, string fallback)
    {
        var list = page.ToLowerInvariant() switch
        {
            "sales" => Sales,
            "analytics" => Analytics,
            "reports" => Reports,
            _ => []
        };
        return list.FirstOrDefault(k => string.Equals(k.Id, id, StringComparison.OrdinalIgnoreCase))?.Label ?? fallback;
    }
}

public sealed record BiKpiDefinition(
    string Id,
    string Label,
    string SourceField,
    string Format,
    string Tone);

public sealed class BiPlaybookConfig
{
    public Dictionary<string, string> RuleLabels { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["follow-up-pending-deposits"] = "Follow up on pending deposits to secure booking dates",
        ["assign-backup-crew"] = "Assign backup crew for high-demand wedding dates",
        ["schedule-gear-maintenance"] = "Schedule preventive maintenance for heavily dispatched cameras",
        ["optimize-off-peak-slots"] = "Offer promotional incentives for weekday off-peak slots"
    };

    public Dictionary<string, string> EffortMapping { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Low Effort"] = "Low",
        ["Medium Effort"] = "Medium",
        ["High Effort"] = "High"
    };

    public Dictionary<int, string> PriorityMapping { get; set; } = new()
    {
        [3] = "High",
        [2] = "Medium",
        [1] = "Low"
    };
}
