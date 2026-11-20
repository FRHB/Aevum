using PhotoAevumStudio.Application.DTOs;

namespace PhotoAevumStudio.Application.Interfaces;

public interface ISalesReportService
{
    Task<SalesResponseDto> GetSalesAsync(SalesQuery query, CancellationToken ct = default);
    Task<string> ExportSalesCsvAsync(SalesQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<RecordableAppointmentDto>> GetRecordableAppointmentsAsync(CancellationToken ct = default);
    Task<RecordSaleResultDto> RecordSaleAsync(RecordSaleRequest request, string userId, string userEmail, string userName, CancellationToken ct = default);
    Task<ReportSummaryDto> GetReportAsync(int year, int month, CancellationToken ct = default);
}
