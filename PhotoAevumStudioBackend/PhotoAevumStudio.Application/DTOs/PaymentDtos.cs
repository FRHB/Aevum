using PhotoAevumStudio.Application.Entities;

namespace PhotoAevumStudio.Application.DTOs;

public record CustomerSubmitPaymentRequest
{
    public int? BookingId { get; init; }
    public string? EventAppointmentId { get; init; }
    public decimal TotalAmount { get; init; }
    public PaymentChannel Channel { get; init; } = PaymentChannel.Online;
    public string? EmployeeId { get; init; }
    public string? EmployeeName { get; init; }
    public string? ExternalReference { get; init; }
    public string? Notes { get; init; }
}

public record EmployeeLogCashCollectionRequest
{
    public int? BookingId { get; init; }
    public string? EventAppointmentId { get; init; }
    public decimal Amount { get; init; }
    public decimal TotalBookingAmount { get; init; }
    public string EmployeeId { get; init; } = string.Empty;
    public string? EmployeeName { get; init; }
    public bool IsDeposit { get; init; } = false;
    public string? Notes { get; init; }
}

public record AdminApproveRemittanceRequest
{
    public string? AdminId { get; init; }
    public string? AdminName { get; init; }
    public string? Notes { get; init; }
}

public record PaymentBreakdownDto(
    decimal TotalAmount,
    decimal DepositAmount,
    decimal RemainingBalance,
    decimal DepositRate = 0.15m);

public record PaymentSummaryDto(
    int Id,
    int? BookingId,
    string? EventAppointmentId,
    decimal TotalAmount,
    decimal DepositAmount,
    decimal RemainingBalance,
    decimal Amount,
    string PaymentType,
    string CustomerPaymentStatus,
    string EmployeeRemittanceStatus,
    string PaymentChannel,
    string? EmployeeId,
    string? EmployeeName,
    string? AdminApproverId,
    string? AdminApproverName,
    DateTime? ApprovalTimestamp,
    DateTime CreatedAt,
    string? ExternalReference,
    string? Notes);
