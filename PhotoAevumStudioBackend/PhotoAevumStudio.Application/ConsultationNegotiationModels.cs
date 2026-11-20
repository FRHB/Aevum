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
    public string? NewEmployeeId { get; set; }
    public string? NewEmployeeName { get; set; }
}

public class EventServiceLineInput
{
    public string ServicesId { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public int? DurationHours { get; set; }
    public string? RateType { get; set; }
}

public class CreateEventAppointmentFromNegotiationRequest
{
    public string NegotiationId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string? EventDescription { get; set; }
    public DateOnly EventDate { get; set; }
    public TimeOnly EventTime { get; set; }
    public TimeOnly? EventEndTime { get; set; }
    public int? DurationMinutes { get; set; }
    public string? EventLocation { get; set; }
    public string? PaymentMethod { get; set; }
    public List<string> ServiceIds { get; set; } = new();
    public List<EventServiceLineInput> ServiceSelections { get; set; } = new();
    public List<string> EmployeeIds { get; set; } = new();
    public string TeamLeaderEmployeeId { get; set; } = string.Empty;
    public List<string> EquipmentIds { get; set; } = new();
}

public class UpdateAppointmentRosterRequest
{
    public List<string> EmployeeIds { get; set; } = new();
    public string TeamLeaderEmployeeId { get; set; } = string.Empty;
    public List<string> EquipmentIds { get; set; } = new();
    public bool MarkRemovedAsSuddenAbsence { get; set; }
    public List<string>? SuddenAbsenceEmployeeIds { get; set; }
    public string? SuddenAbsenceReason { get; set; }
    public string? ActorId { get; set; }
    public string? ActorRole { get; set; }
    public string? ActorName { get; set; }
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
    public TimeOnly? EventEndTime { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public int PrepBufferMinutes { get; set; } = 120;
    public int PostBufferMinutes { get; set; } = 60;
    public string? EventLocation { get; set; }
    public string EventStatus { get; set; } = "Awaiting_Downpayment";
    public decimal TotalAmount { get; set; }
    public decimal DownpaymentAmount { get; set; }
    public decimal FinalBalanceAmount { get; set; }
    public string DownpaymentStatus { get; set; } = "Pending";
    public DateTime? HoldExpiresAt { get; set; }
    public DateTime? LockExpiresAt { get; set; }
    public string? QrString { get; set; }
    public string? XenditInvoiceUrl { get; set; }
    public string? XenditInvoiceId { get; set; }
    public string? XenditExternalId { get; set; }
    public string? FinalPaymentMethod { get; set; }
    public string? FinalPaymentStatus { get; set; }
    public string? FinalQrString { get; set; }
    public string? FinalPaymentReferenceId { get; set; }
    public DateTime? FinalPaymentPaidAt { get; set; }
    public List<AssignedEmployeeDto> AssignedEmployees { get; set; } = new();
    public List<AssignedEquipmentDto> AssignedEquipment { get; set; } = new();
    public List<EventCrewReplacementRequestDto> CrewReplacementRequests { get; set; } = new();
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
    public bool IsVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? VerifiedByEmployeeId { get; set; }
    public string DutyStatus { get; set; } = "Assigned";
}

public class EventCrewReplacementRequestDto
{
    public int RequestId { get; set; }
    public string EventAppointmentId { get; set; } = string.Empty;
    public string? AbsentEmployeeId { get; set; }
    public string? AbsentEmployeeName { get; set; }
    public string? RequestedByEmployeeId { get; set; }
    public string? RequestedByName { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ReplacementEmployeeId { get; set; }
    public string? ReplacementEmployeeName { get; set; }
    public string? FulfilledBy { get; set; }
    public DateTime? FulfilledAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class VerifyCrewDutyRequest
{
    public List<string>? EmployeeIds { get; set; }
    public bool IsVerified { get; set; } = true;
    public string DutyStatus { get; set; } = "On Duty";
    public string? EmployeeId { get; set; }
}

public class RequestCrewReplacementRequest
{
    public string? AbsentEmployeeId { get; set; }
    public string? Reason { get; set; }
    public string? RequestedByEmployeeId { get; set; }
    public string? RequestedByName { get; set; }
}

public class FulfillCrewReplacementRequest
{
    public string ReplacementEmployeeId { get; set; } = string.Empty;
    public bool PromoteToTeamLeader { get; set; }
    public bool MarkAbsentAsSuddenLeave { get; set; } = true;
    public string? Reason { get; set; }
    public string? ActorId { get; set; }
    public string? ActorRole { get; set; }
    public string? ActorName { get; set; }
}

public class AssignedEquipmentDto
{
    public int EventEquipmentId { get; set; }
    public string EquipmentId { get; set; } = string.Empty;
    public string EquipmentName { get; set; } = string.Empty;
    public string EquipmentType { get; set; } = string.Empty;
    public string EquipmentStatus { get; set; } = "Available";
    public bool IsTaken { get; set; }
    public DateTime? TakenAt { get; set; }
    public string? TakenByEmployeeId { get; set; }
    public bool IsAdditionalRequest { get; set; }
    public string? RequestedNotes { get; set; }
}

public class EventServiceLineDto
{
    public string ServicesId { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public decimal ServicePrice { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public string RateType { get; set; } = "Per Session";
    public int? DurationHours { get; set; }
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
    public string PaymentMethod { get; set; } = "Cash_OnHand"; // 'Cash_OnHand', 'Xendit_QR'
    public string TeamLeaderEmployeeId { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class PaymentTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string EventAppointmentId { get; set; } = string.Empty;
    public string PaymentType { get; set; } = "Downpayment_25"; // 'Downpayment_25', 'Final_Settlement_75', 'Overtime_Surcharge'
    public string PaymentMethod { get; set; } = "Xendit_QR";
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


public class UpdatePaymentBookingStatusRequest
{
    public string Status { get; set; } = string.Empty; // "Deal_Won", "Deal_Lost", "Rescheduled"
    public string? Reason { get; set; }
    public DateOnly? NewDate { get; set; }
    public TimeOnly? NewTime { get; set; }
    public string? PaymentMethod { get; set; }
}

public class TeamLeaderEventDto : EventAppointmentDto
{
    public bool IsTeamLeader { get; set; } = true;
    public string? CustomerPhone { get; set; }
    public DateTime? PostEventTriggerAt { get; set; }
    public bool IsPostEventAutoTriggered { get; set; }
}

public class UpdateTeamLeaderStatusRequest
{
    public string Status { get; set; } = string.Empty; // "Early", "Late", "Preparing", "Ongoing", "Event Done", "Pending Payment"
    public string? ArrivalStatus { get; set; } // "Early", "Late"
    public string? EmployeeId { get; set; }
}

public class MarkEquipmentTakenRequest
{
    public List<int>? EventEquipmentIds { get; set; }
    public bool IsTaken { get; set; } = true;
    public bool TransitionToDispatch { get; set; } = true;
    public string? EmployeeId { get; set; }
}

public class AddEventEquipmentRequest
{
    public string EquipmentId { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? EmployeeId { get; set; }
}

public class LiveEventOverviewDto : EventAppointmentDto
{
    public string? CustomerPhone { get; set; }
    public string DisplayStage { get; set; } = string.Empty;
    public string PaymentVerificationStatus { get; set; } = string.Empty;
    public int? PendingRemittancePaymentId { get; set; }
}

public class CustomerCancelAppointmentRequest
{
    public string? Reason { get; set; }
}

public class CustomerRescheduleAppointmentRequest
{
    public DateOnly NewDate { get; set; }
    public TimeOnly NewTime { get; set; }
    public string? Reason { get; set; }
}

public class CustomerAppointmentActionResultDto
{
    public bool Success { get; set; }
    public string EventAppointmentId { get; set; } = string.Empty;
    public string? NegotiationId { get; set; }
    public string EventStatus { get; set; } = string.Empty;
    public string? AssignedClerkId { get; set; }
    public string? AssignedClerkName { get; set; }
    public bool RetainedOriginalClerk { get; set; }
    public bool SlotReleased { get; set; }
    public bool ClerkReleased { get; set; }
    public bool EquipmentReleased { get; set; }
    public string Message { get; set; } = string.Empty;
    public EventAppointmentDto? Appointment { get; set; }
}

public static class LiveEventOverviewSelector
{
    private static readonly HashSet<string> ExecutingStages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ongoing",
        "On Going",
        "In Execution",
        "Ready",
        "Preparation",
        "Preparing",
        "Dispatch",
        "Post Event",
        "Event Done",
        "Paid Cash Pending Verification",
        "Pending Verification",
        "Payment Verification (Cash)",
        "Payment Verification (QR:Pending Payment)"
    };

    private static readonly HashSet<string> FinishedOrCancelledStages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Complete",
        "Completed",
        "Shot",
        "Delivered",
        "Cancelled",
        "Expired",
        "Deal_Lost",
        "Payment Verification (QR:Paid)"
    };

    public static bool IsExecuting(string? eventStatus, string? displayStage = null) =>
        (!string.IsNullOrWhiteSpace(eventStatus) && ExecutingStages.Contains(eventStatus.Trim())) ||
        (!string.IsNullOrWhiteSpace(displayStage) && ExecutingStages.Contains(displayStage.Trim()));

    public static bool IsFinishedOrCancelled(string? eventStatus, string? displayStage = null) =>
        (!string.IsNullOrWhiteSpace(eventStatus) && FinishedOrCancelledStages.Contains(eventStatus.Trim())) ||
        (!string.IsNullOrWhiteSpace(displayStage) && FinishedOrCancelledStages.Contains(displayStage.Trim()));

    public static IReadOnlyList<LiveEventOverviewDto> SelectForOverview(
        IEnumerable<LiveEventOverviewDto> events,
        DateTime studioNowLocal)
    {
        var list = (events ?? Enumerable.Empty<LiveEventOverviewDto>()).ToList();
        var executing = list
            .Where(e => IsExecuting(e.EventStatus, e.DisplayStage))
            .OrderBy(e => GetStagePriority(e.EventStatus, e.DisplayStage))
            .ThenBy(e => e.EventDate)
            .ThenBy(e => e.EventTime)
            .ToList();

        if (executing.Count > 0)
            return executing;

        var candidates = list
            .Where(e => !IsFinishedOrCancelled(e.EventStatus, e.DisplayStage))
            .Where(e =>
            {
                var startDt = e.EventDate.ToDateTime(e.EventTime);
                var endTime = e.EventEndTime.HasValue && e.EventEndTime.Value > e.EventTime
                    ? e.EventEndTime.Value
                    : e.EventTime.AddMinutes(Math.Max(15, e.DurationMinutes));
                var endDt = e.EventDate.ToDateTime(endTime);
                return startDt >= studioNowLocal || endDt >= studioNowLocal;
            })
            .OrderBy(e => e.EventDate.ToDateTime(e.EventTime) >= studioNowLocal ? 0 : 1)
            .ThenBy(e => e.EventDate)
            .ThenBy(e => e.EventTime)
            .Take(1)
            .ToList();

        return candidates;
    }

    private static int GetStagePriority(string? eventStatus, string? displayStage)
    {
        var stage = !string.IsNullOrWhiteSpace(displayStage) ? displayStage.Trim() : (eventStatus ?? string.Empty).Trim();
        return stage.ToUpperInvariant() switch
        {
            "PAID CASH PENDING VERIFICATION" or "PENDING VERIFICATION" or "PAYMENT VERIFICATION (CASH)" => 1,
            "PAYMENT VERIFICATION (QR:PENDING PAYMENT)" => 2,
            "ONGOING" or "ON GOING" or "IN EXECUTION" => 3,
            "POST EVENT" or "EVENT DONE" => 4,
            "DISPATCH" => 5,
            "READY" => 6,
            "PREPARATION" or "PREPARING" => 7,
            _ => 8
        };
    }
}

public sealed record TeamLeaderProcessStep(string Key, string Label, string State);

public static class TeamLeaderMonitorSelector
{
    private static readonly HashSet<string> FinishedOrInactiveStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Complete",
        "Completed",
        "Shot",
        "Delivered",
        "Cancelled",
        "Expired",
        "Deal_Lost"
    };

    private static readonly HashSet<string> OngoingStages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Preparing",
        "Preparation",
        "Dispatch",
        "Ready",
        "Ongoing",
        "On Going",
        "In Execution",
        "Event Done",
        "Post Event",
        "Pending Verification",
        "Paid Cash Pending Verification",
        "Payment Verification (Cash)",
        "Payment Verification (QR:Pending Payment)"
    };

    public static bool IsCompletedOrInactive(EventAppointmentDto? ev)
    {
        if (ev == null) return true;
        var status = (ev.EventStatus ?? string.Empty).Trim();
        var finalStatus = (ev.FinalPaymentStatus ?? string.Empty).Trim();
        return FinishedOrInactiveStatuses.Contains(status) ||
               string.Equals(finalStatus, "Paid", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(finalStatus, "Payment Verification (QR:Paid)", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPaidAndBooked(EventAppointmentDto? ev)
    {
        if (ev == null) return false;
        if (string.Equals(ev.DownpaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(ev.FinalPaymentReferenceId))
            return true;

        var status = (ev.EventStatus ?? string.Empty).Trim();
        return !string.Equals(status, "Pending Payment", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(status, "Awaiting_Downpayment", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(status, "Expired", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(status, "Deal_Lost", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsOngoingOrInProcess(EventAppointmentDto? ev)
    {
        if (ev == null || IsCompletedOrInactive(ev)) return false;
        var status = (ev.EventStatus ?? string.Empty).Trim();
        var finalStatus = (ev.FinalPaymentStatus ?? string.Empty).Trim();

        if (OngoingStages.Contains(status) || OngoingStages.Contains(finalStatus))
            return true;

        return string.Equals(status, "Pending Payment", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(ev.FinalPaymentReferenceId);
    }

    public static bool IsUpcoming(EventAppointmentDto? ev, DateTime studioNowLocal)
    {
        if (ev == null || IsCompletedOrInactive(ev) || IsOngoingOrInProcess(ev))
            return false;

        if (!IsPaidAndBooked(ev))
            return false;

        return ev.EventDate >= DateOnly.FromDateTime(studioNowLocal.Date);
    }

    public static IReadOnlyList<T> SelectUpcomingAndOngoing<T>(
        IEnumerable<T>? events,
        DateTime? studioNowLocal = null) where T : EventAppointmentDto
    {
        var now = studioNowLocal ?? DateTime.Now;
        return (events ?? Enumerable.Empty<T>())
            .Where(e => IsOngoingOrInProcess(e) || IsUpcoming(e, now))
            .OrderBy(e => IsOngoingOrInProcess(e) ? 0 : 1)
            .ThenBy(e => GetOngoingPriority(e))
            .ThenBy(e => e.EventDate)
            .ThenBy(e => e.EventTime)
            .ToList();
    }

    public static IReadOnlyList<TeamLeaderProcessStep> GetProcessSteps(EventAppointmentDto ev)
    {
        var status = (ev.EventStatus ?? string.Empty).Trim();
        var finalStatus = (ev.FinalPaymentStatus ?? string.Empty).Trim();

        var isCashPending = string.Equals(status, "Payment Verification (Cash)", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(status, "Pending Verification", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(status, "Paid Cash Pending Verification", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(finalStatus, "Payment Verification (Cash)", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(finalStatus, "Pending Verification", StringComparison.OrdinalIgnoreCase);

        var isQrPending = string.Equals(finalStatus, "Payment Verification (QR:Pending Payment)", StringComparison.OrdinalIgnoreCase)
                          || (string.Equals(ev.FinalPaymentMethod, "Xendit_QR", StringComparison.OrdinalIgnoreCase)
                              && !string.IsNullOrWhiteSpace(ev.FinalPaymentReferenceId)
                              && !IsCompletedOrInactive(ev));

        int activeRank;
        if (IsCompletedOrInactive(ev))
            activeRank = 6;
        else if (isCashPending || isQrPending || string.Equals(status, "Pending Payment", StringComparison.OrdinalIgnoreCase))
            activeRank = 5;
        else if (string.Equals(status, "Event Done", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "Post Event", StringComparison.OrdinalIgnoreCase))
            activeRank = 4;
        else if (string.Equals(status, "Ongoing", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "On Going", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "In Execution", StringComparison.OrdinalIgnoreCase))
            activeRank = 3;
        else if (string.Equals(status, "Preparation", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "Preparing", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "Dispatch", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "Ready", StringComparison.OrdinalIgnoreCase))
            activeRank = 2;
        else
            activeRank = 1;

        var settlementLabel = isCashPending
            ? "Payment Verification (Cash)"
            : isQrPending
                ? "Payment Verification (QR:Pending Payment)"
                : "Settlement";

        string StateFor(int rank)
        {
            if (rank < activeRank) return "done";
            if (rank == activeRank) return rank == 5 ? "active-amber" : "active";
            return "pending";
        }

        return new[]
        {
            new TeamLeaderProcessStep("Booked", "Booked (Upcoming)", StateFor(1)),
            new TeamLeaderProcessStep("Preparing", "Preparing & Gear", StateFor(2)),
            new TeamLeaderProcessStep("Ongoing", "Ongoing", StateFor(3)),
            new TeamLeaderProcessStep("Event Done", "Event Done", StateFor(4)),
            new TeamLeaderProcessStep("Settlement", settlementLabel, StateFor(5))
        };
    }

    private static int GetOngoingPriority(EventAppointmentDto ev)
    {
        var status = (ev.EventStatus ?? string.Empty).Trim().ToUpperInvariant();
        return status switch
        {
            "ONGOING" or "ON GOING" or "IN EXECUTION" => 1,
            "DISPATCH" => 2,
            "PREPARATION" or "PREPARING" or "READY" => 3,
            "EVENT DONE" or "POST EVENT" => 4,
            "PENDING PAYMENT" or "PENDING VERIFICATION" or "PAID CASH PENDING VERIFICATION" or "PAYMENT VERIFICATION (CASH)" => 5,
            "CONFIRMED" => 6,
            _ => 7
        };
    }
}
