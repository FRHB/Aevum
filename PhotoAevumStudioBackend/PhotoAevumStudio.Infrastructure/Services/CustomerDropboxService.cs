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
              CREATE SEQUENCE dbo.Seq_DropboxID AS INT START WITH 1 INCREMENT BY 1;",

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
                  ExpiresAt datetime2 NOT NULL,
                  CreatedAt datetime2 NOT NULL CONSTRAINT DF_CustomerDropbox_CreatedAt DEFAULT SYSUTCDATETIME(),
                  CONSTRAINT UQ_CustomerDropbox_Event UNIQUE (EventAppointmentID),
                  CONSTRAINT CK_CustomerDropbox_Status CHECK (DropboxStatus IN ('Waiting', 'Uploaded', 'Expired'))
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

          IF OBJECT_ID(N'dbo.CustomerDropbox', N'U') IS NOT NULL
             AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CustomerDropbox_Customer_ExpiresAt' AND object_id = OBJECT_ID(N'dbo.CustomerDropbox'))
              CREATE INDEX IX_CustomerDropbox_Customer_ExpiresAt ON dbo.CustomerDropbox (CustomerID, ExpiresAt);

          IF OBJECT_ID(N'dbo.CustomerDropbox', N'U') IS NOT NULL
             AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CustomerDropbox_ExpiresAt' AND object_id = OBJECT_ID(N'dbo.CustomerDropbox'))
              CREATE INDEX IX_CustomerDropbox_ExpiresAt ON dbo.CustomerDropbox (ExpiresAt);"
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

        var expiresAt = completedAt.AddDays(DropboxCountdownHelper.RetentionDays);
        if (expiresAt <= DateTime.UtcNow)
            return;

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
                @CompletedAt, @ExpiresAt, SYSUTCDATETIME()
            );";

        await using var ins = new SqlCommand(insertSql, conn);
        ins.Parameters.AddWithValue("@DropboxId", dropboxId);
        ins.Parameters.AddWithValue("@EvtId", eventAppointmentId);
        ins.Parameters.AddWithValue("@CustId", customerId);
        ins.Parameters.AddWithValue("@Bucket", _r2Storage.DropboxBucket);
        ins.Parameters.AddWithValue("@CompletedAt", completedAt);
        ins.Parameters.AddWithValue("@ExpiresAt", expiresAt);
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
                var evtId = r.GetString(0);
                var custId = r.GetString(1);
                var completedAt = r.GetDateTime(2);
                if (completedAt.AddDays(DropboxCountdownHelper.RetentionDays) > DateTime.UtcNow)
                {
                    missing.Add((evtId, custId, completedAt));
                }
            }
        }

        foreach (var item in missing)
        {
            var dropboxId = await GenerateNextDropboxIdAsync(conn, ct);
            var expiresAt = item.CompletedAt.AddDays(DropboxCountdownHelper.RetentionDays);
            const string insertSql = @"
                IF NOT EXISTS (SELECT 1 FROM dbo.CustomerDropbox WHERE EventAppointmentID = @EvtId)
                INSERT INTO dbo.CustomerDropbox (
                    DropboxID, EventAppointmentID, CustomerID, BucketName, DropboxStatus,
                    EventCompletedAt, ExpiresAt, CreatedAt
                )
                VALUES (
                    @DropboxId, @EvtId, @CustId, @Bucket, 'Waiting',
                    @CompletedAt, @ExpiresAt, SYSUTCDATETIME()
                );";
            await using var ins = new SqlCommand(insertSql, conn);
            ins.Parameters.AddWithValue("@DropboxId", dropboxId);
            ins.Parameters.AddWithValue("@EvtId", item.EventAppointmentId);
            ins.Parameters.AddWithValue("@CustId", item.CustomerId);
            ins.Parameters.AddWithValue("@Bucket", _r2Storage.DropboxBucket);
            ins.Parameters.AddWithValue("@CompletedAt", item.CompletedAt);
            ins.Parameters.AddWithValue("@ExpiresAt", expiresAt);
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
                   cd.ExpiresAt
            FROM dbo.CustomerDropbox cd
            INNER JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = cd.EventAppointmentID
            LEFT JOIN dbo.Customer c ON c.CustomerID = cd.CustomerID
            WHERE cd.CustomerID = @CustomerId
              AND cd.ExpiresAt > SYSUTCDATETIME()
            ORDER BY cd.EventCompletedAt DESC, cd.CreatedAt DESC;";

        var list = new List<CustomerDropboxFolderDto>();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CustomerId", normalizedCustomerId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var nowUtc = DateTime.UtcNow;
        while (await r.ReadAsync(ct))
        {
            list.Add(MapFolder(r, nowUtc));
        }

        return list;
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
            WHERE cd.ExpiresAt > SYSUTCDATETIME()
            ORDER BY
                CASE WHEN cd.DropboxStatus = 'Waiting' THEN 0 ELSE 1 END,
                cd.EventCompletedAt DESC;";

        var list = new List<CustomerDropboxFolderDto>();
        await using var cmd = new SqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var nowUtc = DateTime.UtcNow;
        while (await r.ReadAsync(ct))
        {
            list.Add(MapFolder(r, nowUtc));
        }

        return list;
    }

    public async Task<CustomerDropboxFolderDto> UploadZipAsync(
        string eventAppointmentId,
        Stream zipStream,
        string originalFileName,
        long fileSizeBytes,
        string? contentType,
        string uploadedByEmployeeId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventAppointmentId))
            throw new ArgumentException("Event appointment ID is required.", nameof(eventAppointmentId));
        if (zipStream == null)
            throw new ArgumentNullException(nameof(zipStream));
        if (fileSizeBytes <= 0)
            throw new ArgumentException("Uploaded ZIP file cannot be empty.", nameof(fileSizeBytes));

        var safeName = SanitizeFileName(originalFileName);
        if (!Path.GetExtension(safeName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .zip archives are accepted for Customer Dropbox uploads.");

        string? tempFilePath = null;
        Stream uploadStream = zipStream;
        try
        {
            if (zipStream.CanSeek)
            {
                zipStream.Position = 0;
                await ValidateZipSignatureAsync(zipStream, ct);
                zipStream.Position = 0;
            }
            else
            {
                tempFilePath = Path.Combine(Path.GetTempPath(), $"aevum-dropbox-{Guid.NewGuid():N}.zip");
                await using (var fs = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await zipStream.CopyToAsync(fs, ct);
                }

                var readFs = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                uploadStream = readFs;
                await ValidateZipSignatureAsync(readFs, ct);
                readFs.Position = 0;
            }

            await using var conn = await OpenAsync(ct);
            await EnsureFolderForEventOnConnectionAsync(conn, eventAppointmentId.Trim(), ct);

            const string fetchSql = @"
                SELECT TOP 1 cd.DropboxID, cd.CustomerID, cd.BucketName, cd.R2ObjectKey, cd.ExpiresAt
                FROM dbo.CustomerDropbox cd
                WHERE cd.EventAppointmentID = @EvtId;";

            string? dropboxId = null, customerId = null, bucketName = null, oldKey = null;
            DateTime expiresAt = DateTime.UtcNow;

            await using (var cmd = new SqlCommand(fetchSql, conn))
            {
                cmd.Parameters.AddWithValue("@EvtId", eventAppointmentId.Trim());
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct))
                    throw new KeyNotFoundException($"No completed event Dropbox container found for appointment '{eventAppointmentId}'.");

                dropboxId = r.GetString(0);
                customerId = r.GetString(1);
                bucketName = r.IsDBNull(2) ? _r2Storage.DropboxBucket : r.GetString(2);
                oldKey = r.IsDBNull(3) ? null : r.GetString(3);
                expiresAt = r.GetDateTime(4);
            }

            if (expiresAt <= DateTime.UtcNow)
                throw new InvalidOperationException($"The 30-day Dropbox window for event '{eventAppointmentId}' has already expired.");

            var targetBucket = string.IsNullOrWhiteSpace(bucketName) ? _r2Storage.DropboxBucket : bucketName.Trim();
            await EnsureDropboxBucketAsync();

            // Strict per-customer/per-event key prefix isolation in Cloudflare R2
            var objectKey = $"customers/{SanitizeKeySegment(customerId!)}/events/{SanitizeKeySegment(eventAppointmentId)}/{Guid.NewGuid():N}_{safeName}";
            var resolvedContentType = string.IsNullOrWhiteSpace(contentType) ? "application/zip" : contentType.Trim();

            var uploadedUri = await _r2Storage.UploadFileAsync(targetBucket, objectKey, uploadStream, resolvedContentType);
            if (string.IsNullOrWhiteSpace(uploadedUri))
                throw new InvalidOperationException("Cloudflare R2 upload did not return a success confirmation.");

            if (!string.IsNullOrWhiteSpace(oldKey) && !string.Equals(oldKey, objectKey, StringComparison.Ordinal))
            {
                await _r2Storage.DeleteFileAsync(targetBucket, oldKey);
            }

            const string updateSql = @"
                UPDATE dbo.CustomerDropbox
                SET BucketName = @Bucket,
                    R2ObjectKey = @Key,
                    OriginalFileName = @FileName,
                    FileSizeBytes = @Size,
                    ContentType = @ContentType,
                    DropboxStatus = 'Uploaded',
                    UploadedAt = SYSUTCDATETIME(),
                    UploadedByEmployeeID = NULLIF(@UploadedBy, '')
                WHERE DropboxID = @DropboxId;

                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (
                        ActionType, EntityName, EntityID, ActionDetails,
                        EventAppointmentID, CustomerID, ChangedBy, ChangedByType, LoggedAt
                    )
                    VALUES (
                        'UPLOAD', 'CustomerDropbox', @DropboxId,
                        'Uploaded customer gallery ZIP (' + @FileName + ') to Cloudflare R2.',
                        @EvtId, @CustId, NULLIF(@UploadedBy, ''), 'Staff', SYSUTCDATETIME()
                    );
                END;";

            await using (var upd = new SqlCommand(updateSql, conn))
            {
                upd.Parameters.AddWithValue("@Bucket", targetBucket);
                upd.Parameters.AddWithValue("@Key", objectKey);
                upd.Parameters.AddWithValue("@FileName", safeName);
                upd.Parameters.AddWithValue("@Size", fileSizeBytes);
                upd.Parameters.AddWithValue("@ContentType", resolvedContentType);
                upd.Parameters.AddWithValue("@UploadedBy", uploadedByEmployeeId ?? string.Empty);
                upd.Parameters.AddWithValue("@DropboxId", dropboxId!);
                upd.Parameters.AddWithValue("@EvtId", eventAppointmentId.Trim());
                upd.Parameters.AddWithValue("@CustId", customerId!);
                await upd.ExecuteNonQueryAsync(ct);
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

            await using var readCmd = new SqlCommand(readBackSql, conn);
            readCmd.Parameters.AddWithValue("@DropboxId", dropboxId!);
            await using var reader = await readCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException("Uploaded Dropbox record could not be reloaded.");

            return MapFolder(reader, DateTime.UtcNow);
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

    public async Task<(string FileName, string ContentType, Stream FileStream, string PresignedUrl)> GetCustomerDownloadAsync(
        string dropboxId,
        string callerCustomerId,
        CancellationToken ct = default)
    {
        var row = await GetAuthorizedDownloadRecordAsync(dropboxId, callerCustomerId, ct);
        var presigned = _r2Storage.GetPresignedDownloadUrl(row.BucketName, row.R2ObjectKey, row.FileName, TimeSpan.FromMinutes(15));
        var stream = await _r2Storage.DownloadFileAsync(row.BucketName, row.R2ObjectKey);
        return (row.FileName, row.ContentType, stream, presigned);
    }

    public async Task<DropboxDownloadUrlDto> GetCustomerDownloadInfoAsync(
        string dropboxId,
        string callerCustomerId,
        string apiBaseUrl,
        CancellationToken ct = default)
    {
        var row = await GetAuthorizedDownloadRecordAsync(dropboxId, callerCustomerId, ct);
        var presigned = _r2Storage.GetPresignedDownloadUrl(row.BucketName, row.R2ObjectKey, row.FileName, TimeSpan.FromMinutes(15));

        if (string.IsNullOrWhiteSpace(presigned))
        {
            var token = CreateSignedDownloadToken(row.DropboxId, row.CustomerId, TimeSpan.FromMinutes(10));
            var basePrefix = string.IsNullOrWhiteSpace(apiBaseUrl) ? string.Empty : apiBaseUrl.TrimEnd('/');
            presigned = $"{basePrefix}/api/dropbox/download/{Uri.EscapeDataString(token)}";
        }

        return new DropboxDownloadUrlDto
        {
            DropboxId = row.DropboxId,
            EventAppointmentId = row.EventAppointmentId,
            FileName = row.FileName,
            FileSizeBytes = row.FileSizeBytes,
            DownloadUrl = presigned,
            ExpiresAt = row.ExpiresAt
        };
    }

    public string CreateSignedDownloadToken(string dropboxId, string customerId, TimeSpan ttl)
    {
        var exp = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();
        var payload = $"{dropboxId}|{customerId}|{exp}";
        var sig = ComputeHmac(payload);
        var raw = $"{payload}|{sig}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public (bool Valid, string DropboxId, string CustomerId) ValidateSignedDownloadToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (false, string.Empty, string.Empty);

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
            if (parts.Length != 4) return (false, string.Empty, string.Empty);

            if (!long.TryParse(parts[2], out var expUnix))
                return (false, string.Empty, string.Empty);

            if (DateTimeOffset.FromUnixTimeSeconds(expUnix) <= DateTimeOffset.UtcNow)
                return (false, string.Empty, string.Empty);

            var payload = $"{parts[0]}|{parts[1]}|{parts[2]}";
            var expectedSig = ComputeHmac(payload);
            var actualBytes = Encoding.UTF8.GetBytes(parts[3]);
            var expectedBytes = Encoding.UTF8.GetBytes(expectedSig);

            if (actualBytes.Length != expectedBytes.Length ||
                !CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes))
            {
                return (false, string.Empty, string.Empty);
            }

            return (true, parts[0], parts[1]);
        }
        catch
        {
            return (false, string.Empty, string.Empty);
        }
    }

    private string ComputeHmac(string payload)
    {
        var key = _configuration["Jwt:Key"] ?? "PhotoAevumStudio-Dev-Fallback-Secret-Key-2026-Minimum32Chars!";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    private async Task<(string DropboxId, string EventAppointmentId, string CustomerId, string BucketName, string R2ObjectKey, string FileName, long FileSizeBytes, string ContentType, DateTime ExpiresAt)> GetAuthorizedDownloadRecordAsync(
        string dropboxId,
        string callerCustomerId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dropboxId))
            throw new ArgumentException("Dropbox ID is required.", nameof(dropboxId));
        if (string.IsNullOrWhiteSpace(callerCustomerId))
            throw new UnauthorizedAccessException("Customer identity is required.");

        await using var conn = await OpenAsync(ct);

        const string sql = @"
            SELECT TOP 1
                   DropboxID, EventAppointmentID, CustomerID, BucketName, R2ObjectKey,
                   OriginalFileName, FileSizeBytes, ContentType, DropboxStatus, ExpiresAt
            FROM dbo.CustomerDropbox
            WHERE DropboxID = @DropboxId;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@DropboxId", dropboxId.Trim());
        await using var r = await cmd.ExecuteReaderAsync(ct);

        if (!await r.ReadAsync(ct))
            throw new KeyNotFoundException($"Dropbox folder '{dropboxId}' was not found.");

        var dbDropboxId = r.GetString(0);
        var evtId = r.GetString(1);
        var ownerCustomerId = r.GetString(2);
        var bucket = r.IsDBNull(3) ? _r2Storage.DropboxBucket : r.GetString(3);
        var key = r.IsDBNull(4) ? string.Empty : r.GetString(4);
        var fileName = r.IsDBNull(5) ? $"{evtId}.zip" : r.GetString(5);
        var fileSize = r.IsDBNull(6) ? 0L : r.GetInt64(6);
        var contentType = r.IsDBNull(7) ? "application/zip" : r.GetString(7);
        var status = r.IsDBNull(8) ? DropboxStatuses.Waiting : r.GetString(8);
        var expiresAt = r.GetDateTime(9);

        // Strict customer isolation enforcement
        if (!string.Equals(ownerCustomerId, callerCustomerId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Blocked unauthorized Dropbox download attempt: caller '{CallerId}' tried to access Dropbox '{DropboxId}' owned by '{OwnerId}'.",
                callerCustomerId, dbDropboxId, ownerCustomerId);
            throw new UnauthorizedAccessException("You are not authorized to access this customer's Dropbox.");
        }

        if (expiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException("This Dropbox gallery has expired and is no longer available.");

        if (!string.Equals(status, DropboxStatuses.Uploaded, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("No ZIP archive has been uploaded to this Dropbox folder yet.");

        return (dbDropboxId, evtId, ownerCustomerId, bucket, key, fileName, fileSize, contentType, expiresAt);
    }

    public async Task<DropboxPurgeResultDto> PurgeExpiredDropboxesAsync(CancellationToken ct = default)
    {
        var result = new DropboxPurgeResultDto();
        await using var conn = await OpenAsync(ct);

        const string selectExpiredSql = @"
            SELECT DropboxID, EventAppointmentID, CustomerID, BucketName, R2ObjectKey
            FROM dbo.CustomerDropbox
            WHERE ExpiresAt <= SYSUTCDATETIME();";

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
                        "Skipping DB record removal for expired Dropbox {DropboxId} because R2 deletion failed for {Bucket}/{Key}.",
                        item.DropboxId, item.BucketName, item.R2ObjectKey);
                    continue;
                }
                result.DeletedR2Keys.Add(item.R2ObjectKey);
            }

            const string deleteSql = @"
                DELETE FROM dbo.CustomerDropbox WHERE DropboxID = @DropboxId;

                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (
                        ActionType, EntityName, EntityID, ActionDetails,
                        EventAppointmentID, CustomerID, ChangedByType, LoggedAt
                    )
                    VALUES (
                        'AUTO_DELETE', 'CustomerDropbox', @DropboxId,
                        'Automatically deleted expired customer Dropbox ZIP from Cloudflare R2 and removed database record after 30 days.',
                        @EvtId, @CustId, 'System', SYSUTCDATETIME()
                    );
                END;";

            await using var delCmd = new SqlCommand(deleteSql, conn);
            delCmd.Parameters.AddWithValue("@DropboxId", item.DropboxId);
            delCmd.Parameters.AddWithValue("@EvtId", item.EventAppointmentId);
            delCmd.Parameters.AddWithValue("@CustId", item.CustomerId);
            await delCmd.ExecuteNonQueryAsync(ct);

            result.PurgedDropboxIds.Add(item.DropboxId);
            result.PurgedCount++;
        }

        if (result.PurgedCount > 0)
        {
            _logger.LogInformation("Purged {Count} expired Customer Dropbox record(s) and their R2 objects.", result.PurgedCount);
        }

        return result;
    }

    private static CustomerDropboxFolderDto MapFolder(SqlDataReader r, DateTime nowUtc)
    {
        var expiresAt = DateTime.SpecifyKind(r.GetDateTime(15), DateTimeKind.Utc);
        var completedAt = DateTime.SpecifyKind(r.GetDateTime(13), DateTimeKind.Utc);
        DateTime? uploadedAt = r.IsDBNull(14) ? null : DateTime.SpecifyKind(r.GetDateTime(14), DateTimeKind.Utc);

        return new CustomerDropboxFolderDto
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
            DropboxStatus = r.IsDBNull(10) ? DropboxStatuses.Waiting : r.GetString(10),
            OriginalFileName = r.IsDBNull(11) ? null : r.GetString(11),
            FileSizeBytes = r.IsDBNull(12) ? null : r.GetInt64(12),
            EventCompletedAt = completedAt,
            UploadedAt = uploadedAt,
            ExpiresAt = expiresAt,
            DaysRemaining = DropboxCountdownHelper.ComputeDaysRemaining(expiresAt, nowUtc),
            CountdownLabel = DropboxCountdownHelper.FormatCountdownLabel(expiresAt, nowUtc)
        };
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
