using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Scheduling;

namespace PhotoAevumStudio.Infrastructure.Scheduling;

/// <summary>
/// Double-booking protection, in layers:
///  1. SERIALIZABLE transaction + UPDLOCK/HOLDLOCK range lock on the slot, so concurrent
///     requests for the same slot queue up and the loser sees the winner's row.
///  2. Filtered unique index UX_Appointment_Slot as the last line of defence; a violation
///     is caught and turned into <see cref="SlotUnavailableException"/> after rollback.
///  3. BookingOverview uses a rowversion (optimistic concurrency) with a small retry loop.
/// Transient faults (incl. deadlock victims, error 1205) are retried by the EF execution strategy.
/// </summary>
public sealed class ScheduleBookingService(
    SchedulingDbContext db,
    IMeetingLinkService meetingLinks,
    IBookingNotificationService notifications,
    IOptions<SchedulingOptions> options,
    TimeProvider clock,
    ILogger<ScheduleBookingService> logger) : IScheduleBookingService
{
    private const int MaxServices = 20;
    private const int MaxLocationLength = 300;
    private const int MaxOverviewAttempts = 3;

    private readonly SchedulingOptions _options = options.Value;
    private readonly TimeZoneInfo _studioZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId);

    public async Task<IReadOnlyList<ScheduleServiceDto>> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var list = new List<ScheduleServiceDto>();
            var conn = db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(cancellationToken);

            try
            {
                await using var schemaCmd = conn.CreateCommand();
                schemaCmd.CommandText = @"
                    IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Services', N'RateType') IS NULL
                        ALTER TABLE dbo.Services ADD RateType nvarchar(50) NOT NULL CONSTRAINT DF_Services_RateType DEFAULT 'Per Hour';
                    IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Services', N'IsArchived') IS NULL
                        ALTER TABLE dbo.Services ADD IsArchived bit NOT NULL CONSTRAINT DF_Services_IsArchived DEFAULT 0;
                    IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComboPackage', N'RateType') IS NULL
                        ALTER TABLE dbo.ComboPackage ADD RateType nvarchar(50) NOT NULL CONSTRAINT DF_ComboPackage_RateType DEFAULT 'Per Hour';
                    IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComboPackage', N'IsArchived') IS NULL
                        ALTER TABLE dbo.ComboPackage ADD IsArchived bit NOT NULL CONSTRAINT DF_ComboPackage_IsArchived DEFAULT 0;
                    IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AdditionalServices', N'RateType') IS NULL
                        ALTER TABLE dbo.AdditionalServices ADD RateType nvarchar(50) NOT NULL CONSTRAINT DF_AdditionalServices_RateType DEFAULT 'Per Hour';
                    IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AdditionalServices', N'IsArchived') IS NULL
                        ALTER TABLE dbo.AdditionalServices ADD IsArchived bit NOT NULL CONSTRAINT DF_AdditionalServices_IsArchived DEFAULT 0;";
                await schemaCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch { }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT ServicesID AS Id, ServiceName AS Name, ISNULL(ServicesDescription, '') AS [Desc], CAST(ServicePrice AS decimal(18,2)) AS Price, ISNULL(RateType, 'Per Hour') AS RateType
                FROM dbo.Services
                WHERE ISNULL(IsArchived, 0) = 0
                UNION ALL
                SELECT ComboPackageID AS Id, ComboPackageName AS Name, ISNULL(ComboPackageDescription, '') AS [Desc], CAST(ComboPackagePrice AS decimal(18,2)) AS Price, ISNULL(RateType, 'Per Hour') AS RateType
                FROM dbo.ComboPackage
                WHERE ISNULL(IsArchived, 0) = 0
                UNION ALL
                SELECT AdditionalServicesID AS Id, AdditionalServiceName AS Name, ISNULL(AdditionalServicesDescription, '') AS [Desc], CAST(ServicePrice AS decimal(18,2)) AS Price, ISNULL(RateType, 'Per Hour') AS RateType
                FROM dbo.AdditionalServices
                WHERE ISNULL(IsArchived, 0) = 0
                ORDER BY Name ASC;
            ";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0);
                var name = reader.GetString(1);
                var desc = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var price = reader.IsDBNull(3) ? 0m : Convert.ToDecimal(reader.GetValue(3));
                var rateType = reader.FieldCount > 4 && !reader.IsDBNull(4) ? reader.GetString(4) : "Per Session";
                list.Add(new ScheduleServiceDto(id, name, desc, price, rateType));
            }

            return list;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to query unified service catalog via ADO.NET; falling back to CatalogServices.");
            return await db.CatalogServices.AsNoTracking()
                .OrderBy(s => s.ServiceName)
                .Select(s => new ScheduleServiceDto(s.ServicesId, s.ServiceName, s.ServicesDescription, s.ServicePrice))
                .ToListAsync(cancellationToken);
        }
    }

    public async Task<MonthAvailabilityDto> GetMonthAvailabilityAsync(int year, int month, string? clerkId = null, CancellationToken cancellationToken = default)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            throw new ScheduleValidationException("Invalid month requested.");

        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var booked = await db.Appointments.AsNoTracking()
            .Where(a => a.SlotDate >= first && a.SlotDate <= last && a.Status != AppointmentStatus.Cancelled)
            .Select(a => new { a.SlotDate, a.SlotStart })
            .ToListAsync(cancellationToken);
        var taken = booked.Select(b => (b.SlotDate, b.SlotStart)).ToHashSet();

        var activeClerks = await GetActiveClerksDataAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(clerkId) && !clerkId.Equals("any", StringComparison.OrdinalIgnoreCase) && !clerkId.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            activeClerks = activeClerks.Where(c => string.Equals(c.EmployeeId, clerkId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var leaves = await GetApprovedLeavesRangeAsync(first, last, cancellationToken);
        var assignedEvents = await GetAssignedEventsRangeAsync(first, last, cancellationToken);
        var bookedConsultations = await GetBookedConsultationsRangeAsync(first, last, cancellationToken);

        var now = StudioNow();
        var days = Enumerable.Range(0, last.Day)
            .Select(i => first.AddDays(i))
            .Select(d => new DayAvailabilityDto(d, StudioSlots.All
                .Select(s =>
                {
                    bool isSlotTaken = taken.Contains((d, s.Start));
                    bool isSlotTimeBookable = IsBookable(d, s.Start, now);
                    bool hasAvailableClerk = HasAnyAvailableClerk(activeClerks, leaves, assignedEvents, bookedConsultations, d, s.Start);

                    return new SlotAvailabilityDto(s.Start, s.End, isSlotTimeBookable && !isSlotTaken && hasAvailableClerk);
                })
                .ToList()))
            .ToList();

        return new MonthAvailabilityDto(year, month, days);
    }

    public async Task<ShootMonthAvailabilityDto> GetShootMonthAvailabilityAsync(
        int year,
        int month,
        int durationMinutes = 60,
        string? excludeAppointmentId = null,
        CancellationToken cancellationToken = default)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            throw new ScheduleValidationException("Invalid month requested.");

        var normalizedDuration = durationMinutes <= 0
            ? ShootAvailabilityCore.MinimumShootDurationMinutes
            : Math.Max(ShootAvailabilityCore.MinimumShootDurationMinutes, durationMinutes);

        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var normalizedExcludeId = string.IsNullOrWhiteSpace(excludeAppointmentId) ? null : excludeAppointmentId.Trim();

        var crew = await GetDispatchableShootCrewAsync(cancellationToken);
        var leaves = await GetApprovedLeavesRangeAsync(first, last, cancellationToken);
        var shootEvents = await GetShootEventsRangeAsync(first, last, normalizedExcludeId, cancellationToken);

        return EvaluateShootMonthAvailability(year, month, normalizedDuration, crew, leaves, shootEvents);
    }

    public async Task<DayAvailabilityDto> GetDayAvailabilityAsync(DateOnly date, string? clerkId = null, CancellationToken cancellationToken = default)
    {
        var taken = (await db.Appointments.AsNoTracking()
                .Where(a => a.SlotDate == date && a.Status != AppointmentStatus.Cancelled)
                .Select(a => a.SlotStart)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var activeClerks = await GetActiveClerksDataAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(clerkId) && !clerkId.Equals("any", StringComparison.OrdinalIgnoreCase) && !clerkId.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            activeClerks = activeClerks.Where(c => string.Equals(c.EmployeeId, clerkId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var leaves = await GetApprovedLeavesRangeAsync(date, date, cancellationToken);
        var assignedEvents = await GetAssignedEventsRangeAsync(date, date, cancellationToken);
        var bookedConsultations = await GetBookedConsultationsRangeAsync(date, date, cancellationToken);

        var now = StudioNow();
        return new DayAvailabilityDto(date, StudioSlots.All
            .Select(s =>
            {
                bool isSlotTaken = taken.Contains(s.Start);
                bool isSlotTimeBookable = IsBookable(date, s.Start, now);
                bool hasAvailableClerk = HasAnyAvailableClerk(activeClerks, leaves, assignedEvents, bookedConsultations, date, s.Start);

                return new SlotAvailabilityDto(s.Start, s.End, isSlotTimeBookable && !isSlotTaken && hasAvailableClerk);
            })
            .ToList());
    }

    public async Task<AvailabilityCheckResultDto> CheckAvailabilityAsync(DateOnly date, TimeOnly? targetTime = null, CancellationToken cancellationToken = default)
    {
        var taken = (await db.Appointments.AsNoTracking()
                .Where(a => a.SlotDate == date && a.Status != AppointmentStatus.Cancelled)
                .Select(a => a.SlotStart)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var activeClerks = await GetActiveClerksDataAsync(cancellationToken);
        var activeCrew = await GetActiveCrewDataAsync(cancellationToken);
        var leaves = await GetApprovedLeavesRangeAsync(date, date, cancellationToken);
        var assignedEvents = await GetAssignedEventsWithDirectAssignmentRangeAsync(date, date, cancellationToken);
        var bookedConsultations = await GetBookedConsultationsRangeAsync(date, date, cancellationToken);

        return EvaluateAvailability(
            date,
            targetTime,
            StudioNow(),
            _options.BookingWindowDays,
            taken,
            activeClerks,
            activeCrew,
            leaves,
            assignedEvents,
            bookedConsultations);
    }

    public async Task<IReadOnlyList<OfficeClerkDto>> GetOfficeClerksAsync(CancellationToken cancellationToken = default)
    {
        var clerks = await GetActiveClerksDataAsync(cancellationToken);
        return clerks.Select(c => new OfficeClerkDto(c.EmployeeId, c.FullName, c.DaysOff)).ToList();
    }

    public async Task<IReadOnlyList<MyBookingDto>> GetMyBookingsAsync(string customerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerId)) return [];

        var rows = await db.Appointments.AsNoTracking()
            .Where(a => a.CustomerId == customerId)
            .OrderBy(a => a.Status == AppointmentStatus.Cancelled ? 1 : 0)
            .ThenByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.AppointmentId)
            .Select(a => new
            {
                a.AppointmentId,
                a.SlotDate,
                a.SlotStart,
                a.SlotEnd,
                a.Status,
                a.TotalPrice,
                a.Platform,
                a.MeetingLink,
                a.EventLocation,
                a.CreatedAt,
                Services = a.Services.Select(s => s.ServiceName).ToList()
            })
            .ToListAsync(cancellationToken);

        var clerkLookup = new Dictionary<string, (string? ClerkId, string? ClerkName, string? NegotiationId, string? NegotiationStatus, string? LinkedEventAppointmentId)>(StringComparer.OrdinalIgnoreCase);
        if (rows.Count > 0)
        {
            try
            {
                var conn = db.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open) await conn.OpenAsync(cancellationToken);
                var negIds = rows.Select(r => $"NEG-{r.AppointmentId:D4}").ToList();
                var pNames = string.Join(",", negIds.Select((_, i) => $"@n{i}"));

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
                    IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
                    BEGIN
                        SELECT bn.NegotiationID,
                               bn.EmployeeID,
                               COALESCE(
                                   NULLIF(LTRIM(RTRIM(CONCAT(ISNULL(e.EmployeeFirstName, ''), ' ', ISNULL(e.EmployeeLastName, '')))), ''),
                                   NULLIF(bn.AssignedEmployeeName, ''),
                                   bn.EmployeeID
                               ) AS ClerkName,
                               bn.NegotiationStatus,
                               bn.EventAppointmentID
                        FROM dbo.BookingNegotiation bn
                        LEFT JOIN dbo.Employees e ON e.EmployeeID = bn.EmployeeID
                        WHERE bn.NegotiationID IN ({pNames});
                    END";
                for (var i = 0; i < negIds.Count; i++)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = $"@n{i}";
                    p.Value = negIds[i];
                    cmd.Parameters.Add(p);
                }

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var nid = reader.GetString(0);
                    var cid = reader.IsDBNull(1) ? null : reader.GetString(1);
                    var cname = reader.IsDBNull(2) ? null : reader.GetString(2);
                    var nstatus = reader.IsDBNull(3) ? null : reader.GetString(3);
                    var evtId = reader.IsDBNull(4) ? null : reader.GetString(4);
                    clerkLookup[nid] = (cid, cname, nid, nstatus, evtId);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Optional BookingNegotiation clerk enrichment skipped for customer {CustomerId}.", customerId);
            }
        }

        return rows.Select(r =>
        {
            var negKey = $"NEG-{r.AppointmentId:D4}";
            clerkLookup.TryGetValue(negKey, out var info);
            return new MyBookingDto(
                r.AppointmentId,
                string.Join(", ", r.Services),
                r.SlotDate,
                r.SlotStart,
                r.SlotEnd,
                r.Status.ToString(),
                r.TotalPrice,
                r.Platform,
                r.MeetingLink,
                r.EventLocation,
                info.ClerkId,
                info.ClerkName,
                info.NegotiationId ?? negKey,
                r.CreatedAt,
                info.NegotiationStatus,
                info.LinkedEventAppointmentId);
        }).ToList();
    }

    public async Task<CustomerCancelBookingResultDto> CancelBookingAsync(
        int appointmentId,
        string customerId,
        string? reason = null,
        bool isPrivileged = false,
        CancellationToken cancellationToken = default)
    {
        var negId = $"NEG-{appointmentId:D4}";
        var equipmentReleased = false;
        var clerkReleased = false;

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async ct =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var entity = await db.Appointments
                .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId, ct)
                ?? throw new KeyNotFoundException($"Booking #{appointmentId} was not found.");

            if (!isPrivileged && !string.Equals(entity.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("You can only cancel your own appointments.");

            if (string.Equals(entity.Status, AppointmentStatus.Cancelled, StringComparison.OrdinalIgnoreCase))
                throw new ScheduleValidationException($"Booking #{appointmentId} is already cancelled.");

            await EnsureConsultationCanBeModifiedAsync(negId, "cancel", ct);

            var slotDate = entity.SlotDate;
            entity.Status = AppointmentStatus.Cancelled;
            await db.SaveChangesAsync(ct);

            var overview = await db.BookingOverviews
                .FromSql($"SELECT * FROM dbo.BookingOverview WITH (UPDLOCK, HOLDLOCK) WHERE OverviewDate = {slotDate}")
                .SingleOrDefaultAsync(ct);
            if (overview is not null && overview.TotalAppointments > 0)
            {
                overview.TotalAppointments--;
                overview.LastUpdatedAt = clock.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync(ct);
            }

            var conn = db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

            string? linkedEventId = null;
            await using (var findCmd = conn.CreateCommand())
            {
                findCmd.Transaction = tx.GetDbTransaction();
                findCmd.CommandText = @"
                    IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
                    BEGIN
                        SELECT TOP 1 EventAppointmentID
                        FROM dbo.BookingNegotiation
                        WHERE NegotiationID = @NegId;
                    END";
                var pNeg = findCmd.CreateParameter();
                pNeg.ParameterName = "@NegId";
                pNeg.Value = negId;
                findCmd.Parameters.Add(pNeg);
                linkedEventId = await findCmd.ExecuteScalarAsync(ct) as string;
            }

            await using (var relCmd = conn.CreateCommand())
            {
                relCmd.Transaction = tx.GetDbTransaction();
                relCmd.CommandText = @"
                    IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
                    BEGIN
                        UPDATE dbo.BookingNegotiation
                        SET NegotiationStatus = 'Cancelled',
                            EmployeeID = NULL,
                            AssignedEmployeeName = NULL
                        WHERE NegotiationID = @NegId;
                    END;

                    IF @EvtId IS NOT NULL AND OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
                    BEGIN
                        IF OBJECT_ID(N'dbo.EventEmployee', N'U') IS NOT NULL
                            DELETE FROM dbo.EventEmployee WHERE EventAppointmentID = @EvtId;

                        IF OBJECT_ID(N'dbo.EventEquipment', N'U') IS NOT NULL
                            DELETE FROM dbo.EventEquipment WHERE EventAppointmentID = @EvtId;

                        UPDATE dbo.EventAppointment
                        SET EventStatus = 'Cancelled',
                            DownpaymentStatus = CASE WHEN ISNULL(DownpaymentStatus, 'Pending') = 'Paid' THEN DownpaymentStatus ELSE 'Cancelled' END,
                            EmployeeID = NULL,
                            AssignedEmployeeName = NULL,
                            LockExpiresAt = NULL,
                            HoldExpiresAt = NULL,
                            QrString = NULL
                        WHERE EventAppointmentID = @EvtId;
                    END;

                    IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                    BEGIN
                        INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ActionDetails, NegotiationID, EventAppointmentID, CustomerID, ChangedByType, ChangedByCustomerID, LoggedAt)
                        VALUES ('STATUS_CHANGE', 'BookingNegotiation', @NegId, @LogMsg, @NegId, @EvtId, @CustId, @ChangedByType, @ChangedCustId, SYSUTCDATETIME());
                    END";

                void AddParam(System.Data.Common.DbCommand cmd, string name, object? val)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = name;
                    p.Value = val ?? DBNull.Value;
                    cmd.Parameters.Add(p);
                }

                var msg = string.IsNullOrWhiteSpace(reason)
                    ? $"Customer cancelled booking #{appointmentId} ({negId}). Locked slot, Office Clerk, and any linked equipment released."
                    : $"Customer cancelled booking #{appointmentId} ({negId}). Reason: {reason.Trim()}. Locked slot, Office Clerk, and any linked equipment released.";

                AddParam(relCmd, "@NegId", negId);
                AddParam(relCmd, "@EvtId", linkedEventId);
                AddParam(relCmd, "@LogMsg", msg);
                AddParam(relCmd, "@CustId", entity.CustomerId);
                AddParam(relCmd, "@ChangedByType", isPrivileged ? "Admin" : "Customer");
                AddParam(relCmd, "@ChangedCustId", isPrivileged ? null : entity.CustomerId);
                await relCmd.ExecuteNonQueryAsync(ct);

                clerkReleased = true;
                equipmentReleased = !string.IsNullOrWhiteSpace(linkedEventId);
            }

            await tx.CommitAsync(ct);
        }, cancellationToken);

        return new CustomerCancelBookingResultDto(
            Success: true,
            AppointmentId: appointmentId,
            NegotiationId: negId,
            SlotReleased: true,
            ClerkReleased: clerkReleased,
            EquipmentReleased: equipmentReleased,
            Message: $"Appointment #{appointmentId} cancelled. Your locked time slot, assigned Office Clerk, and any reserved equipment have been released.");
    }

    public async Task<CustomerRescheduleBookingResultDto> RescheduleBookingAsync(
        int appointmentId,
        CustomerRescheduleBookingRequest request,
        string customerId,
        bool isPrivileged = false,
        CancellationToken cancellationToken = default)
    {
        var slot = StudioSlots.Find(request.NewSlotStart)
            ?? throw new ScheduleValidationException("Invalid time slot.");
        if (!IsBookable(request.NewDate, slot.Start, StudioNow()))
            throw new ScheduleValidationException("Selected date and time slot must be in the future within the booking window.");

        var entityCheck = await db.Appointments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Booking #{appointmentId} was not found.");

        if (!isPrivileged && !string.Equals(entityCheck.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("You can only reschedule your own appointments.");

        if (string.Equals(entityCheck.Status, AppointmentStatus.Cancelled, StringComparison.OrdinalIgnoreCase))
            throw new ScheduleValidationException($"Cancelled booking #{appointmentId} cannot be rescheduled.");

        var negId = $"NEG-{appointmentId:D4}";
        await EnsureConsultationCanBeModifiedAsync(negId, "reschedule", cancellationToken);
        var originalClerkId = await GetAssignedClerkIdForNegotiationAsync(negId, cancellationToken);
        var clerkCandidates = await GetActiveClerkCandidatesWithRecencyAsync(cancellationToken);
        var leaves = (await GetApprovedLeavesRangeAsync(request.NewDate, request.NewDate, cancellationToken))
            .Select(l => new ClerkLeaveWindow(l.EmployeeStudioId, l.StartDate, l.EndDate))
            .ToList();
        var busyWindows = await GetClerkBusyWindowsForDateAsync(request.NewDate, cancellationToken);

        var selection = OfficeClerkAssignmentResolver.ResolveClerkForReschedule(
            originalClerkId,
            clerkCandidates,
            leaves,
            busyWindows,
            request.NewDate,
            slot.Start,
            slot.End,
            excludeSourceId: negId);

        if (!selection.IsAvailable || string.IsNullOrWhiteSpace(selection.ClerkId))
        {
            throw new SlotUnavailableException(
                request.NewDate,
                slot.Start,
                selection.FailureReason ?? $"No Office Clerk is available for {request.NewDate:MMM d, yyyy} at {slot.Start:h:mm tt}.");
        }

        var assignedClerkId = selection.ClerkId!;
        var assignedClerkName = selection.ClerkName ?? assignedClerkId;

        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async ct =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

                var slotTaken = await db.Appointments
                    .FromSql($"""
                        SELECT * FROM dbo.Appointment WITH (UPDLOCK, HOLDLOCK)
                        WHERE SlotDate = {request.NewDate} AND SlotStart = {slot.Start} AND Status <> 'Cancelled' AND AppointmentID <> {appointmentId}
                        """)
                    .AnyAsync(ct);
                if (slotTaken)
                    throw new SlotUnavailableException(request.NewDate, slot.Start);

                var entity = await db.Appointments
                    .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId, ct)
                    ?? throw new KeyNotFoundException($"Booking #{appointmentId} was not found.");

                var oldDate = entity.SlotDate;
                entity.SlotDate = request.NewDate;
                entity.SlotStart = slot.Start;
                entity.SlotEnd = slot.End;
                await db.SaveChangesAsync(ct);

                if (oldDate != request.NewDate)
                {
                    var oldOverview = await db.BookingOverviews
                        .FromSql($"SELECT * FROM dbo.BookingOverview WITH (UPDLOCK, HOLDLOCK) WHERE OverviewDate = {oldDate}")
                        .SingleOrDefaultAsync(ct);
                    if (oldOverview is not null && oldOverview.TotalAppointments > 0)
                    {
                        oldOverview.TotalAppointments--;
                        oldOverview.LastUpdatedAt = clock.GetUtcNow().UtcDateTime;
                        await db.SaveChangesAsync(ct);
                    }
                    await UpsertOverviewAsync(request.NewDate, 0m, ct);
                }

                var conn = db.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

                await using (var updCmd = conn.CreateCommand())
                {
                    updCmd.Transaction = tx.GetDbTransaction();
                    updCmd.CommandText = @"
                        IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
                        BEGIN
                            UPDATE dbo.BookingNegotiation
                            SET ConsultationDate = @NewDate,
                                ConsultationTime = @NewTime,
                                EmployeeID = @ClerkId,
                                AssignedEmployeeName = @ClerkName,
                                NegotiationStatus = 'Rescheduled',
                                InquiryNotes = CASE
                                    WHEN @Reason IS NOT NULL AND LEN(@Reason) > 0
                                    THEN ISNULL(InquiryNotes, '') + CHAR(13) + CHAR(10) + '[Customer Reschedule]: ' + @Reason
                                    ELSE InquiryNotes
                                END
                            WHERE NegotiationID = @NegId;
                        END;

                        IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                        BEGIN
                            INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ActionDetails, NegotiationID, CustomerID, ChangedByType, ChangedByCustomerID, LoggedAt)
                            VALUES ('UPDATE', 'BookingNegotiation', @NegId, @LogMsg, @NegId, @CustId, @ChangedByType, @ChangedCustId, SYSUTCDATETIME());
                        END";

                    void AddParam(System.Data.Common.DbCommand cmd, string name, object? val)
                    {
                        var p = cmd.CreateParameter();
                        p.ParameterName = name;
                        p.Value = val ?? DBNull.Value;
                        cmd.Parameters.Add(p);
                    }

                    var clerkMsg = selection.RetainedOriginalClerk
                        ? $"Retained original Office Clerk {assignedClerkName} ({assignedClerkId})."
                        : $"Original Office Clerk unavailable; automatically reassigned to {assignedClerkName} ({assignedClerkId}).";
                    var logMsg = $"Customer rescheduled booking #{appointmentId} ({negId}) to {request.NewDate:yyyy-MM-dd} {slot.Start:HH:mm}. {clerkMsg}";

                    AddParam(updCmd, "@NewDate", request.NewDate.ToDateTime(TimeOnly.MinValue));
                    AddParam(updCmd, "@NewTime", slot.Start.ToTimeSpan());
                    AddParam(updCmd, "@ClerkId", assignedClerkId);
                    AddParam(updCmd, "@ClerkName", assignedClerkName);
                    AddParam(updCmd, "@Reason", string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim());
                    AddParam(updCmd, "@NegId", negId);
                    AddParam(updCmd, "@LogMsg", logMsg);
                    AddParam(updCmd, "@CustId", entity.CustomerId);
                    AddParam(updCmd, "@ChangedByType", isPrivileged ? "Admin" : "Customer");
                    AddParam(updCmd, "@ChangedCustId", isPrivileged ? null : entity.CustomerId);
                    await updCmd.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsSlotUniqueViolation(ex))
        {
            throw new SlotUnavailableException(request.NewDate, slot.Start);
        }

        var message = selection.RetainedOriginalClerk
            ? $"Rescheduled to {request.NewDate:MMM d, yyyy} at {slot.Start:h:mm tt}. Your originally assigned Office Clerk ({assignedClerkName}) remains assigned."
            : $"Rescheduled to {request.NewDate:MMM d, yyyy} at {slot.Start:h:mm tt}. Your original clerk was unavailable, so {assignedClerkName} was automatically assigned.";

        return new CustomerRescheduleBookingResultDto(
            Success: true,
            AppointmentId: appointmentId,
            NegotiationId: negId,
            NewDate: request.NewDate,
            NewSlotStart: slot.Start,
            NewSlotEnd: slot.End,
            AssignedClerkId: assignedClerkId,
            AssignedClerkName: assignedClerkName,
            RetainedOriginalClerk: selection.RetainedOriginalClerk,
            Status: "Rescheduled",
            Message: message);
    }

    private static readonly HashSet<string> SucceededNegotiationStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Deal_Won",
        "Appointment Booked",
        "Pending Payment",
        "Pending_Payment",
        "Awaiting_Downpayment",
        "Confirmed",
        "Completed",
        "Complete",
        "Delivered",
        "Shot"
    };

    private static readonly HashSet<string> EndedNegotiationStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Deal_Lost",
        "Cancelled",
        "Expired"
    };

    private async Task EnsureConsultationCanBeModifiedAsync(string negId, string actionLabel, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        string? negStatus = null;
        string? linkedEventId = null;

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText = @"
                IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
                BEGIN
                    SELECT TOP 1 NegotiationStatus, EventAppointmentID
                    FROM dbo.BookingNegotiation
                    WHERE NegotiationID = @NegId;
                END";
            var p = cmd.CreateParameter();
            p.ParameterName = "@NegId";
            p.Value = negId;
            cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                negStatus = reader.IsDBNull(0) ? null : reader.GetString(0);
                linkedEventId = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
        }
        catch (System.Data.Common.DbException ex) when (
            ex.Message.Contains("syntax error", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
            // Bare SQLite unit-test DbContext without BookingNegotiation table
            return;
        }

        if (EndedNegotiationStatuses.Contains(negStatus ?? string.Empty))
        {
            throw new ScheduleValidationException(
                $"Consultation ({negId}) is '{negStatus}' and can no longer be modified.");
        }

        if (!string.IsNullOrWhiteSpace(linkedEventId) ||
            SucceededNegotiationStatuses.Contains(negStatus ?? string.Empty))
        {
            var eventSuffix = string.IsNullOrWhiteSpace(linkedEventId) ? string.Empty : $" (#{linkedEventId})";
            throw new ScheduleValidationException(
                $"Consultation ({negId}) negotiation has succeeded and is linked to a booked event{eventSuffix}. You cannot {actionLabel} or change the Google Meet consultation; you may only modify the connected event if it is not completed.");
        }
    }

    private async Task<string?> GetAssignedClerkIdForNegotiationAsync(string negId, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
                SELECT TOP 1 EmployeeID FROM dbo.BookingNegotiation WHERE NegotiationID = @NegId;";
        var p = cmd.CreateParameter();
        p.ParameterName = "@NegId";
        p.Value = negId;
        cmd.Parameters.Add(p);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    private async Task<List<ClerkCandidate>> GetActiveClerkCandidatesWithRecencyAsync(CancellationToken ct)
    {
        var clerks = await GetActiveClerksDataAsync(ct);
        var recency = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
            BEGIN
                SELECT EmployeeID, MAX(CreatedAt)
                FROM dbo.BookingNegotiation
                WHERE EmployeeID IS NOT NULL
                GROUP BY EmployeeID;
            END";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!reader.IsDBNull(0) && !reader.IsDBNull(1))
                recency[reader.GetString(0)] = reader.GetDateTime(1);
        }

        return clerks.Select(c => new ClerkCandidate(
            c.EmployeeId,
            c.FullName,
            c.DaysOff,
            recency.TryGetValue(c.EmployeeId, out var dt) ? dt : null)).ToList();
    }

    private async Task<List<ClerkBusyWindow>> GetClerkBusyWindowsForDateAsync(DateOnly date, CancellationToken ct)
    {
        var list = new List<ClerkBusyWindow>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
            BEGIN
                SELECT EmployeeID, ConsultationDate, ConsultationTime, NegotiationID
                FROM dbo.BookingNegotiation
                WHERE ConsultationDate = @Date
                  AND EmployeeID IS NOT NULL
                  AND ConsultationTime IS NOT NULL
                  AND NegotiationStatus NOT IN ('Cancelled', 'Deal_Lost');
            END;
            IF OBJECT_ID(N'dbo.EventEmployee', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
            BEGIN
                SELECT ee.EmployeeID, ea.EventDate, ea.EventTime, ea.EventEndTime, ISNULL(ea.DurationMinutes, 60), ea.EventAppointmentID
                FROM dbo.EventEmployee ee
                JOIN dbo.EventAppointment ea ON ee.EventAppointmentID = ea.EventAppointmentID
                WHERE ea.EventDate = @Date
                  AND ee.EmployeeID IS NOT NULL
                  AND ea.EventStatus NOT IN ('Cancelled', 'Expired');
            END;";
        var p = cmd.CreateParameter();
        p.ParameterName = "@Date";
        p.Value = date.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var start = TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(2));
            list.Add(new ClerkBusyWindow(
                reader.GetString(0),
                DateOnly.FromDateTime(reader.GetDateTime(1)),
                start,
                start.AddHours(1),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        if (await reader.NextResultAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var start = TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(2));
                var dur = reader.IsDBNull(4) ? 60 : Math.Max(15, reader.GetInt32(4));
                var end = !reader.IsDBNull(3) ? TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(3)) : start.AddMinutes(dur);
                if (end <= start) end = start.AddMinutes(dur);
                list.Add(new ClerkBusyWindow(
                    reader.GetString(0),
                    DateOnly.FromDateTime(reader.GetDateTime(1)),
                    start,
                    end,
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
        }
        return list;
    }

    public async Task<MeetingLinkDto> PreviewMeetingLinkAsync(MeetingPlatform platform, DateOnly date, TimeOnly slotStart, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(platform)) throw new ScheduleValidationException("Unsupported meeting platform.");
        var slot = StudioSlots.Find(slotStart) ?? throw new ScheduleValidationException("Invalid time slot.");
        var link = await meetingLinks.CreateMeetingLinkAsync(platform, date, slot.Start, slot.End, cancellationToken);
        return new MeetingLinkDto(platform, link);
    }

    public async Task<ScheduleBookingResultDto> BookAsync(ScheduleBookingRequest request, BookingCustomer customer, CancellationToken cancellationToken = default)
    {
        var (serviceIds, slot, location) = Validate(request, customer);

        var availableServices = await GetServicesAsync(cancellationToken);
        var services = availableServices.Where(s => serviceIds.Contains(s.ServicesId)).ToList();
        if (services.Count != serviceIds.Count)
            throw new ScheduleValidationException("One or more selected services are no longer available.");

        var total = services.Sum(s => s.Price);

        // Verify that an active Office Clerk is available for this slot (and not on Day Off)
        var activeClerks = await GetActiveClerksDataAsync(cancellationToken);
        var leaves = await GetApprovedLeavesRangeAsync(request.Date, request.Date, cancellationToken);
        var assignedEvents = await GetAssignedEventsRangeAsync(request.Date, request.Date, cancellationToken);
        var bookedConsultations = await GetBookedConsultationsRangeAsync(request.Date, request.Date, cancellationToken);

        var assignedEmployeeId = GetFirstAvailableClerkId(activeClerks, leaves, assignedEvents, bookedConsultations, request.Date, slot.Start);
        if (assignedEmployeeId == null)
        {
            var dayName = request.Date.DayOfWeek.ToString();
            if (activeClerks.All(c => c.DaysOff.Contains(dayName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ScheduleValidationException($"Cannot schedule consultation: The Office Clerk has a scheduled Day Off on {request.Date:dddd, MMMM dd, yyyy}.");
            }
            throw new SlotUnavailableException(request.Date, slot.Start, "No Office Clerk is available to be assigned for this schedule slot.");
        }

        // External call happens before the transaction so no DB locks are held while waiting on it.
        var meetingLink = meetingLinks.IsIssuedLink(request.Platform, request.MeetingLink)
            ? request.MeetingLink!
            : await meetingLinks.CreateMeetingLinkAsync(request.Platform, request.Date, slot.Start, slot.End, cancellationToken);

        Appointment appointment;
        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            appointment = await strategy.ExecuteAsync(async ct =>
            {
                db.ChangeTracker.Clear(); // clean slate if the strategy retries
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

                var slotTaken = await db.Appointments
                    .FromSql($"""
                        SELECT * FROM dbo.Appointment WITH (UPDLOCK, HOLDLOCK)
                        WHERE SlotDate = {request.Date} AND SlotStart = {slot.Start} AND Status <> 'Cancelled'
                        """)
                    .AnyAsync(ct);
                if (slotTaken)
                    throw new SlotUnavailableException(request.Date, slot.Start); // disposing tx rolls back

                var entity = new Appointment
                {
                    CustomerId = customer.UserId,
                    CustomerEmail = customer.Email,
                    CustomerName = customer.FullName,
                    SlotDate = request.Date,
                    SlotStart = slot.Start,
                    SlotEnd = slot.End,
                    EventLocation = location,
                    Platform = request.Platform.ToString(),
                    MeetingLink = meetingLink,
                    Status = AppointmentStatus.Confirmed,
                    TotalPrice = 0m,
                    CreatedAt = clock.GetUtcNow().UtcDateTime,
                    Services = services.Select(s => new AppointmentServiceLine
                    {
                        ServicesId = s.ServicesId,
                        ServiceName = s.Name,
                        ServicePrice = 0m
                    }).ToList()
                };
                db.Appointments.Add(entity);
                await db.SaveChangesAsync(ct);

                await UpsertOverviewAsync(request.Date, 0m, ct);

                // Insert into BookingNegotiation for the Office Clerk (only 1 consultation entry, no duplicate event or senior photographer)
                var conn = db.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

                await using (var evtCmd = conn.CreateCommand())
                {
                    evtCmd.Transaction = tx.GetDbTransaction();
                    evtCmd.CommandText = @"
                        IF OBJECT_ID(N'dbo.Customer', N'U') IS NOT NULL
                        BEGIN
                            IF NOT EXISTS (SELECT 1 FROM dbo.Customer WHERE CustomerID = @CustId)
                            BEGIN
                                INSERT INTO dbo.Customer (CustomerID, CustomerFirstName, CustomerLastName, CustomerEmail, CustomerStatus)
                                VALUES (@CustId, @CustName, '', @CustEmail, 'Active');
                            END
                        END

                        IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
                        BEGIN
                            IF NOT EXISTS (SELECT 1 FROM dbo.BookingNegotiation WHERE NegotiationID = @NegId)
                            BEGIN
                                DECLARE @ClerkId nvarchar(30);
                                SET @ClerkId = @EmpId;
                                IF @ClerkId IS NULL
                                BEGIN
                                    SELECT TOP 1 @ClerkId = e.EmployeeID
                                    FROM dbo.Employees e
                                    LEFT JOIN dbo.EmployeeJointSpecialization ejs ON e.EmployeeID = ejs.EmployeeID
                                    LEFT JOIN dbo.EmployeeSpecialization es ON ejs.SpecializationID = es.SpecializationID
                                    LEFT JOIN (
                                        SELECT EmployeeID, MAX(CreatedAt) AS LastAssignedAt
                                        FROM dbo.BookingNegotiation
                                        GROUP BY EmployeeID
                                    ) bn ON e.EmployeeID = bn.EmployeeID
                                    WHERE (e.EmployeeStatus = 'Active' OR e.Status = 'Active')
                                      AND (
                                          UPPER(LTRIM(RTRIM(COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization, '')))) = 'OFFICE CLERK'
                                          OR e.Specializations LIKE '%Office Clerk%'
                                      )
                                      AND (
                                          COL_LENGTH(N'dbo.Employees', N'DaysOff') IS NULL
                                          OR CHARINDEX(DATENAME(dw, @EvtDate), ISNULL(e.DaysOff, 'Sunday')) = 0
                                      )
                                      AND NOT EXISTS (
                                          SELECT 1 FROM dbo.BookingNegotiation bn2
                                          WHERE bn2.EmployeeID = e.EmployeeID
                                            AND bn2.ConsultationDate = @EvtDate
                                            AND bn2.NegotiationStatus NOT IN ('Cancelled', 'Deal_Lost')
                                            AND bn2.ConsultationTime IS NOT NULL
                                            AND @EvtTime < DATEADD(hour, 2, bn2.ConsultationTime)
                                            AND DATEADD(hour, -2, bn2.ConsultationTime) < DATEADD(hour, 1, @EvtTime)
                                      )
                                    ORDER BY COALESCE(bn.LastAssignedAt, '1900-01-01') ASC, e.EmployeeID ASC;
                                END

                                INSERT INTO dbo.BookingNegotiation (NegotiationID, EventAppointmentID, CustomerID, EmployeeID, ConsultationDate, ConsultationTime, MeetingLink, InquiryNotes, NegotiationStatus, CreatedAt)
                                VALUES (@NegId, NULL, @CustId, @ClerkId, @EvtDate, @EvtTime, @MeetingLink, @InquiryNotes, 'Pending_Call', SYSUTCDATETIME());
                            END
                        END

                        IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                        BEGIN
                            INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ActionDetails, ChangedByCustomerID, ChangedByType, LoggedAt)
                            VALUES ('CREATE', 'BookingNegotiation', @NegId, @LogMsg, @CustId, 'Customer', SYSUTCDATETIME());
                        END";

                    var pCustId = evtCmd.CreateParameter(); pCustId.ParameterName = "@CustId"; pCustId.Value = customer.UserId; evtCmd.Parameters.Add(pCustId);
                    var pCustName = evtCmd.CreateParameter(); pCustName.ParameterName = "@CustName"; pCustName.Value = customer.FullName; evtCmd.Parameters.Add(pCustName);
                    var pCustEmail = evtCmd.CreateParameter(); pCustEmail.ParameterName = "@CustEmail"; pCustEmail.Value = customer.Email; evtCmd.Parameters.Add(pCustEmail);
                    var pEvtDate = evtCmd.CreateParameter(); pEvtDate.ParameterName = "@EvtDate"; pEvtDate.Value = request.Date.ToDateTime(TimeOnly.MinValue); evtCmd.Parameters.Add(pEvtDate);
                    var pEvtTime = evtCmd.CreateParameter(); pEvtTime.ParameterName = "@EvtTime"; pEvtTime.Value = slot.Start.ToTimeSpan(); evtCmd.Parameters.Add(pEvtTime);
                    var pEmpId = evtCmd.CreateParameter(); pEmpId.ParameterName = "@EmpId"; pEmpId.Value = assignedEmployeeId; evtCmd.Parameters.Add(pEmpId);
                    var pNegId = evtCmd.CreateParameter(); pNegId.ParameterName = "@NegId"; pNegId.Value = $"NEG-{entity.AppointmentId:D4}"; evtCmd.Parameters.Add(pNegId);
                    var pMeetingLink = evtCmd.CreateParameter(); pMeetingLink.ParameterName = "@MeetingLink"; pMeetingLink.Value = meetingLink ?? (object)DBNull.Value; evtCmd.Parameters.Add(pMeetingLink);
                    var pInquiryNotes = evtCmd.CreateParameter(); pInquiryNotes.ParameterName = "@InquiryNotes"; pInquiryNotes.Value = location ?? "Consultation Booking"; evtCmd.Parameters.Add(pInquiryNotes);
                    var pLogMsg = evtCmd.CreateParameter(); pLogMsg.ParameterName = "@LogMsg"; pLogMsg.Value = $"New consultation booked by {customer.FullName} ({customer.Email}) for initial negotiation."; evtCmd.Parameters.Add(pLogMsg);

                    await evtCmd.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
                return entity;
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsSlotUniqueViolation(ex))
        {
            logger.LogWarning("Double-booking race on {Date} {Slot} rejected by UX_Appointment_Slot; transaction rolled back.", request.Date, slot.Start);
            throw new SlotUnavailableException(request.Date, slot.Start);
        }
        catch (SqlException ex) when (ex.Message.Contains("Office Clerk") || ex.Number == 50000)
        {
            logger.LogWarning("Booking rejected by business rule: {Message}", ex.Message);
            throw new ScheduleValidationException(ex.Message);
        }
        catch (Exception ex) when (ex.InnerException is SqlException sqlEx && (sqlEx.Message.Contains("Office Clerk") || sqlEx.Number == 50000))
        {
            logger.LogWarning("Booking rejected by business rule: {Message}", ex.InnerException!.Message);
            throw new ScheduleValidationException(ex.InnerException!.Message);
        }

        logger.LogInformation("Appointment {AppointmentId} booked for {Date} {Slot} by {CustomerId}. Assigned: {AssignedEmployeeId}",
            appointment.AppointmentId, appointment.SlotDate, appointment.SlotStart, customer.UserId, assignedEmployeeId);

        // The booking is committed. Email failure must not undo it; report it instead.
        var emailSent = false;
        try
        {
            await notifications.SendBookingConfirmationAsync(new BookingConfirmation(
                appointment.AppointmentId, customer, appointment.SlotDate, appointment.SlotStart, appointment.SlotEnd,
                services, 0m, request.Platform, meetingLink, location), CancellationToken.None);
            emailSent = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Appointment {AppointmentId} saved but the confirmation email failed.", appointment.AppointmentId);
        }

        return new ScheduleBookingResultDto(appointment.AppointmentId, appointment.SlotDate, appointment.SlotStart, appointment.SlotEnd,
            services, 0m, request.Platform, meetingLink, location, emailSent);
    }

    public sealed record ClerkSummary(string EmployeeId, string DaysOff, string FullName);
    public sealed record CrewMemberSummary(string EmployeeId, string DaysOff, bool IsPhotographer, bool IsVideographer);
    public sealed record ShootCrewMemberSummary(string EmployeeId, string DaysOff);
    public sealed record ShootEventSummary(
        string EventAppointmentId,
        DateOnly EventDate,
        TimeSpan StartTime,
        TimeSpan EndTime,
        IReadOnlyList<string> AssignedEmployeeIds);
    public sealed record LeaveSummary(string EmployeeStudioId, DateOnly StartDate, DateOnly EndDate);
    public sealed record EventSummary(string EmployeeId, DateOnly EventDate, TimeOnly EventTime);
    public sealed record ConsultationSummary(string EmployeeId, DateOnly ConsultationDate, TimeOnly ConsultationTime);

    public static ShootMonthAvailabilityDto EvaluateShootMonthAvailability(
        int year,
        int month,
        int durationMinutes,
        IReadOnlyList<ShootCrewMemberSummary> crew,
        IReadOnlyList<LeaveSummary> leaves,
        IReadOnlyList<ShootEventSummary> shootEvents)
    {
        var normalizedDuration = durationMinutes <= 0
            ? ShootAvailabilityCore.MinimumShootDurationMinutes
            : Math.Max(ShootAvailabilityCore.MinimumShootDurationMinutes, durationMinutes);

        var first = new DateOnly(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var days = new List<ShootDayAvailabilityDto>(daysInMonth);

        for (int i = 0; i < daysInMonth; i++)
        {
            var date = first.AddDays(i);
            days.Add(EvaluateShootDayAvailability(date, normalizedDuration, crew, leaves, shootEvents));
        }

        return new ShootMonthAvailabilityDto(year, month, normalizedDuration, days);
    }

    public static ShootDayAvailabilityDto EvaluateShootDayAvailability(
        DateOnly date,
        int durationMinutes,
        IReadOnlyList<ShootCrewMemberSummary> crew,
        IReadOnlyList<LeaveSummary> leaves,
        IReadOnlyList<ShootEventSummary> shootEvents)
    {
        var dayEvents = shootEvents.Where(e => e.EventDate == date).ToList();
        var eligibleCrewBusy = new List<IReadOnlyList<ShootAvailabilityCore.ShootTimeInterval>>();

        foreach (var member in crew)
        {
            if (member.EmployeeId.StartsWith("CLK-", StringComparison.OrdinalIgnoreCase))
                continue;

            if (ShootAvailabilityCore.IsDayOff(member.DaysOff, null, date.DayOfWeek))
                continue;

            if (leaves.Any(l =>
                string.Equals(l.EmployeeStudioId, member.EmployeeId, StringComparison.OrdinalIgnoreCase) &&
                date >= l.StartDate && date <= l.EndDate))
                continue;

            var memberEvents = dayEvents
                .Where(ev => ev.AssignedEmployeeIds.Any(id => string.Equals(id, member.EmployeeId, StringComparison.OrdinalIgnoreCase)))
                .Select(ev =>
                {
                    var sMin = ShootAvailabilityCore.ToStartMinutes(ev.StartTime);
                    var effectiveEnd = ev.EndTime > ev.StartTime ? ev.EndTime : ev.StartTime.Add(TimeSpan.FromHours(1));
                    var eMin = ShootAvailabilityCore.ToEndMinutes(effectiveEnd);
                    return new ShootAvailabilityCore.ShootTimeInterval(sMin, eMin);
                });

            eligibleCrewBusy.Add(ShootAvailabilityCore.BufferAndMergeEvents(memberEvents));
        }

        var eval = ShootAvailabilityCore.EvaluateDay(
            durationMinutes,
            hasBookings: dayEvents.Count > 0,
            bookedEventCount: dayEvents.Count,
            eligibleCrewBusy);

        var status = eval.StatusCode switch
        {
            ShootAvailabilityCore.StatusBookingsAvailable => ShootDayStatus.BookingsAvailable,
            ShootAvailabilityCore.StatusFullyBooked => ShootDayStatus.FullyBooked,
            _ => ShootDayStatus.Free
        };

        var ranges = eval.FeasibleStartRanges
            .Select(r => new ShootTimeRangeDto(
                TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(r.MinStartMin)),
                TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(r.MaxStartMin))))
            .ToList();

        var quickSlots = eval.QuickSlots
            .Select(q => new ShootQuickSlotDto(
                TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(q.StartMin)),
                TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(q.EndMin)),
                TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(q.PrepStartMin)),
                TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(q.PostEndMin)),
                q.AvailableCrewCount))
            .ToList();

        return new ShootDayAvailabilityDto(
            date,
            status,
            eval.HasBookings,
            eval.BookedEventCount,
            eval.EligibleCrewCount,
            ranges,
            quickSlots);
    }

    private async Task<List<ShootCrewMemberSummary>> GetDispatchableShootCrewAsync(CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        bool hasEmployeesTable = false;
        bool hasDaysOff = false;
        bool hasStatusCol = false;
        bool hasSpecializationsCol = false;
        bool hasJointSpecs = false;

        await using (var metaCmd = conn.CreateCommand())
        {
            metaCmd.CommandText = @"
                SELECT
                    CASE WHEN OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL THEN 1 ELSE 0 END,
                    CASE WHEN COL_LENGTH(N'dbo.Employees', N'DaysOff') IS NOT NULL THEN 1 ELSE 0 END,
                    CASE WHEN COL_LENGTH(N'dbo.Employees', N'Status') IS NOT NULL THEN 1 ELSE 0 END,
                    CASE WHEN COL_LENGTH(N'dbo.Employees', N'Specializations') IS NOT NULL THEN 1 ELSE 0 END,
                    CASE WHEN OBJECT_ID(N'dbo.EmployeeJointSpecialization', N'U') IS NOT NULL
                          AND OBJECT_ID(N'dbo.EmployeeSpecialization', N'U') IS NOT NULL THEN 1 ELSE 0 END;";
            await using var metaReader = await metaCmd.ExecuteReaderAsync(ct);
            if (await metaReader.ReadAsync(ct))
            {
                hasEmployeesTable = metaReader.GetInt32(0) == 1;
                hasDaysOff = metaReader.GetInt32(1) == 1;
                hasStatusCol = metaReader.GetInt32(2) == 1;
                hasSpecializationsCol = metaReader.GetInt32(3) == 1;
                hasJointSpecs = metaReader.GetInt32(4) == 1;
            }
        }

        if (!hasEmployeesTable) return [];

        var daysOffSelect = hasDaysOff ? "ISNULL(e.DaysOff, 'Sunday')" : "'Sunday'";
        var empSpecsSelect = hasSpecializationsCol ? "ISNULL(e.Specializations, '')" : "''";
        var activePredicate = hasStatusCol
            ? "(UPPER(ISNULL(e.EmployeeStatus, 'Active')) IN ('ACTIVE', 'CLOCK-IN (DUTY)', 'CLOCK-OUT (OFF DUTY)') OR UPPER(ISNULL(e.Status, '')) = 'ACTIVE') AND UPPER(ISNULL(e.EmployeeStatus, 'Active')) NOT IN ('INACTIVE', 'ARCHIVE', 'ARCHIVED', 'DELETED')"
            : "UPPER(ISNULL(e.EmployeeStatus, 'Active')) IN ('ACTIVE', 'CLOCK-IN (DUTY)', 'CLOCK-OUT (OFF DUTY)')";

        var sql = hasJointSpecs
            ? $@"
                SELECT e.EmployeeID,
                       {daysOffSelect} AS DaysOff,
                       {empSpecsSelect} AS EmpSpecs,
                       ISNULL(COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization), '') AS SpecName
                FROM dbo.Employees e
                LEFT JOIN dbo.EmployeeJointSpecialization ejs ON e.EmployeeID = ejs.EmployeeID
                LEFT JOIN dbo.EmployeeSpecialization es ON ejs.SpecializationID = es.SpecializationID
                WHERE {activePredicate};"
            : $@"
                SELECT e.EmployeeID,
                       {daysOffSelect} AS DaysOff,
                       {empSpecsSelect} AS EmpSpecs,
                       '' AS SpecName
                FROM dbo.Employees e
                WHERE {activePredicate};";

        var crewMap = new Dictionary<string, (string DaysOff, bool IsClerk)>(StringComparer.OrdinalIgnoreCase);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(0)) continue;
                var empId = reader.GetString(0).Trim();
                if (string.IsNullOrWhiteSpace(empId)) continue;

                var daysOff = reader.IsDBNull(1) ? "Sunday" : reader.GetString(1);
                var empSpecs = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var specName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);

                bool isClerk = empId.StartsWith("CLK-", StringComparison.OrdinalIgnoreCase)
                    || empSpecs.Contains("clerk", StringComparison.OrdinalIgnoreCase)
                    || specName.Contains("clerk", StringComparison.OrdinalIgnoreCase);

                if (crewMap.TryGetValue(empId, out var existing))
                {
                    crewMap[empId] = (existing.DaysOff, existing.IsClerk || isClerk);
                }
                else
                {
                    crewMap[empId] = (daysOff, isClerk);
                }
            }
        }

        return crewMap
            .Where(kvp => !kvp.Value.IsClerk)
            .Select(kvp => new ShootCrewMemberSummary(kvp.Key, kvp.Value.DaysOff))
            .ToList();
    }

    private async Task<List<ShootEventSummary>> GetShootEventsRangeAsync(
        DateOnly first,
        DateOnly last,
        string? excludeAppointmentId,
        CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        bool hasEventTable = false;
        bool hasEventEndTime = false;
        bool hasDurationMinutes = false;
        bool hasEventEmployee = false;
        bool hasNegotiation = false;

        await using (var metaCmd = conn.CreateCommand())
        {
            metaCmd.CommandText = @"
                SELECT
                    CASE WHEN OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL THEN 1 ELSE 0 END,
                    CASE WHEN COL_LENGTH(N'dbo.EventAppointment', N'EventEndTime') IS NOT NULL THEN 1 ELSE 0 END,
                    CASE WHEN COL_LENGTH(N'dbo.EventAppointment', N'DurationMinutes') IS NOT NULL THEN 1 ELSE 0 END,
                    CASE WHEN OBJECT_ID(N'dbo.EventEmployee', N'U') IS NOT NULL THEN 1 ELSE 0 END,
                    CASE WHEN OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL THEN 1 ELSE 0 END;";
            await using var metaReader = await metaCmd.ExecuteReaderAsync(ct);
            if (await metaReader.ReadAsync(ct))
            {
                hasEventTable = metaReader.GetInt32(0) == 1;
                hasEventEndTime = metaReader.GetInt32(1) == 1;
                hasDurationMinutes = metaReader.GetInt32(2) == 1;
                hasEventEmployee = metaReader.GetInt32(3) == 1;
                hasNegotiation = metaReader.GetInt32(4) == 1;
            }
        }

        if (!hasEventTable) return [];

        var endTimeCol = hasEventEndTime ? "ea.EventEndTime" : "CAST(NULL AS time)";
        var durCol = hasDurationMinutes ? "ISNULL(ea.DurationMinutes, 60)" : "CAST(60 AS int)";
        var negFilter = hasNegotiation
            ? "AND NOT EXISTS (SELECT 1 FROM dbo.BookingNegotiation bn WHERE bn.EventAppointmentID = ea.EventAppointmentID AND bn.NegotiationStatus IN ('Deal_Lost', 'Cancelled'))"
            : string.Empty;

        var sql = hasEventEmployee
            ? $@"
                SELECT ea.EventAppointmentID,
                       ea.EventDate,
                       ea.EventTime,
                       {endTimeCol} AS EventEndTime,
                       {durCol} AS DurationMinutes,
                       ea.EmployeeID AS LeadEmployeeID,
                       ee.EmployeeID AS CrewEmployeeID
                FROM dbo.EventAppointment ea
                LEFT JOIN dbo.EventEmployee ee ON ee.EventAppointmentID = ea.EventAppointmentID
                WHERE ea.EventDate >= @First AND ea.EventDate <= @Last
                  AND ISNULL(ea.EventStatus, '') NOT IN ('Cancelled', 'Expired')
                  AND (@ExcludeEvtId IS NULL OR ea.EventAppointmentID <> @ExcludeEvtId)
                  {negFilter};"
            : $@"
                SELECT ea.EventAppointmentID,
                       ea.EventDate,
                       ea.EventTime,
                       {endTimeCol} AS EventEndTime,
                       {durCol} AS DurationMinutes,
                       ea.EmployeeID AS LeadEmployeeID,
                       CAST(NULL AS nvarchar(30)) AS CrewEmployeeID
                FROM dbo.EventAppointment ea
                WHERE ea.EventDate >= @First AND ea.EventDate <= @Last
                  AND ISNULL(ea.EventStatus, '') NOT IN ('Cancelled', 'Expired')
                  AND (@ExcludeEvtId IS NULL OR ea.EventAppointmentID <> @ExcludeEvtId)
                  {negFilter};";

        var byEvent = new Dictionary<string, (DateOnly Date, TimeSpan Start, TimeSpan End, HashSet<string> CrewIds)>(StringComparer.OrdinalIgnoreCase);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql;

            var pFirst = cmd.CreateParameter();
            pFirst.ParameterName = "@First";
            pFirst.Value = first.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add(pFirst);

            var pLast = cmd.CreateParameter();
            pLast.ParameterName = "@Last";
            pLast.Value = last.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add(pLast);

            var pExclude = cmd.CreateParameter();
            pExclude.ParameterName = "@ExcludeEvtId";
            pExclude.Value = (object?)excludeAppointmentId ?? DBNull.Value;
            cmd.Parameters.Add(pExclude);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2)) continue;
                var evtId = reader.GetString(0).Trim();
                var evtDate = DateOnly.FromDateTime(reader.GetDateTime(1));
                var startSpan = reader.GetFieldValue<TimeSpan>(2);
                var dbEndSpan = reader.IsDBNull(3) ? (TimeSpan?)null : reader.GetFieldValue<TimeSpan>(3);
                var durMins = reader.IsDBNull(4) ? 60 : Math.Max(15, reader.GetInt32(4));
                var leadEmp = reader.IsDBNull(5) ? null : reader.GetString(5).Trim();
                var crewEmp = reader.IsDBNull(6) ? null : reader.GetString(6).Trim();

                var endSpan = dbEndSpan.HasValue && dbEndSpan.Value > startSpan
                    ? dbEndSpan.Value
                    : startSpan.Add(TimeSpan.FromMinutes(durMins));
                if (endSpan > new TimeSpan(23, 59, 59))
                    endSpan = new TimeSpan(23, 59, 59);

                if (!byEvent.TryGetValue(evtId, out var entry))
                {
                    entry = (evtDate, startSpan, endSpan, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    byEvent[evtId] = entry;
                }

                if (!string.IsNullOrWhiteSpace(leadEmp))
                    entry.CrewIds.Add(leadEmp);
                if (!string.IsNullOrWhiteSpace(crewEmp))
                    entry.CrewIds.Add(crewEmp);
            }
        }

        return byEvent
            .Select(kvp => new ShootEventSummary(
                kvp.Key,
                kvp.Value.Date,
                kvp.Value.Start,
                kvp.Value.End,
                kvp.Value.CrewIds.ToList()))
            .ToList();
    }

    private async Task<List<ClerkSummary>> GetActiveClerksDataAsync(CancellationToken ct)
    {
        var list = new List<ClerkSummary>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        await using (var cmd = conn.CreateCommand())
        {
            // Specifically query active Office Clerks
            cmd.CommandText = @"
                SELECT DISTINCT e.EmployeeID, ISNULL(e.DaysOff, 'Sunday'), LTRIM(RTRIM(CONCAT(ISNULL(e.EmployeeFirstName, ''), ' ', ISNULL(e.EmployeeLastName, ''))))
                FROM dbo.Employees e
                LEFT JOIN dbo.EmployeeJointSpecialization ejs ON e.EmployeeID = ejs.EmployeeID
                LEFT JOIN dbo.EmployeeSpecialization es ON ejs.SpecializationID = es.SpecializationID
                WHERE (e.EmployeeStatus = 'Active' OR e.Status = 'Active')
                  AND (
                      UPPER(LTRIM(RTRIM(COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization, '')))) = 'OFFICE CLERK'
                      OR e.Specializations LIKE '%Office Clerk%'
                  );";
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    var id = reader.GetString(0);
                    var daysOff = reader.GetString(1);
                    var name = reader.IsDBNull(2) || string.IsNullOrWhiteSpace(reader.GetString(2)) ? id : reader.GetString(2);
                    list.Add(new ClerkSummary(id, daysOff, name));
                }
            }
        }

        // Fallback: If no clerks explicitly found in specialization tables (e.g. initial dev seed), use any active employee
        if (list.Count == 0)
        {
            await using var fallbackCmd = conn.CreateCommand();
            fallbackCmd.CommandText = "SELECT DISTINCT EmployeeID, ISNULL(DaysOff, 'Sunday'), LTRIM(RTRIM(CONCAT(ISNULL(EmployeeFirstName, ''), ' ', ISNULL(EmployeeLastName, '')))) FROM dbo.Employees WHERE EmployeeStatus = 'Active' OR Status = 'Active';";
            await using var fallbackReader = await fallbackCmd.ExecuteReaderAsync(ct);
            while (await fallbackReader.ReadAsync(ct))
            {
                var id = fallbackReader.GetString(0);
                var daysOff = fallbackReader.GetString(1);
                var name = fallbackReader.IsDBNull(2) || string.IsNullOrWhiteSpace(fallbackReader.GetString(2)) ? id : fallbackReader.GetString(2);
                list.Add(new ClerkSummary(id, daysOff, name));
            }
        }

        return list;
    }

    private async Task<List<ConsultationSummary>> GetBookedConsultationsRangeAsync(DateOnly first, DateOnly last, CancellationToken ct)
    {
        var list = new List<ConsultationSummary>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
            BEGIN
                SELECT EmployeeID, ConsultationDate, ConsultationTime
                FROM dbo.BookingNegotiation
                WHERE ConsultationDate >= @First AND ConsultationDate <= @Last
                  AND NegotiationStatus NOT IN ('Cancelled', 'Deal_Lost')
                  AND EmployeeID IS NOT NULL;
            END";
        var pFirst = cmd.CreateParameter();
        pFirst.ParameterName = "@First";
        pFirst.Value = first.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add(pFirst);

        var pLast = cmd.CreateParameter();
        pLast.ParameterName = "@Last";
        pLast.Value = last.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add(pLast);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2)) continue;
            var empId = reader.GetString(0);
            var d = DateOnly.FromDateTime(reader.GetDateTime(1));
            var t = TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(2));
            list.Add(new ConsultationSummary(empId, d, t));
        }
        return list;
    }

    private async Task<List<LeaveSummary>> GetApprovedLeavesRangeAsync(DateOnly first, DateOnly last, CancellationToken ct)
    {
        var list = new List<LeaveSummary>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NOT NULL
            BEGIN
                SELECT EmployeeStudioId, StartDate, EndDate
                FROM dbo.EmployeeLeaves
                WHERE Status = 'Approved' AND EndDate >= @First AND StartDate <= @Last;
            END";
        var pFirst = cmd.CreateParameter();
        pFirst.ParameterName = "@First";
        pFirst.Value = first.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add(pFirst);

        var pLast = cmd.CreateParameter();
        pLast.ParameterName = "@Last";
        pLast.Value = last.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add(pLast);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var empId = reader.GetString(0);
            var s = DateOnly.FromDateTime(reader.GetDateTime(1));
            var e = DateOnly.FromDateTime(reader.GetDateTime(2));
            list.Add(new LeaveSummary(empId, s, e));
        }
        return list;
    }

    private async Task<List<EventSummary>> GetAssignedEventsRangeAsync(DateOnly first, DateOnly last, CancellationToken ct)
    {
        var list = new List<EventSummary>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            IF OBJECT_ID(N'dbo.EventEmployee', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
            BEGIN
                SELECT ee.EmployeeID, ea.EventDate, ea.EventTime
                FROM dbo.EventEmployee ee
                JOIN dbo.EventAppointment ea ON ee.EventAppointmentID = ea.EventAppointmentID
                WHERE ea.EventDate >= @First AND ea.EventDate <= @Last AND ea.EventStatus <> 'Cancelled';
            END";
        var pFirst = cmd.CreateParameter();
        pFirst.ParameterName = "@First";
        pFirst.Value = first.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add(pFirst);

        var pLast = cmd.CreateParameter();
        pLast.ParameterName = "@Last";
        pLast.Value = last.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add(pLast);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var empId = reader.GetString(0);
            var d = DateOnly.FromDateTime(reader.GetDateTime(1));
            var t = TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(2));
            list.Add(new EventSummary(empId, d, t));
        }
        return list;
    }

    private async Task<List<CrewMemberSummary>> GetActiveCrewDataAsync(CancellationToken ct)
    {
        var crewMap = new Dictionary<string, (string DaysOff, bool IsPhotographer, bool IsVideographer)>(StringComparer.OrdinalIgnoreCase);
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT e.EmployeeID,
                   ISNULL(e.DaysOff, 'Sunday'),
                   ISNULL(e.DepartmentID, ''),
                   ISNULL(e.Specializations, ''),
                   ISNULL(es.DepartmentID, ''),
                   ISNULL(es.DepartmentName, ''),
                   ISNULL(es.SpecializationName, ''),
                   ISNULL(es.Specialization, '')
            FROM dbo.Employees e
            LEFT JOIN dbo.EmployeeJointSpecialization ejs ON e.EmployeeID = ejs.EmployeeID
            LEFT JOIN dbo.EmployeeSpecialization es ON ejs.SpecializationID = es.SpecializationID
            WHERE (e.EmployeeStatus = 'Active' OR e.Status = 'Active');";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetString(0);
            var daysOff = reader.GetString(1);
            var empDeptId = reader.GetString(2);
            var empSpecs = reader.GetString(3);
            var specDeptId = reader.GetString(4);
            var specDeptName = reader.GetString(5);
            var specName = reader.GetString(6);
            var specAlt = reader.GetString(7);

            bool isPhoto = string.Equals(empDeptId, "DEP-0001", StringComparison.OrdinalIgnoreCase)
                || string.Equals(specDeptId, "DEP-0001", StringComparison.OrdinalIgnoreCase)
                || specDeptName.Contains("Photography", StringComparison.OrdinalIgnoreCase)
                || specName.Contains("Photograph", StringComparison.OrdinalIgnoreCase)
                || specAlt.Contains("Photograph", StringComparison.OrdinalIgnoreCase)
                || empSpecs.Contains("Photograph", StringComparison.OrdinalIgnoreCase);

            bool isVideo = string.Equals(empDeptId, "DEP-0002", StringComparison.OrdinalIgnoreCase)
                || string.Equals(specDeptId, "DEP-0002", StringComparison.OrdinalIgnoreCase)
                || specDeptName.Contains("Videography", StringComparison.OrdinalIgnoreCase)
                || specName.Contains("Videograph", StringComparison.OrdinalIgnoreCase)
                || specAlt.Contains("Videograph", StringComparison.OrdinalIgnoreCase)
                || empSpecs.Contains("Videograph", StringComparison.OrdinalIgnoreCase);

            if (!isPhoto && !isVideo)
                continue;

            if (crewMap.TryGetValue(id, out var existing))
            {
                crewMap[id] = (existing.DaysOff, existing.IsPhotographer || isPhoto, existing.IsVideographer || isVideo);
            }
            else
            {
                crewMap[id] = (daysOff, isPhoto, isVideo);
            }
        }

        return crewMap.Select(kvp => new CrewMemberSummary(kvp.Key, kvp.Value.DaysOff, kvp.Value.IsPhotographer, kvp.Value.IsVideographer)).ToList();
    }

    private async Task<List<EventSummary>> GetAssignedEventsWithDirectAssignmentRangeAsync(DateOnly first, DateOnly last, CancellationToken ct)
    {
        var list = new List<EventSummary>();
        var seen = new HashSet<(string EmployeeId, DateOnly EventDate, TimeOnly EventTime)>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
            BEGIN
                IF OBJECT_ID(N'dbo.EventEmployee', N'U') IS NOT NULL
                BEGIN
                    SELECT ee.EmployeeID, ea.EventDate, ea.EventTime
                    FROM dbo.EventEmployee ee
                    JOIN dbo.EventAppointment ea ON ee.EventAppointmentID = ea.EventAppointmentID
                    WHERE ea.EventDate >= @First AND ea.EventDate <= @Last
                      AND ea.EventStatus NOT IN ('Cancelled', 'Expired')
                    UNION
                    SELECT ea.EmployeeID, ea.EventDate, ea.EventTime
                    FROM dbo.EventAppointment ea
                    WHERE ea.EventDate >= @First AND ea.EventDate <= @Last
                      AND ea.EventStatus NOT IN ('Cancelled', 'Expired')
                      AND ea.EmployeeID IS NOT NULL;
                END
                ELSE
                BEGIN
                    SELECT ea.EmployeeID, ea.EventDate, ea.EventTime
                    FROM dbo.EventAppointment ea
                    WHERE ea.EventDate >= @First AND ea.EventDate <= @Last
                      AND ea.EventStatus NOT IN ('Cancelled', 'Expired')
                      AND ea.EmployeeID IS NOT NULL;
                END
            END";

        var pFirst = cmd.CreateParameter();
        pFirst.ParameterName = "@First";
        pFirst.Value = first.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add(pFirst);

        var pLast = cmd.CreateParameter();
        pLast.ParameterName = "@Last";
        pLast.Value = last.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add(pLast);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2)) continue;
            var empId = reader.GetString(0);
            var d = DateOnly.FromDateTime(reader.GetDateTime(1));
            var t = TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(2));
            if (seen.Add((empId, d, t)))
                list.Add(new EventSummary(empId, d, t));
        }
        return list;
    }

    public static AvailabilityCheckResultDto EvaluateAvailability(
        DateOnly date,
        TimeOnly? targetTime,
        DateTime studioNow,
        int bookingWindowDays,
        IReadOnlySet<TimeOnly> takenConsultationSlots,
        IReadOnlyList<ClerkSummary> clerks,
        IReadOnlyList<CrewMemberSummary> crew,
        IReadOnlyList<LeaveSummary> leaves,
        IReadOnlyList<EventSummary> assignedEvents,
        IReadOnlyList<ConsultationSummary> bookedConsultations)
    {
        var today = DateOnly.FromDateTime(studioNow);
        bool isDateInWindow = date >= today && date <= today.AddDays(bookingWindowDays);

        int totalPhoto = crew.Count(c => c.IsPhotographer);
        int totalVideo = crew.Count(c => c.IsVideographer);

        var slotResults = new List<CrewSlotStatusDto>(StudioSlots.All.Count);
        foreach (var slot in StudioSlots.All)
        {
            bool isBookable = isDateInWindow && date.ToDateTime(slot.Start) > studioNow;
            var (availPhoto, availVideo) = CountAvailableCrew(crew, leaves, assignedEvents, bookedConsultations, date, slot.Start, slot.End, isBookable);
            bool isConsultationOpen = isBookable
                && !takenConsultationSlots.Contains(slot.Start)
                && HasAnyAvailableClerk(clerks.ToList(), leaves.ToList(), assignedEvents.ToList(), bookedConsultations.ToList(), date, slot.Start);

            string slotMsg = FormatSlotStatusMessage(isBookable, availPhoto, availVideo, totalPhoto, totalVideo);
            slotResults.Add(new CrewSlotStatusDto(
                slot.Start,
                slot.End,
                isBookable,
                availPhoto > 0,
                availVideo > 0,
                availPhoto,
                totalPhoto,
                availVideo,
                totalVideo,
                isConsultationOpen,
                slotMsg));
        }

        bool isFullDayAvailable = slotResults.All(s => s.IsBookable && s.IsPhotographyAvailable && s.IsVideographyAvailable);
        bool isWholeDayUnavailable = slotResults.All(s => !s.IsBookable || (!s.IsPhotographyAvailable && !s.IsVideographyAvailable));

        string dayStatusMessage = isFullDayAvailable
            ? "The full day is available."
            : isWholeDayUnavailable
                ? "The whole day is not available."
                : "Only some time slots are available for this day.";

        string? timeStatusMessage = null;
        bool isPhotoForTime = false;
        bool isVideoForTime = false;

        if (targetTime.HasValue)
        {
            var matchedSlot = StudioSlots.All.FirstOrDefault(s => targetTime.Value >= s.Start && targetTime.Value < s.End);
            if (matchedSlot is null)
            {
                timeStatusMessage = isWholeDayUnavailable
                    ? "The whole day is not available."
                    : "Selected time is outside studio hours (9:00 AM - 11:00 AM, 1:00 PM - 5:00 PM).";
            }
            else
            {
                bool isSlotBookable = isDateInWindow && date.ToDateTime(matchedSlot.Start) > studioNow;
                var firstHalfEnd = matchedSlot.Start.AddMinutes(30);
                var (p1, v1) = CountAvailableCrew(crew, leaves, assignedEvents, bookedConsultations, date, matchedSlot.Start, firstHalfEnd, isSlotBookable);
                var (p2, v2) = CountAvailableCrew(crew, leaves, assignedEvents, bookedConsultations, date, firstHalfEnd, matchedSlot.End, isSlotBookable);

                bool half1Open = p1 > 0 || v1 > 0;
                bool half2Open = p2 > 0 || v2 > 0;

                isPhotoForTime = p1 > 0 || p2 > 0;
                isVideoForTime = v1 > 0 || v2 > 0;

                if (!isSlotBookable || (!half1Open && !half2Open))
                {
                    timeStatusMessage = isWholeDayUnavailable
                        ? "The whole day is not available."
                        : "No crew available for this time.";
                }
                else if (half1Open != half2Open)
                {
                    timeStatusMessage = "Only some time slots are available for this hour.";
                }
                else if (isPhotoForTime && isVideoForTime)
                {
                    timeStatusMessage = "Both Photography and Videography available.";
                }
                else if (isPhotoForTime)
                {
                    timeStatusMessage = "Available for this time: Photography only";
                }
                else
                {
                    timeStatusMessage = "Available for this time: Videography only";
                }
            }
        }

        return new AvailabilityCheckResultDto(
            date,
            targetTime,
            dayStatusMessage,
            timeStatusMessage,
            isFullDayAvailable,
            isWholeDayUnavailable,
            isPhotoForTime,
            isVideoForTime,
            totalPhoto,
            totalVideo,
            slotResults);
    }

    private static (int AvailablePhotographers, int AvailableVideographers) CountAvailableCrew(
        IReadOnlyList<CrewMemberSummary> crew,
        IReadOnlyList<LeaveSummary> leaves,
        IReadOnlyList<EventSummary> assignedEvents,
        IReadOnlyList<ConsultationSummary> bookedConsultations,
        DateOnly date,
        TimeOnly windowStart,
        TimeOnly windowEnd,
        bool isBookable)
    {
        if (!isBookable) return (0, 0);

        var dayName = date.DayOfWeek.ToString();
        int photo = 0;
        int video = 0;

        foreach (var member in crew)
        {
            if (member.DaysOff.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(d => string.Equals(d, dayName, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (leaves.Any(l => string.Equals(l.EmployeeStudioId, member.EmployeeId, StringComparison.OrdinalIgnoreCase)
                && date >= l.StartDate && date <= l.EndDate))
                continue;

            if (assignedEvents.Any(ev => string.Equals(ev.EmployeeId, member.EmployeeId, StringComparison.OrdinalIgnoreCase)
                && ev.EventDate == date
                && DoesSlotOverlapWithBufferedBooking(windowStart, windowEnd, ev.EventTime, ev.EventTime.AddHours(1))))
                continue;

            if (bookedConsultations.Any(c => string.Equals(c.EmployeeId, member.EmployeeId, StringComparison.OrdinalIgnoreCase)
                && c.ConsultationDate == date
                && DoesSlotOverlapWithBufferedBooking(windowStart, windowEnd, c.ConsultationTime, c.ConsultationTime.AddHours(1))))
                continue;

            if (member.IsPhotographer) photo++;
            if (member.IsVideographer) video++;
        }

        return (photo, video);
    }

    private static string FormatSlotStatusMessage(bool isBookable, int availPhoto, int availVideo, int totalPhoto, int totalVideo)
    {
        if (!isBookable) return "Unavailable";
        if (totalPhoto == 0 && totalVideo == 0) return "No crew configured";
        if (availPhoto > 0 && availVideo > 0) return "Both Photography and Videography available.";
        if (availPhoto > 0) return "Available for this time: Photography only";
        if (availVideo > 0) return "Available for this time: Videography only";
        return "Not available (crew booked or on leave)";
    }

    public static bool DoesSlotOverlapWithBufferedBooking(TimeOnly slotStart, TimeOnly slotEnd, TimeOnly bookingStart, TimeOnly bookingEnd)
    {
        if (bookingEnd <= bookingStart)
        {
            bookingEnd = bookingStart.AddHours(1);
        }

        // 2 hours advance preparation buffer before bookingStart
        var bufferStart = bookingStart.Hour >= 2 ? bookingStart.AddHours(-2) : TimeOnly.MinValue;

        // 1 hour buffer after bookingEnd
        var bufferEnd = bookingEnd.Hour < 23 ? bookingEnd.AddHours(1) : TimeOnly.MaxValue;

        // Slot [slotStart, slotEnd) overlaps with [bufferStart, bufferEnd) if slotStart < bufferEnd and bufferStart < slotEnd
        return slotStart < bufferEnd && bufferStart < slotEnd;
    }

    private static bool HasAnyAvailableClerk(
        List<ClerkSummary> clerks,
        List<LeaveSummary> leaves,
        List<EventSummary> assignedEvents,
        List<ConsultationSummary> bookedConsultations,
        DateOnly date,
        TimeOnly slotStart)
    {
        var dayName = date.DayOfWeek.ToString();
        var slotEnd = StudioSlots.Find(slotStart)?.End ?? slotStart.AddHours(1);

        foreach (var clerk in clerks)
        {
            // If Office Clerk is on Day Off, that specific date/time is completely unavailable for customer booking negotiations
            if (clerk.DaysOff.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(d => string.Equals(d, dayName, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (leaves.Any(l => string.Equals(l.EmployeeStudioId, clerk.EmployeeId, StringComparison.OrdinalIgnoreCase) && date >= l.StartDate && date <= l.EndDate))
                continue;

            // 2 hours advance preparation buffer and 1 hour post-event buffer
            if (assignedEvents.Any(ev => string.Equals(ev.EmployeeId, clerk.EmployeeId, StringComparison.OrdinalIgnoreCase) 
                && ev.EventDate == date 
                && DoesSlotOverlapWithBufferedBooking(slotStart, slotEnd, ev.EventTime, ev.EventTime.AddHours(1))))
                continue;

            // 2 hours advance preparation buffer and 1 hour post-consultation buffer
            if (bookedConsultations.Any(c => string.Equals(c.EmployeeId, clerk.EmployeeId, StringComparison.OrdinalIgnoreCase) 
                && c.ConsultationDate == date 
                && DoesSlotOverlapWithBufferedBooking(slotStart, slotEnd, c.ConsultationTime, c.ConsultationTime.AddHours(1))))
                continue;

            return true;
        }

        return false;
    }

    private static string? GetFirstAvailableClerkId(
        List<ClerkSummary> clerks,
        List<LeaveSummary> leaves,
        List<EventSummary> assignedEvents,
        List<ConsultationSummary> bookedConsultations,
        DateOnly date,
        TimeOnly slotStart)
    {
        var dayName = date.DayOfWeek.ToString();
        var slotEnd = StudioSlots.Find(slotStart)?.End ?? slotStart.AddHours(1);

        foreach (var clerk in clerks)
        {
            // Skip clerk if date is their scheduled Day Off
            if (clerk.DaysOff.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(d => string.Equals(d, dayName, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (leaves.Any(l => string.Equals(l.EmployeeStudioId, clerk.EmployeeId, StringComparison.OrdinalIgnoreCase) && date >= l.StartDate && date <= l.EndDate))
                continue;

            // 2 hours advance preparation buffer and 1 hour post-event buffer
            if (assignedEvents.Any(ev => string.Equals(ev.EmployeeId, clerk.EmployeeId, StringComparison.OrdinalIgnoreCase) 
                && ev.EventDate == date 
                && DoesSlotOverlapWithBufferedBooking(slotStart, slotEnd, ev.EventTime, ev.EventTime.AddHours(1))))
                continue;

            // 2 hours advance preparation buffer and 1 hour post-consultation buffer
            if (bookedConsultations.Any(c => string.Equals(c.EmployeeId, clerk.EmployeeId, StringComparison.OrdinalIgnoreCase) 
                && c.ConsultationDate == date 
                && DoesSlotOverlapWithBufferedBooking(slotStart, slotEnd, c.ConsultationTime, c.ConsultationTime.AddHours(1))))
                continue;

            return clerk.EmployeeId;
        }

        return null;
    }

    private async Task UpsertOverviewAsync(DateOnly date, decimal revenue, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var overview = await db.BookingOverviews
                .FromSql($"SELECT * FROM dbo.BookingOverview WITH (UPDLOCK, HOLDLOCK) WHERE OverviewDate = {date}")
                .SingleOrDefaultAsync(ct);

            var now = clock.GetUtcNow().UtcDateTime;
            if (overview is null)
            {
                db.BookingOverviews.Add(new BookingOverview { OverviewDate = date, TotalAppointments = 1, TotalRevenue = revenue, LastUpdatedAt = now });
            }
            else
            {
                overview.TotalAppointments++;
                overview.TotalRevenue += revenue;
                overview.LastUpdatedAt = now;
            }

            try
            {
                await db.SaveChangesAsync(ct); // RowVer is checked here
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < MaxOverviewAttempts)
            {
                logger.LogInformation("BookingOverview {Date} changed concurrently; retrying ({Attempt}).", date, attempt);
                foreach (var entry in ex.Entries) entry.State = EntityState.Detached;
            }
        }
    }

    private (List<string> ServiceIds, SlotDefinition Slot, string? Location) Validate(ScheduleBookingRequest request, BookingCustomer customer)
    {
        if (string.IsNullOrWhiteSpace(customer.UserId) || string.IsNullOrWhiteSpace(customer.Email))
            throw new ScheduleValidationException("Your account has no email address on file.");

        if (!request.HasAcceptedDataPolicy)
            throw new ScheduleValidationException("You must read and agree to the PhotoAevum Studio Data Privacy Policy and Terms before confirming your booking.");

        var ids = (request.ServiceIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0) throw new ScheduleValidationException("Select at least one service.");
        if (ids.Count > MaxServices) throw new ScheduleValidationException($"You can select up to {MaxServices} services.");

        var slot = StudioSlots.Find(request.SlotStart) ?? throw new ScheduleValidationException("Invalid time slot.");
        if (!IsBookable(request.Date, slot.Start, StudioNow()))
            throw new ScheduleValidationException("That slot is in the past or outside the booking window.");

        if (!Enum.IsDefined(request.Platform)) throw new ScheduleValidationException("Unsupported meeting platform.");

        var location = string.IsNullOrWhiteSpace(request.EventLocation) ? null : request.EventLocation.Trim();
        if (location?.Length > MaxLocationLength)
            throw new ScheduleValidationException($"Event location must be {MaxLocationLength} characters or fewer.");

        return (ids, slot, location);
    }

    private DateTime StudioNow() => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), _studioZone).DateTime;

    private bool IsBookable(DateOnly date, TimeOnly start, DateTime studioNow)
    {
        var today = DateOnly.FromDateTime(studioNow);
        return date >= today
            && date <= today.AddDays(_options.BookingWindowDays)
            && date.ToDateTime(start) > studioNow;
    }

    private static bool IsSlotUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 } sql
        && sql.Message.Contains("UX_Appointment_Slot", StringComparison.OrdinalIgnoreCase);
}
