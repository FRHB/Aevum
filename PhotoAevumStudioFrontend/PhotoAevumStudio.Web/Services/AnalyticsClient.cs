using System.Net;
using System.Net.Http.Json;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public sealed class AnalyticsClient(
    IHttpClientFactory httpClientFactory,
    ITokenStore tokenStore,
    ILogger<AnalyticsClient> logger) : IAnalyticsClient
{
    private readonly HttpClient _http = httpClientFactory.CreateClient("AevumApi.Authorized");

    public Task<ApiResult<StudioAnalyticsOverviewModel>> GetOverviewAsync(
        DateOnly startDate,
        DateOnly endDate,
        string? statusFilter = null,
        string? categoryFilter = null,
        string granularity = "month",
        string scenario = "expected",
        CancellationToken ct = default)
    {
        var url = BuildQueryUrl("api/analytics/overview", startDate, endDate, statusFilter, categoryFilter, granularity, scenario);
        return SendJsonAsync<StudioAnalyticsOverviewModel>(() => _http.GetAsync(url, ct), ct);
    }

    public Task<ApiResult<ApplyPrescriptiveActionsResultModel>> ApplyPrescriptiveActionsAsync(
        ApplyPrescriptiveActionsRequestModel request,
        CancellationToken ct = default) =>
        SendJsonAsync<ApplyPrescriptiveActionsResultModel>(
            () => _http.PostAsJsonAsync("api/analytics/prescriptive/apply", request, ct),
            ct);

    public async Task<ApiResult<string>> ExportCsvAsync(
        DateOnly startDate,
        DateOnly endDate,
        string? statusFilter = null,
        string? categoryFilter = null,
        string granularity = "month",
        string scenario = "expected",
        CancellationToken ct = default)
    {
        try
        {
            await tokenStore.GetTokenAsync(ct);
            var url = BuildQueryUrl("api/analytics/export", startDate, endDate, statusFilter, categoryFilter, granularity, scenario);
            using var response = await _http.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
            {
                var csv = await response.Content.ReadAsStringAsync(ct);
                return ApiResult<string>.Ok(csv);
            }
            return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? ApiResult<string>.Fail(ApiErrorKind.Unauthorized, "Session expired or unauthorized.")
                : ApiResult<string>.Fail(ApiErrorKind.Server, "Failed to export analytics CSV.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ApiResult<string>.Fail(ApiErrorKind.Network, "Export canceled.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Analytics CSV export failed.");
            return ApiResult<string>.Fail(ApiErrorKind.Network, "Connection failed while exporting CSV.");
        }
    }

    private static string BuildQueryUrl(
        string baseRoute,
        DateOnly startDate,
        DateOnly endDate,
        string? statusFilter,
        string? categoryFilter,
        string granularity,
        string scenario)
    {
        var q = new List<string>
        {
            $"startDate={startDate:yyyy-MM-dd}",
            $"endDate={endDate:yyyy-MM-dd}",
            $"granularity={Uri.EscapeDataString(string.IsNullOrWhiteSpace(granularity) ? "month" : granularity)}",
            $"scenario={Uri.EscapeDataString(string.IsNullOrWhiteSpace(scenario) ? "expected" : scenario)}"
        };

        if (!string.IsNullOrWhiteSpace(statusFilter) && !string.Equals(statusFilter, "All", StringComparison.OrdinalIgnoreCase))
            q.Add($"status={Uri.EscapeDataString(statusFilter)}");

        if (!string.IsNullOrWhiteSpace(categoryFilter) && !string.Equals(categoryFilter, "All", StringComparison.OrdinalIgnoreCase))
            q.Add($"category={Uri.EscapeDataString(categoryFilter)}");

        return $"{baseRoute}?{string.Join("&", q)}";
    }

    private async Task<ApiResult<T>> SendJsonAsync<T>(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        try
        {
            await tokenStore.GetTokenAsync(ct);
            using var response = await send();

            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(ct);
                return value is null
                    ? ApiResult<T>.Fail(ApiErrorKind.Server, "The analytics service returned an empty response.")
                    : ApiResult<T>.Ok(value);
            }

            return response.StatusCode switch
            {
                HttpStatusCode.BadRequest => ApiResult<T>.Fail(ApiErrorKind.Validation, "Invalid analytics filter parameters."),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ApiResult<T>.Fail(ApiErrorKind.Unauthorized, "Session expired or insufficient role access."),
                _ => ApiResult<T>.Fail(ApiErrorKind.Server, $"Analytics API returned {(int)response.StatusCode}.")
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ApiResult<T>.Fail(ApiErrorKind.Network, "The request was canceled.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Analytics API request failed.");
            return ApiResult<T>.Fail(ApiErrorKind.Network, "Could not reach the Studio Analytics API.");
        }
    }
}
