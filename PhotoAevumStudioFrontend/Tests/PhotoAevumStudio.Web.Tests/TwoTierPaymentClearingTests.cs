using System;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.DTOs;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class TwoTierPaymentClearingTests
{
    [Fact]
    public void CalculateReservationFee_Computes_15PercentDeposit_And_85PercentBalance()
    {
        // Arrange
        decimal total = 10000.00m;

        // Act
        var (deposit, balance) = Payment.CalculateReservationFee(total);

        // Assert
        Assert.Equal(1500.00m, deposit);
        Assert.Equal(8500.00m, balance);
        Assert.Equal(total, deposit + balance);
    }

    [Theory]
    [InlineData(1250.50, 187.58, 1062.92)]
    [InlineData(5000.00, 750.00, 4250.00)]
    [InlineData(0.00, 0.00, 0.00)]
    public void CalculateReservationFee_Handles_Various_Amounts_With_Rounding(decimal total, decimal expectedDeposit, decimal expectedBalance)
    {
        var (deposit, balance) = Payment.CalculateReservationFee(total);

        Assert.Equal(expectedDeposit, deposit);
        Assert.Equal(expectedBalance, balance);
    }

    [Fact]
    public void Booking_RecalculateReservationFee_Updates_Deposit_And_Balance()
    {
        // Arrange
        var booking = new Booking
        {
            Id = 1,
            CustomerId = "cust-1"
        };

        // Act
        booking.RecalculateReservationFee(20000m);

        // Assert
        Assert.Equal(20000m, booking.TotalAmount);
        Assert.Equal(3000m, booking.DepositAmount);
        Assert.Equal(17000m, booking.RemainingBalance);
    }

    [Fact]
    public void Payment_Cash_Received_By_Employee_Has_CustomerCleared_And_EmployeePending()
    {
        // Arrange
        var payment = new Payment
        {
            Id = 101,
            BookingId = 42,
            TotalAmount = 5000m,
            DepositAmount = 750m,
            RemainingBalance = 4250m,
            Channel = PaymentChannel.Cash,
            PaymentType = PaymentType.ReservationDeposit_15,
            CustomerPaymentStatus = CustomerPaymentStatus.Cleared,
            EmployeeRemittanceStatus = EmployeeRemittanceStatus.PendingAdminApproval,
            EmployeeId = "EMP-007",
            EmployeeName = "Jane Photographer"
        };

        // Assert
        Assert.Equal(CustomerPaymentStatus.Cleared, payment.CustomerPaymentStatus);
        Assert.Equal(EmployeeRemittanceStatus.PendingAdminApproval, payment.EmployeeRemittanceStatus);
        Assert.Null(payment.AdminApproverId);
        Assert.Null(payment.ApprovalTimestamp);
    }

    [Fact]
    public void Payment_Online_Channel_Sets_EmployeeRemittance_NotApplicable()
    {
        // Arrange
        var payment = new Payment
        {
            Id = 102,
            BookingId = 43,
            TotalAmount = 5000m,
            DepositAmount = 750m,
            RemainingBalance = 4250m,
            Channel = PaymentChannel.Online,
            PaymentType = PaymentType.ReservationDeposit_15,
            CustomerPaymentStatus = CustomerPaymentStatus.Cleared,
            EmployeeRemittanceStatus = EmployeeRemittanceStatus.NotApplicable
        };

        // Assert
        Assert.Equal(CustomerPaymentStatus.Cleared, payment.CustomerPaymentStatus);
        Assert.Equal(EmployeeRemittanceStatus.NotApplicable, payment.EmployeeRemittanceStatus);
    }

    [Fact]
    public void Admin_Approval_Transitions_RemittanceStatus_To_Cleared_And_Audits()
    {
        // Arrange
        var payment = new Payment
        {
            Id = 103,
            BookingId = 44,
            TotalAmount = 8000m,
            DepositAmount = 1200m,
            RemainingBalance = 6800m,
            Channel = PaymentChannel.Cash,
            PaymentType = PaymentType.ReservationDeposit_15,
            CustomerPaymentStatus = CustomerPaymentStatus.Cleared,
            EmployeeRemittanceStatus = EmployeeRemittanceStatus.PendingAdminApproval,
            EmployeeId = "EMP-009",
            EmployeeName = "John Assistant"
        };

        // Act - Simulate Admin Approval
        payment.EmployeeRemittanceStatus = EmployeeRemittanceStatus.Cleared;
        payment.AdminApproverId = "ADM-001";
        payment.AdminApproverName = "Chief Studio Admin";
        payment.ApprovalTimestamp = DateTime.UtcNow;
        payment.Notes = "Verified on-site physical cash envelope.";

        // Assert
        Assert.Equal(CustomerPaymentStatus.Cleared, payment.CustomerPaymentStatus);
        Assert.Equal(EmployeeRemittanceStatus.Cleared, payment.EmployeeRemittanceStatus);
        Assert.Equal("ADM-001", payment.AdminApproverId);
        Assert.NotNull(payment.ApprovalTimestamp);
        Assert.Contains("Verified on-site", payment.Notes);
    }
}
