namespace PhotoAevumStudio.Application.Entities;

public enum CustomerPaymentStatus
{
    Pending,
    Cleared,
    Failed
}

public enum EmployeeRemittanceStatus
{
    NotApplicable,
    PendingAdminApproval,
    Cleared
}

public enum PaymentChannel
{
    Cash,
    Online
}

public enum PaymentType
{
    ReservationDeposit_15,
    BalanceSettlement_85,
    Overtime_Surcharge,
    FullPayment
}

public class Payment
{
    public int Id { get; set; }

    // Associated Booking (EF entity)
    public int? BookingId { get; set; }
    public Booking? Booking { get; set; }

    // Associated EventAppointment (SQL/SP domain model identifier)
    public string? EventAppointmentId { get; set; }

    // Financial breakdown: 15% deposit vs Total amount & 85% balance
    public decimal TotalAmount { get; set; }
    public decimal DepositAmount { get; set; }
    public decimal RemainingBalance { get; set; }
    public decimal Amount { get; set; }

    public PaymentType PaymentType { get; set; } = PaymentType.ReservationDeposit_15;

    // Two-Tier Clearing Tracking
    // Customer view state: Cleared immediately once payment is submitted/cash handed over
    public CustomerPaymentStatus CustomerPaymentStatus { get; set; } = CustomerPaymentStatus.Pending;

    // Employee handover state: PendingAdminApproval when employee receives cash
    public EmployeeRemittanceStatus EmployeeRemittanceStatus { get; set; } = EmployeeRemittanceStatus.NotApplicable;

    // Payment channel: Cash or Online
    public PaymentChannel Channel { get; set; } = PaymentChannel.Online;

    // Handling Employee (who collected cash/payment on-site)
    public string? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }

    // Admin Verification & Reconciliation
    public string? AdminApproverId { get; set; }
    public string? AdminApproverName { get; set; }
    public DateTime? ApprovalTimestamp { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ExternalReference { get; set; }
    public string? Notes { get; set; }

    public static (decimal Deposit, decimal Balance) CalculateReservationFee(decimal totalAmount)
    {
        var deposit = Math.Round(totalAmount * 0.15m, 2);
        var balance = Math.Round(totalAmount - deposit, 2);
        return (deposit, balance);
    }
}
