namespace PhotoAevumStudio.Web.Models;

public static class PaymentStatuses
{
    public const string Pending = "Pending";
    public const string Cleared = "Cleared";
    public const string Failed = "Failed";
}

public static class RemittanceStatuses
{
    public const string NotApplicable = "NotApplicable";
    public const string PendingAdminApproval = "PendingAdminApproval";
    public const string Cleared = "Cleared";
}

public record PaymentBreakdownModel(
    decimal TotalAmount,
    decimal DepositAmount,
    decimal RemainingBalance,
    decimal DepositRate = 0.15m);

public class CustomerSubmitPaymentModel
{
    public int? BookingId { get; set; }
    public string? EventAppointmentId { get; set; }
    public decimal TotalAmount { get; set; }
    public string Channel { get; set; } = "Online"; // "Online" or "Cash"
    public string? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string? ExternalReference { get; set; }
    public string? Notes { get; set; }
}

public class EmployeeLogCashModel
{
    public int? BookingId { get; set; }
    public string? EventAppointmentId { get; set; }
    public decimal Amount { get; set; }
    public decimal TotalBookingAmount { get; set; }
    public string EmployeeId { get; set; } = string.Empty;
    public string? EmployeeName { get; set; }
    public bool IsDeposit { get; set; } = false;
    public string? Notes { get; set; }
}

public class AdminApproveRemittanceModel
{
    public string? AdminId { get; set; }
    public string? AdminName { get; set; }
    public string? Notes { get; set; }
}

public class PaymentSummaryModel
{
    public int Id { get; set; }
    public int? BookingId { get; set; }
    public string? EventAppointmentId { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal DepositAmount { get; set; }
    public decimal RemainingBalance { get; set; }
    public decimal Amount { get; set; }
    public string PaymentType { get; set; } = "ReservationDeposit_15";
    public string CustomerPaymentStatus { get; set; } = "Pending";
    public string EmployeeRemittanceStatus { get; set; } = "NotApplicable";
    public string PaymentChannel { get; set; } = "Online";
    public string? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string? AdminApproverId { get; set; }
    public string? AdminApproverName { get; set; }
    public DateTime? ApprovalTimestamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ExternalReference { get; set; }
    public string? Notes { get; set; }
}
