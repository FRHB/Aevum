using System.Text.Json.Serialization;

namespace PhotoAevumStudio.Application;

public class CreateConsultationRequest
{
    public string CustomerId { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public DateOnly ConsultationDate { get; set; }
    public TimeOnly ConsultationTime { get; set; }
    public string? InquiryNotes { get; set; }
    public string Platform { get; set; } = "GoogleMeet";
    public bool HasAcceptedDataPolicy { get; set; }
    public DateTime? DataPolicyAcceptedAt { get; set; }
}

public class ConsultationNegotiationDto
{
    public string NegotiationId { get; set; } = string.Empty;
    public string? EventAppointmentId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeEmail { get; set; } = string.Empty;
    public DateOnly ConsultationDate { get; set; }
    public TimeOnly ConsultationTime { get; set; }
    public string MeetingLink { get; set; } = string.Empty;
    public string InquiryNotes { get; set; } = string.Empty;
    public string NegotiationStatus { get; set; } = "Pending_Call";
    public DateTime CreatedAt { get; set; }
    public EventAppointmentDto? Appointment { get; set; }
}

public class UpdateNegotiationOutcomeRequest
{
    public string NegotiationStatus { get; set; } = "Deal_Won";
    public string? Notes { get; set; }
    public DateOnly? NewConsultationDate { get; set; }
    public TimeOnly? NewConsultationTime { get; set; }
}

public class CreateEventAppointmentFromNegotiationRequest
{
    public string NegotiationId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string? EventDescription { get; set; }
    public DateOnly EventDate { get; set; }
    public TimeOnly EventTime { get; set; }
    public string? EventLocation { get; set; }
    public List<string> ServiceIds { get; set; } = new();
    public List<string> EmployeeIds { get; set; } = new();
    public string TeamLeaderEmployeeId { get; set; } = string.Empty;
    public List<string> EquipmentIds { get; set; } = new();
}

public class UpdateAppointmentRosterRequest
{
    public List<string> EmployeeIds { get; set; } = new();
    public string TeamLeaderEmployeeId { get; set; } = string.Empty;
    public List<string> EquipmentIds { get; set; } = new();
}

public class EventAppointmentDto
{
    public string EventAppointmentId { get; set; } = string.Empty;
    public string? NegotiationId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string EventDescription { get; set; } = string.Empty;
    public DateOnly EventDate { get; set; }
    public TimeOnly EventTime { get; set; }
    public string EventStatus { get; set; } = "Awaiting_Downpayment";
    public decimal TotalAmount { get; set; }
    public decimal DownpaymentAmount { get; set; }
    public decimal FinalBalanceAmount { get; set; }
    public string DownpaymentStatus { get; set; } = "Pending";
    public DateTime? HoldExpiresAt { get; set; }
    public string? XenditInvoiceUrl { get; set; }
    public string? XenditInvoiceId { get; set; }
    public string? XenditExternalId { get; set; }
    public List<AssignedEmployeeDto> AssignedEmployees { get; set; } = new();
    public List<AssignedEquipmentDto> AssignedEquipment { get; set; } = new();
    public List<EventServiceLineDto> SelectedServices { get; set; } = new();
    public List<EventOvertimeDto> OvertimeCharges { get; set; } = new();
    public List<PaymentTransactionDto> Transactions { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class AssignedEmployeeDto
{
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public bool IsTeamLeader { get; set; }
}

public class AssignedEquipmentDto
{
    public string EquipmentId { get; set; } = string.Empty;
    public string EquipmentName { get; set; } = string.Empty;
    public string EquipmentType { get; set; } = string.Empty;
}

public class EventServiceLineDto
{
    public string ServicesId { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public decimal ServicePrice { get; set; }
}

public class EventOvertimeDto
{
    public int OvertimeId { get; set; }
    public string EventAppointmentId { get; set; } = string.Empty;
    public decimal ExtraHours { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal SurchargeAmount { get; set; }
    public string? Notes { get; set; }
    public string LoggedByEmployeeId { get; set; } = string.Empty;
    public string LoggedByEmployeeName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class CompleteEventRequest
{
    public string EventAppointmentId { get; set; } = string.Empty;
    public decimal ExtraHours { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal SurchargeAmount { get; set; }
    public string? OvertimeNotes { get; set; }
    public string PaymentMethod { get; set; } = "Cash_OnHand"; // 'Cash_OnHand', 'Xendit_QR', 'Xendit_Card'
    public string TeamLeaderEmployeeId { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class PaymentTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string EventAppointmentId { get; set; } = string.Empty;
    public string PaymentType { get; set; } = "Downpayment_25"; // 'Downpayment_25', 'Final_Settlement_75', 'Overtime_Surcharge'
    public string PaymentMethod { get; set; } = "Xendit_Card";
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ExternalReference { get; set; }
    public string? XenditInvoiceId { get; set; }
    public string? XenditPaymentId { get; set; }
    public string? XenditPaymentChannel { get; set; }
    public string? ReceivedByEmployeeId { get; set; }
    public string CustomerPaymentStatus { get; set; } = "Cleared";
    public string EmployeeRemittanceStatus { get; set; } = "NotApplicable";
    public string? AdminApproverId { get; set; }
    public DateTime? ApprovalTimestamp { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class XenditInvoiceCallbackPayload
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("external_id")]
    public string ExternalId { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("paid_amount")]
    public decimal? PaidAmount { get; set; }

    [JsonPropertyName("paid_at")]
    public DateTime? PaidAt { get; set; }

    [JsonPropertyName("payer_email")]
    public string? PayerEmail { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("payment_method")]
    public string? PaymentMethod { get; set; }

    [JsonPropertyName("payment_channel")]
    public string? PaymentChannel { get; set; }

    [JsonPropertyName("payment_destination")]
    public string? PaymentDestination { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "PHP";
}
