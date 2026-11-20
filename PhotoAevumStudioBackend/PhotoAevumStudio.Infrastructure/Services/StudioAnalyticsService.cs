using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Infrastructure.Services;

public sealed class StudioAnalyticsService : IStudioAnalyticsService
{
    private static volatile bool _schemaEnsured;
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);

    private readonly ApplicationDbContext _db;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<StudioAnalyticsService> _logger;

    public StudioAnalyticsService(
        ApplicationDbContext db,
        IAuditLogService auditLogService,
        ILogger<StudioAnalyticsService> logger)
    {
        _db = db;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        if (_schemaEnsured) return;
        await SchemaLock.WaitAsync(ct);
        try
        {
            if (_schemaEnsured) return;
            var connStr = _db.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connStr)) return;

            await using var conn = await SqlResilienceHelper.OpenConnectionAsync(connStr, ct);
            const string sql = @"
                IF OBJECT_ID(N'dbo.StudioAnalyticsActions', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.StudioAnalyticsActions (
                        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StudioAnalyticsActions PRIMARY KEY,
                        ActionKey nvarchar(120) NOT NULL,
                        Title nvarchar(255) NOT NULL,
                        Category nvarchar(80) NOT NULL,
                        EffortTag nvarchar(50) NOT NULL,
                        EstimatedImpact nvarchar(255) NOT NULL,
                        TriggerSignal nvarchar(500) NULL,
                        RelatedReferences nvarchar(1000) NULL,
                        Notes nvarchar(1000) NULL,
                        AppliedByUserId nvarchar(100) NOT NULL,
                        AppliedByName nvarchar(200) NOT NULL,
                        AppliedAtUtc datetime2 NOT NULL CONSTRAINT DF_StudioAnalyticsActions_AppliedAtUtc DEFAULT SYSUTCDATETIME(),
                        Status nvarchar(40) NOT NULL CONSTRAINT DF_StudioAnalyticsActions_Status DEFAULT 'Applied'
                    );
                    CREATE INDEX IX_StudioAnalyticsActions_Key_Date ON dbo.StudioAnalyticsActions (ActionKey, AppliedAtUtc DESC);
                END;";

            await using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(ct);
            _schemaEnsured = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Non-fatal warning ensuring dbo.StudioAnalyticsActions schema.");
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    public async Task<StudioAnalyticsOverviewDto> GetOverviewAsync(
        StudioAnalyticsQuery query,
        CancellationToken ct = default)
    {
        var snapshot = await LoadSnapshotAsync(ct);
        return StudioAnalyticsCalculator.Compute(snapshot, query);
    }

    public async Task<ApplyPrescriptiveActionsResultDto> ApplyPrescriptiveActionsAsync(
        ApplyPrescriptiveActionsRequest request,
        string userId,
        string userEmail,
        string userName,
        CancellationToken ct = default)
    {
        if (request.Actions is null || request.Actions.Count == 0)
        {
            return new ApplyPrescriptiveActionsResultDto(0, [], "No prescriptive actions selected.");
        }

        await EnsureSchemaAsync(ct);

        var now = DateTime.UtcNow;
        var createdEntities = new List<StudioAnalyticsAction>();

        foreach (var item in request.Actions.Where(a => !string.IsNullOrWhiteSpace(a.ActionKey)))
        {
            var refs = item.RelatedReferences is { Count: > 0 }
                ? string.Join(", ", item.RelatedReferences.Where(r => !string.IsNullOrWhiteSpace(r)))
                : null;

            var entity = new StudioAnalyticsAction
            {
                ActionKey = item.ActionKey.Trim(),
                Title = item.Title.Trim(),
                Category = string.IsNullOrWhiteSpace(item.Category) ? "Operations" : item.Category.Trim(),
                EffortTag = string.IsNullOrWhiteSpace(item.EffortTag) ? "Medium Effort" : item.EffortTag.Trim(),
                EstimatedImpact = item.EstimatedImpact?.Trim() ?? "Operational readiness",
                TriggerSignal = item.TriggerSignal?.Trim(),
                RelatedReferences = refs,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                AppliedByUserId = string.IsNullOrWhiteSpace(userId) ? "STAFF" : userId.Trim(),
                AppliedByName = string.IsNullOrWhiteSpace(userName) ? userEmail : userName.Trim(),
                AppliedAtUtc = now,
                Status = "Applied"
            };

            _db.StudioAnalyticsActions.Add(entity);
            createdEntities.Add(entity);
        }

        if (createdEntities.Count == 0)
        {
            return new ApplyPrescriptiveActionsResultDto(0, [], "No valid prescriptive actions to apply.");
        }

        await _db.SaveChangesAsync(ct);

        foreach (var entity in createdEntities)
        {
            await _auditLogService.LogAsync(
                userId: entity.AppliedByUserId,
                userEmail: userEmail,
                action: "Apply Prescriptive Action",
                resource: "StudioAnalytics",
                resourceId: entity.ActionKey,
                details: $"{entity.Title} [{entity.EffortTag}] — Impact: {entity.EstimatedImpact}" +
                         (string.IsNullOrWhiteSpace(entity.Notes) ? "" : $" | Note: {entity.Notes}"),
                ct: ct);
        }

        var dtos = createdEntities.Select(a => new AppliedAnalyticsActionDto(
            a.Id,
            a.ActionKey,
            a.Title,
            a.Category,
            a.EffortTag,
            a.EstimatedImpact,
            a.TriggerSignal,
            a.RelatedReferences,
            a.Notes,
            a.AppliedByUserId,
            a.AppliedByName,
            a.AppliedAtUtc,
            a.Status)).ToList();

        return new ApplyPrescriptiveActionsResultDto(
            AppliedCount: dtos.Count,
            AppliedActions: dtos,
            Message: $"Applied and persisted {dtos.Count} studio recommendation(s) to operational audit log.");
    }

    public async Task<string> ExportCsvAsync(StudioAnalyticsQuery query, CancellationToken ct = default)
    {
        var overview = await GetOverviewAsync(query, ct);
        return StudioAnalyticsCalculator.BuildCsvReport(overview);
    }

    private async Task<StudioAnalyticsSnapshot> LoadSnapshotAsync(CancellationToken ct)
    {
        var phtToday = DateOnly.FromDateTime(GetPhtNow());
        var connStr = _db.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connStr))
        {
            return new StudioAnalyticsSnapshot { Today = phtToday };
        }

        await EnsureSchemaAsync(ct);

        await using var conn = await SqlResilienceHelper.OpenConnectionAsync(connStr, ct);

        var lineItemsByEvent = await LoadEventLineItemsAsync(conn, ct);
        var bookings = await LoadBookingsAsync(conn, lineItemsByEvent, ct);
        var timesheets = await LoadTimesheetsAsync(conn, ct);
        var (employees, specs) = await LoadEmployeesAndSpecializationsAsync(conn, ct);
        var leaves = await LoadLeavesAsync(conn, ct);
        var equipment = await LoadEquipmentAsync(conn, ct);
        var maintenance = await LoadMaintenanceAsync(conn, ct);
        var equipmentAssignments = await LoadEquipmentAssignmentsAsync(conn, ct);
        var negotiations = await LoadNegotiationsAsync(conn, ct);
        var auditRecords = await LoadAuditRecordsAsync(conn, ct);
        var appliedActions = await LoadAppliedActionsAsync(conn, ct);
        var categories = await LoadServiceCategoriesAsync(conn, ct);

        return new StudioAnalyticsSnapshot
        {
            Today = phtToday,
            Bookings = bookings,
            Timesheets = timesheets,
            Employees = employees,
            RegisteredSpecializations = specs,
            Leaves = leaves,
            Equipment = equipment,
            MaintenanceSchedules = maintenance,
            EquipmentAssignments = equipmentAssignments,
            Negotiations = negotiations,
            AuditRecords = auditRecords,
            AppliedActions = appliedActions,
            AvailableServiceCategories = categories
        };
    }

    private static async Task<Dictionary<string, List<AnalyticsLineItemRecord>>> LoadEventLineItemsAsync(
        SqlConnection conn,
        CancellationToken ct)
    {
        var map = new Dictionary<string, List<AnalyticsLineItemRecord>>(StringComparer.OrdinalIgnoreCase);

        const string sql = @"
            IF OBJECT_ID(N'dbo.EventLineItem', N'U') IS NOT NULL
            BEGIN
                SELECT
                    eli.EventAppointmentID,
                    COALESCE(eli.ServicesID, eli.ComboPackageID, eli.AdditionalServicesID, eli.EventLineItemID) AS ItemId,
                    COALESCE(s.ServiceName, cp.ComboPackageName, ads.AdditionalServiceName, N'Studio Package') AS ItemName,
                    CASE
                        WHEN eli.ComboPackageID IS NOT NULL THEN N'Combo Package'
                        WHEN eli.AdditionalServicesID IS NOT NULL THEN N'Add-on'
                        ELSE N'Service'
                    END AS ItemKind,
                    COALESCE(scCat.CategoryName, N'Uncategorized') AS CategoryName,
                    ISNULL(eli.Quantity, 1) AS Quantity
                FROM dbo.EventLineItem eli
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
                    ORDER BY sc.ServiceCategoryName
                ) scCat;
            END;";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var evtId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            if (string.IsNullOrWhiteSpace(evtId)) continue;

            if (!map.TryGetValue(evtId, out var list))
            {
                list = [];
                map[evtId] = list;
            }

            list.Add(new AnalyticsLineItemRecord(
                ItemId: reader.IsDBNull(1) ? "ITEM" : reader.GetString(1),
                Name: reader.IsDBNull(2) ? "Studio Package" : reader.GetString(2),
                Kind: reader.IsDBNull(3) ? "Service" : reader.GetString(3),
                CategoryName: reader.IsDBNull(4) ? "Uncategorized" : reader.GetString(4),
                Quantity: reader.IsDBNull(5) ? 1 : reader.GetInt32(5)));
        }

        return map;
    }

    private static async Task<List<AnalyticsBookingRecord>> LoadBookingsAsync(
        SqlConnection conn,
        Dictionary<string, List<AnalyticsLineItemRecord>> lineItemsByEvent,
        CancellationToken ct)
    {
        var results = new List<AnalyticsBookingRecord>();

        const string eventSql = @"
            IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
            BEGIN
                SELECT
                    ea.EventAppointmentID,
                    ea.EventName,
                    COALESCE(LTRIM(RTRIM(CONCAT(c.CustomerFirstName, ' ', c.CustomerLastName))), ea.CustomerID) AS CustomerName,
                    ea.EventDate,
                    ea.EventTime,
                    ISNULL(ea.DurationMinutes, 60) AS DurationMinutes,
                    ISNULL(ea.EventStatus, 'Pending') AS EventStatus,
                    ISNULL(ea.DownpaymentStatus, 'Pending') AS DownpaymentStatus,
                    ea.LockExpiresAt,
                    ea.AssignedEmployeeName,
                    ea.CreatedAt
                FROM dbo.EventAppointment ea
                LEFT JOIN dbo.Customer c ON c.CustomerID = ea.CustomerID;
            END;";

        await using (var cmd = new SqlCommand(eventSql, conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetString(0);
                var rawStatus = reader.IsDBNull(6) ? "Pending" : reader.GetString(6);
                var eventTime = reader.IsDBNull(4)
                    ? TimeOnly.MinValue
                    : TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(4));

                lineItemsByEvent.TryGetValue(id, out var items);

                results.Add(new AnalyticsBookingRecord(
                    BookingId: id,
                    Source: "EventAppointment",
                    Title: reader.IsDBNull(1) ? "Photoshoot Event" : reader.GetString(1),
                    CustomerLabel: reader.IsDBNull(2) ? "Client" : reader.GetString(2),
                    EventDate: DateOnly.FromDateTime(reader.GetDateTime(3)),
                    EventTime: eventTime,
                    DurationMinutes: reader.IsDBNull(5) ? 60 : reader.GetInt32(5),
                    RawStatus: rawStatus,
                    NormalizedStatus: StudioAnalyticsCalculator.NormalizeStatus(rawStatus),
                    DownpaymentStatus: reader.IsDBNull(7) ? "Pending" : reader.GetString(7),
                    LockExpiresAtUtc: reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                    AssignedEmployeeName: reader.IsDBNull(9) ? null : reader.GetString(9),
                    CreatedAtUtc: reader.IsDBNull(10) ? DateTime.UtcNow : reader.GetDateTime(10),
                    LineItems: items ?? []));
            }
        }

        // Also include Book Schedule wizard appointments (dbo.Appointment) if present and distinct
        var apptLineItems = new Dictionary<int, List<AnalyticsLineItemRecord>>();
        const string apptServicesSql = @"
            IF OBJECT_ID(N'dbo.AppointmentService', N'U') IS NOT NULL
            BEGIN
                SELECT aps.AppointmentID, aps.ServicesID, aps.ServiceName
                FROM dbo.AppointmentService aps;
            END;";

        await using (var cmd = new SqlCommand(apptServicesSql, conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                int apptId = reader.GetInt32(0);
                if (!apptLineItems.TryGetValue(apptId, out var list))
                {
                    list = [];
                    apptLineItems[apptId] = list;
                }
                list.Add(new AnalyticsLineItemRecord(
                    ItemId: reader.IsDBNull(1) ? "SVC" : reader.GetString(1),
                    Name: reader.IsDBNull(2) ? "Consultation Service" : reader.GetString(2),
                    Kind: "Consultation",
                    CategoryName: "Consultation",
                    Quantity: 1));
            }
        }

        const string apptSql = @"
            IF OBJECT_ID(N'dbo.Appointment', N'U') IS NOT NULL
            BEGIN
                SELECT
                    a.AppointmentID,
                    a.CustomerName,
                    a.SlotDate,
                    a.SlotStart,
                    DATEDIFF(MINUTE, a.SlotStart, a.SlotEnd) AS DurationMinutes,
                    a.Status,
                    a.Platform,
                    a.CreatedAt
                FROM dbo.Appointment a;
            END;";

        await using (var cmd = new SqlCommand(apptSql, conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                int apptId = reader.GetInt32(0);
                string rawStatus = reader.IsDBNull(5) ? "Confirmed" : reader.GetString(5);
                string platform = reader.IsDBNull(6) ? "Online" : reader.GetString(6);
                apptLineItems.TryGetValue(apptId, out var items);

                results.Add(new AnalyticsBookingRecord(
                    BookingId: $"APT-{apptId:0000}",
                    Source: "ScheduleAppointment",
                    Title: $"Studio Consultation ({platform})",
                    CustomerLabel: reader.IsDBNull(1) ? "Client" : reader.GetString(1),
                    EventDate: DateOnly.FromDateTime(reader.GetDateTime(2)),
                    EventTime: reader.IsDBNull(3) ? TimeOnly.MinValue : TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(3)),
                    DurationMinutes: reader.IsDBNull(4) ? 60 : Math.Max(30, reader.GetInt32(4)),
                    RawStatus: rawStatus,
                    NormalizedStatus: StudioAnalyticsCalculator.NormalizeStatus(rawStatus),
                    DownpaymentStatus: "Consultation",
                    LockExpiresAtUtc: null,
                    AssignedEmployeeName: null,
                    CreatedAtUtc: reader.IsDBNull(7) ? DateTime.UtcNow : reader.GetDateTime(7),
                    LineItems: items ?? [new AnalyticsLineItemRecord("CONSULT", "Book Schedule Consultation", "Consultation", "Consultation", 1)]));
            }
        }

        return results;
    }

    private static async Task<List<AnalyticsTimesheetRecord>> LoadTimesheetsAsync(SqlConnection conn, CancellationToken ct)
    {
        var list = new List<AnalyticsTimesheetRecord>();
        const string sql = @"
            IF OBJECT_ID(N'dbo.EmployeeTimesheets', N'U') IS NOT NULL
            BEGIN
                SELECT
                    TimesheetId,
                    EmployeeStudioId,
                    EmployeeName,
                    WorkDate,
                    CASE
                        WHEN ISNULL(TotalHours, 0) > 0 THEN TotalHours
                        WHEN ClockOut IS NOT NULL THEN CAST(DATEDIFF(MINUTE, ClockIn, ClockOut) AS float) / 60.0
                        ELSE 0.0
                    END AS HoursLogged,
                    ISNULL(Status, 'Open') AS Status,
                    ISNULL(IsDayOffWork, 0) AS IsDayOffWork
                FROM dbo.EmployeeTimesheets;
            END;";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new AnalyticsTimesheetRecord(
                TimesheetId: reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                EmployeeStudioId: reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                EmployeeName: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                WorkDate: DateOnly.FromDateTime(reader.GetDateTime(3)),
                Hours: reader.IsDBNull(4) ? 0.0 : Convert.ToDouble(reader.GetValue(4)),
                Status: reader.IsDBNull(5) ? "Open" : reader.GetString(5),
                IsDayOffWork: !reader.IsDBNull(6) && reader.GetBoolean(6)));
        }
        return list;
    }

    private static async Task<(List<AnalyticsEmployeeRecord> Employees, List<string> Specs)> LoadEmployeesAndSpecializationsAsync(
        SqlConnection conn,
        CancellationToken ct)
    {
        var empJointSpecs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var registeredSpecs = new List<string>();

        const string specSql = @"
            IF OBJECT_ID(N'dbo.EmployeeSpecialization', N'U') IS NOT NULL
            BEGIN
                SELECT COALESCE(NULLIF(SpecializationName, ''), Specialization) AS SpecName
                FROM dbo.EmployeeSpecialization
                WHERE COALESCE(NULLIF(SpecializationName, ''), Specialization) IS NOT NULL;
            END;
            IF OBJECT_ID(N'dbo.EmployeeJointSpecialization', N'U') IS NOT NULL
               AND OBJECT_ID(N'dbo.EmployeeSpecialization', N'U') IS NOT NULL
            BEGIN
                SELECT ejs.EmployeeID, COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization) AS SpecName
                FROM dbo.EmployeeJointSpecialization ejs
                INNER JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID;
            END;";

        await using (var cmd = new SqlCommand(specSql, conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                if (!reader.IsDBNull(0))
                    registeredSpecs.Add(reader.GetString(0));
            }

            if (await reader.NextResultAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;
                    var empId = reader.GetString(0);
                    var spec = reader.GetString(1);
                    if (!empJointSpecs.TryGetValue(empId, out var specs))
                    {
                        specs = [];
                        empJointSpecs[empId] = specs;
                    }
                    specs.Add(spec);
                }
            }
        }

        var employees = new List<AnalyticsEmployeeRecord>();
        const string empSql = @"
            IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
            BEGIN
                SELECT
                    EmployeeID,
                    LTRIM(RTRIM(CONCAT(ISNULL(EmployeeFirstName, ''), ' ', ISNULL(EmployeeLastName, '')))) AS FullName,
                    COALESCE(NULLIF(EmployeeStatus, ''), Status, 'Active') AS EmpStatus,
                    ISNULL(DaysOff, 'Sunday') AS DaysOff,
                    ISNULL(Specializations, '[]') AS SpecializationsJson
                FROM dbo.Employees;
            END;";

        await using (var cmd = new SqlCommand(empSql, conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var empId = reader.GetString(0);
                var name = reader.IsDBNull(1) || string.IsNullOrWhiteSpace(reader.GetString(1)) ? empId : reader.GetString(1);
                var status = reader.IsDBNull(2) ? "Active" : reader.GetString(2);
                var daysOff = reader.IsDBNull(3) ? "Sunday" : reader.GetString(3);
                var specJson = reader.IsDBNull(4) ? "[]" : reader.GetString(4);

                var specs = ParseSpecializations(specJson);
                if (empJointSpecs.TryGetValue(empId, out var joint))
                {
                    specs = specs.Concat(joint).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }

                employees.Add(new AnalyticsEmployeeRecord(empId, name, status, daysOff, specs));
            }
        }

        return (employees, registeredSpecs.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList());
    }

    private static List<string> ParseSpecializations(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "[]") return [];
        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(raw);
            return parsed?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? [];
        }
        catch
        {
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
    }

    private static async Task<List<AnalyticsLeaveRecord>> LoadLeavesAsync(SqlConnection conn, CancellationToken ct)
    {
        var list = new List<AnalyticsLeaveRecord>();
        const string sql = @"
            IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NOT NULL
            BEGIN
                SELECT LeaveId, EmployeeStudioId, EmployeeName, LeaveType, StartDate, EndDate, Status, Reason
                FROM dbo.EmployeeLeaves;
            END;";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new AnalyticsLeaveRecord(
                LeaveId: reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                EmployeeStudioId: reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                EmployeeName: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                LeaveType: reader.IsDBNull(3) ? "Leave" : reader.GetString(3),
                StartDate: DateOnly.FromDateTime(reader.GetDateTime(4)),
                EndDate: DateOnly.FromDateTime(reader.GetDateTime(5)),
                Status: reader.IsDBNull(6) ? "Pending" : reader.GetString(6),
                Reason: reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return list;
    }

    private static async Task<List<AnalyticsEquipmentRecord>> LoadEquipmentAsync(SqlConnection conn, CancellationToken ct)
    {
        var list = new List<AnalyticsEquipmentRecord>();
        const string sql = @"
            IF OBJECT_ID(N'dbo.Equipment', N'U') IS NOT NULL
            BEGIN
                SELECT
                    e.EquipmentID,
                    e.EquipmentName,
                    ISNULL(et.EquipmentTypeName, e.EquipmentTypeID) AS TypeName,
                    ISNULL(e.EquipmentStatus, 'Available') AS EquipmentStatus
                FROM dbo.Equipment e
                LEFT JOIN dbo.EquipmentType et ON et.EquipmentTypeID = e.EquipmentTypeID;
            END;";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new AnalyticsEquipmentRecord(
                EquipmentId: reader.GetString(0),
                EquipmentName: reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                EquipmentTypeName: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                EquipmentStatus: reader.IsDBNull(3) ? "Available" : reader.GetString(3)));
        }
        return list;
    }

    private static async Task<List<AnalyticsMaintenanceRecord>> LoadMaintenanceAsync(SqlConnection conn, CancellationToken ct)
    {
        var list = new List<AnalyticsMaintenanceRecord>();
        const string sql = @"
            IF OBJECT_ID(N'dbo.MaintenanceSchedule', N'U') IS NOT NULL
            BEGIN
                SELECT
                    ms.MaintenanceScheduleID,
                    ms.EquipmentID,
                    ISNULL(e.EquipmentName, ms.EquipmentID) AS EquipmentName,
                    ISNULL(ms.ServiceType, 'Repair') AS ServiceType,
                    ISNULL(ms.Status, 'In Maintenance') AS Status,
                    ms.SubStatus,
                    ms.IssueDetails,
                    ms.ScheduledDate,
                    ms.UpdatedAt,
                    ms.ResolvedAt
                FROM dbo.MaintenanceSchedule ms
                LEFT JOIN dbo.Equipment e ON e.EquipmentID = ms.EquipmentID;
            END;";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new AnalyticsMaintenanceRecord(
                MaintenanceScheduleId: reader.GetInt32(0),
                EquipmentId: reader.GetString(1),
                EquipmentName: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                ServiceType: reader.IsDBNull(3) ? "Repair" : reader.GetString(3),
                Status: reader.IsDBNull(4) ? "In Maintenance" : reader.GetString(4),
                SubStatus: reader.IsDBNull(5) ? null : reader.GetString(5),
                IssueDetails: reader.IsDBNull(6) ? null : reader.GetString(6),
                ScheduledDateUtc: reader.GetDateTime(7),
                UpdatedAtUtc: reader.GetDateTime(8),
                ResolvedAtUtc: reader.IsDBNull(9) ? null : reader.GetDateTime(9)));
        }
        return list;
    }

    private static async Task<List<AnalyticsEquipmentAssignmentRecord>> LoadEquipmentAssignmentsAsync(SqlConnection conn, CancellationToken ct)
    {
        var list = new List<AnalyticsEquipmentAssignmentRecord>();
        const string sql = @"
            IF OBJECT_ID(N'dbo.EventEquipment', N'U') IS NOT NULL
               AND OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
            BEGIN
                SELECT
                    ee.EventAppointmentID,
                    ee.EquipmentID,
                    ISNULL(eq.EquipmentName, ee.EquipmentID) AS EquipmentName,
                    ea.EventDate,
                    ISNULL(ea.EventStatus, 'Pending') AS EventStatus,
                    ISNULL(ee.IsAdditionalRequest, 0) AS IsAdditionalRequest
                FROM dbo.EventEquipment ee
                INNER JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = ee.EventAppointmentID
                LEFT JOIN dbo.Equipment eq ON eq.EquipmentID = ee.EquipmentID;
            END;";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new AnalyticsEquipmentAssignmentRecord(
                EventAppointmentId: reader.GetString(0),
                EquipmentId: reader.GetString(1),
                EquipmentName: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                EventDate: DateOnly.FromDateTime(reader.GetDateTime(3)),
                EventStatus: reader.IsDBNull(4) ? "Pending" : reader.GetString(4),
                IsAdditionalRequest: !reader.IsDBNull(5) && reader.GetBoolean(5)));
        }
        return list;
    }

    private static async Task<List<AnalyticsNegotiationRecord>> LoadNegotiationsAsync(SqlConnection conn, CancellationToken ct)
    {
        var list = new List<AnalyticsNegotiationRecord>();
        const string sql = @"
            IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
            BEGIN
                SELECT
                    NegotiationID,
                    EventAppointmentID,
                    CustomerID,
                    AssignedEmployeeName,
                    ConsultationDate,
                    ISNULL(NegotiationStatus, 'Pending_Call') AS NegotiationStatus,
                    InquiryNotes,
                    CreatedAt
                FROM dbo.BookingNegotiation;
            END;";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new AnalyticsNegotiationRecord(
                NegotiationId: reader.GetString(0),
                EventAppointmentId: reader.IsDBNull(1) ? null : reader.GetString(1),
                CustomerId: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                AssignedEmployeeName: reader.IsDBNull(3) ? null : reader.GetString(3),
                ConsultationDate: reader.IsDBNull(4) ? null : DateOnly.FromDateTime(reader.GetDateTime(4)),
                NegotiationStatus: reader.IsDBNull(5) ? "Pending_Call" : reader.GetString(5),
                InquiryNotes: reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAtUtc: reader.IsDBNull(7) ? DateTime.UtcNow : reader.GetDateTime(7)));
        }
        return list;
    }

    private static async Task<List<AnalyticsAuditRecord>> LoadAuditRecordsAsync(SqlConnection conn, CancellationToken ct)
    {
        var list = new List<AnalyticsAuditRecord>();
        const string sql = @"
            IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL
            BEGIN
                SELECT TOP 250
                    COALESCE(NULLIF(ResourceId, ''), CAST(Id AS nvarchar(30))) AS RefId,
                    Action,
                    Resource,
                    ISNULL(Details, '') AS Details,
                    Timestamp
                FROM dbo.AuditLogs
                ORDER BY Timestamp DESC;
            END;
            IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
            BEGIN
                SELECT TOP 250
                    COALESCE(NULLIF(EventAppointmentID, ''), NULLIF(NegotiationID, ''), NULLIF(EntityID, ''), EntityName) AS RefId,
                    ActionType,
                    EntityName,
                    ISNULL(ActionDetails, '') AS Details,
                    LoggedAt
                FROM dbo.HistoryLog
                ORDER BY LoggedAt DESC;
            END;";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new AnalyticsAuditRecord(
                Source: "AuditLog",
                ReferenceId: reader.IsDBNull(0) ? "AUD" : reader.GetString(0),
                Action: reader.IsDBNull(1) ? "Event" : reader.GetString(1),
                Resource: reader.IsDBNull(2) ? "System" : reader.GetString(2),
                Details: reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                TimestampUtc: reader.GetDateTime(4)));
        }

        if (await reader.NextResultAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                list.Add(new AnalyticsAuditRecord(
                    Source: "HistoryLog",
                    ReferenceId: reader.IsDBNull(0) ? "HIST" : reader.GetString(0),
                    Action: reader.IsDBNull(1) ? "UPDATE" : reader.GetString(1),
                    Resource: reader.IsDBNull(2) ? "Entity" : reader.GetString(2),
                    Details: reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    TimestampUtc: reader.GetDateTime(4)));
            }
        }

        return list;
    }

    private static async Task<List<AnalyticsAppliedActionRecord>> LoadAppliedActionsAsync(SqlConnection conn, CancellationToken ct)
    {
        var list = new List<AnalyticsAppliedActionRecord>();
        const string sql = @"
            IF OBJECT_ID(N'dbo.StudioAnalyticsActions', N'U') IS NOT NULL
            BEGIN
                SELECT TOP 50
                    Id, ActionKey, Title, Category, EffortTag, EstimatedImpact,
                    TriggerSignal, RelatedReferences, Notes, AppliedByUserId, AppliedByName,
                    AppliedAtUtc, Status
                FROM dbo.StudioAnalyticsActions
                ORDER BY AppliedAtUtc DESC;
            END;";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new AnalyticsAppliedActionRecord(
                Id: reader.GetInt32(0),
                ActionKey: reader.GetString(1),
                Title: reader.GetString(2),
                Category: reader.GetString(3),
                EffortTag: reader.GetString(4),
                EstimatedImpact: reader.GetString(5),
                TriggerSignal: reader.IsDBNull(6) ? null : reader.GetString(6),
                RelatedReferences: reader.IsDBNull(7) ? null : reader.GetString(7),
                Notes: reader.IsDBNull(8) ? null : reader.GetString(8),
                AppliedByUserId: reader.GetString(9),
                AppliedByName: reader.GetString(10),
                AppliedAtUtc: reader.GetDateTime(11),
                Status: reader.GetString(12)));
        }
        return list;
    }

    private static async Task<List<string>> LoadServiceCategoriesAsync(SqlConnection conn, CancellationToken ct)
    {
        var list = new List<string>();
        const string sql = @"
            IF OBJECT_ID(N'dbo.ServiceCategory', N'U') IS NOT NULL
            BEGIN
                SELECT ServiceCategoryName FROM dbo.ServiceCategory ORDER BY ServiceCategoryName;
            END;";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!reader.IsDBNull(0))
                list.Add(reader.GetString(0));
        }
        return list;
    }

    private static DateTime GetPhtNow()
    {
        try
        {
            var pht = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, pht);
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                var pht = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, pht);
            }
            catch
            {
                return DateTime.UtcNow.AddHours(8);
            }
        }
    }
}
