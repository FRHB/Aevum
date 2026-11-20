namespace PhotoAevumStudio.Application.DTOs;

public sealed record SalesQuery(
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Status,
    string? Search);

public sealed record SaleRowDto(
    string TransactionId,
    string InvoiceNo,
    DateTime Date,
    string ClientName,
    string ServiceName,
    string PaymentMethod,
    decimal Amount,
    string Status,
    string? AppointmentId);

public sealed record SalesSummaryDto(
    decimal TotalRevenue,
    decimal PriorRevenue,
    double? RevenueDeltaPercent,
    int CompletedCount,
    int TotalCount,
    decimal PendingAmount,
    int PendingCount,
    decimal AverageSaleValue);

public sealed record SalesResponseDto(
    SalesSummaryDto Summary,
    IReadOnlyList<SaleRowDto> Rows,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record RecordableAppointmentDto(
    string AppointmentId,
    string EventName,
    string ClientName,
    DateOnly EventDate,
    decimal TotalAmount,
    decimal DownpaymentAmount,
    decimal PaidAmount,
    decimal OutstandingAmount);

public sealed record RecordSaleRequest(
    string AppointmentId,
    decimal Amount,
    string PaymentMethod,
    string? PaymentType,
    string? Reference,
    string? Notes);

public sealed record RecordSaleResultDto(string TransactionId, string InvoiceNo, decimal Amount, string Status);

// ── Reports hub ──────────────────────────────────────────────────────────

public sealed record LabeledAmountDto(string Label, decimal Amount, int Count);

public sealed record HeatmapCellDto(int Weekday, int Slot, int Count);

public sealed record EquipmentUsageDto(string EquipmentName, string TypeName, int DispatchCount, int DaysUsed, double UtilizationPercent, string Status);

public sealed record ReportSummaryDto(
    int Year,
    int Month,
    string PeriodLabel,
    // Gross booking value
    decimal GrossBookingValue,
    decimal PriorGrossBookingValue,
    double? GrossDeltaPercent,
    int ScheduledSessions,
    // Capacity
    double CapacityUtilizationPercent,
    string CapacityBadge,
    string? PeakHours,
    // Staff
    double? StaffEfficiencyPercent,
    double TotalLoggedHours,
    int ActiveStaffLogged,
    // Retention
    double? RetentionPercent,
    int RepeatClients,
    int ClientsInPeriod,
    double? AverageRating,
    // Financial
    IReadOnlyList<LabeledAmountDto> RevenueByCategory,
    double? DepositCollectionPercent,
    int ClearedPayments,
    int UnreconciledPayments,
    // Appointments
    IReadOnlyList<LabeledAmountDto> BookingsByWeekday,
    IReadOnlyList<LabeledAmountDto> PopularServices,
    double? ConsultationConversionPercent,
    int ConsultationsTotal,
    int ConsultationsWon,
    int CompletedSessions,
    // Equipment
    IReadOnlyList<EquipmentUsageDto> EquipmentUsage,
    int OpenMaintenanceTickets,
    int EquipmentOnHold,
    int TotalDispatches,
    IReadOnlyList<HeatmapCellDto>? WeekdayTimeSlots = null);
