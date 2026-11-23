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
    Task<EventAppointmentDto> VerifyDownpaymentAsync(string appointmentId, string? invoiceId, string? paymentChannel, CancellationToken cancellationToken = default);
    Task<bool> HandlePaymentExpiredAsync(string appointmentId, string? invoiceId = null, CancellationToken cancellationToken = default);
    Task<bool> RequestExtensionAsync(string appointmentId, CancellationToken cancellationToken = default);
    Task<EventAppointmentDto> CompleteEventSettlementAsync(CompleteEventRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeamLeaderEventDto>> GetTeamLeaderEventsAsync(string? employeeId = null, CancellationToken cancellationToken = default);
    Task<EventAppointmentDto> UpdateTeamLeaderEventStatusAsync(string appointmentId, UpdateTeamLeaderStatusRequest request, CancellationToken cancellationToken = default);
    Task<EventAppointmentDto> MarkEquipmentTakenAsync(string appointmentId, MarkEquipmentTakenRequest request, CancellationToken cancellationToken = default);
    Task<EventAppointmentDto> AddAdditionalEquipmentToEventAsync(string appointmentId, AddEventEquipmentRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LiveEventOverviewDto>> GetLiveEventsOverviewAsync(CancellationToken cancellationToken = default);
    Task<EventAppointmentDto> VerifyCashSettlementAsync(string appointmentId, string verifierId, string? verifierName = null, string? notes = null, CancellationToken cancellationToken = default);
    Task<bool> IsTeamLeaderForEventAsync(string appointmentId, string employeeId, CancellationToken cancellationToken = default);
    Task<int> AutoTriggerPostEventsAsync(CancellationToken cancellationToken = default);
    Task NotifyTeamLeaderFinalQrPaidAsync(string appointmentId, decimal amountPaid, string? paymentChannel, CancellationToken cancellationToken = default);
    Task<bool> UpdateAppointmentRosterAsync(string appointmentId, UpdateAppointmentRosterRequest request, CancellationToken cancellationToken = default);
    Task<EventAppointmentDto> VerifyCrewDutyAsync(string appointmentId, VerifyCrewDutyRequest request, CancellationToken cancellationToken = default);
    Task<EventAppointmentDto> RequestCrewReplacementAsync(string appointmentId, RequestCrewReplacementRequest request, CancellationToken cancellationToken = default);
    Task<EventAppointmentDto> FulfillCrewReplacementAsync(string appointmentId, int requestId, FulfillCrewReplacementRequest request, CancellationToken cancellationToken = default);
    Task<EventAppointmentDto> RejectCrewReplacementAsync(string appointmentId, int requestId, string reviewerId, string reviewerName, string? rejectionReason, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    Task<EventAppointmentDto> SubmitEquipmentRequestAsync(string appointmentId, SubmitEquipmentRequestDto request, string actorId, string actorName, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    Task<EventAppointmentDto> ReviewEquipmentRequestAsync(string appointmentId, int requestId, ReviewEquipmentRequestDto review, string reviewerId, string reviewerName, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    Task<EventAppointmentDto> UpdateBookingStatusFromPaymentAsync(string appointmentId, UpdatePaymentBookingStatusRequest request, CancellationToken cancellationToken = default);
    Task<CustomerAppointmentActionResultDto> CustomerCancelAppointmentAsync(string appointmentId, string customerId, string? reason = null, bool isPrivileged = false, CancellationToken cancellationToken = default);
    Task<CustomerAppointmentActionResultDto> CustomerRescheduleAppointmentAsync(string appointmentId, CustomerRescheduleAppointmentRequest request, string customerId, bool isPrivileged = false, CancellationToken cancellationToken = default);
}
