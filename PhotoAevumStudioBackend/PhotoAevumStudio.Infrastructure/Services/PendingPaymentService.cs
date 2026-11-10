using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Infrastructure.Services;

/// <summary>
/// Owns the Pending Payment lock lifecycle on dbo.EventAppointment:
/// schema reconciliation, webhook confirmation and expiry release.
/// The DDL below mirrors PhotoAevumStudio.sql / PhotoAevumStudioIdempotent.sql / PhotoAevumStudioStoredProcedures.sql.
/// </summary>
public class PendingPaymentService : IPendingPaymentService
{
    private static volatile bool _schemaEnsured;
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);

    private readonly ApplicationDbContext _db;
    private readonly ILogger<PendingPaymentService> _logger;

    public PendingPaymentService(ApplicationDbContext db, ILogger<PendingPaymentService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ---- DDL (each entry is its own batch; CREATE OR ALTER PROCEDURE must be alone) ----
    internal static readonly string[] SchemaBatches =
    {
        @"IF COL_LENGTH(N'dbo.EventAppointment', N'LockExpiresAt') IS NULL
              ALTER TABLE dbo.EventAppointment ADD LockExpiresAt datetime2 NULL;
          IF COL_LENGTH(N'dbo.EventAppointment', N'QrString') IS NULL
              ALTER TABLE dbo.EventAppointment ADD QrString nvarchar(1000) NULL;",

        @"IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
                         WHERE name = N'CK_EventAppointment_Status' AND definition LIKE N'%Pending Payment%')
          BEGIN
              IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_EventAppointment_Status')
                  ALTER TABLE dbo.EventAppointment DROP CONSTRAINT CK_EventAppointment_Status;
              ALTER TABLE dbo.EventAppointment ADD CONSTRAINT CK_EventAppointment_Status
                  CHECK (EventStatus IN ('Pending','Awaiting_Downpayment','Pending Payment','Confirmed','Shot','Editing','Delivered','Cancelled','Expired'));
          END;
          IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EventAppointment_Status_LockExpiresAt' AND object_id = OBJECT_ID(N'dbo.EventAppointment'))
              CREATE INDEX IX_EventAppointment_Status_LockExpiresAt ON dbo.EventAppointment (EventStatus, LockExpiresAt);",

        @"CREATE OR ALTER PROCEDURE dbo.usp_ReleaseExpiredPendingPayments
          AS
          BEGIN
              SET NOCOUNT ON;
              DECLARE @Released TABLE (
                  EventAppointmentID nvarchar(30), CustomerID nvarchar(30),
                  EventName nvarchar(255), EventDate date, EventTime time);

              UPDATE dbo.EventAppointment
              SET EventStatus = 'Cancelled',
                  DownpaymentStatus = 'Expired',
                  LockExpiresAt = NULL,
                  HoldExpiresAt = NULL,
                  QrString = NULL
              OUTPUT inserted.EventAppointmentID, inserted.CustomerID, inserted.EventName, inserted.EventDate, inserted.EventTime
              INTO @Released
              WHERE EventStatus = 'Pending Payment'
                AND LockExpiresAt IS NOT NULL
                AND LockExpiresAt <= SYSUTCDATETIME();

              -- Free the staff and equipment held by the released slot.
              DELETE ee FROM dbo.EventEmployee ee JOIN @Released r ON r.EventAppointmentID = ee.EventAppointmentID;
              DELETE eq FROM dbo.EventEquipment eq JOIN @Released r ON r.EventAppointmentID = eq.EventAppointmentID;

              IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
                  UPDATE bn
                  SET NegotiationStatus = 'Deal_Lost'
                  FROM dbo.BookingNegotiation bn JOIN @Released r ON r.EventAppointmentID = bn.EventAppointmentID;

              IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                  INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ActionDetails, EventAppointmentID, ChangedByType, LoggedAt)
                  SELECT 'STATUS_CHANGE', 'EventAppointment', r.EventAppointmentID,
                         'Reservation fee not received within the lock window. Booking cancelled and time slot released.',
                         r.EventAppointmentID, 'System', SYSUTCDATETIME()
                  FROM @Released r;

              SELECT r.EventAppointmentID, r.CustomerID,
                     LTRIM(RTRIM(ISNULL(c.CustomerFirstName, '') + ' ' + ISNULL(c.CustomerLastName, ''))) AS CustomerName,
                     ISNULL(c.CustomerEmail, '') AS CustomerEmail,
                     r.EventName, r.EventDate, r.EventTime
              FROM @Released r
              LEFT JOIN dbo.Customer c ON c.CustomerID = r.CustomerID;
          END;"
    };

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        if (_schemaEnsured) return;
        await using var conn = await SqlResilienceHelper.OpenConnectionAsync(_db.Database.GetConnectionString(), ct);
        await EnsureSchemaOnConnectionAsync(conn, ct, _logger);
    }

    internal static async Task EnsureSchemaOnConnectionAsync(SqlConnection conn, CancellationToken ct = default, ILogger? logger = null)
    {
        if (_schemaEnsured) return;
        await SchemaLock.WaitAsync(ct);
        try
        {
            if (_schemaEnsured) return;
            foreach (var batch in SchemaBatches)
            {
                await using var cmd = new SqlCommand(batch, conn);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            _schemaEnsured = true;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to reconcile Pending Payment schema (LockExpiresAt / QrString / release proc).");
            throw;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = await SqlResilienceHelper.OpenConnectionAsync(_db.Database.GetConnectionString(), ct);
        await EnsureSchemaOnConnectionAsync(conn, ct, _logger);
        return conn;
    }

    public async Task<PaymentConfirmation?> ConfirmByReferenceAsync(
        string referenceId, string? qrId, decimal? amount, string? channel, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // Only a booking that is still 'Pending Payment' can be confirmed (idempotent against duplicate callbacks).
            const string confirmSql = @"
                UPDATE dbo.EventAppointment
                SET EventStatus = 'Confirmed',
                    DownpaymentStatus = 'Paid',
                    DownpaymentPaidAt = SYSUTCDATETIME(),
                    LockExpiresAt = NULL,
                    HoldExpiresAt = NULL
                OUTPUT inserted.EventAppointmentID, inserted.CustomerID, inserted.DownpaymentAmount
                WHERE EventAppointmentID = @Ref AND EventStatus = 'Pending Payment';";

            string? apptId = null, custId = null;
            decimal fee = 0m;
            await using (var cmd = new SqlCommand(confirmSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@Ref", referenceId);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (await r.ReadAsync(ct))
                {
                    apptId = r.GetString(0);
                    custId = r.GetString(1);
                    fee = r.GetDecimal(2);
                }
            }

            if (apptId is null)
            {
                await tx.RollbackAsync(ct);
                return null;
            }

            const string followUpSql = @"
                IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
                    UPDATE dbo.BookingNegotiation SET NegotiationStatus = 'Deal_Won' WHERE EventAppointmentID = @Ref;

                IF OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NOT NULL
                BEGIN
                    DECLARE @n INT = ISNULL((SELECT MAX(TRY_CAST(SUBSTRING(PaymentTransactionID, 5, 20) AS INT))
                                             FROM dbo.PaymentTransactions WITH (UPDLOCK, HOLDLOCK)), 0) + 1;
                    INSERT INTO dbo.PaymentTransactions (
                        PaymentTransactionID, EventAppointmentID, PaymentType, PaymentMethod, Amount, Status,
                        ExternalReference, XenditInvoiceId, XenditPaymentId, XenditPaymentChannel, CreatedAt)
                    VALUES (
                        'TXN-' + RIGHT('0000' + CAST(@n AS varchar(10)), 4), @Ref, 'Downpayment_15', 'QR Ph', @Amount, 'Paid',
                        @Ref, NULL, @QrId, @Channel, SYSUTCDATETIME());
                END;

                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ActionDetails, EventAppointmentID, ChangedByType, LoggedAt)
                    VALUES ('STATUS_CHANGE', 'EventAppointment', @Ref,
                            'Reservation fee received via Xendit QR webhook. Booking Confirmed; time slot locked in.',
                            @Ref, 'System', SYSUTCDATETIME());";

            await using (var cmd = new SqlCommand(followUpSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@Ref", referenceId);
                cmd.Parameters.AddWithValue("@Amount", amount ?? fee);
                cmd.Parameters.AddWithValue("@QrId", (object?)qrId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Channel", (object?)channel ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return new PaymentConfirmation(apptId, custId!, amount ?? fee);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<ExpiredBooking>> ReleaseExpiredAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand("dbo.usp_ReleaseExpiredPendingPayments", conn)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };
        var list = new List<ExpiredBooking>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new ExpiredBooking(
                r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.GetDateTime(5), r.GetFieldValue<TimeSpan>(6)));
        }
        return list;
    }

    public async Task<QrPaymentInfoDto?> GetQrPaymentAsync(string appointmentId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        const string sql = @"
            SELECT EventAppointmentID, CustomerID, EventName, EventDate, EventTime, EventStatus,
                   DownpaymentAmount, QrString, LockExpiresAt
            FROM dbo.EventAppointment WHERE EventAppointmentID = @Id;";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", appointmentId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new QrPaymentInfoDto
        {
            AppointmentId = r.GetString(0),
            CustomerId = r.GetString(1),
            EventName = r.GetString(2),
            EventDate = r.GetDateTime(3),
            EventTime = r.GetFieldValue<TimeSpan>(4),
            EventStatus = r.GetString(5),
            Amount = r.GetDecimal(6),
            QrString = r.IsDBNull(7) ? null : r.GetString(7),
            LockExpiresAt = r.IsDBNull(8) ? null : r.GetDateTime(8)
        };
    }
}
