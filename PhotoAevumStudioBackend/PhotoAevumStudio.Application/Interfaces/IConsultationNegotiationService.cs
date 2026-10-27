namespace PhotoAevumStudio.Application.Interfaces;

public interface IConsultationNegotiationService
{
    Task<ConsultationNegotiationDto> CreateConsultationAsync(CreateConsultationRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConsultationNegotiationDto>> GetNegotiationsAsync(string? clerkEmployeeId = null, string? status = null, CancellationToken cancellationToken = default);
    Task<ConsultationNegotiationDto?> GetNegotiationByIdAsync(string negotiationId, CancellationToken cancellationToken = default);
    Task<bool> UpdateNegotiationOutcomeAsync(string negotiationId, UpdateNegotiationOutcomeRequest request, CancellationToken cancellationToken = default);
    Task<EventAppointmentDto> CreateEventAppointmentFromNegotiationAsync(CreateEventAppointmentFromNegotiationRequest request, CancellationToken cancellationToken = default);
    Task<EventAppointmentDto?> GetAppointmentByIdAsync(string appointmentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventAppointmentDto>> GetAppointmentsAsync(string? customerId = null, string? status = null, CancellationToken cancellationToken = default);
    Task<EventAppointmentDto> VerifyDownpaymentAsync(string appointmentId, string? invoiceId = null, CancellationToken cancellationToken = default);
    Task<bool> HandlePaymentExpiredAsync(string appointmentId, string? invoiceId = null, CancellationToken cancellationToken = default);
    Task<bool> RequestExtensionAsync(string appointmentId, CancellationToken cancellationToken = default);
    Task<EventAppointmentDto> CompleteEventSettlementAsync(CompleteEventRequest request, CancellationToken cancellationToken = default);
}
