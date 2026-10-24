using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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

    public async Task<IReadOnlyList<ScheduleServiceDto>> GetServicesAsync(CancellationToken cancellationToken = default) =>
        await db.CatalogServices.AsNoTracking()
            .OrderBy(s => s.ServiceName)
            .Select(s => new ScheduleServiceDto(s.ServicesId, s.ServiceName, s.ServicesDescription, s.ServicePrice))
            .ToListAsync(cancellationToken);

    public async Task<MonthAvailabilityDto> GetMonthAvailabilityAsync(int year, int month, CancellationToken cancellationToken = default)
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

        var now = StudioNow();
        var days = Enumerable.Range(0, last.Day)
            .Select(i => first.AddDays(i))
            .Select(d => new DayAvailabilityDto(d, StudioSlots.All
                .Select(s => new SlotAvailabilityDto(s.Start, s.End, IsBookable(d, s.Start, now) && !taken.Contains((d, s.Start))))
                .ToList()))
            .ToList();

        return new MonthAvailabilityDto(year, month, days);
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

        var services = await db.CatalogServices.AsNoTracking()
            .Where(s => serviceIds.Contains(s.ServicesId))
            .Select(s => new ScheduleServiceDto(s.ServicesId, s.ServiceName, s.ServicesDescription, s.ServicePrice))
            .ToListAsync(cancellationToken);
        if (services.Count != serviceIds.Count)
            throw new ScheduleValidationException("One or more selected services are no longer available.");

        var total = services.Sum(s => s.Price);

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
                    TotalPrice = total,
                    CreatedAt = clock.GetUtcNow().UtcDateTime,
                    Services = services.Select(s => new AppointmentServiceLine
                    {
                        ServicesId = s.ServicesId,
                        ServiceName = s.Name,
                        ServicePrice = s.Price
                    }).ToList()
                };
                db.Appointments.Add(entity);
                await db.SaveChangesAsync(ct);

                await UpsertOverviewAsync(request.Date, total, ct);

                await tx.CommitAsync(ct);
                return entity;
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsSlotUniqueViolation(ex))
        {
            logger.LogWarning("Double-booking race on {Date} {Slot} rejected by UX_Appointment_Slot; transaction rolled back.", request.Date, slot.Start);
            throw new SlotUnavailableException(request.Date, slot.Start);
        }

        logger.LogInformation("Appointment {AppointmentId} booked for {Date} {Slot} by {CustomerId}.",
            appointment.AppointmentId, appointment.SlotDate, appointment.SlotStart, customer.UserId);

        // The booking is committed. Email failure must not undo it; report it instead.
        var emailSent = false;
        try
        {
            await notifications.SendBookingConfirmationAsync(new BookingConfirmation(
                appointment.AppointmentId, customer, appointment.SlotDate, appointment.SlotStart, appointment.SlotEnd,
                services, total, request.Platform, meetingLink, location), CancellationToken.None);
            emailSent = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Appointment {AppointmentId} saved but the confirmation email failed.", appointment.AppointmentId);
        }

        return new ScheduleBookingResultDto(appointment.AppointmentId, appointment.SlotDate, appointment.SlotStart, appointment.SlotEnd,
            services, total, request.Platform, meetingLink, location, emailSent);
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
