namespace PhotoAevumStudio.Application.DTOs;

public record StudioAnalyticsQuery(
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    string? StatusFilter = null,
    string? ServiceCategoryFilter = null,
    string Granularity = "month",
    string Scenario = "expected");

public record StudioAnalyticsOverviewDto(
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
    DescriptiveAnalyticsDto Descriptive,
    DiagnosticAnalyticsDto Diagnostic,
    PredictiveAnalyticsDto Predictive,
    PrescriptiveAnalyticsDto Prescriptive);

// ── Stage 1: Descriptive ("What happened") ───────────────────────────────────

public record DescriptiveAnalyticsDto(
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
    IReadOnlyList<AnalyticsTimeSeriesPointDto> VolumeSeries,
    IReadOnlyList<RankedServiceBreakdownDto> TopServices);

public record AnalyticsTimeSeriesPointDto(
    string Key,
    string Label,
    int TotalBookings,
    int CompletedCount,
    int ConfirmedCount,
    int PendingCount,
    int CancelledOrExpiredCount,
    double LoggedCrewHours);

public record RankedServiceBreakdownDto(
    string ServiceId,
    string Name,
    string Kind,
    string CategoryName,
    int BookingCount,
    int TotalQuantity,
    double SharePct);

// ── Stage 2: Diagnostic ("Why it happened") ────────────────────────────────────

public record DiagnosticAnalyticsDto(
    string CurrentPeriodLabel,
    string PriorPeriodLabel,
    int CurrentTotalBookings,
    int PriorTotalBookings,
    double? BookingsChangePct,
    IReadOnlyList<BookingStatusShiftDto> StatusShifts,
    IReadOnlyList<DiagnosticDriverDto> Drivers);

public record BookingStatusShiftDto(
    string StatusGroup,
    int CurrentCount,
    int PriorCount,
    int Delta,
    double ShareOfCurrentPct);

public record DiagnosticDriverDto(
    string DriverId,
    string Category,
    string Title,
    string Severity,
    string HeadlineMetric,
    string DeltaLabel,
    string Summary,
    string DetailedFinding,
    IReadOnlyList<DiagnosticAffectedItemDto> AffectedItems);

public record DiagnosticAffectedItemDto(
    string ReferenceId,
    string Title,
    string DateLabel,
    string Status,
    string ContextDetail);

// ── Stage 3: Predictive ("What will happen") ───────────────────────────────────

public record PredictiveAnalyticsDto(
    int HorizonMonths,
    string ActiveScenario,
    int HistoricalMonthsCount,
    int HistoricalBookingsCount,
    double? HistoricalMonthlyAverage,
    double? ModelErrorMae,
    string ConfidenceLevel,
    string ConfidenceNote,
    string MethodologyNote,
    IReadOnlyList<PredictiveScenarioDto> Scenarios);

public record PredictiveScenarioDto(
    string Key,
    string Label,
    double Multiplier,
    int ProjectedBookingsTotal,
    string PeakPeriodLabel,
    int PeakPeriodProjectedBookings,
    double EstimatedCrewHoursTotal,
    IReadOnlyList<ForecastSeriesPointDto> Series);

public record ForecastSeriesPointDto(
    string PeriodKey,
    string Label,
    bool IsHistorical,
    int? HistoricalBookings,
    int? ConfirmedOnBooksCount,
    double ProjectedBookings,
    double LowerBound,
    double UpperBound,
    double EstimatedCapacityPct);

// ── Stage 4: Prescriptive ("What should we do") ────────────────────────────────

public record PrescriptiveAnalyticsDto(
    IReadOnlyList<PrescriptiveRecommendationDto> Recommendations,
    IReadOnlyList<AppliedAnalyticsActionDto> AppliedHistory);

public record PrescriptiveRecommendationDto(
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

public record AppliedAnalyticsActionDto(
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

public record ApplyPrescriptiveActionsRequest(
    IReadOnlyList<ApplyPrescriptiveActionItem> Actions,
    string? Notes = null);

public record ApplyPrescriptiveActionItem(
    string ActionKey,
    string Title,
    string Category,
    string EffortTag,
    string EstimatedImpact,
    string? TriggerSignal = null,
    IReadOnlyList<string>? RelatedReferences = null);

public record ApplyPrescriptiveActionsResultDto(
    int AppliedCount,
    IReadOnlyList<AppliedAnalyticsActionDto> AppliedActions,
    string Message);
