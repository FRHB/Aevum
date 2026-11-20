namespace PhotoAevumStudio.Web.Models;

public sealed record StudioAnalyticsOverviewModel(
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly PriorStartDate,
    DateOnly PriorEndDate,
    string Granularity,
    string? AppliedStatusFilter,
    string? AppliedServiceCategoryFilter,
    IReadOnlyList<string> AvailableStatuses,
    IReadOnlyList<string> AvailableServiceCategories,
    DateTime GeneratedAtUtc,
    bool HasAnyData,
    bool IsLowData,
    DescriptiveAnalyticsModel Descriptive,
    DiagnosticAnalyticsModel Diagnostic,
    PredictiveAnalyticsModel Predictive,
    PrescriptiveAnalyticsModel Prescriptive);

public sealed record DescriptiveAnalyticsModel(
    int TotalBookings,
    int EventAppointmentsCount,
    int ConsultationBookingsCount,
    double? BookingsDeltaPct,
    double SessionCompletionRate,
    double? CompletionRateDeltaPts,
    int CompletedDeliveredCount,
    int ConfirmedActiveCount,
    int PendingHoldCount,
    int CancelledExpiredCount,
    double LoggedCrewHours,
    double? CrewHoursDeltaPct,
    int TimesheetEntriesCount,
    int ActiveCrewLoggedCount,
    int DayOffShiftsCount,
    int EquipmentMaintenanceHolds,
    int TotalEquipmentCount,
    int EquipmentInMaintenanceCount,
    int EquipmentNeedsReplacementCount,
    int ActiveMaintenanceTicketsInPeriod,
    int EquipmentAssignmentsInPeriod,
    IReadOnlyList<AnalyticsTimeSeriesPointModel> VolumeSeries,
    IReadOnlyList<RankedServiceBreakdownModel> TopServices);

public sealed record AnalyticsTimeSeriesPointModel(
    string Key,
    string Label,
    int TotalBookings,
    int CompletedCount,
    int ConfirmedCount,
    int PendingCount,
    int CancelledOrExpiredCount,
    double LoggedCrewHours);

public sealed record RankedServiceBreakdownModel(
    string ServiceId,
    string Name,
    string Kind,
    string CategoryName,
    int BookingCount,
    int TotalQuantity,
    double SharePct);

public sealed record DiagnosticAnalyticsModel(
    string CurrentPeriodLabel,
    string PriorPeriodLabel,
    int CurrentTotalBookings,
    int PriorTotalBookings,
    double? BookingsChangePct,
    IReadOnlyList<BookingStatusShiftModel> StatusShifts,
    IReadOnlyList<DiagnosticDriverModel> Drivers);

public sealed record BookingStatusShiftModel(
    string StatusGroup,
    int CurrentCount,
    int PriorCount,
    int Delta,
    double ShareOfCurrentPct);

public sealed record DiagnosticDriverModel(
    string DriverId,
    string Category,
    string Title,
    string Severity,
    string HeadlineMetric,
    string DeltaLabel,
    string Summary,
    string DetailedFinding,
    IReadOnlyList<DiagnosticAffectedItemModel> AffectedItems);

public sealed record DiagnosticAffectedItemModel(
    string ReferenceId,
    string Title,
    string DateLabel,
    string Status,
    string ContextDetail);

public sealed record PredictiveAnalyticsModel(
    int HorizonMonths,
    string ActiveScenario,
    int HistoricalMonthsCount,
    int HistoricalBookingsCount,
    double? HistoricalMonthlyAverage,
    double? ModelErrorMae,
    string ConfidenceLevel,
    string ConfidenceNote,
    string MethodologyNote,
    IReadOnlyList<PredictiveScenarioModel> Scenarios);

public sealed record PredictiveScenarioModel(
    string Key,
    string Label,
    double Multiplier,
    int ProjectedBookingsTotal,
    string PeakPeriodLabel,
    int PeakPeriodProjectedBookings,
    double EstimatedCrewHoursTotal,
    IReadOnlyList<ForecastSeriesPointModel> Series);

public sealed record ForecastSeriesPointModel(
    string PeriodKey,
    string Label,
    bool IsHistorical,
    int? HistoricalBookings,
    int? ConfirmedOnBooksCount,
    double ProjectedBookings,
    double LowerBound,
    double UpperBound,
    double EstimatedCapacityPct);

public sealed record PrescriptiveAnalyticsModel(
    IReadOnlyList<PrescriptiveRecommendationModel> Recommendations,
    IReadOnlyList<AppliedAnalyticsActionModel> AppliedHistory);

public sealed record PrescriptiveRecommendationModel(
    string ActionKey,
    string Title,
    string Description,
    string Category,
    string EffortTag,
    string EstimatedImpact,
    string TriggerSignal,
    int Priority,
    bool IsSignalTriggered,
    bool IsAlreadyApplied,
    DateTime? LastAppliedAtUtc,
    string? LastAppliedBy,
    IReadOnlyList<string> RelatedReferences);

public sealed record AppliedAnalyticsActionModel(
    int Id,
    string ActionKey,
    string Title,
    string Category,
    string EffortTag,
    string EstimatedImpact,
    string? TriggerSignal,
    string? RelatedReferences,
    string? Notes,
    string AppliedByUserId,
    string AppliedByName,
    DateTime AppliedAtUtc,
    string Status);

public sealed record ApplyPrescriptiveActionsRequestModel(
    IReadOnlyList<ApplyPrescriptiveActionItemModel> Actions,
    string? Notes = null);

public sealed record ApplyPrescriptiveActionItemModel(
    string ActionKey,
    string Title,
    string Category,
    string EffortTag,
    string EstimatedImpact,
    string? TriggerSignal = null,
    IReadOnlyList<string>? RelatedReferences = null);

public sealed record ApplyPrescriptiveActionsResultModel(
    int AppliedCount,
    IReadOnlyList<AppliedAnalyticsActionModel> AppliedActions,
    string Message);
