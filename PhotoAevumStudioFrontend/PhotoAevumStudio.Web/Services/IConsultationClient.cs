using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public interface IConsultationClient
{
    Task<ApiResult<ConsultationNegotiationDto>> ScheduleConsultationAsync(CreateConsultationRequest request, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<ConsultationNegotiationDto>>> GetNegotiationsAsync(string? clerkEmployeeId = null, string? status = null, CancellationToken ct = default);
    Task<ApiResult<ConsultationNegotiationDto>> GetNegotiationByIdAsync(string id, CancellationToken ct = default);
    Task<ApiResult<bool>> UpdateNegotiationOutcomeAsync(string id, UpdateNegotiationOutcomeRequest request, CancellationToken ct = default);
    Task<ApiResult<EventAppointmentDto>> CreateEventAppointmentAsync(string negotiationId, CreateEventAppointmentFromNegotiationRequest request, CancellationToken ct = default);
    Task<ApiResult<EventAppointmentDto>> GetAppointmentByIdAsync(string id, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<EventAppointmentDto>>> GetCustomerAppointmentsAsync(string? status = null, CancellationToken ct = default);
    Task<ApiResult<EventAppointmentDto>> VerifyDownpaymentAsync(string appointmentId, string? invoiceId = null, CancellationToken ct = default);
    Task<ApiResult<bool>> RequestExtensionAsync(string appointmentId, CancellationToken ct = default);
    Task<ApiResult<EventAppointmentDto>> CompleteEventAsync(string appointmentId, CompleteEventRequest request, CancellationToken ct = default);
}
