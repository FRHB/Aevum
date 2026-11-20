using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public sealed record SaleRowModel(string TransactionId, string InvoiceNo, DateTime Date, string ClientName, string ServiceName, string PaymentMethod, decimal Amount, string Status, string? AppointmentId);
public sealed record SalesSummaryModel(decimal TotalRevenue, decimal PriorRevenue, double? RevenueDeltaPercent, int CompletedCount, int TotalCount, decimal PendingAmount, int PendingCount, decimal AverageSaleValue);
public sealed record SalesResponseModel(SalesSummaryModel Summary, List<SaleRowModel> Rows, DateOnly StartDate, DateOnly EndDate);
public sealed record RecordableBookingModel(string AppointmentId, string EventName, string ClientName, DateOnly EventDate, decimal TotalAmount, decimal DownpaymentAmount, decimal PaidAmount, decimal OutstandingAmount);
public sealed record RecordSaleRequestModel
{
    public RecordSaleRequestModel() { }
    public RecordSaleRequestModel(string appointmentId, decimal amount, string paymentMethod, string? paymentType, string? reference, string? notes)
    {
        AppointmentId = appointmentId;
        Amount = amount;
        PaymentMethod = paymentMethod;
        PaymentType = paymentType;
        Reference = reference;
        Notes = notes;
    }

    public string AppointmentId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? PaymentType { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}
public sealed record RecordSaleResultModel(string TransactionId, string InvoiceNo, decimal Amount, string Status);

public sealed record LabeledAmountModel(string Label, decimal Amount, int Count);
public sealed record EquipmentUsageModel(string EquipmentName, string TypeName, int DispatchCount, int DaysUsed, double UtilizationPercent, string Status);
public sealed record ReportSummaryModel(
    int Year, int Month, string PeriodLabel,
    decimal GrossBookingValue, decimal PriorGrossBookingValue, double? GrossDeltaPercent, int ScheduledSessions,
    double CapacityUtilizationPercent, string CapacityBadge, string? PeakHours,
    double? StaffEfficiencyPercent, double TotalLoggedHours, int ActiveStaffLogged,
    double? RetentionPercent, int RepeatClients, int ClientsInPeriod, double? AverageRating,
    List<LabeledAmountModel> RevenueByCategory, double? DepositCollectionPercent, int ClearedPayments, int UnreconciledPayments,
    List<LabeledAmountModel> BookingsByWeekday, List<LabeledAmountModel> PopularServices,
    double? ConsultationConversionPercent, int ConsultationsTotal, int ConsultationsWon, int CompletedSessions,
    List<EquipmentUsageModel> EquipmentUsage, int OpenMaintenanceTickets, int EquipmentOnHold, int TotalDispatches);

public interface ISalesReportsClient
{
    Task<ApiResult<SalesResponseModel>> GetSalesAsync(DateOnly start, DateOnly end, string? status, string? search, CancellationToken ct = default);
    Task<ApiResult<string>> ExportSalesCsvAsync(DateOnly start, DateOnly end, string? status, string? search, CancellationToken ct = default);
    Task<ApiResult<List<RecordableBookingModel>>> GetRecordableBookingsAsync(CancellationToken ct = default);
    Task<ApiResult<RecordSaleResultModel>> RecordSaleAsync(RecordSaleRequestModel request, CancellationToken ct = default);
    Task<ApiResult<ReportSummaryModel>> GetReportAsync(int year, int month, CancellationToken ct = default);
}

public sealed class SalesReportsClient(
    IHttpClientFactory httpClientFactory,
    ITokenStore tokenStore,
    ILogger<SalesReportsClient> logger) : ISalesReportsClient
{
    private readonly HttpClient _http = httpClientFactory.CreateClient("AevumApi.Authorized");

    private static string Qs(DateOnly s, DateOnly e, string? status, string? search)
    {
        var q = $"startDate={s:yyyy-MM-dd}&endDate={e:yyyy-MM-dd}";
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase)) q += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrWhiteSpace(search)) q += $"&search={Uri.EscapeDataString(search.Trim())}";
        return q;
    }

    public Task<ApiResult<SalesResponseModel>> GetSalesAsync(DateOnly start, DateOnly end, string? status, string? search, CancellationToken ct = default) =>
        SendAsync<SalesResponseModel>(() => _http.GetAsync($"api/sales?{Qs(start, end, status, search)}", ct), ct);

    public async Task<ApiResult<string>> ExportSalesCsvAsync(DateOnly start, DateOnly end, string? status, string? search, CancellationToken ct = default)
    {
        try
        {
            await tokenStore.GetTokenAsync(ct);
            using var resp = await _http.GetAsync($"api/sales/export?{Qs(start, end, status, search)}", ct);
            return resp.IsSuccessStatusCode
                ? ApiResult<string>.Ok(await resp.Content.ReadAsStringAsync(ct))
                : ApiResult<string>.Fail(ApiErrorKind.Server, $"Sales export failed ({(int)resp.StatusCode}).");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sales export failed.");
            return ApiResult<string>.Fail(ApiErrorKind.Network, "Connection failed while exporting CSV.");
        }
    }

    public Task<ApiResult<List<RecordableBookingModel>>> GetRecordableBookingsAsync(CancellationToken ct = default) =>
        SendAsync<List<RecordableBookingModel>>(() => _http.GetAsync("api/sales/recordable-bookings", ct), ct);

    public Task<ApiResult<RecordSaleResultModel>> RecordSaleAsync(RecordSaleRequestModel request, CancellationToken ct = default) =>
        SendAsync<RecordSaleResultModel>(() => _http.PostAsJsonAsync("api/sales", request, ct), ct);

    public Task<ApiResult<ReportSummaryModel>> GetReportAsync(int year, int month, CancellationToken ct = default) =>
        SendAsync<ReportSummaryModel>(() => _http.GetAsync($"api/reports/summary?year={year}&month={month}", ct), ct);

    private async Task<ApiResult<T>> SendAsync<T>(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        try
        {
            await tokenStore.GetTokenAsync(ct);
            using var resp = await send();
            if (resp.IsSuccessStatusCode)
            {
                var v = await resp.Content.ReadFromJsonAsync<T>(ct);
                return v is null ? ApiResult<T>.Fail(ApiErrorKind.Server, "The service returned an empty response.") : ApiResult<T>.Ok(v);
            }
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return ApiResult<T>.Fail(ApiErrorKind.Unauthorized, "Session expired or insufficient role access.");
            if (resp.StatusCode == HttpStatusCode.BadRequest)
            {
                var msg = "Invalid request.";
                try { using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)); if (doc.RootElement.TryGetProperty("message", out var m)) msg = m.GetString() ?? msg; } catch { }
                return ApiResult<T>.Fail(ApiErrorKind.Validation, msg);
            }
            return ApiResult<T>.Fail(ApiErrorKind.Server, $"The service returned {(int)resp.StatusCode}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ApiResult<T>.Fail(ApiErrorKind.Network, "The request was canceled.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sales/Reports API request failed.");
            return ApiResult<T>.Fail(ApiErrorKind.Network, "Could not reach the studio API.");
        }
    }
}
