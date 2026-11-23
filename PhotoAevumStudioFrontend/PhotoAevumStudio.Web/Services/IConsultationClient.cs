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
    Task<ApiResult<QrPaymentInfoDto>> GetQrPaymentAsync(string appointmentId, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<EventAppointmentDto>>> GetCustomerAppointmentsAsync(string? status = null, CancellationToken ct = default);
    Task<ApiResult<EventAppointmentDto>> VerifyDownpaymentAsync(string appointmentId, string? invoiceId = null, CancellationToken ct = default);
    Task<ApiResult<EventAppointmentDto>> SimulateWebhookPaymentAsync(string appointmentId, string? paymentMethod = null, CancellationToken ct = default);
    Task<ApiResult<SimulateQrResultDto>> SimulateQrPaymentAsync(string externalId, CancellationToken ct = default);
    Task<ApiResult<bool>> RequestExtensionAsync(string appointmentId, CancellationToken ct = default);
    Task<ApiResult<EventAppointmentDto>> CompleteEventAsync(string appointmentId, CompleteEventRequest request, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<TeamLeaderEventDto>>> GetTeamLeaderEventsAsync(string? employeeId = null, CancellationToken ct = default);
    Task<ApiResult<EventAppointmentDto>> UpdateTeamLeaderEventStatusAsync(string appointmentId, UpdateTeamLeaderStatusRequest request, CancellationToken ct = default);
    Task<ApiResult<EventAppointmentDto>> MarkEquipmentTakenAsync(string appointmentId, MarkEquipmentTakenRequest request, CancellationToken ct = default);
    Task<ApiResult<EventAppointmentDto>> AddEventEquipmentAsync(string appointmentId, AddEventEquipmentRequest request, CancellationToken ct = default);
    Task<ApiResult<EventAppointmentDto>> VerifyCrewDutyAsync(string appointmentId, VerifyCrewDutyRequest request, CancellationToken ct = default) =>
        Task.FromResult(new ApiResult<EventAppointmentDto>(null, new ApiError(default, "Not implemented.")));
    Task<ApiResult<EventAppointmentDto>> RequestCrewReplacementAsync(string appointmentId, RequestCrewReplacementRequest request, CancellationToken ct = default) =>
        Task.FromResult(new ApiResult<EventAppointmentDto>(null, new ApiError(default, "Not implemented.")));
    Task<ApiResult<EventAppointmentDto>> FulfillCrewReplacementAsync(string appointmentId, int requestId, FulfillCrewReplacementRequest request, CancellationToken ct = default) =>
        Task.FromResult(new ApiResult<EventAppointmentDto>(null, new ApiError(default, "Not implemented.")));
    Task<ApiResult<EventAppointmentDto>> RejectCrewReplacementAsync(string appointmentId, int requestId, RejectCrewReplacementRequest request, CancellationToken ct = default) =>
        Task.FromResult(new ApiResult<EventAppointmentDto>(null, new ApiError(default, "Not implemented.")));
    Task<ApiResult<EventAppointmentDto>> SubmitEquipmentRequestAsync(string appointmentId, SubmitEquipmentRequestDto request, CancellationToken ct = default) =>
        Task.FromResult(new ApiResult<EventAppointmentDto>(null, new ApiError(default, "Not implemented.")));
    Task<ApiResult<EventAppointmentDto>> ReviewEquipmentRequestAsync(string appointmentId, int requestId, ReviewEquipmentRequestDto review, CancellationToken ct = default) =>
        Task.FromResult(new ApiResult<EventAppointmentDto>(null, new ApiError(default, "Not implemented.")));
    Task<ApiResult<IReadOnlyList<LiveEventOverviewDto>>> GetLiveEventsOverviewAsync(CancellationToken ct = default);
    Task<ApiResult<EventAppointmentDto>> VerifyCashSettlementAsync(string appointmentId, AdminApproveRemittanceModel? request = null, CancellationToken ct = default);
    Task<ApiResult<PaymentBreakdownModel>> CalculateReservationFeeAsync(decimal total, CancellationToken ct = default);
    Task<ApiResult<PaymentSummaryModel>> SubmitCustomerPaymentAsync(CustomerSubmitPaymentModel request, CancellationToken ct = default);
    Task<ApiResult<PaymentSummaryModel>> LogEmployeeCashAsync(EmployeeLogCashModel request, CancellationToken ct = default);
    Task<ApiResult<PaymentSummaryModel>> ApproveRemittanceAsync(int paymentId, AdminApproveRemittanceModel? request = null, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<PaymentSummaryModel>>> GetPendingRemittancesAsync(CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<PaymentSummaryModel>>> GetEmployeeLedgerAsync(string? employeeId = null, CancellationToken ct = default);
    Task<ApiResult<bool>> UpdateAppointmentRosterAsync(string appointmentId, UpdateAppointmentRosterRequest request, CancellationToken ct = default);
    Task<ApiResult<EventAppointmentDto>> UpdateBookingStatusAsync(string appointmentId, UpdatePaymentBookingStatusRequest request, CancellationToken ct = default);
    Task<ApiResult<CustomerAppointmentActionResultDto>> CustomerCancelAppointmentAsync(string appointmentId, CustomerCancelAppointmentRequest? request = null, CancellationToken ct = default);
    Task<ApiResult<CustomerAppointmentActionResultDto>> CustomerRescheduleAppointmentAsync(string appointmentId, CustomerRescheduleAppointmentRequest request, CancellationToken ct = default);

    Task<ApiResult<TermsVersionDto>> GetActiveTermsAsync(CancellationToken ct = default) =>
        Task.FromResult(new ApiResult<TermsVersionDto>(null, new ApiError(default, "Not implemented.")));
    Task<ApiResult<BookingDocumentsBundleDto>> GetBookingDocumentsAsync(string appointmentId, CancellationToken ct = default) =>
        Task.FromResult(new ApiResult<BookingDocumentsBundleDto>(null, new ApiError(default, "Not implemented.")));
    Task<ApiResult<BookingContractDto>> ConfigureBookingContractAsync(string appointmentId, ConfigureBookingContractRequest request, CancellationToken ct = default) =>
        Task.FromResult(new ApiResult<BookingContractDto>(null, new ApiError(default, "Not implemented.")));
    Task<ApiResult<BookingContractDto>> AcceptContractAsync(string appointmentId, AcceptContractRequest request, CancellationToken ct = default) =>
        Task.FromResult(new ApiResult<BookingContractDto>(null, new ApiError(default, "Not implemented.")));
    Task<ApiResult<byte[]>> DownloadContractPdfAsync(string appointmentId, CancellationToken ct = default) =>
        Task.FromResult(new ApiResult<byte[]>(null, new ApiError(default, "Not implemented.")));
    Task<ApiResult<byte[]>> DownloadReceiptPdfAsync(string appointmentId, string receiptType, CancellationToken ct = default) =>
        Task.FromResult(new ApiResult<byte[]>(null, new ApiError(default, "Not implemented.")));
    Task<ApiResult<byte[]>> DownloadTermsPdfAsync(CancellationToken ct = default) =>
        Task.FromResult(new ApiResult<byte[]>(null, new ApiError(default, "Not implemented.")));
}
