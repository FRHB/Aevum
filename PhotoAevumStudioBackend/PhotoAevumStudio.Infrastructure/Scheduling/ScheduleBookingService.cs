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

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT ServicesID AS Id, ServiceName AS Name, ISNULL(ServicesDescription, '') AS [Desc], CAST(ServicePrice AS decimal(18,2)) AS Price
                FROM dbo.Services
                UNION ALL
                SELECT ComboPackageID AS Id, ComboPackageName AS Name, ISNULL(ComboPackageDescription, '') AS [Desc], CAST(ComboPackagePrice AS decimal(18,2)) AS Price
                FROM dbo.ComboPackage
                UNION ALL
                SELECT AdditionalServicesID AS Id, AdditionalServiceName AS Name, ISNULL(AdditionalServicesDescription, '') AS [Desc], CAST(ServicePrice AS decimal(18,2)) AS Price
                FROM dbo.AdditionalServices
                ORDER BY Name ASC;
            ";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0);
                var name = reader.GetString(1);
                var desc = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var price = reader.IsDBNull(3) ? 0m : Convert.ToDecimal(reader.GetValue(3));
                list.Add(new ScheduleServiceDto(id, name, desc, price));
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
            .OrderByDescending(a => a.SlotDate).ThenByDescending(a => a.SlotStart)
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
                Services = a.Services.Select(s => s.ServiceName).ToList()
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new MyBookingDto(
            r.AppointmentId, string.Join(", ", r.Services), r.SlotDate, r.SlotStart, r.SlotEnd,
            r.Status.ToString(), r.TotalPrice, r.Platform, r.MeetingLink, r.EventLocation)).ToList();
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

    private sealed record ClerkSummary(string EmployeeId, string DaysOff, string FullName);
    private sealed record LeaveSummary(string EmployeeStudioId, DateOnly StartDate, DateOnly EndDate);
    private sealed record EventSummary(string EmployeeId, DateOnly EventDate, TimeOnly EventTime);
    private sealed record ConsultationSummary(string EmployeeId, DateOnly ConsultationDate, TimeOnly ConsultationTime);

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
