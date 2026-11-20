using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public interface IAnalyticsClient
{
    Task<ApiResult<StudioAnalyticsOverviewModel>> GetOverviewAsync(
        DateOnly startDate,
        DateOnly endDate,
        string? statusFilter = null,
        string? categoryFilter = null,
        string granularity = "month",
        string scenario = "expected",
        CancellationToken ct = default);

    Task<ApiResult<ApplyPrescriptiveActionsResultModel>> ApplyPrescriptiveActionsAsync(
        ApplyPrescriptiveActionsRequestModel request,
        CancellationToken ct = default);

    Task<ApiResult<string>> ExportCsvAsync(
        DateOnly startDate,
        DateOnly endDate,
        string? statusFilter = null,
        string? categoryFilter = null,
        string granularity = "month",
        string scenario = "expected",
        CancellationToken ct = default);
}
