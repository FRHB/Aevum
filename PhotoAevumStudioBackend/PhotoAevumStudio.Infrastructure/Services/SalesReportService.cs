using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Infrastructure.Services;

/// <summary>
/// Live Sales &amp; Reports queries over dbo.PaymentTransactions / dbo.EventAppointment and related tables.
/// No figures are hardcoded: an empty period yields genuine zeros / empty lists.
/// </summary>
public sealed class SalesReportService : ISalesReportService
{
    // Operating assumption used only as the denominator for capacity utilization.
    private const int StudioHoursPerDay = 10;
    private const string NotCancelled = "ea.EventStatus NOT IN (N'Cancelled', N'Expired')";
    private const string CompletedStatuses = "N'Complete', N'Completed', N'Delivered', N'Event Done'";

    private readonly ApplicationDbContext _db;
    private readonly IAuditLogService _audit;
    private readonly ILogger<SalesReportService> _logger;

    public SalesReportService(ApplicationDbContext db, IAuditLogService audit, ILogger<SalesReportService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    private Task<SqlConnection> OpenAsync(CancellationToken ct) =>
        SqlResilienceHelper.OpenConnectionAsync(_db.Database.GetConnectionString(), ct);

    private static (DateOnly Start, DateOnly End) ResolveRange(SalesQuery q)
    {
        var end = q.EndDate ?? DateOnly.FromDateTime(DateTime.Today);
        var start = q.StartDate ?? end.AddDays(-29);
        if (start > end) (start, end) = (end, start);
        return (start, end);
    }

    // ── Sales ────────────────────────────────────────────────────────────

    public async Task<SalesResponseDto> GetSalesAsync(SalesQuery query, CancellationToken ct = default)
    {
        var (start, end) = ResolveRange(query);
        var days = end.DayNumber - start.DayNumber + 1;
        var priorEnd = start.AddDays(-1);
        var priorStart = priorEnd.AddDays(-(days - 1));

        await using var conn = await OpenAsync(ct);
        var all = await LoadSalesAsync(conn, start, end, ct);
        var priorRevenue = await SumCompletedAsync(conn, priorStart, priorEnd, ct);

        var completed = all.Where(r => r.Status == "Completed").ToList();
        var pending = all.Where(r => r.Status == "Pending").ToList();
        var revenue = completed.Sum(r => r.Amount);
        double? delta = priorRevenue > 0 ? (double)((revenue - priorRevenue) / priorRevenue * 100m) : null;

        var summary = new SalesSummaryDto(
            revenue, priorRevenue, delta,
            completed.Count, all.Count,
            pending.Sum(r => r.Amount), pending.Count,
            completed.Count > 0 ? Math.Round(revenue / completed.Count, 2) : 0m);

        return new SalesResponseDto(summary, ApplyFilters(all, query), start, end);
    }

    private static List<SaleRowDto> ApplyFilters(IEnumerable<SaleRowDto> rows, SalesQuery q)
    {
        var result = rows;
        if (!string.IsNullOrWhiteSpace(q.Status) && !q.Status.Equals("All", StringComparison.OrdinalIgnoreCase))
            result = result.Where(r => r.Status.Equals(q.Status, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            result = result.Where(r =>
                r.InvoiceNo.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                r.TransactionId.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                r.ClientName.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                r.ServiceName.Contains(s, StringComparison.OrdinalIgnoreCase));
        }
        return result.ToList();
    }

    private const string StatusCase = @"
        CASE
            WHEN pt.Status = N'Paid' AND ISNULL(pt.EmployeeRemittanceStatus, N'') <> N'PendingAdminApproval' THEN N'Completed'
            WHEN pt.Status IN (N'Paid', N'Pending') THEN N'Pending'
            ELSE pt.Status
        END";

    private static async Task<List<SaleRowDto>> LoadSalesAsync(SqlConnection conn, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var sql = $@"
            SELECT pt.PaymentTransactionID,
                   COALESCE(pt.PaidAt, pt.CreatedAt) AS TxDate,
                   COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(c.CustomerFirstName, N' ', c.CustomerLastName))), N''), ea.CustomerID, N'Walk-in') AS ClientName,
                   COALESCE(NULLIF(svc.Names, N''), ea.EventName, N'Studio Package') AS ServiceName,
                   pt.PaymentMethod,
                   pt.Amount,
                   {StatusCase} AS SaleStatus,
                   pt.EventAppointmentID
            FROM dbo.PaymentTransactions pt
            LEFT JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = pt.EventAppointmentID
            LEFT JOIN dbo.Customer c ON c.CustomerID = ea.CustomerID
            OUTER APPLY (
                SELECT STRING_AGG(COALESCE(s.ServiceName, cp.ComboPackageName, ads.AdditionalServiceName, eli.ServiceNameSnapshot), N', ') AS Names
                FROM dbo.EventLineItem eli
                LEFT JOIN dbo.Services s ON s.ServicesID = eli.ServicesID
                LEFT JOIN dbo.ComboPackage cp ON cp.ComboPackageID = eli.ComboPackageID
                LEFT JOIN dbo.AdditionalServices ads ON ads.AdditionalServicesID = eli.AdditionalServicesID
                WHERE eli.EventAppointmentID = pt.EventAppointmentID
            ) svc
            WHERE CAST(COALESCE(pt.PaidAt, pt.CreatedAt) AS date) BETWEEN @s AND @e
            ORDER BY TxDate DESC;";

        var list = new List<SaleRowDto>();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@s", start.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@e", end.ToDateTime(TimeOnly.MinValue));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var id = r.GetString(0);
            var date = r.GetDateTime(1);
            list.Add(new SaleRowDto(
                id, MakeInvoiceNo(id, date), date,
                r.GetString(2), r.GetString(3),
                FormatMethod(r.IsDBNull(4) ? null : r.GetString(4)),
                r.GetDecimal(5), r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7)));
        }
        return list;
    }

    private static async Task<decimal> SumCompletedAsync(SqlConnection conn, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var sql = $@"
            SELECT ISNULL(SUM(pt.Amount), 0)
            FROM dbo.PaymentTransactions pt
            WHERE pt.Status = N'Paid' AND ISNULL(pt.EmployeeRemittanceStatus, N'') <> N'PendingAdminApproval'
              AND CAST(COALESCE(pt.PaidAt, pt.CreatedAt) AS date) BETWEEN @s AND @e;";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@s", start.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@e", end.ToDateTime(TimeOnly.MinValue));
        return Convert.ToDecimal(await cmd.ExecuteScalarAsync(ct) ?? 0m, CultureInfo.InvariantCulture);
    }

    private static string MakeInvoiceNo(string txId, DateTime date) =>
        txId.StartsWith("INV", StringComparison.OrdinalIgnoreCase) ? txId : $"INV-{date:yyyy}-{txId}";

    private static string FormatMethod(string? raw) => raw switch
    {
        null or "" => "—",
        "Cash" => "Cash",
        "Bank_Transfer" => "Bank Transfer",
        _ => raw.Replace("Xendit_", "", StringComparison.OrdinalIgnoreCase).Replace('_', ' ')
    };

    public async Task<string> ExportSalesCsvAsync(SalesQuery query, CancellationToken ct = default)
    {
        var data = await GetSalesAsync(query, ct);
        var sb = new StringBuilder();
        sb.AppendLine("Invoice #,Transaction ID,Date,Client,Package / Service,Payment Method,Amount (PHP),Status");
        foreach (var r in data.Rows)
        {
            sb.AppendLine(string.Join(',',
                Csv(r.InvoiceNo), Csv(r.TransactionId), Csv(r.Date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                Csv(r.ClientName), Csv(r.ServiceName), Csv(r.PaymentMethod),
                r.Amount.ToString("0.00", CultureInfo.InvariantCulture), Csv(r.Status)));
        }
        return sb.ToString();
    }

    private static string Csv(string? v)
    {
        v ??= string.Empty;
        // Neutralize spreadsheet formula injection and escape quotes.
        if (v.Length > 0 && "=+-@".Contains(v[0])) v = "'" + v;
        return $"\"{v.Replace("\"", "\"\"")}\"";
    }

    // ── Record sale ──────────────────────────────────────────────────────

    public async Task<IReadOnlyList<RecordableAppointmentDto>> GetRecordableAppointmentsAsync(CancellationToken ct = default)
    {
        const string sql = $@"
            SELECT TOP 200 ea.EventAppointmentID, ea.EventName,
                   COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(c.CustomerFirstName, N' ', c.CustomerLastName))), N''), ea.CustomerID) AS ClientName,
                   ea.EventDate, ea.TotalAmount, ea.DownpaymentAmount,
                   ISNULL((SELECT SUM(p.Amount) FROM dbo.PaymentTransactions p
                           WHERE p.EventAppointmentID = ea.EventAppointmentID AND p.Status = N'Paid'), 0) AS PaidAmount
            FROM dbo.EventAppointment ea
            LEFT JOIN dbo.Customer c ON c.CustomerID = ea.CustomerID
            WHERE " + NotCancelled + @"
            ORDER BY ea.EventDate DESC;";

        var list = new List<RecordableAppointmentDto>();
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var total = r.GetDecimal(4);
            var paid = r.GetDecimal(6);
            var outstanding = Math.Max(total - paid, 0m);
            if (outstanding <= 0) continue;
            list.Add(new RecordableAppointmentDto(
                r.GetString(0), r.GetString(1), r.GetString(2),
                DateOnly.FromDateTime(r.GetDateTime(3)), total, r.GetDecimal(5), paid, outstanding));
        }
        return list;
    }

    private static readonly Dictionary<string, string> MethodMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cash"] = "Cash",
        ["GCash"] = "Xendit_GCash",
        ["Bank Transfer"] = "Bank_Transfer",
        ["Maya"] = "Xendit_Maya",
        ["Card"] = "Xendit_Card"
    };

    public async Task<RecordSaleResultDto> RecordSaleAsync(
        RecordSaleRequest request, string userId, string userEmail, string userName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.AppointmentId))
            throw new ArgumentException("Select a booking to record the sale against.");
        if (request.Amount <= 0)
            throw new ArgumentException("Amount must be greater than zero.");
        if (!MethodMap.TryGetValue(request.PaymentMethod?.Trim() ?? "", out var method))
            throw new ArgumentException("Unsupported payment method.");

        await using var conn = await OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        decimal total, paid; string downpaymentStatus;
        await using (var lookup = new SqlCommand($@"
            SELECT ea.TotalAmount, ea.DownpaymentStatus,
                   ISNULL((SELECT SUM(p.Amount) FROM dbo.PaymentTransactions p
                           WHERE p.EventAppointmentID = ea.EventAppointmentID AND p.Status = N'Paid'), 0)
            FROM dbo.EventAppointment ea WITH (UPDLOCK)
            WHERE ea.EventAppointmentID = @id AND {NotCancelled};", conn, tx))
        {
            lookup.Parameters.AddWithValue("@id", request.AppointmentId.Trim());
            await using var rd = await lookup.ExecuteReaderAsync(ct);
            if (!await rd.ReadAsync(ct))
                throw new ArgumentException("Booking not found or no longer active.");
            total = rd.GetDecimal(0);
            downpaymentStatus = rd.IsDBNull(1) ? "Pending" : rd.GetString(1);
            paid = rd.GetDecimal(2);
        }

        var outstanding = total - paid;
        if (request.Amount > outstanding + 0.005m)
            throw new ArgumentException($"Amount exceeds the outstanding balance of ₱{Math.Max(outstanding, 0):N2}.");

        var isDownpayment = string.Equals(request.PaymentType, "Downpayment", StringComparison.OrdinalIgnoreCase)
                            || (request.PaymentType is null && !downpaymentStatus.Equals("Paid", StringComparison.OrdinalIgnoreCase));
        var paymentType = isDownpayment ? "Downpayment_25" : "Final_Balance";
        var now = DateTime.UtcNow;
        var txId = $"PTX-{Guid.NewGuid():N}"[..16].ToUpperInvariant();

        await using (var ins = new SqlCommand(@"
            INSERT INTO dbo.PaymentTransactions
                (PaymentTransactionID, EventAppointmentID, PaymentType, PaymentMethod, Amount, Status, ExternalReference,
                 CustomerPaymentStatus, EmployeeRemittanceStatus, AdminApproverID, ApprovalTimestamp, PaidAt, CreatedAt)
            VALUES (@tx, @ea, @type, @method, @amt, N'Paid', @ref, N'Cleared', N'NotApplicable', @admin, @now, @now, @now);", conn, tx))
        {
            ins.Parameters.AddWithValue("@tx", txId);
            ins.Parameters.AddWithValue("@ea", request.AppointmentId.Trim());
            ins.Parameters.AddWithValue("@type", paymentType);
            ins.Parameters.AddWithValue("@method", method);
            ins.Parameters.AddWithValue("@amt", request.Amount);
            ins.Parameters.AddWithValue("@ref", (object?)Truncate(request.Reference, 100) ?? DBNull.Value);
            ins.Parameters.AddWithValue("@admin", Truncate(userId, 100) ?? "STAFF");
            ins.Parameters.AddWithValue("@now", now);
            await ins.ExecuteNonQueryAsync(ct);
        }

        var upd = isDownpayment
            ? "UPDATE dbo.EventAppointment SET DownpaymentStatus = N'Paid', DownpaymentPaidAt = @now WHERE EventAppointmentID = @ea;"
            : "UPDATE dbo.EventAppointment SET FinalPaymentStatus = N'Paid', FinalPaymentMethod = @method, FinalPaymentPaidAt = @now WHERE EventAppointmentID = @ea;";
        await using (var u = new SqlCommand(upd, conn, tx))
        {
            u.Parameters.AddWithValue("@now", now);
            u.Parameters.AddWithValue("@ea", request.AppointmentId.Trim());
            u.Parameters.AddWithValue("@method", method);
            await u.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);

        var invoice = MakeInvoiceNo(txId, now);
        try
        {
            await _audit.LogAsync(userId, userEmail, "Record Sale", "PaymentTransaction", txId,
                $"{invoice}; booking {request.AppointmentId}; ₱{request.Amount:N2} via {request.PaymentMethod}; by {userName}. {request.Notes}".Trim(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sale {TxId} recorded but audit entry failed.", txId);
        }

        return new RecordSaleResultDto(txId, invoice, request.Amount, "Completed");
    }

    private static string? Truncate(string? v, int max) =>
        string.IsNullOrWhiteSpace(v) ? null : (v.Trim().Length <= max ? v.Trim() : v.Trim()[..max]);

    // ── Reports ──────────────────────────────────────────────────────────

    public async Task<ReportSummaryDto> GetReportAsync(int year, int month, CancellationToken ct = default)
    {
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);
        var priorStart = start.AddMonths(-1);
        var daysInMonth = DateTime.DaysInMonth(year, month);

        await using var conn = await OpenAsync(ct);

        async Task<SqlDataReader> Run(string sql, DateTime s, DateTime e)
        {
            var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@s", s);
            cmd.Parameters.AddWithValue("@e", e);
            return await cmd.ExecuteReaderAsync(ct);
        }

        // Gross booking value + sessions + capacity hours
        const string grossSql = "SELECT ISNULL(SUM(ea.TotalAmount),0), COUNT(*), ISNULL(SUM(ISNULL(ea.DurationMinutes,60)),0) FROM dbo.EventAppointment ea WHERE ea.EventDate >= @s AND ea.EventDate < @e AND " + NotCancelled + ";";
        decimal gross, priorGross; int sessions; long minutes;
        await using (var r = await Run(grossSql, start, end)) { await r.ReadAsync(ct); gross = r.GetDecimal(0); sessions = r.GetInt32(1); minutes = Convert.ToInt64(r.GetValue(2)); }
        await using (var r = await Run(grossSql, priorStart, start)) { await r.ReadAsync(ct); priorGross = r.GetDecimal(0); }
        double? grossDelta = priorGross > 0 ? (double)((gross - priorGross) / priorGross * 100m) : null;

        var capacity = Math.Round(minutes / 60.0 / (daysInMonth * StudioHoursPerDay) * 100, 1);
        var badge = sessions == 0 ? "No bookings" : capacity < 40 ? "Under-used" : capacity <= 85 ? "Optimal" : "Near capacity";

        string? peak = null;
        await using (var r = await Run("SELECT TOP 1 DATEPART(hour, ea.EventTime) h, COUNT(*) c FROM dbo.EventAppointment ea WHERE ea.EventDate >= @s AND ea.EventDate < @e AND " + NotCancelled + " GROUP BY DATEPART(hour, ea.EventTime) ORDER BY c DESC, h;", start, end))
            if (await r.ReadAsync(ct)) { var h = r.GetInt32(0); peak = $"{DateTime.Today.AddHours(h):h:00 tt} – {DateTime.Today.AddHours(h + 1):h:00 tt}"; }

        // Staff
        double hours = 0; int staff = 0; double? efficiency = null;
        await using (var r = await Run("SELECT ISNULL(SUM(TotalHours),0), COUNT(DISTINCT EmployeeStudioId), COUNT(*), SUM(CASE WHEN ClockOut IS NOT NULL THEN 1 ELSE 0 END) FROM dbo.EmployeeTimesheets WHERE WorkDate >= @s AND WorkDate < @e;", start, end))
        {
            await r.ReadAsync(ct);
            hours = Math.Round(Convert.ToDouble(r.GetValue(0)), 1); staff = r.GetInt32(1);
            var n = r.GetInt32(2); var closed = r.IsDBNull(3) ? 0 : r.GetInt32(3);
            efficiency = n > 0 ? Math.Round(closed * 100.0 / n, 1) : null;
        }

        // Retention
        int clients = 0, repeat = 0;
        await using (var r = await Run(@"SELECT COUNT(*), ISNULL(SUM(CASE WHEN x.Total > 1 THEN 1 ELSE 0 END),0) FROM (
                SELECT ea.CustomerID, (SELECT COUNT(*) FROM dbo.EventAppointment e2 WHERE e2.CustomerID = ea.CustomerID AND e2.EventStatus NOT IN (N'Cancelled', N'Expired') AND e2.EventDate < @e) AS Total
                FROM dbo.EventAppointment ea WHERE ea.EventDate >= @s AND ea.EventDate < @e AND " + NotCancelled + " GROUP BY ea.CustomerID) x;", start, end))
        { await r.ReadAsync(ct); clients = r.GetInt32(0); repeat = r.GetInt32(1); }

        // Revenue by category (line items)
        const string itemFrom = @"
            FROM dbo.EventLineItem eli
            INNER JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = eli.EventAppointmentID
            LEFT JOIN dbo.Services s ON s.ServicesID = eli.ServicesID
            LEFT JOIN dbo.ComboPackage cp ON cp.ComboPackageID = eli.ComboPackageID
            LEFT JOIN dbo.AdditionalServices ads ON ads.AdditionalServicesID = eli.AdditionalServicesID
            OUTER APPLY (
                SELECT TOP 1 sc.ServiceCategoryName AS CategoryName
                FROM dbo.ServiceCategoryJoins scj
                INNER JOIN dbo.ServiceCategory sc ON sc.ServiceCategoryID = scj.ServiceCategoryID
                WHERE (eli.ServicesID IS NOT NULL AND scj.ServicesID = eli.ServicesID)
                   OR (eli.AdditionalServicesID IS NOT NULL AND scj.AdditionalServicesID = eli.AdditionalServicesID)
                   OR (eli.ComboPackageID IS NOT NULL AND scj.ComboPackageID = eli.ComboPackageID)
                ORDER BY sc.ServiceCategoryName) cat
            WHERE ea.EventDate >= @s AND ea.EventDate < @e AND ";
        var byCategory = new List<LabeledAmountDto>();
        await using (var r = await Run("SELECT COALESCE(cat.CategoryName, N'Uncategorized'), SUM(eli.LineTotal), COUNT(*) " + itemFrom + NotCancelled + " GROUP BY COALESCE(cat.CategoryName, N'Uncategorized') ORDER BY 2 DESC;", start, end))
            while (await r.ReadAsync(ct)) byCategory.Add(new(r.GetString(0), r.GetDecimal(1), r.GetInt32(2)));

        var popular = new List<LabeledAmountDto>();
        await using (var r = await Run("SELECT TOP 5 COALESCE(s.ServiceName, cp.ComboPackageName, ads.AdditionalServiceName, eli.ServiceNameSnapshot, N'Studio Package'), SUM(eli.LineTotal), COUNT(*) " + itemFrom + NotCancelled + " GROUP BY COALESCE(s.ServiceName, cp.ComboPackageName, ads.AdditionalServiceName, eli.ServiceNameSnapshot, N'Studio Package') ORDER BY 3 DESC, 2 DESC;", start, end))
            while (await r.ReadAsync(ct)) popular.Add(new(r.GetString(0), r.GetDecimal(1), r.GetInt32(2)));

        // Deposit collection, reconciliation, completion
        double? depositRate = null; int completedSessions = 0;
        await using (var r = await Run($"SELECT ISNULL(SUM(ea.DownpaymentAmount),0), ISNULL(SUM(CASE WHEN ea.DownpaymentStatus = N'Paid' THEN ea.DownpaymentAmount ELSE 0 END),0), SUM(CASE WHEN ea.EventStatus IN ({CompletedStatuses}) THEN 1 ELSE 0 END) FROM dbo.EventAppointment ea WHERE ea.EventDate >= @s AND ea.EventDate < @e AND {NotCancelled};", start, end))
        {
            await r.ReadAsync(ct);
            var due = r.GetDecimal(0); var got = r.GetDecimal(1);
            depositRate = due > 0 ? Math.Round((double)(got / due * 100m), 1) : null;
            completedSessions = r.IsDBNull(2) ? 0 : r.GetInt32(2);
        }
        int cleared = 0, unreconciled = 0;
        await using (var r = await Run("SELECT ISNULL(SUM(CASE WHEN Status = N'Paid' AND ISNULL(EmployeeRemittanceStatus,N'') <> N'PendingAdminApproval' THEN 1 ELSE 0 END),0), ISNULL(SUM(CASE WHEN ISNULL(EmployeeRemittanceStatus,N'') = N'PendingAdminApproval' THEN 1 ELSE 0 END),0) FROM dbo.PaymentTransactions WHERE CreatedAt >= @s AND CreatedAt < @e;", start, end))
        { await r.ReadAsync(ct); cleared = r.GetInt32(0); unreconciled = r.GetInt32(1); }

        // Weekday trend (0 = Monday; 1900-01-01 was a Monday)
        var weekdayNames = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        var weekdayCounts = new int[7];
        await using (var r = await Run("SELECT DATEDIFF(day, '19000101', ea.EventDate) % 7, COUNT(*) FROM dbo.EventAppointment ea WHERE ea.EventDate >= @s AND ea.EventDate < @e AND " + NotCancelled + " GROUP BY DATEDIFF(day, '19000101', ea.EventDate) % 7;", start, end))
            while (await r.ReadAsync(ct)) weekdayCounts[r.GetInt32(0)] = r.GetInt32(1);
        var weekdays = weekdayNames.Select((n, i) => new LabeledAmountDto(n, 0m, weekdayCounts[i])).ToList();

        // Weekday x timeslot heatmap (Slot 0: <12h, Slot 1: 12-16h, Slot 2: >=16h)
        var heatmapCells = new List<HeatmapCellDto>();
        await using (var r = await Run(@"
            SELECT DATEDIFF(day, '19000101', ea.EventDate) % 7,
                   CASE
                       WHEN DATEPART(hour, ea.EventTime) < 12 THEN 0
                       WHEN DATEPART(hour, ea.EventTime) < 16 THEN 1
                       ELSE 2
                   END,
                   COUNT(*)
            FROM dbo.EventAppointment ea
            WHERE ea.EventDate >= @s AND ea.EventDate < @e AND " + NotCancelled + @"
            GROUP BY DATEDIFF(day, '19000101', ea.EventDate) % 7,
                     CASE
                         WHEN DATEPART(hour, ea.EventTime) < 12 THEN 0
                         WHEN DATEPART(hour, ea.EventTime) < 16 THEN 1
                         ELSE 2
                     END;", start, end))
        {
            while (await r.ReadAsync(ct))
                heatmapCells.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2)));
        }

        // Consultation conversion
        int consults = 0, won = 0;
        await using (var r = await Run("SELECT COUNT(*), ISNULL(SUM(CASE WHEN NegotiationStatus IN (N'Deal_Won', N'Appointment Booked') THEN 1 ELSE 0 END),0) FROM dbo.BookingNegotiation WHERE CreatedAt >= @s AND CreatedAt < @e;", start, end))
        { await r.ReadAsync(ct); consults = r.GetInt32(0); won = r.GetInt32(1); }

        // Equipment
        var equipment = new List<EquipmentUsageDto>();
        await using (var r = await Run($@"SELECT e.EquipmentName, ISNULL(et.EquipmentTypeName, e.EquipmentTypeID), ISNULL(u.Dispatches,0), ISNULL(u.DaysUsed,0), ISNULL(e.EquipmentStatus, N'Available')
            FROM dbo.Equipment e
            LEFT JOIN dbo.EquipmentType et ON et.EquipmentTypeID = e.EquipmentTypeID
            OUTER APPLY (SELECT COUNT(*) AS Dispatches, COUNT(DISTINCT ea.EventDate) AS DaysUsed
                         FROM dbo.EventEquipment ee INNER JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = ee.EventAppointmentID
                         WHERE ee.EquipmentID = e.EquipmentID AND ea.EventDate >= @s AND ea.EventDate < @e AND {NotCancelled}) u
            ORDER BY ISNULL(u.Dispatches,0) DESC, e.EquipmentName;", start, end))
            while (await r.ReadAsync(ct))
            {
                var d = r.GetInt32(3);
                equipment.Add(new(r.GetString(0), r.GetString(1), r.GetInt32(2), d, Math.Round(d * 100.0 / daysInMonth, 1), r.GetString(4)));
            }
        int openTickets = 0;
        await using (var r = await Run("SELECT COUNT(*) FROM dbo.MaintenanceSchedule WHERE ResolvedAt IS NULL AND @s IS NOT NULL AND @e IS NOT NULL;", start, end))
        { await r.ReadAsync(ct); openTickets = r.GetInt32(0); }

        return new ReportSummaryDto(
            year, month, start.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
            gross, priorGross, grossDelta, sessions,
            capacity, badge, peak,
            efficiency, hours, staff,
            clients > 0 ? Math.Round(repeat * 100.0 / clients, 1) : null, repeat, clients, null,
            byCategory, depositRate, cleared, unreconciled,
            weekdays, popular,
            consults > 0 ? Math.Round(won * 100.0 / consults, 1) : null, consults, won, completedSessions,
            equipment.Take(10).ToList(), openTickets,
            equipment.Count(e => e.Status is "Maintenance" or "Needs Replacement"),
            equipment.Sum(e => e.DispatchCount),
            heatmapCells);
    }
}
