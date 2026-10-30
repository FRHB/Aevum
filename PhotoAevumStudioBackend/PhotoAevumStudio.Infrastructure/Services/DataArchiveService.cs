using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Infrastructure.Services;

public class DataArchiveService : IDataArchiveService
{
    private readonly ApplicationDbContext _db;
    private readonly IR2StorageService _r2StorageService;
    private readonly ILogger<DataArchiveService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public DataArchiveService(
        ApplicationDbContext db,
        IR2StorageService r2StorageService,
        ILogger<DataArchiveService> logger)
    {
        _db = db;
        _r2StorageService = r2StorageService;
        _logger = logger;
    }

    public async Task<string> ArchiveHistoricalDataAsync(DateTime? cutoffDate = null, CancellationToken cancellationToken = default)
    {
        var effectiveCutoff = cutoffDate ?? DateTime.UtcNow.AddYears(-1);
        var archiveBucket = _r2StorageService.ArchiveBucket;

        await EnsureArchiveIndexTableExistsAsync(cancellationToken);
        await _r2StorageService.CreateBucketIfNotExistsAsync(archiveBucket);

        var uploadedUris = new List<string>();

        // 1. Archive dbo.HistoryLog (older than cutoff)
        var historyUri = await ArchiveHistoryLogsAsync(archiveBucket, effectiveCutoff, cancellationToken);
        if (!string.IsNullOrWhiteSpace(historyUri))
            uploadedUris.Add(historyUri);

        // 2. Archive dbo.AuditLogs (older than cutoff)
        var auditUri = await ArchiveAuditLogsAsync(archiveBucket, effectiveCutoff, cancellationToken);
        if (!string.IsNullOrWhiteSpace(auditUri))
            uploadedUris.Add(auditUri);

        // 3. Archive Completed/Cancelled dbo.EventAppointment (older than cutoff)
        var appointmentUri = await ArchiveEventAppointmentsAsync(archiveBucket, effectiveCutoff, cancellationToken);
        if (!string.IsNullOrWhiteSpace(appointmentUri))
            uploadedUris.Add(appointmentUri);

        // 4. Archive Completed/Cancelled dbo.Bookings (older than cutoff)
        var bookingsUri = await ArchiveBookingsAsync(archiveBucket, effectiveCutoff, cancellationToken);
        if (!string.IsNullOrWhiteSpace(bookingsUri))
            uploadedUris.Add(bookingsUri);

        // 5. Archive Inactive dbo.Employees (older than cutoff)
        var employeesUri = await ArchiveInactiveEmployeesAsync(archiveBucket, effectiveCutoff, cancellationToken);
        if (!string.IsNullOrWhiteSpace(employeesUri))
            uploadedUris.Add(employeesUri);

        // 6. Archive Inactive dbo.Customer (older than cutoff)
        var customersUri = await ArchiveInactiveCustomersAsync(archiveBucket, effectiveCutoff, cancellationToken);
        if (!string.IsNullOrWhiteSpace(customersUri))
            uploadedUris.Add(customersUri);

        // 7. Archive Inactive dbo.Admin (older than cutoff)
        var adminsUri = await ArchiveInactiveAdminsAsync(archiveBucket, effectiveCutoff, cancellationToken);
        if (!string.IsNullOrWhiteSpace(adminsUri))
            uploadedUris.Add(adminsUri);

        return string.Join(";", uploadedUris);
    }

    public async Task<IReadOnlyList<ArchiveIndex>> GetArchiveIndicesAsync(string? entityName = null, CancellationToken cancellationToken = default)
    {
        await EnsureArchiveIndexTableExistsAsync(cancellationToken);

        var query = _db.ArchiveIndices.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityName))
        {
            query = query.Where(a => a.EntityName == entityName);
        }

        return await query
            .OrderByDescending(a => a.ArchivedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Stream> DownloadArchivedDataAsync(int archiveId, CancellationToken cancellationToken = default)
    {
        await EnsureArchiveIndexTableExistsAsync(cancellationToken);

        var index = await _db.ArchiveIndices
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ArchiveId == archiveId, cancellationToken)
            ?? throw new KeyNotFoundException($"ArchiveIndex record with ID {archiveId} was not found.");

        return await _r2StorageService.DownloadFileAsync(index.BucketName, index.R2Key);
    }

    public async Task<bool> DeleteArchivedDataAsync(int archiveId, CancellationToken cancellationToken = default)
    {
        await EnsureArchiveIndexTableExistsAsync(cancellationToken);

        var index = await _db.ArchiveIndices
            .FirstOrDefaultAsync(a => a.ArchiveId == archiveId, cancellationToken);

        if (index == null)
            return false;

        var deletedFromR2 = await _r2StorageService.DeleteFileAsync(index.BucketName, index.R2Key);
        if (!deletedFromR2)
            return false;

        _db.ArchiveIndices.Remove(index);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<string?> ArchiveHistoryLogsAsync(string archiveBucket, DateTime cutoff, CancellationToken ct)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(ct);

        if (!await TableExistsAsync(conn, "HistoryLog", ct))
            return null;

        var rows = new List<Dictionary<string, object?>>();
        var ids = new List<long>();
        DateTime? minDate = null;
        DateTime? maxDate = null;

        const string selectSql = @"
            SELECT HistoryLogID, ActionType, EntityName, EntityID, ActionDetails,
                   EventAppointmentID, NegotiationID, CustomerID, EmployeeID,
                   ChangedByType, ChangedByCustomerID, ChangedByEmployeeID, ChangedByAdminID, LoggedAt
            FROM dbo.HistoryLog
            WHERE LoggedAt <= @Cutoff
            ORDER BY LoggedAt ASC;";

        await using (var cmd = new SqlCommand(selectSql, conn))
        {
            cmd.Parameters.AddWithValue("@Cutoff", cutoff);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetInt64(0);
                var loggedAt = reader.GetDateTime(13);
                ids.Add(id);
                minDate = !minDate.HasValue || loggedAt < minDate.Value ? loggedAt : minDate;
                maxDate = !maxDate.HasValue || loggedAt > maxDate.Value ? loggedAt : maxDate;
                rows.Add(ReadRow(reader));
            }
        }

        if (rows.Count == 0)
            return null;

        var uri = await ExecuteArchiveAndIndexPipelineAsync(
            archiveBucket,
            "HistoryLog",
            rows,
            rows.Count,
            minDate,
            maxDate,
            ct);

        // Step 5: Only after serialize + upload + URI check + ArchiveIndex insert succeed, execute hard delete
        const string deleteSql = "DELETE FROM dbo.HistoryLog WHERE LoggedAt <= @Cutoff;";
        await using (var delCmd = new SqlCommand(deleteSql, conn))
        {
            delCmd.Parameters.AddWithValue("@Cutoff", cutoff);
            await delCmd.ExecuteNonQueryAsync(ct);
        }

        _logger.LogInformation("Archived and hard-deleted {Count} rows from dbo.HistoryLog to {Uri}.", rows.Count, uri);
        return uri;
    }

    private async Task<string?> ArchiveAuditLogsAsync(string archiveBucket, DateTime cutoff, CancellationToken ct)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(ct);

        if (!await TableExistsAsync(conn, "AuditLogs", ct))
            return null;

        var logs = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.Timestamp <= cutoff)
            .OrderBy(a => a.Timestamp)
            .ToListAsync(ct);

        if (logs.Count == 0)
            return null;

        var minDate = logs.Min(a => a.Timestamp);
        var maxDate = logs.Max(a => a.Timestamp);

        var uri = await ExecuteArchiveAndIndexPipelineAsync(
            archiveBucket,
            "AuditLogs",
            logs,
            logs.Count,
            minDate,
            maxDate,
            ct);

        // Step 5: Hard delete source records only after R2 upload and ArchiveIndex insert succeed
        await _db.AuditLogs
            .Where(a => a.Timestamp <= cutoff)
            .ExecuteDeleteAsync(ct);

        _logger.LogInformation("Archived and hard-deleted {Count} rows from dbo.AuditLogs to {Uri}.", logs.Count, uri);
        return uri;
    }

    private async Task<string?> ArchiveEventAppointmentsAsync(string archiveBucket, DateTime cutoff, CancellationToken ct)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(ct);

        if (!await TableExistsAsync(conn, "EventAppointment", ct))
            return null;

        var rows = new List<Dictionary<string, object?>>();
        var appointmentIds = new List<string>();
        DateTime? minDate = null;
        DateTime? maxDate = null;

        const string selectSql = @"
            SELECT EventAppointmentID, EventName, EventDescription, CustomerID, EventDate, EventTime,
                   EventStatus, TotalAmount, DownpaymentAmount, FinalBalanceAmount, DownpaymentStatus,
                   DownpaymentPaidAt, HoldExpiresAt, XenditInvoiceId, XenditExternalId, XenditInvoiceUrl, CreatedAt
            FROM dbo.EventAppointment
            WHERE EventStatus IN ('Completed', 'Delivered', 'Cancelled', 'Expired')
              AND CAST(EventDate AS datetime2) <= @Cutoff
            ORDER BY EventDate ASC;";

        await using (var cmd = new SqlCommand(selectSql, conn))
        {
            cmd.Parameters.AddWithValue("@Cutoff", cutoff);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetString(0);
                var eventDate = reader.GetDateTime(4);
                appointmentIds.Add(id);
                minDate = !minDate.HasValue || eventDate < minDate.Value ? eventDate : minDate;
                maxDate = !maxDate.HasValue || eventDate > maxDate.Value ? eventDate : maxDate;
                rows.Add(ReadRow(reader));
            }
        }

        if (rows.Count == 0)
            return null;

        var uri = await ExecuteArchiveAndIndexPipelineAsync(
            archiveBucket,
            "EventAppointment",
            rows,
            rows.Count,
            minDate,
            maxDate,
            ct);

        // Step 5: Hard delete source records only after R2 upload and ArchiveIndex insert succeed
        const string deleteSql = @"
            DELETE FROM dbo.EventAppointment
            WHERE EventStatus IN ('Completed', 'Delivered', 'Cancelled', 'Expired')
              AND CAST(EventDate AS datetime2) <= @Cutoff;";

        await using (var delCmd = new SqlCommand(deleteSql, conn))
        {
            delCmd.Parameters.AddWithValue("@Cutoff", cutoff);
            await delCmd.ExecuteNonQueryAsync(ct);
        }

        _logger.LogInformation("Archived and hard-deleted {Count} rows from dbo.EventAppointment to {Uri}.", rows.Count, uri);
        return uri;
    }

    private async Task<string?> ArchiveBookingsAsync(string archiveBucket, DateTime cutoff, CancellationToken ct)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(ct);

        if (!await TableExistsAsync(conn, "Bookings", ct))
            return null;

        var cutoffDate = DateOnly.FromDateTime(cutoff);
        var bookings = await _db.Bookings
            .AsNoTracking()
            .Where(b => (b.Status == BookingStatus.Delivered || b.Status == BookingStatus.Cancelled) && b.ScheduleDate <= cutoffDate)
            .OrderBy(b => b.ScheduleDate)
            .ToListAsync(ct);

        if (bookings.Count == 0)
            return null;

        var minDate = bookings.Min(b => b.ScheduleDate).ToDateTime(TimeOnly.MinValue);
        var maxDate = bookings.Max(b => b.ScheduleDate).ToDateTime(TimeOnly.MinValue);

        var uri = await ExecuteArchiveAndIndexPipelineAsync(
            archiveBucket,
            "Bookings",
            bookings,
            bookings.Count,
            minDate,
            maxDate,
            ct);

        // Step 5: Hard delete source records only after R2 upload and ArchiveIndex insert succeed
        await _db.Bookings
            .Where(b => (b.Status == BookingStatus.Delivered || b.Status == BookingStatus.Cancelled) && b.ScheduleDate <= cutoffDate)
            .ExecuteDeleteAsync(ct);

        _logger.LogInformation("Archived and hard-deleted {Count} rows from dbo.Bookings to {Uri}.", bookings.Count, uri);
        return uri;
    }

    private async Task<string?> ArchiveInactiveEmployeesAsync(string archiveBucket, DateTime cutoff, CancellationToken ct)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(ct);

        if (!await TableExistsAsync(conn, "Employees", ct))
            return null;

        var rows = new List<Dictionary<string, object?>>();
        DateTime? minDate = null;
        DateTime? maxDate = null;

        const string selectSql = @"
            SELECT EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeMiddleName,
                   EmployeeEmail, EmployeeStatus, isFirstLogin, CreatedAt, DaysOff, Status, Specializations
            FROM dbo.Employees
            WHERE (EmployeeStatus = 'Inactive' OR Status = 'Inactive')
              AND CreatedAt <= @Cutoff
            ORDER BY CreatedAt ASC;";

        await using (var cmd = new SqlCommand(selectSql, conn))
        {
            cmd.Parameters.AddWithValue("@Cutoff", cutoff);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var createdAt = reader.GetDateTime(7);
                minDate = !minDate.HasValue || createdAt < minDate.Value ? createdAt : minDate;
                maxDate = !maxDate.HasValue || createdAt > maxDate.Value ? createdAt : maxDate;
                rows.Add(ReadRow(reader));
            }
        }

        if (rows.Count == 0)
            return null;

        var uri = await ExecuteArchiveAndIndexPipelineAsync(
            archiveBucket,
            "Employees",
            rows,
            rows.Count,
            minDate,
            maxDate,
            ct);

        // Step 5: Detach any non-cascading FK references, then hard delete inactive employees
        const string deleteSql = @"
            UPDATE dbo.HistoryLog SET EmployeeID = NULL
            WHERE EmployeeID IN (
                SELECT EmployeeID FROM dbo.Employees
                WHERE (EmployeeStatus = 'Inactive' OR Status = 'Inactive') AND CreatedAt <= @Cutoff
            );

            DELETE FROM dbo.Employees
            WHERE (EmployeeStatus = 'Inactive' OR Status = 'Inactive')
              AND CreatedAt <= @Cutoff;";

        await using (var delCmd = new SqlCommand(deleteSql, conn))
        {
            delCmd.Parameters.AddWithValue("@Cutoff", cutoff);
            await delCmd.ExecuteNonQueryAsync(ct);
        }

        _logger.LogInformation("Archived and hard-deleted {Count} rows from dbo.Employees to {Uri}.", rows.Count, uri);
        return uri;
    }

    private async Task<string?> ArchiveInactiveCustomersAsync(string archiveBucket, DateTime cutoff, CancellationToken ct)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(ct);

        if (!await TableExistsAsync(conn, "Customer", ct))
            return null;

        var rows = new List<Dictionary<string, object?>>();
        DateTime? minDate = null;
        DateTime? maxDate = null;

        const string selectSql = @"
            SELECT c.CustomerID, c.CustomerFirstName, c.CustomerLastName, c.CustomerMiddleName,
                   c.CustomerEmail, c.CustomerPhoneNumber, c.CustomerStatus, c.CreatedAt
            FROM dbo.Customer c
            WHERE c.CustomerStatus = 'Inactive'
              AND c.CreatedAt <= @Cutoff
              AND NOT EXISTS (SELECT 1 FROM dbo.EventAppointment ea WHERE ea.CustomerID = c.CustomerID)
              AND NOT EXISTS (SELECT 1 FROM dbo.BookingNegotiation bn WHERE bn.CustomerID = c.CustomerID)
            ORDER BY c.CreatedAt ASC;";

        await using (var cmd = new SqlCommand(selectSql, conn))
        {
            cmd.Parameters.AddWithValue("@Cutoff", cutoff);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var createdAt = reader.GetDateTime(7);
                minDate = !minDate.HasValue || createdAt < minDate.Value ? createdAt : minDate;
                maxDate = !maxDate.HasValue || createdAt > maxDate.Value ? createdAt : maxDate;
                rows.Add(ReadRow(reader));
            }
        }

        if (rows.Count == 0)
            return null;

        var uri = await ExecuteArchiveAndIndexPipelineAsync(
            archiveBucket,
            "Customer",
            rows,
            rows.Count,
            minDate,
            maxDate,
            ct);

        // Step 5: Detach HistoryLog references and hard delete inactive customers
        const string deleteSql = @"
            UPDATE dbo.HistoryLog SET CustomerID = NULL
            WHERE CustomerID IN (
                SELECT c.CustomerID FROM dbo.Customer c
                WHERE c.CustomerStatus = 'Inactive'
                  AND c.CreatedAt <= @Cutoff
                  AND NOT EXISTS (SELECT 1 FROM dbo.EventAppointment ea WHERE ea.CustomerID = c.CustomerID)
                  AND NOT EXISTS (SELECT 1 FROM dbo.BookingNegotiation bn WHERE bn.CustomerID = c.CustomerID)
            );

            DELETE c FROM dbo.Customer c
            WHERE c.CustomerStatus = 'Inactive'
              AND c.CreatedAt <= @Cutoff
              AND NOT EXISTS (SELECT 1 FROM dbo.EventAppointment ea WHERE ea.CustomerID = c.CustomerID)
              AND NOT EXISTS (SELECT 1 FROM dbo.BookingNegotiation bn WHERE bn.CustomerID = c.CustomerID);";

        await using (var delCmd = new SqlCommand(deleteSql, conn))
        {
            delCmd.Parameters.AddWithValue("@Cutoff", cutoff);
            await delCmd.ExecuteNonQueryAsync(ct);
        }

        _logger.LogInformation("Archived and hard-deleted {Count} rows from dbo.Customer to {Uri}.", rows.Count, uri);
        return uri;
    }

    private async Task<string?> ArchiveInactiveAdminsAsync(string archiveBucket, DateTime cutoff, CancellationToken ct)
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(ct);

        if (!await TableExistsAsync(conn, "Admin", ct))
            return null;

        var rows = new List<Dictionary<string, object?>>();
        DateTime? minDate = null;
        DateTime? maxDate = null;

        const string selectSql = @"
            SELECT AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, Email, isFirstLogin, CreatedAt
            FROM dbo.Admin
            WHERE AdminRole = 'Inactive'
              AND CreatedAt <= @Cutoff
            ORDER BY CreatedAt ASC;";

        await using (var cmd = new SqlCommand(selectSql, conn))
        {
            cmd.Parameters.AddWithValue("@Cutoff", cutoff);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var createdAt = reader.GetDateTime(7);
                minDate = !minDate.HasValue || createdAt < minDate.Value ? createdAt : minDate;
                maxDate = !maxDate.HasValue || createdAt > maxDate.Value ? createdAt : maxDate;
                rows.Add(ReadRow(reader));
            }
        }

        if (rows.Count == 0)
            return null;

        var uri = await ExecuteArchiveAndIndexPipelineAsync(
            archiveBucket,
            "Admin",
            rows,
            rows.Count,
            minDate,
            maxDate,
            ct);

        // Step 5: Hard delete inactive admins only after R2 upload and ArchiveIndex insert succeed
        const string deleteSql = @"
            DELETE FROM dbo.Admin
            WHERE AdminRole = 'Inactive'
              AND CreatedAt <= @Cutoff;";

        await using (var delCmd = new SqlCommand(deleteSql, conn))
        {
            delCmd.Parameters.AddWithValue("@Cutoff", cutoff);
            await delCmd.ExecuteNonQueryAsync(ct);
        }

        _logger.LogInformation("Archived and hard-deleted {Count} rows from dbo.Admin to {Uri}.", rows.Count, uri);
        return uri;
    }

    /// <summary>
    /// Enforces steps 1-4 of the strict data-loss prevention protocol:
    /// 1. Serialize the data to JSON stream.
    /// 2. Upload the JSON stream to the Cloudflare R2 'archive' bucket.
    /// 3. Verify a successful HTTP 200 OK / non-empty URI return from R2.
    /// 4. Insert the new index record into the dbo.ArchiveIndex SQL table.
    /// </summary>
    private async Task<string> ExecuteArchiveAndIndexPipelineAsync<T>(
        string archiveBucket,
        string entityName,
        T payload,
        int recordCount,
        DateTime? dateRangeFrom,
        DateTime? dateRangeTo,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var objectKey = $"{now:yyyy}/{entityName.ToLowerInvariant()}-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}.json";

        // 1. Serialize data to JSON stream
        await using var jsonStream = new MemoryStream();
        await JsonSerializer.SerializeAsync(jsonStream, payload, JsonOptions, ct);
        jsonStream.Position = 0;

        // 2. Upload JSON stream to Cloudflare R2 'archive' bucket
        var fileUri = await _r2StorageService.UploadFileAsync(
            archiveBucket,
            objectKey,
            jsonStream,
            "application/json");

        // 3. Verify a successful HTTP 200 OK / URI return from R2
        if (string.IsNullOrWhiteSpace(fileUri))
        {
            throw new InvalidOperationException(
                $"Cloudflare R2 archive upload failed for entity '{entityName}' (key: '{objectKey}'). Source records were NOT deleted.");
        }

        // 4. Insert the new index record into the ArchiveIndex SQL table
        var indexEntry = new ArchiveIndex
        {
            EntityName = entityName,
            BucketName = archiveBucket,
            R2Key = objectKey,
            FileUri = fileUri,
            RecordCount = recordCount,
            DateRangeFrom = dateRangeFrom,
            DateRangeTo = dateRangeTo,
            ArchivedAt = now
        };

        _db.ArchiveIndices.Add(indexEntry);
        await _db.SaveChangesAsync(ct);

        return fileUri;
    }

    private async Task EnsureArchiveIndexTableExistsAsync(CancellationToken ct)
    {
        const string sql = @"
            IF OBJECT_ID(N'dbo.ArchiveIndex', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ArchiveIndex (
                    ArchiveId     int           IDENTITY(1,1) NOT NULL CONSTRAINT PK_ArchiveIndex PRIMARY KEY,
                    EntityName    nvarchar(128) NOT NULL,
                    BucketName    nvarchar(100) NOT NULL CONSTRAINT DF_ArchiveIndex_Bucket DEFAULT 'archive',
                    R2Key         nvarchar(500) NOT NULL,
                    FileUri       nvarchar(1000) NOT NULL,
                    RecordCount   int           NOT NULL CONSTRAINT DF_ArchiveIndex_RecordCount DEFAULT 0,
                    DateRangeFrom datetime2     NULL,
                    DateRangeTo   datetime2     NULL,
                    ArchivedAt    datetime2     NOT NULL CONSTRAINT DF_ArchiveIndex_ArchivedAt DEFAULT SYSUTCDATETIME()
                );
            END;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ArchiveIndex_Entity_ArchivedAt' AND object_id = OBJECT_ID(N'dbo.ArchiveIndex'))
                CREATE INDEX IX_ArchiveIndex_Entity_ArchivedAt ON dbo.ArchiveIndex (EntityName, ArchivedAt DESC);";

        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> TableExistsAsync(SqlConnection conn, string tableName, CancellationToken ct)
    {
        const string sql = "SELECT CASE WHEN OBJECT_ID(@TableName, N'U') IS NOT NULL THEN 1 ELSE 0 END;";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@TableName", $"dbo.{tableName}");
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) == 1;
    }

    private static Dictionary<string, object?> ReadRow(SqlDataReader reader)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var val = reader.IsDBNull(i) ? null : reader.GetValue(i);
            dict[reader.GetName(i)] = val;
        }
        return dict;
    }

    private string GetConnectionString() =>
        _db.Database.GetConnectionString()
        ?? throw new InvalidOperationException("Database connection string is not configured.");
}
