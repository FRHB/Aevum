using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Infrastructure.Services;

public class CustomerDropboxService : ICustomerDropboxService
{
    private static volatile bool _schemaEnsured;
    private static volatile bool _bucketEnsured;
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);

    private static readonly string[] CompletedStatuses =
    [
        "Shot", "Editing", "Delivered", "Complete", "Completed"
    ];

    private readonly ApplicationDbContext _db;
    private readonly IR2StorageService _r2Storage;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CustomerDropboxService> _logger;

    public CustomerDropboxService(
        ApplicationDbContext db,
        IR2StorageService r2Storage,
        IConfiguration configuration,
        ILogger<CustomerDropboxService> logger)
    {
        _db = db;
        _r2Storage = r2Storage;
        _configuration = configuration;
        _logger = logger;
    }

    internal static readonly string[] SchemaBatches =
    {
        @"IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_DropboxID' AND schema_id = SCHEMA_ID(N'dbo'))
              CREATE SEQUENCE dbo.Seq_DropboxID AS INT START WITH 1 INCREMENT BY 1;
          IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_DropboxFileID' AND schema_id = SCHEMA_ID(N'dbo'))
              CREATE SEQUENCE dbo.Seq_DropboxFileID AS INT START WITH 1 INCREMENT BY 1;",

        @"IF OBJECT_ID(N'dbo.CustomerDropbox', N'U') IS NULL
          BEGIN
              CREATE TABLE dbo.CustomerDropbox (
                  DropboxID nvarchar(30) NOT NULL PRIMARY KEY,
                  EventAppointmentID nvarchar(30) NOT NULL,
                  CustomerID nvarchar(30) NOT NULL,
                  BucketName nvarchar(100) NOT NULL CONSTRAINT DF_CustomerDropbox_Bucket DEFAULT 'dropbox',
                  R2ObjectKey nvarchar(500) NULL,
                  OriginalFileName nvarchar(255) NULL,
                  FileSizeBytes bigint NULL,
                  ContentType nvarchar(100) NULL,
                  DropboxStatus nvarchar(30) NOT NULL CONSTRAINT DF_CustomerDropbox_Status DEFAULT 'Waiting',
                  EventCompletedAt datetime2 NOT NULL CONSTRAINT DF_CustomerDropbox_CompletedAt DEFAULT SYSUTCDATETIME(),
                  UploadedAt datetime2 NULL,
                  UploadedByEmployeeID nvarchar(30) NULL,
                  ExpiresAt datetime2 NULL,
                  CreatedAt datetime2 NOT NULL CONSTRAINT DF_CustomerDropbox_CreatedAt DEFAULT SYSUTCDATETIME(),
                  CONSTRAINT UQ_CustomerDropbox_Event UNIQUE (EventAppointmentID),
                  CONSTRAINT CK_CustomerDropbox_Status CHECK (DropboxStatus IN ('Waiting', 'Uploaded', 'Expired'))
              );
          END;

          IF OBJECT_ID(N'dbo.CustomerDropboxFiles', N'U') IS NULL
          BEGIN
              CREATE TABLE dbo.CustomerDropboxFiles (
                  DropboxFileID nvarchar(30) NOT NULL PRIMARY KEY,
                  DropboxID nvarchar(30) NOT NULL,
                  EventAppointmentID nvarchar(30) NOT NULL,
                  CustomerID nvarchar(30) NOT NULL,
                  BucketName nvarchar(100) NOT NULL CONSTRAINT DF_CustomerDropboxFiles_Bucket DEFAULT 'dropbox',
                  R2ObjectKey nvarchar(500) NULL,
                  OriginalFileName nvarchar(255) NOT NULL,
                  FileSizeBytes bigint NOT NULL,
                  ContentType nvarchar(100) NULL,
                  FileStatus nvarchar(30) NOT NULL CONSTRAINT DF_CustomerDropboxFiles_Status DEFAULT 'Active',
                  UploadedAt datetime2 NOT NULL CONSTRAINT DF_CustomerDropboxFiles_UploadedAt DEFAULT SYSUTCDATETIME(),
                  UploadedByEmployeeID nvarchar(30) NULL,
                  ExpiresAt datetime2 NOT NULL,
                  CreatedAt datetime2 NOT NULL CONSTRAINT DF_CustomerDropboxFiles_CreatedAt DEFAULT SYSUTCDATETIME(),
                  CONSTRAINT CK_CustomerDropboxFiles_Status CHECK (FileStatus IN ('Active', 'Expired')),
                  CONSTRAINT CK_CustomerDropboxFiles_Size CHECK (FileSizeBytes > 0 AND FileSizeBytes <= 524288000)
              );
          END;",

        @"IF OBJECT_ID(N'dbo.CustomerDropbox', N'U') IS NOT NULL
             AND COL_LENGTH(N'dbo.CustomerDropbox', N'ExpiresAt') IS NOT NULL
             AND COLUMNPROPERTY(OBJECT_ID(N'dbo.CustomerDropbox'), N'ExpiresAt', N'AllowsNull') = 0
          BEGIN
              ALTER TABLE dbo.CustomerDropbox ALTER COLUMN ExpiresAt datetime2 NULL;
          END;

          IF OBJECT_ID(N'dbo.CustomerDropbox', N'U') IS NOT NULL
          BEGIN
              UPDATE dbo.CustomerDropbox
              SET ExpiresAt = NULL
              WHERE DropboxStatus = 'Waiting'
                AND UploadedAt IS NULL
                AND ExpiresAt IS NOT NULL;

              UPDATE dbo.CustomerDropbox
              SET ExpiresAt = DATEADD(day, 30, UploadedAt)
              WHERE DropboxStatus = 'Uploaded'
                AND UploadedAt IS NOT NULL
                AND (ExpiresAt IS NULL OR ExpiresAt < DATEADD(day, 30, UploadedAt));
          END;

          IF OBJECT_ID(N'dbo.CustomerDropbox', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.CustomerDropboxFiles', N'U') IS NOT NULL
          BEGIN
              INSERT INTO dbo.CustomerDropboxFiles (
                  DropboxFileID, DropboxID, EventAppointmentID, CustomerID, BucketName,
                  R2ObjectKey, OriginalFileName, FileSizeBytes, ContentType, FileStatus,
                  UploadedAt, UploadedByEmployeeID, ExpiresAt, CreatedAt
              )
              SELECT
                  N'DRPF-' + RIGHT(N'0000' + CAST(NEXT VALUE FOR dbo.Seq_DropboxFileID AS nvarchar(10)), 4),
                  cd.DropboxID,
                  cd.EventAppointmentID,
                  cd.CustomerID,
                  cd.BucketName,
                  cd.R2ObjectKey,
                  COALESCE(cd.OriginalFileName, cd.EventAppointmentID + N'.zip'),
                  CASE WHEN ISNULL(cd.FileSizeBytes, 0) > 0 AND cd.FileSizeBytes <= 524288000 THEN cd.FileSizeBytes ELSE 1 END,
                  cd.ContentType,
                  CASE WHEN cd.DropboxStatus = N'Expired' THEN N'Expired' ELSE N'Active' END,
                  COALESCE(cd.UploadedAt, cd.CreatedAt, SYSUTCDATETIME()),
                  cd.UploadedByEmployeeID,
                  COALESCE(cd.ExpiresAt, DATEADD(day, 30, COALESCE(cd.UploadedAt, cd.CreatedAt, SYSUTCDATETIME()))),
                  COALESCE(cd.CreatedAt, SYSUTCDATETIME())
              FROM dbo.CustomerDropbox cd
              WHERE cd.DropboxStatus IN (N'Uploaded', N'Expired')
                AND (cd.R2ObjectKey IS NOT NULL OR cd.OriginalFileName IS NOT NULL)
                AND NOT EXISTS (
                    SELECT 1 FROM dbo.CustomerDropboxFiles cdf WHERE cdf.DropboxID = cd.DropboxID
                );
          END;",

        @"IF OBJECT_ID(N'dbo.CustomerDropbox', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.FK_CustomerDropbox_EventAppointment', N'F') IS NULL
              ALTER TABLE dbo.CustomerDropbox ADD CONSTRAINT FK_CustomerDropbox_EventAppointment
                  FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;

          IF OBJECT_ID(N'dbo.CustomerDropbox', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.Customer', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.FK_CustomerDropbox_Customer', N'F') IS NULL
              ALTER TABLE dbo.CustomerDropbox ADD CONSTRAINT FK_CustomerDropbox_Customer
                  FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID) ON DELETE NO ACTION;

          IF OBJECT_ID(N'dbo.CustomerDropboxFiles', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.CustomerDropbox', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.FK_CustomerDropboxFiles_CustomerDropbox', N'F') IS NULL
              ALTER TABLE dbo.CustomerDropboxFiles ADD CONSTRAINT FK_CustomerDropboxFiles_CustomerDropbox
                  FOREIGN KEY (DropboxID) REFERENCES dbo.CustomerDropbox(DropboxID) ON DELETE CASCADE;

          IF OBJECT_ID(N'dbo.CustomerDropboxFiles', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.FK_CustomerDropboxFiles_EventAppointment', N'F') IS NULL
              ALTER TABLE dbo.CustomerDropboxFiles ADD CONSTRAINT FK_CustomerDropboxFiles_EventAppointment
                  FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE NO ACTION;

          IF OBJECT_ID(N'dbo.CustomerDropboxFiles', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.Customer', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.FK_CustomerDropboxFiles_Customer', N'F') IS NULL
              ALTER TABLE dbo.CustomerDropboxFiles ADD CONSTRAINT FK_CustomerDropboxFiles_Customer
                  FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID) ON DELETE NO ACTION;

          IF OBJECT_ID(N'dbo.CustomerDropbox', N'U') IS NOT NULL
             AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CustomerDropbox_Customer_ExpiresAt' AND object_id = OBJECT_ID(N'dbo.CustomerDropbox'))
              CREATE INDEX IX_CustomerDropbox_Customer_ExpiresAt ON dbo.CustomerDropbox (CustomerID, ExpiresAt);

          IF OBJECT_ID(N'dbo.CustomerDropbox', N'U') IS NOT NULL
             AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CustomerDropbox_ExpiresAt' AND object_id = OBJECT_ID(N'dbo.CustomerDropbox'))
              CREATE INDEX IX_CustomerDropbox_ExpiresAt ON dbo.CustomerDropbox (ExpiresAt);

          IF OBJECT_ID(N'dbo.CustomerDropboxFiles', N'U') IS NOT NULL
             AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CustomerDropboxFiles_Dropbox_Status' AND object_id = OBJECT_ID(N'dbo.CustomerDropboxFiles'))
              CREATE INDEX IX_CustomerDropboxFiles_Dropbox_Status ON dbo.CustomerDropboxFiles (DropboxID, FileStatus, ExpiresAt);

          IF OBJECT_ID(N'dbo.CustomerDropboxFiles', N'U') IS NOT NULL
             AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CustomerDropboxFiles_ExpiresAt' AND object_id = OBJECT_ID(N'dbo.CustomerDropboxFiles'))
              CREATE INDEX IX_CustomerDropboxFiles_ExpiresAt ON dbo.CustomerDropboxFiles (ExpiresAt);"
    };

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        if (_schemaEnsured) return;
        await using var conn = await SqlResilienceHelper.OpenConnectionAsync(_db.Database.GetConnectionString(), ct);
        await EnsureSchemaOnConnectionAsync(conn, ct);
    }

    private async Task EnsureSchemaOnConnectionAsync(SqlConnection conn, CancellationToken ct)
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
            _logger.LogError(ex, "Failed to reconcile CustomerDropbox schema.");
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
        await EnsureSchemaOnConnectionAsync(conn, ct);
        return conn;
    }

    private async Task EnsureDropboxBucketAsync()
    {
        if (_bucketEnsured || !_r2Storage.IsConfigured) return;
        try
        {
            await _r2Storage.CreateBucketIfNotExistsAsync(_r2Storage.DropboxBucket);
            _bucketEnsured = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not auto-create Cloudflare R2 bucket '{Bucket}'. Upload will proceed against configured bucket.", _r2Storage.DropboxBucket);
        }
    }

    public async Task EnsureFolderForCompletedEventAsync(string eventAppointmentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventAppointmentId)) return;

        await using var conn = await OpenAsync(ct);
        await EnsureFolderForEventOnConnectionAsync(conn, eventAppointmentId.Trim(), ct);
    }

    private async Task EnsureFolderForEventOnConnectionAsync(SqlConnection conn, string eventAppointmentId, CancellationToken ct)
    {
        const string lookupSql = @"
            SELECT TOP 1
                ea.EventAppointmentID,
                ea.CustomerID,
                ea.EventStatus,
                COALESCE(ea.FinalPaymentPaidAt, SYSUTCDATETIME()) AS CompletedAt
            FROM dbo.EventAppointment ea
            WHERE ea.EventAppointmentID = @EvtId;";

        string? customerId = null;
        string? status = null;
        DateTime completedAt = DateTime.UtcNow;

        await using (var cmd = new SqlCommand(lookupSql, conn))
        {
            cmd.Parameters.AddWithValue("@EvtId", eventAppointmentId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return;
            customerId = r.GetString(1);
            status = r.IsDBNull(2) ? string.Empty : r.GetString(2);
            completedAt = r.GetDateTime(3);
        }

        if (string.IsNullOrWhiteSpace(customerId) ||
            !CompletedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        const string existsSql = "SELECT COUNT(1) FROM dbo.CustomerDropbox WHERE EventAppointmentID = @EvtId;";
        await using (var chk = new SqlCommand(existsSql, conn))
        {
            chk.Parameters.AddWithValue("@EvtId", eventAppointmentId);
            var count = Convert.ToInt32(await chk.ExecuteScalarAsync(ct));
            if (count > 0) return;
        }

        var dropboxId = await GenerateNextDropboxIdAsync(conn, ct);
        const string insertSql = @"
            INSERT INTO dbo.CustomerDropbox (
                DropboxID, EventAppointmentID, CustomerID, BucketName, DropboxStatus,
                EventCompletedAt, ExpiresAt, CreatedAt
            )
            VALUES (
                @DropboxId, @EvtId, @CustId, @Bucket, 'Waiting',
                @CompletedAt, NULL, SYSUTCDATETIME()
            );";

        await using var ins = new SqlCommand(insertSql, conn);
        ins.Parameters.AddWithValue("@DropboxId", dropboxId);
        ins.Parameters.AddWithValue("@EvtId", eventAppointmentId);
        ins.Parameters.AddWithValue("@CustId", customerId);
        ins.Parameters.AddWithValue("@Bucket", _r2Storage.DropboxBucket);
        ins.Parameters.AddWithValue("@CompletedAt", completedAt);
        await ins.ExecuteNonQueryAsync(ct);
    }

    private async Task SyncCompletedEventsAsync(SqlConnection conn, string? customerIdFilter, CancellationToken ct)
    {
        const string findMissingSql = @"
            SELECT ea.EventAppointmentID,
                   ea.CustomerID,
                   COALESCE(
                       ea.FinalPaymentPaidAt,
                       CASE
                           WHEN CAST(ea.EventDate AS datetime2) > DATEADD(day, -30, SYSUTCDATETIME())
                               THEN SYSUTCDATETIME()
                           ELSE CAST(ea.EventDate AS datetime2)
                       END
                   ) AS CompletedAt
            FROM dbo.EventAppointment ea
            WHERE (@CustId IS NULL OR LTRIM(RTRIM(@CustId)) = '' OR ea.CustomerID = @CustId)
              AND ea.EventStatus IN ('Shot', 'Editing', 'Delivered', 'Complete', 'Completed')
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.CustomerDropbox cd WHERE cd.EventAppointmentID = ea.EventAppointmentID
              );";

        var missing = new List<(string EventAppointmentId, string CustomerId, DateTime CompletedAt)>();
        await using (var cmd = new SqlCommand(findMissingSql, conn))
        {
            cmd.Parameters.AddWithValue("@CustId", (object?)customerIdFilter ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                missing.Add((r.GetString(0), r.GetString(1), r.GetDateTime(2)));
            }
        }

        foreach (var item in missing)
        {
            var dropboxId = await GenerateNextDropboxIdAsync(conn, ct);
            const string insertSql = @"
                IF NOT EXISTS (SELECT 1 FROM dbo.CustomerDropbox WHERE EventAppointmentID = @EvtId)
                INSERT INTO dbo.CustomerDropbox (
                    DropboxID, EventAppointmentID, CustomerID, BucketName, DropboxStatus,
                    EventCompletedAt, ExpiresAt, CreatedAt
                )
                VALUES (
                    @DropboxId, @EvtId, @CustId, @Bucket, 'Waiting',
                    @CompletedAt, NULL, SYSUTCDATETIME()
                );";
            await using var ins = new SqlCommand(insertSql, conn);
            ins.Parameters.AddWithValue("@DropboxId", dropboxId);
            ins.Parameters.AddWithValue("@EvtId", item.EventAppointmentId);
            ins.Parameters.AddWithValue("@CustId", item.CustomerId);
            ins.Parameters.AddWithValue("@Bucket", _r2Storage.DropboxBucket);
            ins.Parameters.AddWithValue("@CompletedAt", item.CompletedAt);
            await ins.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task<string> GenerateNextDropboxIdAsync(SqlConnection conn, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            await using var cmd = new SqlCommand("SELECT NEXT VALUE FOR dbo.Seq_DropboxID;", conn);
            var num = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            var candidate = $"DRP-{num:D4}";

            await using var chk = new SqlCommand("SELECT COUNT(1) FROM dbo.CustomerDropbox WHERE DropboxID = @Id;", conn);
            chk.Parameters.AddWithValue("@Id", candidate);
            if (Convert.ToInt32(await chk.ExecuteScalarAsync(ct)) == 0)
                return candidate;
        }

        return $"DRP-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
    }

    private static async Task<string> GenerateNextDropboxFileIdAsync(SqlConnection conn, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            await using var cmd = new SqlCommand("SELECT NEXT VALUE FOR dbo.Seq_DropboxFileID;", conn);
            var num = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            var candidate = $"DRPF-{num:D4}";

            await using var chk = new SqlCommand("SELECT COUNT(1) FROM dbo.CustomerDropboxFiles WHERE DropboxFileID = @Id;", conn);
            chk.Parameters.AddWithValue("@Id", candidate);
            if (Convert.ToInt32(await chk.ExecuteScalarAsync(ct)) == 0)
                return candidate;
        }

        return $"DRPF-{Guid.NewGuid():N}"[..13].ToUpperInvariant();
    }

    public async Task<IReadOnlyList<CustomerDropboxFolderDto>> GetCustomerDropboxesAsync(
        string customerId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            return Array.Empty<CustomerDropboxFolderDto>();

        var normalizedCustomerId = customerId.Trim();
        await using var conn = await OpenAsync(ct);
        await SyncCompletedEventsAsync(conn, normalizedCustomerId, ct);

        const string sql = @"
            SELECT cd.DropboxID,
                   cd.EventAppointmentID,
                   cd.CustomerID,
                   LTRIM(RTRIM(ISNULL(c.CustomerFirstName, '') + ' ' + ISNULL(c.CustomerLastName, ''))) AS CustomerName,
                   ISNULL(c.CustomerEmail, '') AS CustomerEmail,
                   ea.EventName,
                   ISNULL(ea.EventDescription, '') AS EventDescription,
                   ea.EventDate,
                   ea.EventTime,
                   ea.EventStatus,
                   cd.DropboxStatus,
                   cd.OriginalFileName,
                   cd.FileSizeBytes,
                   cd.EventCompletedAt,
                   cd.UploadedAt,
                   cd.ExpiresAt,
                   CASE
                       WHEN ISNULL(ea.IsDeliverablesUnlocked, 0) = 1 AND ISNULL(ea.RemainingBalance, 0) <= 0 THEN CAST(0 AS decimal(18,2))
                       WHEN ea.FinalPaymentStatus IN ('Paid', 'Payment Verification (QR:Paid)') AND ISNULL(ea.RemainingBalance, 0) <= 0 THEN CAST(0 AS decimal(18,2))
                       WHEN ISNULL(ea.RemainingBalance, 0) > 0 THEN ea.RemainingBalance
                       WHEN ea.FinalBalanceAmount > 0 AND ISNULL(ea.FinalPaymentStatus, '') NOT IN ('Paid', 'Payment Verification (QR:Paid)') THEN ea.FinalBalanceAmount
                       ELSE CAST(0 AS decimal(18,2))
                   END AS EffectiveRemainingBalance,
                   CASE
                       WHEN ISNULL(ea.IsDeliverablesUnlocked, 0) = 1 AND ISNULL(ea.RemainingBalance, 0) <= 0 THEN CAST(1 AS bit)
                       WHEN ea.FinalPaymentStatus IN ('Paid', 'Payment Verification (QR:Paid)') AND ISNULL(ea.RemainingBalance, 0) <= 0 THEN CAST(1 AS bit)
                       WHEN ISNULL(ea.TotalAmount, 0) <= 0 AND ea.EventStatus IN ('Completed', 'Complete', 'Delivered') THEN CAST(1 AS bit)
                       ELSE CAST(0 AS bit)
                   END AS DeliverablesUnlocked
            FROM dbo.CustomerDropbox cd
            INNER JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = cd.EventAppointmentID
            LEFT JOIN dbo.Customer c ON c.CustomerID = cd.CustomerID
            WHERE cd.CustomerID = @CustomerId
            ORDER BY
                CASE
                    WHEN cd.DropboxStatus = 'Uploaded' AND (cd.ExpiresAt IS NULL OR cd.ExpiresAt > SYSUTCDATETIME()) THEN 0
                    WHEN cd.DropboxStatus = 'Waiting' THEN 1
                    ELSE 2
                END,
                cd.EventCompletedAt DESC,
                cd.CreatedAt DESC;";

        var rows = new List<CustomerDropboxFolderDto>();
        await using (var cmd = new SqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("@CustomerId", normalizedCustomerId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var nowUtc = DateTime.UtcNow;
            while (await r.ReadAsync(ct))
            {
                rows.Add(MapFolder(r, nowUtc, DropboxCountdownHelper.DefaultDeliveryBusinessDays));
            }
        }

        await LoadFilesForFoldersAsync(conn, rows, DateTime.UtcNow, ct);
        await ApplyDeliveryWindowsAsync(conn, rows, DateTime.UtcNow, ct);
        return rows;
    }

    public async Task<IReadOnlyList<CustomerDropboxFolderDto>> GetAdminEventContainersAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await SyncCompletedEventsAsync(conn, null, ct);

        const string sql = @"
            SELECT cd.DropboxID,
                   cd.EventAppointmentID,
                   cd.CustomerID,
                   LTRIM(RTRIM(ISNULL(c.CustomerFirstName, '') + ' ' + ISNULL(c.CustomerLastName, ''))) AS CustomerName,
                   ISNULL(c.CustomerEmail, '') AS CustomerEmail,
                   ea.EventName,
                   ISNULL(ea.EventDescription, '') AS EventDescription,
                   ea.EventDate,
                   ea.EventTime,
                   ea.EventStatus,
                   cd.DropboxStatus,
                   cd.OriginalFileName,
                   cd.FileSizeBytes,
                   cd.EventCompletedAt,
                   cd.UploadedAt,
                   cd.ExpiresAt
            FROM dbo.CustomerDropbox cd
            INNER JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = cd.EventAppointmentID
            LEFT JOIN dbo.Customer c ON c.CustomerID = cd.CustomerID
            ORDER BY
                CASE
                    WHEN cd.DropboxStatus = 'Waiting' THEN 0
                    WHEN cd.DropboxStatus = 'Uploaded' AND (cd.ExpiresAt IS NULL OR cd.ExpiresAt > SYSUTCDATETIME()) THEN 1
                    ELSE 2
                END,
                cd.EventCompletedAt DESC;";

        var rows = new List<CustomerDropboxFolderDto>();
        await using (var cmd = new SqlCommand(sql, conn))
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var nowUtc = DateTime.UtcNow;
            while (await r.ReadAsync(ct))
            {
                rows.Add(MapFolder(r, nowUtc, DropboxCountdownHelper.DefaultDeliveryBusinessDays));
            }
        }

        await LoadFilesForFoldersAsync(conn, rows, DateTime.UtcNow, ct);
        await ApplyDeliveryWindowsAsync(conn, rows, DateTime.UtcNow, ct);
        return rows;
    }

    public async Task ValidateUploadTargetAsync(string eventAppointmentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventAppointmentId))
            throw new ArgumentException("Event appointment ID is required.", nameof(eventAppointmentId));

        await using var conn = await OpenAsync(ct);
        await EnsureFolderForEventOnConnectionAsync(conn, eventAppointmentId.Trim(), ct);

        const string fetchSql = @"
            SELECT TOP 1 cd.DropboxStatus, cd.ExpiresAt
            FROM dbo.CustomerDropbox cd
            WHERE cd.EventAppointmentID = @EvtId;";

        await using var cmd = new SqlCommand(fetchSql, conn);
        cmd.Parameters.AddWithValue("@EvtId", eventAppointmentId.Trim());
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            throw new KeyNotFoundException($"No completed event Dropbox container found for appointment '{eventAppointmentId}'.");

        var status = r.IsDBNull(0) ? DropboxStatuses.Waiting : r.GetString(0);
        DateTime? expiresAt = r.IsDBNull(1) ? null : DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc);
        if (IsDropboxExpired(status, expiresAt, DateTime.UtcNow))
            throw new InvalidOperationException($"The 30-day Dropbox retention window for event '{eventAppointmentId}' has expired and is no longer available.");
    }

    public Task<CustomerDropboxFolderDto> UploadZipAsync(
        string eventAppointmentId,
        Stream zipStream,
        string originalFileName,
        long fileSizeBytes,
        string? contentType,
        string uploadedByEmployeeId,
        CancellationToken ct = default)
        => UploadZipAsync(eventAppointmentId, zipStream, originalFileName, fileSizeBytes, contentType, uploadedByEmployeeId, null, ct);

    public async Task<CustomerDropboxFolderDto> UploadZipAsync(
        string eventAppointmentId,
        Stream zipStream,
        string originalFileName,
        long fileSizeBytes,
        string? contentType,
        string uploadedByEmployeeId,
        Action<long>? onR2BytesTransferred,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventAppointmentId))
            throw new ArgumentException("Event appointment ID is required.", nameof(eventAppointmentId));
        if (zipStream == null)
            throw new ArgumentNullException(nameof(zipStream));
        if (fileSizeBytes <= 0)
            throw new ArgumentException("Uploaded ZIP file cannot be empty.", nameof(fileSizeBytes));
        if (fileSizeBytes > DropboxCountdownHelper.MaxZipSizeBytes)
            throw new InvalidOperationException("ZIP file must be between 1 byte and 500 MB.");

        var safeName = SanitizeFileName(originalFileName);
        if (!Path.GetExtension(safeName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .zip archives are accepted for Customer Dropbox uploads.");

        string? tempFilePath = null;
        Stream uploadStream = zipStream;
        try
        {
            if (zipStream.CanSeek)
            {
                if (zipStream.Length > DropboxCountdownHelper.MaxZipSizeBytes)
                    throw new InvalidOperationException("ZIP file must be between 1 byte and 500 MB.");
                zipStream.Position = 0;
                await ValidateZipSignatureAsync(zipStream, ct);
                zipStream.Position = 0;
            }
            else
            {
                tempFilePath = Path.Combine(Path.GetTempPath(), $"aevum-dropbox-{Guid.NewGuid():N}.zip");
                await using (var fs = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    var buffer = new byte[81920];
                    long totalCopied = 0;
                    int read;
                    while ((read = await zipStream.ReadAsync(buffer, ct)) > 0)
                    {
                        totalCopied += read;
                        if (totalCopied > DropboxCountdownHelper.MaxZipSizeBytes)
                            throw new InvalidOperationException("ZIP file must be between 1 byte and 500 MB.");
                        await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                }

                var readFs = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                uploadStream = readFs;
                await ValidateZipSignatureAsync(readFs, ct);
                readFs.Position = 0;
            }

            await using var conn = await OpenAsync(ct);
            await EnsureFolderForEventOnConnectionAsync(conn, eventAppointmentId.Trim(), ct);

            const string fetchSql = @"
                SELECT TOP 1 cd.DropboxID, cd.CustomerID, cd.BucketName, cd.R2ObjectKey,
                       cd.OriginalFileName, cd.FileSizeBytes, cd.ContentType,
                       cd.DropboxStatus, cd.UploadedAt, cd.UploadedByEmployeeID, cd.ExpiresAt
                FROM dbo.CustomerDropbox cd
                WHERE cd.EventAppointmentID = @EvtId;";

            string? dropboxId = null, customerId = null, bucketName = null, legacyKey = null;
            string? legacyFileName = null, legacyContentType = null, legacyUploadedBy = null;
            long? legacySize = null;
            string currentStatus = DropboxStatuses.Waiting;
            DateTime? legacyUploadedAt = null, expiresAt = null;

            await using (var cmd = new SqlCommand(fetchSql, conn))
            {
                cmd.Parameters.AddWithValue("@EvtId", eventAppointmentId.Trim());
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct))
                    throw new KeyNotFoundException($"No completed event Dropbox container found for appointment '{eventAppointmentId}'.");

                dropboxId = r.GetString(0);
                customerId = r.GetString(1);
                bucketName = r.IsDBNull(2) ? _r2Storage.DropboxBucket : r.GetString(2);
                legacyKey = r.IsDBNull(3) ? null : r.GetString(3);
                legacyFileName = r.IsDBNull(4) ? null : r.GetString(4);
                legacySize = r.IsDBNull(5) ? null : r.GetInt64(5);
                legacyContentType = r.IsDBNull(6) ? null : r.GetString(6);
                currentStatus = r.IsDBNull(7) ? DropboxStatuses.Waiting : r.GetString(7);
                legacyUploadedAt = r.IsDBNull(8) ? null : DateTime.SpecifyKind(r.GetDateTime(8), DateTimeKind.Utc);
                legacyUploadedBy = r.IsDBNull(9) ? null : r.GetString(9);
                expiresAt = r.IsDBNull(10) ? null : DateTime.SpecifyKind(r.GetDateTime(10), DateTimeKind.Utc);
            }

            if (IsDropboxExpired(currentStatus, expiresAt, DateTime.UtcNow))
                throw new InvalidOperationException($"The 30-day Dropbox retention window for event '{eventAppointmentId}' has expired and is no longer available.");

            // Preserve any legacy single-file record in CustomerDropboxFiles before appending the new ZIP
            if (string.Equals(currentStatus, DropboxStatuses.Uploaded, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(legacyKey))
            {
                await BackfillLegacyFileIfNeededAsync(
                    conn,
                    dropboxId!,
                    eventAppointmentId.Trim(),
                    customerId!,
                    string.IsNullOrWhiteSpace(bucketName) ? _r2Storage.DropboxBucket : bucketName.Trim(),
                    legacyKey,
                    legacyFileName ?? $"{eventAppointmentId.Trim()}.zip",
                    legacySize ?? 1L,
                    legacyContentType,
                    legacyUploadedAt ?? DateTime.UtcNow,
                    legacyUploadedBy,
                    expiresAt ?? (legacyUploadedAt ?? DateTime.UtcNow).AddDays(DropboxCountdownHelper.RetentionDays),
                    ct);
            }

            var targetBucket = string.IsNullOrWhiteSpace(bucketName) ? _r2Storage.DropboxBucket : bucketName.Trim();
            await EnsureDropboxBucketAsync();

            // Strict per-customer/per-event key prefix isolation in Cloudflare R2
            var objectKey = $"customers/{SanitizeKeySegment(customerId!)}/events/{SanitizeKeySegment(eventAppointmentId)}/{Guid.NewGuid():N}_{safeName}";
            var resolvedContentType = string.IsNullOrWhiteSpace(contentType) ? "application/zip" : contentType.Trim();

            var uploadedUri = await _r2Storage.UploadFileAsync(
                targetBucket,
                objectKey,
                uploadStream,
                resolvedContentType,
                onR2BytesTransferred,
                ct);
            if (string.IsNullOrWhiteSpace(uploadedUri))
                throw new InvalidOperationException("Cloudflare R2 upload did not return a success confirmation.");

            var dropboxFileId = await GenerateNextDropboxFileIdAsync(conn, ct);
            const string insertFileSql = @"
                INSERT INTO dbo.CustomerDropboxFiles (
                    DropboxFileID, DropboxID, EventAppointmentID, CustomerID, BucketName,
                    R2ObjectKey, OriginalFileName, FileSizeBytes, ContentType, FileStatus,
                    UploadedAt, UploadedByEmployeeID, ExpiresAt, CreatedAt
                )
                VALUES (
                    @FileId, @DropboxId, @EvtId, @CustId, @Bucket,
                    @Key, @FileName, @Size, @ContentType, 'Active',
                    SYSUTCDATETIME(), NULLIF(@UploadedBy, ''), DATEADD(day, 30, SYSUTCDATETIME()), SYSUTCDATETIME()
                );";

            await using (var insFile = new SqlCommand(insertFileSql, conn))
            {
                insFile.Parameters.AddWithValue("@FileId", dropboxFileId);
                insFile.Parameters.AddWithValue("@DropboxId", dropboxId!);
                insFile.Parameters.AddWithValue("@EvtId", eventAppointmentId.Trim());
                insFile.Parameters.AddWithValue("@CustId", customerId!);
                insFile.Parameters.AddWithValue("@Bucket", targetBucket);
                insFile.Parameters.AddWithValue("@Key", objectKey);
                insFile.Parameters.AddWithValue("@FileName", safeName);
                insFile.Parameters.AddWithValue("@Size", fileSizeBytes);
                insFile.Parameters.AddWithValue("@ContentType", resolvedContentType);
                insFile.Parameters.AddWithValue("@UploadedBy", uploadedByEmployeeId ?? string.Empty);
                await insFile.ExecuteNonQueryAsync(ct);
            }

            const string updateSql = @"
                UPDATE dbo.CustomerDropbox
                SET BucketName = @Bucket,
                    R2ObjectKey = @Key,
                    OriginalFileName = @FileName,
                    FileSizeBytes = @Size,
                    ContentType = @ContentType,
                    DropboxStatus = 'Uploaded',
                    UploadedAt = COALESCE(UploadedAt, SYSUTCDATETIME()),
                    ExpiresAt = DATEADD(day, 30, SYSUTCDATETIME()),
                    UploadedByEmployeeID = NULLIF(@UploadedBy, '')
                WHERE DropboxID = @DropboxId;";

            await using (var upd = new SqlCommand(updateSql, conn))
            {
                upd.Parameters.AddWithValue("@Bucket", targetBucket);
                upd.Parameters.AddWithValue("@Key", objectKey);
                upd.Parameters.AddWithValue("@FileName", safeName);
                upd.Parameters.AddWithValue("@Size", fileSizeBytes);
                upd.Parameters.AddWithValue("@ContentType", resolvedContentType);
                upd.Parameters.AddWithValue("@UploadedBy", uploadedByEmployeeId ?? string.Empty);
                upd.Parameters.AddWithValue("@DropboxId", dropboxId!);
                await upd.ExecuteNonQueryAsync(ct);
            }

            await SyncParentDropboxSummaryAsync(conn, dropboxId!, ct);

            try
            {
                const string historySql = @"
                    IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                    BEGIN
                        DECLARE @EmpActor nvarchar(30) = NULL;
                        DECLARE @AdmActor nvarchar(30) = NULL;
                        DECLARE @ActorType nvarchar(20) = 'System';

                        IF NULLIF(@UploadedBy, '') IS NOT NULL
                        BEGIN
                            IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
                               AND EXISTS (SELECT 1 FROM dbo.Employees WHERE EmployeeID = @UploadedBy)
                            BEGIN
                                SET @EmpActor = @UploadedBy;
                                SET @ActorType = 'Employee';
                            END
                            ELSE IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL
                               AND EXISTS (SELECT 1 FROM dbo.Admin WHERE AdminID = @UploadedBy)
                            BEGIN
                                SET @AdmActor = @UploadedBy;
                                SET @ActorType = 'Admin';
                            END
                            ELSE IF @UploadedBy LIKE 'SADM-%'
                                SET @ActorType = 'SystemAdmin';
                            ELSE IF @UploadedBy LIKE 'ADM-%'
                                SET @ActorType = 'Admin';
                            ELSE IF @UploadedBy LIKE 'EMP-%' OR @UploadedBy LIKE 'CLK-%'
                                SET @ActorType = 'Employee';
                        END;

                        INSERT INTO dbo.HistoryLog (
                            ActionType, EntityName, EntityID, ActionDetails,
                            EventAppointmentID, CustomerID, ChangedByType, ChangedByEmployeeID, ChangedByAdminID, LoggedAt
                        )
                        VALUES (
                            'IMAGE_UPLOAD', 'CustomerDropbox', @DropboxId,
                            'Uploaded customer gallery ZIP (' + @FileName + ') to Cloudflare R2.',
                            @EvtId, @CustId, @ActorType, @EmpActor, @AdmActor, SYSUTCDATETIME()
                        );
                    END;";

                await using var histCmd = new SqlCommand(historySql, conn);
                histCmd.Parameters.AddWithValue("@DropboxId", dropboxId!);
                histCmd.Parameters.AddWithValue("@FileName", safeName);
                histCmd.Parameters.AddWithValue("@EvtId", eventAppointmentId.Trim());
                histCmd.Parameters.AddWithValue("@CustId", customerId!);
                histCmd.Parameters.AddWithValue("@UploadedBy", uploadedByEmployeeId ?? string.Empty);
                await histCmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Non-fatal HistoryLog write skipped after CustomerDropbox ZIP upload {DropboxId}.", dropboxId);
            }

            const string readBackSql = @"
                SELECT TOP 1
                       cd.DropboxID,
                       cd.EventAppointmentID,
                       cd.CustomerID,
                       LTRIM(RTRIM(ISNULL(c.CustomerFirstName, '') + ' ' + ISNULL(c.CustomerLastName, ''))) AS CustomerName,
                       ISNULL(c.CustomerEmail, '') AS CustomerEmail,
                       ea.EventName,
                       ISNULL(ea.EventDescription, '') AS EventDescription,
                       ea.EventDate,
                       ea.EventTime,
                       ea.EventStatus,
                       cd.DropboxStatus,
                       cd.OriginalFileName,
                       cd.FileSizeBytes,
                       cd.EventCompletedAt,
                       cd.UploadedAt,
                       cd.ExpiresAt
                FROM dbo.CustomerDropbox cd
                INNER JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = cd.EventAppointmentID
                LEFT JOIN dbo.Customer c ON c.CustomerID = cd.CustomerID
                WHERE cd.DropboxID = @DropboxId;";

            CustomerDropboxFolderDto dto;
            await using (var readCmd = new SqlCommand(readBackSql, conn))
            {
                readCmd.Parameters.AddWithValue("@DropboxId", dropboxId!);
                await using var reader = await readCmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    throw new InvalidOperationException("Uploaded Dropbox record could not be reloaded.");

                dto = MapFolder(reader, DateTime.UtcNow, DropboxCountdownHelper.DefaultDeliveryBusinessDays);
            }

            await LoadFilesForFoldersAsync(conn, [dto], DateTime.UtcNow, ct);
            await ApplyDeliveryWindowsAsync(conn, [dto], DateTime.UtcNow, ct);
            return dto;
        }
        finally
        {
            if (uploadStream != zipStream)
                await uploadStream.DisposeAsync();

            if (!string.IsNullOrEmpty(tempFilePath) && File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { }
            }
        }
    }

    public Task<(string FileName, string ContentType, Stream FileStream, string PresignedUrl)> GetCustomerDownloadAsync(
        string dropboxId,
        string callerCustomerId,
        CancellationToken ct = default)
        => GetCustomerDownloadAsync(dropboxId, callerCustomerId, null, ct);

    public async Task<(string FileName, string ContentType, Stream FileStream, string PresignedUrl)> GetCustomerDownloadAsync(
        string dropboxId,
        string callerCustomerId,
        string? dropboxFileId,
        CancellationToken ct = default)
    {
        var row = await GetAuthorizedDownloadRecordAsync(dropboxId, callerCustomerId, dropboxFileId, ct);
        var presigned = _r2Storage.GetPresignedDownloadUrl(row.BucketName, row.R2ObjectKey, row.FileName, TimeSpan.FromMinutes(15));
        var stream = await _r2Storage.DownloadFileAsync(row.BucketName, row.R2ObjectKey);
        return (row.FileName, row.ContentType, stream, presigned);
    }

    public Task<DropboxDownloadUrlDto> GetCustomerDownloadInfoAsync(
        string dropboxId,
        string callerCustomerId,
        string apiBaseUrl,
        CancellationToken ct = default)
        => GetCustomerDownloadInfoAsync(dropboxId, callerCustomerId, apiBaseUrl, null, ct);

    public async Task<DropboxDownloadUrlDto> GetCustomerDownloadInfoAsync(
        string dropboxId,
        string callerCustomerId,
        string apiBaseUrl,
        string? dropboxFileId,
        CancellationToken ct = default)
    {
        var row = await GetAuthorizedDownloadRecordAsync(dropboxId, callerCustomerId, dropboxFileId, ct);
        var presigned = _r2Storage.GetPresignedDownloadUrl(row.BucketName, row.R2ObjectKey, row.FileName, TimeSpan.FromMinutes(15));

        if (string.IsNullOrWhiteSpace(presigned))
        {
            var token = CreateSignedDownloadToken(row.DropboxId, row.CustomerId, TimeSpan.FromMinutes(10), row.DropboxFileId);
            var basePrefix = string.IsNullOrWhiteSpace(apiBaseUrl) ? string.Empty : apiBaseUrl.TrimEnd('/');
            presigned = $"{basePrefix}/api/dropbox/download/{Uri.EscapeDataString(token)}";
        }

        return new DropboxDownloadUrlDto
        {
            DropboxId = row.DropboxId,
            DropboxFileId = row.DropboxFileId,
            EventAppointmentId = row.EventAppointmentId,
            FileName = row.FileName,
            FileSizeBytes = row.FileSizeBytes,
            DownloadUrl = presigned,
            ExpiresAt = row.ExpiresAt
        };
    }

    public string CreateSignedDownloadToken(string dropboxId, string customerId, TimeSpan ttl)
        => CreateSignedDownloadToken(dropboxId, customerId, ttl, null);

    public string CreateSignedDownloadToken(string dropboxId, string customerId, TimeSpan ttl, string? dropboxFileId)
    {
        var exp = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();
        var payload = string.IsNullOrWhiteSpace(dropboxFileId)
            ? $"{dropboxId}|{customerId}|{exp}"
            : $"{dropboxId}|{customerId}|{exp}|{dropboxFileId.Trim()}";
        var sig = ComputeHmac(payload);
        var raw = $"{payload}|{sig}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public (bool Valid, string DropboxId, string CustomerId) ValidateSignedDownloadToken(string token)
    {
        var (valid, dropboxId, customerId, _) = ValidateSignedDownloadTokenWithFile(token);
        return (valid, dropboxId, customerId);
    }

    public (bool Valid, string DropboxId, string CustomerId, string? DropboxFileId) ValidateSignedDownloadTokenWithFile(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (false, string.Empty, string.Empty, null);

        try
        {
            var padded = token.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var parts = decoded.Split('|');
            if (parts.Length is not (4 or 5))
                return (false, string.Empty, string.Empty, null);

            if (!long.TryParse(parts[2], out var expUnix))
                return (false, string.Empty, string.Empty, null);

            if (DateTimeOffset.FromUnixTimeSeconds(expUnix) <= DateTimeOffset.UtcNow)
                return (false, string.Empty, string.Empty, null);

            var payload = parts.Length == 5
                ? $"{parts[0]}|{parts[1]}|{parts[2]}|{parts[3]}"
                : $"{parts[0]}|{parts[1]}|{parts[2]}";
            var expectedSig = ComputeHmac(payload);
            var actualBytes = Encoding.UTF8.GetBytes(parts[^1]);
            var expectedBytes = Encoding.UTF8.GetBytes(expectedSig);

            if (actualBytes.Length != expectedBytes.Length ||
                !CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes))
            {
                return (false, string.Empty, string.Empty, null);
            }

            return (true, parts[0], parts[1], parts.Length == 5 ? parts[3] : null);
        }
        catch
        {
            return (false, string.Empty, string.Empty, null);
        }
    }

    private string ComputeHmac(string payload)
    {
        // Use dedicated Dropbox signing key (never share Jwt:Key or a hardcoded fallback secret)
        var key = _configuration["Dropbox:SigningKey"]
            ?? _configuration["Dropbox__SigningKey"]
            ?? _configuration["CloudflareR2:DropboxSigningKey"]
            ?? _configuration["CloudflareR2__DropboxSigningKey"]
            ?? Environment.GetEnvironmentVariable("DROPBOX_SIGNING_KEY");

        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
        {
            throw new InvalidOperationException(
                "Dropbox:SigningKey must be configured with at least 32 characters.");
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    private async Task<(string DropboxId, string? DropboxFileId, string EventAppointmentId, string CustomerId, string BucketName, string R2ObjectKey, string FileName, long FileSizeBytes, string ContentType, DateTime ExpiresAt)> GetAuthorizedDownloadRecordAsync(
        string dropboxId,
        string callerCustomerId,
        string? dropboxFileId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dropboxId))
            throw new ArgumentException("Dropbox ID is required.", nameof(dropboxId));
        if (string.IsNullOrWhiteSpace(callerCustomerId))
            throw new UnauthorizedAccessException("Customer identity is required.");

        await using var conn = await OpenAsync(ct);

        const string sql = @"
            SELECT TOP 1
                   cd.DropboxID, cd.EventAppointmentID, cd.CustomerID, cd.BucketName, cd.R2ObjectKey,
                   cd.OriginalFileName, cd.FileSizeBytes, cd.ContentType, cd.DropboxStatus, cd.ExpiresAt,
                   ea.EventStatus,
                   ISNULL(ea.TotalAmount, 0) AS TotalAmount,
                   ISNULL(ea.OvertimeAmount, 0) AS OvertimeAmount,
                   ISNULL(ea.FinalBalanceAmount, 0) AS FinalBalanceAmount,
                   ISNULL(ea.RemainingBalance, 0) AS RemainingBalance,
                   ea.FinalPaymentStatus,
                   ISNULL(ea.IsDeliverablesUnlocked, 0) AS IsDeliverablesUnlocked
            FROM dbo.CustomerDropbox cd
            LEFT JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = cd.EventAppointmentID
            WHERE cd.DropboxID = @DropboxId;";

        string dbDropboxId, evtId, ownerCustomerId, bucket, key, fileName, contentType, status, evtStatus;
        long fileSize;
        DateTime? expiresAt;
        decimal totalAmt, finalBalAmt, remainingBal;
        string? finalPayStatus;
        bool unlockedFlag;

        await using (var cmd = new SqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("@DropboxId", dropboxId.Trim());
            await using var r = await cmd.ExecuteReaderAsync(ct);

            if (!await r.ReadAsync(ct))
                throw new KeyNotFoundException($"Dropbox folder '{dropboxId}' was not found.");

            dbDropboxId = r.GetString(0);
            evtId = r.GetString(1);
            ownerCustomerId = r.GetString(2);
            bucket = r.IsDBNull(3) ? _r2Storage.DropboxBucket : r.GetString(3);
            key = r.IsDBNull(4) ? string.Empty : r.GetString(4);
            fileName = r.IsDBNull(5) ? $"{evtId}.zip" : r.GetString(5);
            fileSize = r.IsDBNull(6) ? 0L : r.GetInt64(6);
            contentType = r.IsDBNull(7) ? "application/zip" : r.GetString(7);
            status = r.IsDBNull(8) ? DropboxStatuses.Waiting : r.GetString(8);
            expiresAt = r.IsDBNull(9) ? null : DateTime.SpecifyKind(r.GetDateTime(9), DateTimeKind.Utc);
            evtStatus = r.FieldCount > 10 && !r.IsDBNull(10) ? r.GetString(10) : string.Empty;
            totalAmt = r.FieldCount > 11 && !r.IsDBNull(11) ? r.GetDecimal(11) : 0m;
            finalBalAmt = r.FieldCount > 13 && !r.IsDBNull(13) ? r.GetDecimal(13) : 0m;
            remainingBal = r.FieldCount > 14 && !r.IsDBNull(14) ? r.GetDecimal(14) : 0m;
            finalPayStatus = r.FieldCount > 15 && !r.IsDBNull(15) ? r.GetString(15) : null;
            unlockedFlag = r.FieldCount > 16 && !r.IsDBNull(16) && r.GetBoolean(16);
        }

        // Strict customer isolation enforcement
        if (!string.Equals(ownerCustomerId, callerCustomerId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Blocked unauthorized Dropbox download attempt: caller '{CallerId}' tried to access Dropbox '{DropboxId}' owned by '{OwnerId}'.",
                callerCustomerId, dbDropboxId, ownerCustomerId);
            throw new UnauthorizedAccessException("You are not authorized to access this customer's Dropbox.");
        }

        // Server-side Deliverable Release Gate: remaining balance must be <= 0 and final settlement verified
        var finalSettled = string.Equals(finalPayStatus, "Paid", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(finalPayStatus, "Payment Verification (QR:Paid)", StringComparison.OrdinalIgnoreCase);

        if (remainingBal > 0m || (!unlockedFlag && !finalSettled && (totalAmt > 0m || finalBalAmt > 0m)))
        {
            var owed = remainingBal > 0m ? remainingBal : finalBalAmt;
            throw new InvalidOperationException(
                owed > 0m
                    ? $"Deliverables are locked until your remaining balance of PHP {owed:N2} is settled in full (0.00 balance due)."
                    : "Deliverables are locked until final settlement is verified and completed.");
        }

        if (string.Equals(evtStatus, "Paid Cash Pending Verification", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(evtStatus, "Payment Verification (Cash)", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(evtStatus, "Pending Payment", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Deliverables are locked until final settlement verification is complete.");
        }

        string? resolvedFileId = null;
        if (!string.IsNullOrWhiteSpace(dropboxFileId))
        {
            const string fileSql = @"
                SELECT TOP 1 DropboxFileID, BucketName, R2ObjectKey, OriginalFileName, FileSizeBytes, ContentType, FileStatus, ExpiresAt
                FROM dbo.CustomerDropboxFiles
                WHERE DropboxFileID = @FileId AND DropboxID = @DropboxId;";

            await using var fileCmd = new SqlCommand(fileSql, conn);
            fileCmd.Parameters.AddWithValue("@FileId", dropboxFileId.Trim());
            fileCmd.Parameters.AddWithValue("@DropboxId", dbDropboxId);
            await using var fr = await fileCmd.ExecuteReaderAsync(ct);

            if (!await fr.ReadAsync(ct))
                throw new KeyNotFoundException($"Dropbox ZIP file '{dropboxFileId}' was not found in this folder.");

            resolvedFileId = fr.GetString(0);
            bucket = fr.IsDBNull(1) ? bucket : fr.GetString(1);
            key = fr.IsDBNull(2) ? string.Empty : fr.GetString(2);
            fileName = fr.IsDBNull(3) ? fileName : fr.GetString(3);
            fileSize = fr.IsDBNull(4) ? 0L : fr.GetInt64(4);
            contentType = fr.IsDBNull(5) ? "application/zip" : fr.GetString(5);
            var fileStatus = fr.IsDBNull(6) ? DropboxFileStatuses.Active : fr.GetString(6);
            var fileExpiresAt = DateTime.SpecifyKind(fr.GetDateTime(7), DateTimeKind.Utc);
            expiresAt = fileExpiresAt;

            if (string.Equals(fileStatus, DropboxFileStatuses.Expired, StringComparison.OrdinalIgnoreCase) || fileExpiresAt <= DateTime.UtcNow)
                throw new InvalidOperationException("This ZIP archive has expired after its 30-day retention period and is no longer available.");

            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("This ZIP archive is no longer available.");

            return (dbDropboxId, resolvedFileId, evtId, ownerCustomerId, bucket, key, fileName, fileSize, contentType, fileExpiresAt);
        }

        // No specific fileId requested: prefer newest active file from CustomerDropboxFiles, else legacy CustomerDropbox columns
        const string latestFileSql = @"
            SELECT TOP 1 DropboxFileID, BucketName, R2ObjectKey, OriginalFileName, FileSizeBytes, ContentType, FileStatus, ExpiresAt
            FROM dbo.CustomerDropboxFiles
            WHERE DropboxID = @DropboxId
            ORDER BY CASE WHEN FileStatus = 'Active' AND ExpiresAt > SYSUTCDATETIME() THEN 0 ELSE 1 END,
                     UploadedAt DESC, CreatedAt DESC;";

        await using (var latestCmd = new SqlCommand(latestFileSql, conn))
        {
            latestCmd.Parameters.AddWithValue("@DropboxId", dbDropboxId);
            await using var lr = await latestCmd.ExecuteReaderAsync(ct);
            if (await lr.ReadAsync(ct))
            {
                resolvedFileId = lr.GetString(0);
                bucket = lr.IsDBNull(1) ? bucket : lr.GetString(1);
                key = lr.IsDBNull(2) ? string.Empty : lr.GetString(2);
                fileName = lr.IsDBNull(3) ? fileName : lr.GetString(3);
                fileSize = lr.IsDBNull(4) ? 0L : lr.GetInt64(4);
                contentType = lr.IsDBNull(5) ? "application/zip" : lr.GetString(5);
                var fileStatus = lr.IsDBNull(6) ? DropboxFileStatuses.Active : lr.GetString(6);
                var fileExpiresAt = DateTime.SpecifyKind(lr.GetDateTime(7), DateTimeKind.Utc);
                expiresAt = fileExpiresAt;

                if (string.Equals(fileStatus, DropboxFileStatuses.Expired, StringComparison.OrdinalIgnoreCase) || fileExpiresAt <= DateTime.UtcNow)
                    throw new InvalidOperationException("This Dropbox gallery has expired after the 30-day retention period and is no longer available.");

                if (string.IsNullOrWhiteSpace(key))
                    throw new InvalidOperationException("No ZIP archive is available in this Dropbox folder.");

                return (dbDropboxId, resolvedFileId, evtId, ownerCustomerId, bucket, key, fileName, fileSize, contentType, fileExpiresAt);
            }
        }

        if (IsDropboxExpired(status, expiresAt, DateTime.UtcNow))
            throw new InvalidOperationException("This Dropbox gallery has expired after the 30-day retention period and is no longer available.");

        if (!string.Equals(status, DropboxStatuses.Uploaded, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("No ZIP archive has been uploaded to this Dropbox folder yet.");

        return (dbDropboxId, null, evtId, ownerCustomerId, bucket, key, fileName, fileSize, contentType, expiresAt ?? DateTime.UtcNow.AddDays(DropboxCountdownHelper.RetentionDays));
    }

    public async Task<CustomerDropboxFolderDto> DeleteDropboxFileAsync(
        string eventAppointmentId,
        string dropboxFileId,
        string deletedByEmployeeId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventAppointmentId))
            throw new ArgumentException("Event appointment ID is required.", nameof(eventAppointmentId));
        if (string.IsNullOrWhiteSpace(dropboxFileId))
            throw new ArgumentException("Dropbox file ID is required.", nameof(dropboxFileId));

        await using var conn = await OpenAsync(ct);

        const string lookupSql = @"
            SELECT TOP 1 cdf.DropboxFileID, cdf.DropboxID, cdf.EventAppointmentID, cdf.CustomerID,
                   cdf.BucketName, cdf.R2ObjectKey, cdf.OriginalFileName
            FROM dbo.CustomerDropboxFiles cdf
            WHERE cdf.DropboxFileID = @FileId AND cdf.EventAppointmentID = @EvtId;";

        string? dropboxId = null, customerId = null, bucket = null, objectKey = null, fileName = null;
        await using (var cmd = new SqlCommand(lookupSql, conn))
        {
            cmd.Parameters.AddWithValue("@FileId", dropboxFileId.Trim());
            cmd.Parameters.AddWithValue("@EvtId", eventAppointmentId.Trim());
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
                throw new KeyNotFoundException($"Dropbox ZIP file '{dropboxFileId}' was not found for event '{eventAppointmentId}'.");

            dropboxId = r.GetString(1);
            customerId = r.GetString(3);
            bucket = r.IsDBNull(4) ? _r2Storage.DropboxBucket : r.GetString(4);
            objectKey = r.IsDBNull(5) ? null : r.GetString(5);
            fileName = r.IsDBNull(6) ? dropboxFileId : r.GetString(6);
        }

        if (!string.IsNullOrWhiteSpace(objectKey))
        {
            var deleted = await _r2Storage.DeleteFileAsync(bucket!, objectKey);
            if (!deleted && _r2Storage.IsConfigured)
                throw new InvalidOperationException($"Cloudflare R2 failed to delete '{fileName}'. Please try again.");
        }

        const string deleteSql = @"
            DELETE cdf
            FROM dbo.CustomerDropboxFiles cdf
            WHERE cdf.DropboxFileID = @FileId;";

        await using (var delCmd = new SqlCommand(deleteSql, conn))
        {
            delCmd.Parameters.AddWithValue("@FileId", dropboxFileId.Trim());
            await delCmd.ExecuteNonQueryAsync(ct);
        }

        await SyncParentDropboxSummaryAsync(conn, dropboxId!, ct);

        try
        {
            const string historySql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    DECLARE @EmpActor nvarchar(30) = NULL;
                    DECLARE @AdmActor nvarchar(30) = NULL;
                    DECLARE @ActorType nvarchar(20) = 'System';

                    IF NULLIF(@DeletedBy, '') IS NOT NULL
                    BEGIN
                        IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
                           AND EXISTS (SELECT 1 FROM dbo.Employees WHERE EmployeeID = @DeletedBy)
                        BEGIN
                            SET @EmpActor = @DeletedBy;
                            SET @ActorType = 'Employee';
                        END
                        ELSE IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL
                           AND EXISTS (SELECT 1 FROM dbo.Admin WHERE AdminID = @DeletedBy)
                        BEGIN
                            SET @AdmActor = @DeletedBy;
                            SET @ActorType = 'Admin';
                        END
                        ELSE IF @DeletedBy LIKE 'SADM-%'
                            SET @ActorType = 'SystemAdmin';
                        ELSE IF @DeletedBy LIKE 'ADM-%'
                            SET @ActorType = 'Admin';
                        ELSE IF @DeletedBy LIKE 'EMP-%' OR @DeletedBy LIKE 'CLK-%'
                            SET @ActorType = 'Employee';
                    END;

                    INSERT INTO dbo.HistoryLog (
                        ActionType, EntityName, EntityID, ActionDetails,
                        EventAppointmentID, CustomerID, ChangedByType, ChangedByEmployeeID, ChangedByAdminID, LoggedAt
                    )
                    VALUES (
                        'DELETE', 'CustomerDropbox', @DropboxId,
                        'Deleted customer gallery ZIP (' + @FileName + ') from Cloudflare R2.',
                        @EvtId, @CustId, @ActorType, @EmpActor, @AdmActor, SYSUTCDATETIME()
                    );
                END;";

            await using var histCmd = new SqlCommand(historySql, conn);
            histCmd.Parameters.AddWithValue("@DropboxId", dropboxId!);
            histCmd.Parameters.AddWithValue("@FileName", fileName!);
            histCmd.Parameters.AddWithValue("@EvtId", eventAppointmentId.Trim());
            histCmd.Parameters.AddWithValue("@CustId", customerId!);
            histCmd.Parameters.AddWithValue("@DeletedBy", deletedByEmployeeId ?? string.Empty);
            await histCmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Non-fatal HistoryLog write skipped after deleting Dropbox ZIP {FileId}.", dropboxFileId);
        }

        const string readBackSql = @"
            SELECT TOP 1
                   cd.DropboxID,
                   cd.EventAppointmentID,
                   cd.CustomerID,
                   LTRIM(RTRIM(ISNULL(c.CustomerFirstName, '') + ' ' + ISNULL(c.CustomerLastName, ''))) AS CustomerName,
                   ISNULL(c.CustomerEmail, '') AS CustomerEmail,
                   ea.EventName,
                   ISNULL(ea.EventDescription, '') AS EventDescription,
                   ea.EventDate,
                   ea.EventTime,
                   ea.EventStatus,
                   cd.DropboxStatus,
                   cd.OriginalFileName,
                   cd.FileSizeBytes,
                   cd.EventCompletedAt,
                   cd.UploadedAt,
                   cd.ExpiresAt
            FROM dbo.CustomerDropbox cd
            INNER JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = cd.EventAppointmentID
            LEFT JOIN dbo.Customer c ON c.CustomerID = cd.CustomerID
            WHERE cd.DropboxID = @DropboxId;";

        CustomerDropboxFolderDto dto;
        await using (var readCmd = new SqlCommand(readBackSql, conn))
        {
            readCmd.Parameters.AddWithValue("@DropboxId", dropboxId!);
            await using var reader = await readCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException("Dropbox record could not be reloaded.");

            dto = MapFolder(reader, DateTime.UtcNow, DropboxCountdownHelper.DefaultDeliveryBusinessDays);
        }

        await LoadFilesForFoldersAsync(conn, [dto], DateTime.UtcNow, ct);
        await ApplyDeliveryWindowsAsync(conn, [dto], DateTime.UtcNow, ct);
        return dto;
    }

    public async Task<DropboxPurgeResultDto> PurgeExpiredDropboxesAsync(CancellationToken ct = default)
    {
        var result = new DropboxPurgeResultDto();
        await using var conn = await OpenAsync(ct);

        // 1. Purge individual expired ZIP files (independent 30-day expiration per ZIP file)
        const string selectExpiredFilesSql = @"
            SELECT DropboxFileID, DropboxID, EventAppointmentID, CustomerID, BucketName, R2ObjectKey, OriginalFileName
            FROM dbo.CustomerDropboxFiles
            WHERE FileStatus = 'Active'
              AND ExpiresAt <= SYSUTCDATETIME();";

        var expiredFiles = new List<(string FileId, string DropboxId, string EventAppointmentId, string CustomerId, string BucketName, string? R2ObjectKey, string FileName)>();
        await using (var fCmd = new SqlCommand(selectExpiredFilesSql, conn))
        await using (var fr = await fCmd.ExecuteReaderAsync(ct))
        {
            while (await fr.ReadAsync(ct))
            {
                expiredFiles.Add((
                    fr.GetString(0),
                    fr.GetString(1),
                    fr.GetString(2),
                    fr.GetString(3),
                    fr.IsDBNull(4) ? _r2Storage.DropboxBucket : fr.GetString(4),
                    fr.IsDBNull(5) ? null : fr.GetString(5),
                    fr.IsDBNull(6) ? string.Empty : fr.GetString(6)
                ));
            }
        }

        var affectedDropboxIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in expiredFiles)
        {
            if (!string.IsNullOrWhiteSpace(file.R2ObjectKey))
            {
                var deleted = await _r2Storage.DeleteFileAsync(file.BucketName, file.R2ObjectKey);
                if (!deleted && _r2Storage.IsConfigured)
                {
                    _logger.LogWarning(
                        "Skipping DB expiry transition for expired Dropbox ZIP {FileId} ({DropboxId}) because R2 deletion failed for {Bucket}/{Key}.",
                        file.FileId, file.DropboxId, file.BucketName, file.R2ObjectKey);
                    continue;
                }
                result.DeletedR2Keys.Add(file.R2ObjectKey);
            }

            const string expireFileSql = @"
                UPDATE dbo.CustomerDropboxFiles
                SET FileStatus = 'Expired',
                    R2ObjectKey = NULL
                WHERE DropboxFileID = @FileId;

                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (
                        ActionType, EntityName, EntityID, ActionDetails,
                        EventAppointmentID, CustomerID, ChangedByType, LoggedAt
                    )
                    VALUES (
                        'DELETE', 'CustomerDropbox', @DropboxId,
                        'Automatically deleted expired customer Dropbox ZIP (' + @FileName + ') from Cloudflare R2 after 30-day retention period.',
                        @EvtId, @CustId, 'System', SYSUTCDATETIME()
                    );
                END;";

            await using var expFileCmd = new SqlCommand(expireFileSql, conn);
            expFileCmd.Parameters.AddWithValue("@FileId", file.FileId);
            expFileCmd.Parameters.AddWithValue("@DropboxId", file.DropboxId);
            expFileCmd.Parameters.AddWithValue("@FileName", file.FileName);
            expFileCmd.Parameters.AddWithValue("@EvtId", file.EventAppointmentId);
            expFileCmd.Parameters.AddWithValue("@CustId", file.CustomerId);
            await expFileCmd.ExecuteNonQueryAsync(ct);

            affectedDropboxIds.Add(file.DropboxId);
            if (!result.PurgedDropboxIds.Contains(file.DropboxId))
                result.PurgedDropboxIds.Add(file.DropboxId);
            result.PurgedCount++;
        }

        foreach (var affectedId in affectedDropboxIds)
        {
            await SyncParentDropboxSummaryAsync(conn, affectedId, ct);
        }

        // 2. Fallback sweep for any legacy CustomerDropbox rows with no child rows in CustomerDropboxFiles
        const string selectExpiredSql = @"
            SELECT cd.DropboxID, cd.EventAppointmentID, cd.CustomerID, cd.BucketName, cd.R2ObjectKey
            FROM dbo.CustomerDropbox cd
            WHERE cd.DropboxStatus = 'Uploaded'
              AND cd.ExpiresAt IS NOT NULL
              AND cd.ExpiresAt <= SYSUTCDATETIME()
              AND NOT EXISTS (SELECT 1 FROM dbo.CustomerDropboxFiles cdf WHERE cdf.DropboxID = cd.DropboxID);";

        var expired = new List<(string DropboxId, string EventAppointmentId, string CustomerId, string BucketName, string? R2ObjectKey)>();
        await using (var cmd = new SqlCommand(selectExpiredSql, conn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                expired.Add((
                    r.GetString(0),
                    r.GetString(1),
                    r.GetString(2),
                    r.IsDBNull(3) ? _r2Storage.DropboxBucket : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4)
                ));
            }
        }

        foreach (var item in expired)
        {
            if (!string.IsNullOrWhiteSpace(item.R2ObjectKey))
            {
                var deleted = await _r2Storage.DeleteFileAsync(item.BucketName, item.R2ObjectKey);
                if (!deleted && _r2Storage.IsConfigured)
                {
                    _logger.LogWarning(
                        "Skipping DB expiry transition for expired Dropbox {DropboxId} because R2 deletion failed for {Bucket}/{Key}.",
                        item.DropboxId, item.BucketName, item.R2ObjectKey);
                    continue;
                }
                result.DeletedR2Keys.Add(item.R2ObjectKey);
            }

            const string expireSql = @"
                UPDATE dbo.CustomerDropbox
                SET DropboxStatus = 'Expired',
                    R2ObjectKey = NULL
                WHERE DropboxID = @DropboxId;

                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (
                        ActionType, EntityName, EntityID, ActionDetails,
                        EventAppointmentID, CustomerID, ChangedByType, LoggedAt
                    )
                    VALUES (
                        'DELETE', 'CustomerDropbox', @DropboxId,
                        'Automatically deleted expired customer Dropbox ZIP from Cloudflare R2 after 30-day retention period and marked Dropbox unavailable.',
                        @EvtId, @CustId, 'System', SYSUTCDATETIME()
                    );
                END;";

            await using var expCmd = new SqlCommand(expireSql, conn);
            expCmd.Parameters.AddWithValue("@DropboxId", item.DropboxId);
            expCmd.Parameters.AddWithValue("@EvtId", item.EventAppointmentId);
            expCmd.Parameters.AddWithValue("@CustId", item.CustomerId);
            await expCmd.ExecuteNonQueryAsync(ct);

            if (!result.PurgedDropboxIds.Contains(item.DropboxId))
                result.PurgedDropboxIds.Add(item.DropboxId);
            result.PurgedCount++;
        }

        if (result.PurgedCount > 0)
        {
            _logger.LogInformation("Expired {Count} Customer Dropbox record(s) and purged their R2 objects.", result.PurgedCount);
        }

        return result;
    }

    private static async Task BackfillLegacyFileIfNeededAsync(
        SqlConnection conn,
        string dropboxId,
        string eventAppointmentId,
        string customerId,
        string bucketName,
        string legacyKey,
        string legacyFileName,
        long legacySizeBytes,
        string? legacyContentType,
        DateTime legacyUploadedAt,
        string? legacyUploadedBy,
        DateTime legacyExpiresAt,
        CancellationToken ct)
    {
        const string countSql = "SELECT COUNT(1) FROM dbo.CustomerDropboxFiles WHERE DropboxID = @DropboxId;";
        await using (var chk = new SqlCommand(countSql, conn))
        {
            chk.Parameters.AddWithValue("@DropboxId", dropboxId);
            if (Convert.ToInt32(await chk.ExecuteScalarAsync(ct)) > 0)
                return;
        }

        var fileId = await GenerateNextDropboxFileIdAsync(conn, ct);
        var safeSize = legacySizeBytes > 0 && legacySizeBytes <= DropboxCountdownHelper.MaxZipSizeBytes ? legacySizeBytes : 1L;
        const string insertSql = @"
            INSERT INTO dbo.CustomerDropboxFiles (
                DropboxFileID, DropboxID, EventAppointmentID, CustomerID, BucketName,
                R2ObjectKey, OriginalFileName, FileSizeBytes, ContentType, FileStatus,
                UploadedAt, UploadedByEmployeeID, ExpiresAt, CreatedAt
            )
            VALUES (
                @FileId, @DropboxId, @EvtId, @CustId, @Bucket,
                @Key, @FileName, @Size, @ContentType, 'Active',
                @UploadedAt, NULLIF(@UploadedBy, ''), @ExpiresAt, @UploadedAt
            );";

        await using var ins = new SqlCommand(insertSql, conn);
        ins.Parameters.AddWithValue("@FileId", fileId);
        ins.Parameters.AddWithValue("@DropboxId", dropboxId);
        ins.Parameters.AddWithValue("@EvtId", eventAppointmentId);
        ins.Parameters.AddWithValue("@CustId", customerId);
        ins.Parameters.AddWithValue("@Bucket", bucketName);
        ins.Parameters.AddWithValue("@Key", legacyKey);
        ins.Parameters.AddWithValue("@FileName", legacyFileName);
        ins.Parameters.AddWithValue("@Size", safeSize);
        ins.Parameters.AddWithValue("@ContentType", (object?)legacyContentType ?? DBNull.Value);
        ins.Parameters.AddWithValue("@UploadedAt", legacyUploadedAt);
        ins.Parameters.AddWithValue("@UploadedBy", legacyUploadedBy ?? string.Empty);
        ins.Parameters.AddWithValue("@ExpiresAt", legacyExpiresAt);
        await ins.ExecuteNonQueryAsync(ct);
    }

    private static async Task SyncParentDropboxSummaryAsync(SqlConnection conn, string dropboxId, CancellationToken ct)
    {
        const string syncSql = @"
            DECLARE @ActiveCount int = 0;
            DECLARE @ExpiredCount int = 0;

            SELECT @ActiveCount = COUNT(1)
            FROM dbo.CustomerDropboxFiles
            WHERE DropboxID = @DropboxId
              AND FileStatus = 'Active'
              AND ExpiresAt > SYSUTCDATETIME();

            SELECT @ExpiredCount = COUNT(1)
            FROM dbo.CustomerDropboxFiles
            WHERE DropboxID = @DropboxId
              AND (FileStatus = 'Expired' OR ExpiresAt <= SYSUTCDATETIME());

            IF @ActiveCount > 0
            BEGIN
                UPDATE cd
                SET cd.DropboxStatus = 'Uploaded',
                    cd.R2ObjectKey = topFile.R2ObjectKey,
                    cd.OriginalFileName = topFile.OriginalFileName,
                    cd.FileSizeBytes = agg.TotalBytes,
                    cd.ContentType = topFile.ContentType,
                    cd.UploadedAt = agg.FirstUploadedAt,
                    cd.ExpiresAt = agg.MaxExpiresAt
                FROM dbo.CustomerDropbox cd
                CROSS APPLY (
                    SELECT TOP 1 R2ObjectKey, OriginalFileName, ContentType
                    FROM dbo.CustomerDropboxFiles
                    WHERE DropboxID = @DropboxId AND FileStatus = 'Active' AND ExpiresAt > SYSUTCDATETIME()
                    ORDER BY UploadedAt DESC, CreatedAt DESC
                ) topFile
                CROSS APPLY (
                    SELECT SUM(FileSizeBytes) AS TotalBytes,
                           MIN(UploadedAt) AS FirstUploadedAt,
                           MAX(ExpiresAt) AS MaxExpiresAt
                    FROM dbo.CustomerDropboxFiles
                    WHERE DropboxID = @DropboxId AND FileStatus = 'Active' AND ExpiresAt > SYSUTCDATETIME()
                ) agg
                WHERE cd.DropboxID = @DropboxId;
            END
            ELSE IF @ExpiredCount > 0
            BEGIN
                UPDATE dbo.CustomerDropbox
                SET DropboxStatus = 'Expired',
                    R2ObjectKey = NULL
                WHERE DropboxID = @DropboxId;
            END
            ELSE
            BEGIN
                UPDATE dbo.CustomerDropbox
                SET DropboxStatus = 'Waiting',
                    R2ObjectKey = NULL,
                    OriginalFileName = NULL,
                    FileSizeBytes = NULL,
                    ContentType = NULL,
                    UploadedAt = NULL,
                    ExpiresAt = NULL
                WHERE DropboxID = @DropboxId;
            END;";

        await using var cmd = new SqlCommand(syncSql, conn);
        cmd.Parameters.AddWithValue("@DropboxId", dropboxId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task LoadFilesForFoldersAsync(
        SqlConnection conn,
        List<CustomerDropboxFolderDto> folders,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (folders.Count == 0)
            return;

        var ids = folders
            .Select(f => f.DropboxId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0)
            return;

        var paramNames = ids.Select((_, i) => $"@drp{i}").ToArray();
        var sql = $@"
            SELECT DropboxFileID, DropboxID, EventAppointmentID, OriginalFileName, FileSizeBytes,
                   UploadedAt, ExpiresAt, FileStatus
            FROM dbo.CustomerDropboxFiles
            WHERE DropboxID IN ({string.Join(", ", paramNames)})
            ORDER BY UploadedAt ASC, CreatedAt ASC;";

        var byDropbox = new Dictionary<string, List<CustomerDropboxFileDto>>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new SqlCommand(sql, conn))
        {
            for (var i = 0; i < ids.Count; i++)
                cmd.Parameters.AddWithValue(paramNames[i], ids[i]);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var fileId = r.GetString(0);
                var dropboxId = r.GetString(1);
                var evtId = r.GetString(2);
                var fileName = r.GetString(3);
                var size = r.GetInt64(4);
                var uploadedAt = DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc);
                var expiresAt = DateTime.SpecifyKind(r.GetDateTime(6), DateTimeKind.Utc);
                var status = r.IsDBNull(7) ? DropboxFileStatuses.Active : r.GetString(7);
                var isExpired = string.Equals(status, DropboxFileStatuses.Expired, StringComparison.OrdinalIgnoreCase) || expiresAt <= nowUtc;

                var fileDto = new CustomerDropboxFileDto
                {
                    DropboxFileId = fileId,
                    DropboxId = dropboxId,
                    EventAppointmentId = evtId,
                    OriginalFileName = fileName,
                    FileSizeBytes = size,
                    UploadedAt = uploadedAt,
                    ExpiresAt = expiresAt,
                    IsExpired = isExpired,
                    DaysRemaining = isExpired ? 0 : DropboxCountdownHelper.ComputeDaysRemaining(expiresAt, nowUtc),
                    CountdownLabel = isExpired ? "Unavailable" : DropboxCountdownHelper.FormatCountdownLabel(expiresAt, nowUtc)
                };

                if (!byDropbox.TryGetValue(dropboxId, out var list))
                {
                    list = new List<CustomerDropboxFileDto>();
                    byDropbox[dropboxId] = list;
                }
                list.Add(fileDto);
            }
        }

        foreach (var folder in folders)
        {
            if (!byDropbox.TryGetValue(folder.DropboxId, out var files) || files.Count == 0)
                continue;

            folder.Files = files;
            var activeFiles = files.Where(f => !f.IsExpired).ToList();
            if (activeFiles.Count > 0)
            {
                folder.IsExpired = false;
                folder.DropboxStatus = DropboxStatuses.Uploaded;
                folder.OriginalFileName = activeFiles.Last().OriginalFileName;
                folder.FileSizeBytes = activeFiles.Sum(f => f.FileSizeBytes);
                folder.UploadedAt = activeFiles.Min(f => f.UploadedAt);
                folder.ExpiresAt = activeFiles.Max(f => f.ExpiresAt);
            }
            else
            {
                folder.IsExpired = true;
                folder.DropboxStatus = DropboxStatuses.Expired;
            }
        }
    }

    public static bool IsDropboxExpired(string? status, DateTime? expiresAtUtc, DateTime nowUtc)
    {
        if (string.Equals(status, DropboxStatuses.Expired, StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(status, DropboxStatuses.Uploaded, StringComparison.OrdinalIgnoreCase)
               && expiresAtUtc.HasValue
               && expiresAtUtc.Value <= nowUtc;
    }

    private static async Task ApplyDeliveryWindowsAsync(
        SqlConnection conn,
        List<CustomerDropboxFolderDto> folders,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (folders.Count == 0)
            return;

        var defaultDays = DropboxCountdownHelper.DefaultDeliveryBusinessDays;
        const string termsSql = @"
            IF OBJECT_ID(N'dbo.TermsVersions', N'U') IS NOT NULL
                SELECT TOP 1 DeliveryBusinessDays
                FROM dbo.TermsVersions
                WHERE IsActive = 1
                ORDER BY TermsVersionId DESC;";

        await using (var termsCmd = new SqlCommand(termsSql, conn))
        {
            var raw = await termsCmd.ExecuteScalarAsync(ct);
            if (raw != null && raw != DBNull.Value && Convert.ToInt32(raw) > 0)
                defaultDays = Convert.ToInt32(raw);
        }

        var perEventDays = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var eventIds = folders
            .Select(f => f.EventAppointmentId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (eventIds.Count > 0)
        {
            var paramNames = eventIds.Select((_, idx) => $"@evt{idx}").ToArray();
            var contractSql = $@"
                IF OBJECT_ID(N'dbo.BookingContracts', N'U') IS NOT NULL
                    SELECT EventAppointmentID, DeliveryBusinessDays
                    FROM dbo.BookingContracts
                    WHERE EventAppointmentID IN ({string.Join(", ", paramNames)})
                    ORDER BY BookingContractId ASC;";

            await using var contractCmd = new SqlCommand(contractSql, conn);
            for (var i = 0; i < eventIds.Count; i++)
                contractCmd.Parameters.AddWithValue(paramNames[i], eventIds[i]);

            await using var r = await contractCmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var evtId = r.GetString(0);
                var days = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                if (days > 0)
                    perEventDays[evtId] = days;
            }
        }

        foreach (var folder in folders)
        {
            var days = perEventDays.TryGetValue(folder.EventAppointmentId, out var contractDays)
                ? contractDays
                : defaultDays;
            ApplyDeliveryWindowMetrics(folder, days, nowUtc);
        }
    }

    public static void ApplyDeliveryWindowMetrics(CustomerDropboxFolderDto folder, int deliveryBusinessDays, DateTime nowUtc)
    {
        var normalizedDays = deliveryBusinessDays > 0
            ? deliveryBusinessDays
            : DropboxCountdownHelper.DefaultDeliveryBusinessDays;

        folder.DeliveryBusinessDays = normalizedDays;
        folder.DeliveryWindowEndsAt = DropboxCountdownHelper.AddBusinessDays(folder.EventCompletedAt, normalizedDays);

        if (folder.IsExpired)
        {
            folder.DeliveryDaysRemaining = 0;
            folder.DaysRemaining = 0;
            folder.CountdownLabel = "Unavailable";
            return;
        }

        if (string.Equals(folder.DropboxStatus, DropboxStatuses.Uploaded, StringComparison.OrdinalIgnoreCase))
        {
            folder.DeliveryDaysRemaining = 0;
            var effectiveExpires = folder.ExpiresAt ?? (folder.UploadedAt ?? folder.EventCompletedAt).AddDays(DropboxCountdownHelper.RetentionDays);
            folder.ExpiresAt = effectiveExpires;
            folder.DaysRemaining = DropboxCountdownHelper.ComputeDaysRemaining(effectiveExpires, nowUtc);
            folder.CountdownLabel = DropboxCountdownHelper.FormatCountdownLabel(effectiveExpires, nowUtc);
            return;
        }

        folder.ExpiresAt = null;
        folder.DaysRemaining = 0;
        folder.DeliveryDaysRemaining = DropboxCountdownHelper.ComputeBusinessDaysRemaining(folder.EventCompletedAt, normalizedDays, nowUtc);
        folder.CountdownLabel = DropboxCountdownHelper.FormatDeliveryWindowLabel(folder.EventCompletedAt, normalizedDays, nowUtc);
    }

    private static CustomerDropboxFolderDto MapFolder(SqlDataReader r, DateTime nowUtc, int deliveryBusinessDays)
    {
        var completedAt = DateTime.SpecifyKind(r.GetDateTime(13), DateTimeKind.Utc);
        DateTime? uploadedAt = r.IsDBNull(14) ? null : DateTime.SpecifyKind(r.GetDateTime(14), DateTimeKind.Utc);
        DateTime? expiresAt = r.IsDBNull(15) ? null : DateTime.SpecifyKind(r.GetDateTime(15), DateTimeKind.Utc);
        var rawStatus = r.IsDBNull(10) ? DropboxStatuses.Waiting : r.GetString(10);
        var isExpired = IsDropboxExpired(rawStatus, expiresAt, nowUtc);
        var effectiveStatus = isExpired
            ? DropboxStatuses.Expired
            : (string.Equals(rawStatus, DropboxStatuses.Uploaded, StringComparison.OrdinalIgnoreCase)
                ? DropboxStatuses.Uploaded
                : DropboxStatuses.Waiting);

        var remBal = r.FieldCount > 16 && !r.IsDBNull(16) ? r.GetDecimal(16) : 0m;
        var unlocked = r.FieldCount > 17 && !r.IsDBNull(17) ? r.GetBoolean(17) : remBal <= 0m;

        var dto = new CustomerDropboxFolderDto
        {
            DropboxId = r.GetString(0),
            EventAppointmentId = r.GetString(1),
            CustomerId = r.GetString(2),
            CustomerName = r.IsDBNull(3) ? string.Empty : r.GetString(3),
            CustomerEmail = r.IsDBNull(4) ? string.Empty : r.GetString(4),
            EventName = r.IsDBNull(5) ? string.Empty : r.GetString(5),
            EventDescription = r.IsDBNull(6) ? string.Empty : r.GetString(6),
            EventDate = DateOnly.FromDateTime(r.GetDateTime(7)),
            EventTime = TimeOnly.FromTimeSpan(r.GetFieldValue<TimeSpan>(8)),
            EventStatus = r.IsDBNull(9) ? string.Empty : r.GetString(9),
            DropboxStatus = effectiveStatus,
            OriginalFileName = r.IsDBNull(11) ? null : r.GetString(11),
            FileSizeBytes = r.IsDBNull(12) ? null : r.GetInt64(12),
            EventCompletedAt = completedAt,
            UploadedAt = uploadedAt,
            ExpiresAt = expiresAt,
            IsExpired = isExpired,
            RemainingBalance = remBal,
            IsDeliverablesUnlocked = unlocked,
            LockReason = unlocked ? null : $"Locked until remaining balance of PHP {remBal:N2} is settled in full."
        };

        ApplyDeliveryWindowMetrics(dto, deliveryBusinessDays, nowUtc);
        return dto;
    }

    private static async Task ValidateZipSignatureAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        var read = await stream.ReadAsync(header.AsMemory(0, 4), ct);
        if (read < 2 || header[0] != 0x50 || header[1] != 0x4B)
            throw new InvalidOperationException("The uploaded file is not a valid ZIP archive (missing PK signature).");
    }

    private static string SanitizeFileName(string? fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "event-photos.zip" : Path.GetFileName(fileName.Trim());
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (Array.IndexOf(invalid, ch) >= 0 || ch == '"' || ch == '\'' || ch == '\\' || ch == '/')
                sb.Append('_');
            else
                sb.Append(ch);
        }
        var cleaned = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "event-photos.zip" : cleaned;
    }

    private static string SanitizeKeySegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return "unknown";
        var sb = new StringBuilder(segment.Length);
        foreach (var ch in segment.Trim())
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
        }
        return sb.ToString();
    }
}
