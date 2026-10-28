using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Entities;

namespace PhotoAevumStudio.Application.Interfaces;

public interface IPaymentService
{
    PaymentBreakdownDto CalculateReservationFee(decimal totalAmount);

    Task<PaymentSummaryDto> SubmitCustomerPaymentAsync(
        CustomerSubmitPaymentRequest request,
        string? customerId = null,
        CancellationToken ct = default);

    Task<PaymentSummaryDto> LogEmployeeCashCollectionAsync(
        EmployeeLogCashCollectionRequest request,
        string? employeeId = null,
        CancellationToken ct = default);

    Task<PaymentSummaryDto> ApproveRemittanceAsync(
        int paymentId,
        string adminId,
        string? adminName = null,
        string? notes = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<PaymentSummaryDto>> GetPendingRemittancesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<PaymentSummaryDto>> GetPaymentsByBookingAsync(int bookingId, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentSummaryDto>> GetPaymentsByAppointmentAsync(string eventAppointmentId, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentSummaryDto>> GetAllPaymentsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<PaymentSummaryDto>> GetEmployeeLedgerAsync(string employeeId, CancellationToken ct = default);
}
