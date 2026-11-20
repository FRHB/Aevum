using PhotoAevumStudio.Application.DTOs;

namespace PhotoAevumStudio.Application.Interfaces;

public interface IStudioAnalyticsService
{
    Task EnsureSchemaAsync(CancellationToken ct = default);

    Task<StudioAnalyticsOverviewDto> GetOverviewAsync(
        StudioAnalyticsQuery query,
        CancellationToken ct = default);

    Task<ApplyPrescriptiveActionsResultDto> ApplyPrescriptiveActionsAsync(
        ApplyPrescriptiveActionsRequest request,
        string userId,
        string userEmail,
        string userName,
        CancellationToken ct = default);

    Task<string> ExportCsvAsync(
        StudioAnalyticsQuery query,
        CancellationToken ct = default);
}
