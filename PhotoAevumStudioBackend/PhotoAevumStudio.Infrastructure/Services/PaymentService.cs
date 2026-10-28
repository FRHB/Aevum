using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Infrastructure.Services;

public class PaymentService : IPaymentService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<PaymentService> _logger;
    private static volatile bool _schemaEnsured = false;
    private static readonly SemaphoreSlim _schemaLock = new(1, 1);

    public PaymentService(ApplicationDbContext db, ILogger<PaymentService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public PaymentBreakdownDto CalculateReservationFee(decimal totalAmount)
    {
        var (deposit, balance) = Payment.CalculateReservationFee(totalAmount);
        return new PaymentBreakdownDto(totalAmount, deposit, balance, 0.15m);
    }

    public async Task<PaymentSummaryDto> SubmitCustomerPaymentAsync(
        CustomerSubmitPaymentRequest request,
        string? customerId = null,
        CancellationToken ct = default)
    {
        await EnsureSchemaOnceAsync(ct);

        var (deposit, balance) = Payment.CalculateReservationFee(request.TotalAmount);
        var amountPaid = deposit > 0 ? deposit : request.TotalAmount;

        var isCash = request.Channel == PaymentChannel.Cash;
        var remittanceStatus = isCash
            ? EmployeeRemittanceStatus.PendingAdminApproval
            : EmployeeRemittanceStatus.NotApplicable;

        var payment = new Payment
        {
            BookingId = request.BookingId,
            EventAppointmentId = request.EventAppointmentId,
            TotalAmount = request.TotalAmount,
            DepositAmount = deposit,
            RemainingBalance = balance,
            Amount = amountPaid,
            PaymentType = PaymentType.ReservationDeposit_15,
            CustomerPaymentStatus = CustomerPaymentStatus.Cleared, // Immediate confirmation on customer side
            EmployeeRemittanceStatus = remittanceStatus,
            Channel = request.Channel,
            EmployeeId = request.EmployeeId,
            EmployeeName = request.EmployeeName,
            ExternalReference = request.ExternalReference,
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _db.Payments.Add(payment);

        // Update EF Booking if present
        if (request.BookingId.HasValue)
        {
            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == request.BookingId.Value, ct);
            if (booking != null)
            {
                booking.TotalAmount = request.TotalAmount;
                booking.DepositAmount = deposit;
                booking.RemainingBalance = balance;
                booking.PaymentStatus = CustomerPaymentStatus.Cleared;
                booking.Status = BookingStatus.Confirmed;
            }
        }

        // Add audit log
        var audit = new AuditLog
        {
            UserId = customerId ?? request.EmployeeId ?? "System",
            UserEmail = customerId ?? "Customer Portal",
            Action = "Submit Customer 15% Reservation Deposit",
            Resource = "Payment",
            ResourceId = payment.BookingId?.ToString() ?? payment.EventAppointmentId,
            Timestamp = DateTime.UtcNow,
            Details = $"Customer submitted 15% reservation deposit of ₱{deposit:N2} (Total: ₱{request.TotalAmount:N2}, Balance: ₱{balance:N2}) via {request.Channel}. Customer Status: Cleared. Remittance: {remittanceStatus}."
        };
        _db.AuditLogs.Add(audit);

        await _db.SaveChangesAsync(ct);

        // Best effort sync with SQL EventAppointment & PaymentTransactions
        await SyncSqlEventAppointmentAsync(request.EventAppointmentId, deposit, balance, isCash, request.EmployeeId, ct);

        _logger.LogInformation("Customer payment processed: Payment #{PaymentId}, 15% deposit ₱{Deposit}, CustomerStatus: Cleared, RemittanceStatus: {Remittance}",
            payment.Id, deposit, remittanceStatus);

        return ToDto(payment);
    }

    public async Task<PaymentSummaryDto> LogEmployeeCashCollectionAsync(
        EmployeeLogCashCollectionRequest request,
        string? employeeId = null,
        CancellationToken ct = default)
    {
        await EnsureSchemaOnceAsync(ct);

        var total = request.TotalBookingAmount > 0 ? request.TotalBookingAmount : request.Amount;
        var (deposit, balance) = Payment.CalculateReservationFee(total);

        var empId = !string.IsNullOrWhiteSpace(employeeId) ? employeeId : request.EmployeeId;

        var payment = new Payment
        {
            BookingId = request.BookingId,
            EventAppointmentId = request.EventAppointmentId,
            TotalAmount = total,
            DepositAmount = deposit,
            RemainingBalance = balance,
            Amount = request.Amount,
            PaymentType = request.IsDeposit ? PaymentType.ReservationDeposit_15 : PaymentType.BalanceSettlement_85,
            CustomerPaymentStatus = CustomerPaymentStatus.Cleared, // Immediate confirmation for client
            EmployeeRemittanceStatus = EmployeeRemittanceStatus.PendingAdminApproval, // Tagged to employee, not cleared on employee ledger
            Channel = PaymentChannel.Cash,
            EmployeeId = empId,
            EmployeeName = request.EmployeeName,
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _db.Payments.Add(payment);

        var audit = new AuditLog
        {
            UserId = empId,
            UserEmail = request.EmployeeName ?? empId,
            Action = "Employee Cash Collection",
            Resource = "Payment",
            ResourceId = payment.BookingId?.ToString() ?? payment.EventAppointmentId,
            Timestamp = DateTime.UtcNow,
            Details = $"Employee {empId} logged on-site cash collection of ₱{request.Amount:N2}. Customer Status: Cleared. Employee Remittance Status: PendingAdminApproval."
        };
        _db.AuditLogs.Add(audit);

        await _db.SaveChangesAsync(ct);

        await SyncSqlEventAppointmentAsync(request.EventAppointmentId, deposit, balance, true, empId, ct);

        _logger.LogInformation("Employee #{EmpId} logged cash collection of ₱{Amount}. Payment #{PaymentId} tagged as PendingAdminApproval.",
            empId, request.Amount, payment.Id);

        return ToDto(payment);
    }

    public async Task<PaymentSummaryDto> ApproveRemittanceAsync(
        int paymentId,
        string adminId,
        string? adminName = null,
        string? notes = null,
        CancellationToken ct = default)
    {
        await EnsureSchemaOnceAsync(ct);

        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment == null)
            throw new KeyNotFoundException($"Payment #{paymentId} not found.");

        payment.EmployeeRemittanceStatus = EmployeeRemittanceStatus.Cleared;
        payment.AdminApproverId = adminId;
        payment.AdminApproverName = adminName;
        payment.ApprovalTimestamp = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(notes))
        {
            payment.Notes = string.IsNullOrWhiteSpace(payment.Notes)
                ? $"Admin approval note: {notes}"
                : $"{payment.Notes} | Admin approval: {notes}";
        }

        var audit = new AuditLog
        {
            UserId = adminId,
            UserEmail = adminName ?? adminId,
            Action = "Approve Cash Remittance",
            Resource = "Payment",
            ResourceId = payment.Id.ToString(),
            Timestamp = DateTime.UtcNow,
            Details = $"Admin {adminId} approved cash remittance of ₱{payment.Amount:N2} received by Employee {payment.EmployeeId} for Booking #{payment.BookingId ?? 0}/Event #{payment.EventAppointmentId ?? "N/A"}. Employee remittance status is now Cleared."
        };
        _db.AuditLogs.Add(audit);

        await _db.SaveChangesAsync(ct);

        // Sync to SQL HistoryLog
        await SyncSqlAdminApprovalHistoryAsync(payment, adminId, ct);

        _logger.LogInformation("Admin #{AdminId} approved remittance for Payment #{PaymentId}. Employee remittance status changed to Cleared.",
            adminId, paymentId);

        return ToDto(payment);
    }

    public async Task<IReadOnlyList<PaymentSummaryDto>> GetPendingRemittancesAsync(CancellationToken ct = default)
    {
        await EnsureSchemaOnceAsync(ct);

        var list = await _db.Payments.AsNoTracking()
            .Where(p => p.EmployeeRemittanceStatus == EmployeeRemittanceStatus.PendingAdminApproval)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        return list.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<PaymentSummaryDto>> GetPaymentsByBookingAsync(int bookingId, CancellationToken ct = default)
    {
        await EnsureSchemaOnceAsync(ct);

        var list = await _db.Payments.AsNoTracking()
            .Where(p => p.BookingId == bookingId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        return list.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<PaymentSummaryDto>> GetPaymentsByAppointmentAsync(string eventAppointmentId, CancellationToken ct = default)
    {
        await EnsureSchemaOnceAsync(ct);

        var list = await _db.Payments.AsNoTracking()
            .Where(p => p.EventAppointmentId == eventAppointmentId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        return list.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<PaymentSummaryDto>> GetAllPaymentsAsync(CancellationToken ct = default)
    {
        await EnsureSchemaOnceAsync(ct);

        var list = await _db.Payments.AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        return list.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<PaymentSummaryDto>> GetEmployeeLedgerAsync(string employeeId, CancellationToken ct = default)
    {
        await EnsureSchemaOnceAsync(ct);

        var list = await _db.Payments.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId || p.Channel == PaymentChannel.Cash)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        return list.Select(ToDto).ToList();
    }

    private static PaymentSummaryDto ToDto(Payment p) => new(
        p.Id,
        p.BookingId,
        p.EventAppointmentId,
        p.TotalAmount,
        p.DepositAmount,
        p.RemainingBalance,
        p.Amount,
        p.PaymentType.ToString(),
        p.CustomerPaymentStatus.ToString(),
        p.EmployeeRemittanceStatus.ToString(),
        p.Channel.ToString(),
        p.EmployeeId,
        p.EmployeeName,
        p.AdminApproverId,
        p.AdminApproverName,
        p.ApprovalTimestamp,
        p.CreatedAt,
        p.ExternalReference,
        p.Notes);

    private async Task EnsureSchemaOnceAsync(CancellationToken ct)
    {
        if (_schemaEnsured) return;
        await _schemaLock.WaitAsync(ct);
        try
        {
            if (_schemaEnsured) return;

            var connStr = _db.Database.GetConnectionString();
            if (!string.IsNullOrWhiteSpace(connStr))
            {
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync(ct);

                const string createTableSql = @"
                    IF OBJECT_ID(N'dbo.Payments', N'U') IS NULL
                    BEGIN
                        CREATE TABLE dbo.Payments (
                            Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                            BookingId int NULL,
                            EventAppointmentId nvarchar(50) NULL,
                            TotalAmount decimal(18,2) NOT NULL DEFAULT 0.00,
                            DepositAmount decimal(18,2) NOT NULL DEFAULT 0.00,
                            RemainingBalance decimal(18,2) NOT NULL DEFAULT 0.00,
                            Amount decimal(18,2) NOT NULL DEFAULT 0.00,
                            PaymentType nvarchar(50) NOT NULL DEFAULT 'ReservationDeposit_15',
                            CustomerPaymentStatus nvarchar(30) NOT NULL DEFAULT 'Pending',
                            EmployeeRemittanceStatus nvarchar(30) NOT NULL DEFAULT 'NotApplicable',
                            Channel nvarchar(30) NOT NULL DEFAULT 'Online',
                            EmployeeId nvarchar(50) NULL,
                            EmployeeName nvarchar(150) NULL,
                            AdminApproverId nvarchar(50) NULL,
                            AdminApproverName nvarchar(150) NULL,
                            ApprovalTimestamp datetime2 NULL,
                            CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
                            ExternalReference nvarchar(100) NULL,
                            Notes nvarchar(1000) NULL
                        );
                    END;

                    IF OBJECT_ID(N'dbo.Payments', N'U') IS NOT NULL
                    BEGIN
                        IF COL_LENGTH(N'dbo.Payments', N'CustomerPaymentStatus') IS NULL
                            ALTER TABLE dbo.Payments ADD CustomerPaymentStatus nvarchar(30) NOT NULL DEFAULT 'Pending';
                        IF COL_LENGTH(N'dbo.Payments', N'EmployeeRemittanceStatus') IS NULL
                            ALTER TABLE dbo.Payments ADD EmployeeRemittanceStatus nvarchar(30) NOT NULL DEFAULT 'NotApplicable';
                        IF COL_LENGTH(N'dbo.Payments', N'DepositAmount') IS NULL
                            ALTER TABLE dbo.Payments ADD DepositAmount decimal(18,2) NOT NULL DEFAULT 0.00;
                        IF COL_LENGTH(N'dbo.Payments', N'RemainingBalance') IS NULL
                            ALTER TABLE dbo.Payments ADD RemainingBalance decimal(18,2) NOT NULL DEFAULT 0.00;
                    END;

                    IF OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NOT NULL
                    BEGIN
                        IF COL_LENGTH(N'dbo.PaymentTransactions', N'CustomerPaymentStatus') IS NULL
                            ALTER TABLE dbo.PaymentTransactions ADD CustomerPaymentStatus nvarchar(30) NULL;
                        IF COL_LENGTH(N'dbo.PaymentTransactions', N'EmployeeRemittanceStatus') IS NULL
                            ALTER TABLE dbo.PaymentTransactions ADD EmployeeRemittanceStatus nvarchar(30) NULL;
                        IF COL_LENGTH(N'dbo.PaymentTransactions', N'AdminApproverID') IS NULL
                            ALTER TABLE dbo.PaymentTransactions ADD AdminApproverID nvarchar(100) NULL;
                        IF COL_LENGTH(N'dbo.PaymentTransactions', N'ApprovalTimestamp') IS NULL
                            ALTER TABLE dbo.PaymentTransactions ADD ApprovalTimestamp datetime2 NULL;
                    END;";

                await using var cmd = new SqlCommand(createTableSql, conn);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            _schemaEnsured = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Best effort schema reconciliation for Payments table completed with notes.");
            _schemaEnsured = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    private async Task SyncSqlEventAppointmentAsync(
        string? appointmentId,
        decimal deposit,
        decimal balance,
        bool isCash,
        string? employeeId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(appointmentId)) return;

        try
        {
            var connStr = _db.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connStr)) return;

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(ct);

            const string updateSql = @"
                IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.EventAppointment
                    SET EventStatus = 'Confirmed',
                        DownpaymentStatus = 'Paid',
                        DownpaymentAmount = @Deposit,
                        FinalBalanceAmount = @Balance,
                        DownpaymentPaidAt = SYSUTCDATETIME()
                    WHERE EventAppointmentID = @EvtId;
                END;

                IF OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.PaymentTransactions (
                        PaymentTransactionID, EventAppointmentID, PaymentType, PaymentMethod,
                        Amount, Status, ReceivedByEmployeeID, CustomerPaymentStatus,
                        EmployeeRemittanceStatus, CreatedAt
                    )
                    VALUES (
                        'TXN-' + CAST(ABS(CHECKSUM(NEWID())) % 900000 + 100000 AS nvarchar(10)),
                        @EvtId, 'ReservationDeposit_15', @Method,
                        @Deposit, 'Paid', @EmpId, 'Cleared', @Remittance, SYSUTCDATETIME()
                    );
                END;";

            await using var cmd = new SqlCommand(updateSql, conn);
            cmd.Parameters.AddWithValue("@EvtId", appointmentId);
            cmd.Parameters.AddWithValue("@Deposit", deposit);
            cmd.Parameters.AddWithValue("@Balance", balance);
            cmd.Parameters.AddWithValue("@Method", isCash ? "Cash" : "Online");
            cmd.Parameters.AddWithValue("@EmpId", (object?)employeeId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Remittance", isCash ? "PendingAdminApproval" : "NotApplicable");

            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync payment with legacy SQL EventAppointment for {AppointmentId}", appointmentId);
        }
    }

    private async Task SyncSqlAdminApprovalHistoryAsync(Payment payment, string adminId, CancellationToken ct)
    {
        try
        {
            var connStr = _db.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connStr)) return;

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(ct);

            const string sql = @"
                IF OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.PaymentTransactions
                    SET EmployeeRemittanceStatus = 'Cleared',
                        AdminApproverID = @AdminId,
                        ApprovalTimestamp = SYSUTCDATETIME()
                    WHERE (EventAppointmentID = @EvtId OR @EvtId IS NULL)
                      AND ReceivedByEmployeeID = @EmpId
                      AND (EmployeeRemittanceStatus = 'PendingAdminApproval' OR EmployeeRemittanceStatus IS NULL);
                END;

                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (
                        ActionType, EntityName, EntityID, ActionDetails,
                        EventAppointmentID, ChangedByType, ChangedBy, LoggedAt
                    )
                    VALUES (
                        'APPROVAL', 'CashRemittance', CAST(@PaymentId AS nvarchar(30)),
                        'Admin ' + @AdminId + ' approved cash remittance. Employee status cleared.',
                        @EvtId, 'Admin', @AdminId, SYSUTCDATETIME()
                    );
                END;";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@PaymentId", payment.Id);
            cmd.Parameters.AddWithValue("@EvtId", (object?)payment.EventAppointmentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@EmpId", (object?)payment.EmployeeId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AdminId", adminId);

            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync Admin approval to SQL HistoryLog for payment #{PaymentId}", payment.Id);
        }
    }
}
