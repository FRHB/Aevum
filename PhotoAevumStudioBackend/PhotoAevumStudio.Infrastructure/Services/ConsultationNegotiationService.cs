using System.Net;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Infrastructure.Services;

public class ConsultationNegotiationService : IConsultationNegotiationService
{
    private readonly ApplicationDbContext _db;
    private readonly IGoogleMeetAdapter _meetAdapter;
    private readonly IXenditPaymentService _xenditPaymentService;
    private readonly IEmailService _emailService;
    private readonly XenditSettings _xenditSettings;
    private readonly ILogger<ConsultationNegotiationService> _logger;

    public ConsultationNegotiationService(
        ApplicationDbContext db,
        IGoogleMeetAdapter meetAdapter,
        IXenditPaymentService xenditPaymentService,
        IEmailService emailService,
        IOptions<XenditSettings> xenditOptions,
        ILogger<ConsultationNegotiationService> logger)
    {
        _db = db;
        _meetAdapter = meetAdapter;
        _xenditPaymentService = xenditPaymentService;
        _emailService = emailService;
        _xenditSettings = xenditOptions.Value;
        _logger = logger;
    }

    public async Task<ConsultationNegotiationDto> CreateConsultationAsync(
        CreateConsultationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.HasAcceptedDataPolicy)
        {
            throw new ArgumentException("Client must accept the Data Privacy Policy and Terms before scheduling a consultation.");
        }

        await using var conn = await OpenConnectionAsync(cancellationToken);

        // 1. Get next negotiation ID (NEG-####)
        var negotiationId = await GetNextIdAsync(conn, "dbo.BookingNegotiation", "NegotiationID", "NEG-", cancellationToken);

        // 2. Select Office Clerk via least-recently-assigned round robin
        var (clerkId, clerkName, clerkEmail) = await AssignOfficeClerkAsync(conn, cancellationToken);

        // 3. Provision Google Meet link via adapter
        var meetingLink = await _meetAdapter.GenerateMeetingLinkAsync(
            request.ConsultationDate,
            request.ConsultationTime,
            $"Consultation with {request.CustomerName ?? request.CustomerId}",
            cancellationToken);

        // 4. Look up customer details if not provided
        var custName = request.CustomerName;
        var custEmail = request.CustomerEmail;
        var custPhone = request.CustomerPhone;

        if (string.IsNullOrWhiteSpace(custName) || string.IsNullOrWhiteSpace(custEmail))
        {
            const string custSql = "SELECT CustomerFirstName, CustomerLastName, CustomerEmail, CustomerPhoneNumber FROM dbo.Customer WHERE CustomerID = @CustId;";
            await using var cCmd = new SqlCommand(custSql, conn);
            cCmd.Parameters.AddWithValue("@CustId", request.CustomerId);
            await using var cReader = await cCmd.ExecuteReaderAsync(cancellationToken);
            if (await cReader.ReadAsync(cancellationToken))
            {
                var fn = cReader.IsDBNull(0) ? "" : cReader.GetString(0);
                var ln = cReader.IsDBNull(1) ? "" : cReader.GetString(1);
                custName = $"{fn} {ln}".Trim();
                custEmail = cReader.IsDBNull(2) ? custEmail ?? "" : cReader.GetString(2);
                custPhone = cReader.IsDBNull(3) ? custPhone ?? "" : cReader.GetString(3);
            }
            await cReader.CloseAsync();
        }

        // 5. Insert into dbo.BookingNegotiation
        const string insertSql = @"
            INSERT INTO dbo.BookingNegotiation (
                NegotiationID, EventAppointmentID, CustomerID, EmployeeID,
                ConsultationDate, ConsultationTime, MeetingLink, InquiryNotes,
                NegotiationStatus, CreatedAt
            )
            VALUES (
                @NegId, NULL, @CustId, @EmpId,
                @Date, @Time, @Link, @Notes,
                'Pending_Call', SYSUTCDATETIME()
            );

            IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
            BEGIN
                INSERT INTO dbo.HistoryLog (
                    ActionType, EntityName, EntityID, ActionDetails,
                    NegotiationID, CustomerID, ChangedByType, ChangedByCustomerID, LoggedAt
                )
                VALUES (
                    'NEGOTIATE', 'BookingNegotiation', @NegId,
                    'Customer submitted schedule consultation request. Assigned clerk: ' + @EmpId,
                    @NegId, @CustId, 'Customer', @CustId, SYSUTCDATETIME()
                );
            END;";

        await using var cmd = new SqlCommand(insertSql, conn);
        cmd.Parameters.AddWithValue("@NegId", negotiationId);
        cmd.Parameters.AddWithValue("@CustId", request.CustomerId);
        cmd.Parameters.AddWithValue("@EmpId", clerkId);
        cmd.Parameters.AddWithValue("@Date", request.ConsultationDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@Time", request.ConsultationTime.ToTimeSpan());
        cmd.Parameters.AddWithValue("@Link", meetingLink);
        cmd.Parameters.AddWithValue("@Notes", (object?)request.InquiryNotes ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Consultation {NegotiationId} booked by customer {CustomerId}. Assigned clerk: {ClerkId} ({ClerkName})",
            negotiationId, request.CustomerId, clerkId, clerkName);

        // 6. Send email notification to Customer
        try
        {
            var emailBody = $"""
                <div style="font-family:Arial,sans-serif;color:#1e293b;max-width:600px;margin:0 auto;padding:24px;border:1px solid #e2e8f0;border-radius:12px">
                    <h2 style="color:#0f172a;margin-top:0">Consultation Scheduled with Photo Aevum Studio</h2>
                    <p>Dear {custName},</p>
                    <p>Your online consultation request (<strong>#{negotiationId}</strong>) has been scheduled. Our Office Clerk <strong>{clerkName}</strong> is assigned to assist you with inquiries and service customization.</p>
                    <div style="background:#f8fafc;padding:16px;border-radius:8px;margin:20px 0;border:1px solid #cbd5e1">
                        <p style="margin:4px 0"><strong>Date:</strong> {request.ConsultationDate:dddd, MMMM d, yyyy}</p>
                        <p style="margin:4px 0"><strong>Time:</strong> {request.ConsultationTime:h:mm tt}</p>
                        <p style="margin:4px 0"><strong>Assigned Clerk:</strong> {clerkName}</p>
                        <p style="margin:4px 0"><strong>Platform:</strong> Google Meet</p>
                    </div>
                    <div style="text-align:center;margin:28px 0">
                        <a href="{meetingLink}" style="background:#0f172a;color:#ffffff;padding:12px 24px;border-radius:8px;text-decoration:none;font-weight:bold;display:inline-block">
                            Join Google Meet Consultation
                        </a>
                    </div>
                    <p style="font-size:12px;color:#64748b">Direct Link: <a href="{meetingLink}">{meetingLink}</a></p>
                    <hr style="border:none;border-top:1px solid #e2e8f0;margin:20px 0" />
                    <p style="font-size:12px;color:#94a3b8">Photo Aevum Studio &bull; Transforming moments into timeless art.</p>
                </div>
                """;

            if (!string.IsNullOrWhiteSpace(custEmail))
            {
                await _emailService.SendEmailAsync(custEmail, $"Consultation Scheduled: #{negotiationId} - Photo Aevum Studio", emailBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send consultation confirmation email for {NegotiationId}", negotiationId);
        }

        return new ConsultationNegotiationDto
        {
            NegotiationId = negotiationId,
            CustomerId = request.CustomerId,
            CustomerName = custName ?? "Valued Client",
            CustomerEmail = custEmail ?? "",
            CustomerPhone = custPhone ?? "",
            EmployeeId = clerkId,
            EmployeeName = clerkName,
            EmployeeEmail = clerkEmail,
            ConsultationDate = request.ConsultationDate,
            ConsultationTime = request.ConsultationTime,
            MeetingLink = meetingLink,
            InquiryNotes = request.InquiryNotes ?? "",
            NegotiationStatus = "Pending_Call",
            CreatedAt = DateTime.UtcNow
        };
    }

    public async Task<IReadOnlyList<ConsultationNegotiationDto>> GetNegotiationsAsync(
        string? clerkEmployeeId = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var list = new List<ConsultationNegotiationDto>();
        await using var conn = await OpenConnectionAsync(cancellationToken);

        var sql = @"
            SELECT 
                bn.NegotiationID,
                bn.EventAppointmentID,
                bn.CustomerID,
                COALESCE(c.CustomerFirstName + ' ' + c.CustomerLastName, bn.CustomerID) AS CustomerName,
                ISNULL(c.CustomerEmail, '') AS CustomerEmail,
                ISNULL(c.CustomerPhoneNumber, '') AS CustomerPhone,
                bn.EmployeeID,
                COALESCE(e.EmployeeFirstName + ' ' + e.EmployeeLastName, bn.EmployeeID) AS EmployeeName,
                ISNULL(e.EmployeeEmail, '') AS EmployeeEmail,
                bn.ConsultationDate,
                bn.ConsultationTime,
                ISNULL(bn.MeetingLink, '') AS MeetingLink,
                ISNULL(bn.InquiryNotes, '') AS InquiryNotes,
                bn.NegotiationStatus,
                bn.CreatedAt
            FROM dbo.BookingNegotiation bn
            LEFT JOIN dbo.Customer c ON c.CustomerID = bn.CustomerID
            LEFT JOIN dbo.Employees e ON e.EmployeeID = bn.EmployeeID
            WHERE (@ClerkId IS NULL OR bn.EmployeeID = @ClerkId)
              AND (@Status IS NULL OR bn.NegotiationStatus = @Status)
            ORDER BY bn.CreatedAt DESC;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ClerkId", (object?)clerkEmployeeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ConsultationNegotiationDto
            {
                NegotiationId = reader.GetString(0),
                EventAppointmentId = reader.IsDBNull(1) ? null : reader.GetString(1),
                CustomerId = reader.GetString(2),
                CustomerName = reader.GetString(3),
                CustomerEmail = reader.GetString(4),
                CustomerPhone = reader.GetString(5),
                EmployeeId = reader.GetString(6),
                EmployeeName = reader.GetString(7),
                EmployeeEmail = reader.GetString(8),
                ConsultationDate = DateOnly.FromDateTime(reader.GetDateTime(9)),
                ConsultationTime = TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(10)),
                MeetingLink = reader.GetString(11),
                InquiryNotes = reader.GetString(12),
                NegotiationStatus = reader.GetString(13),
                CreatedAt = reader.GetDateTime(14)
            });
        }

        return list;
    }

    public async Task<ConsultationNegotiationDto?> GetNegotiationByIdAsync(
        string negotiationId,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);

        var sql = @"
            SELECT 
                bn.NegotiationID,
                bn.EventAppointmentID,
                bn.CustomerID,
                COALESCE(c.CustomerFirstName + ' ' + c.CustomerLastName, bn.CustomerID) AS CustomerName,
                ISNULL(c.CustomerEmail, '') AS CustomerEmail,
                ISNULL(c.CustomerPhoneNumber, '') AS CustomerPhone,
                bn.EmployeeID,
                COALESCE(e.EmployeeFirstName + ' ' + e.EmployeeLastName, bn.EmployeeID) AS EmployeeName,
                ISNULL(e.EmployeeEmail, '') AS EmployeeEmail,
                bn.ConsultationDate,
                bn.ConsultationTime,
                ISNULL(bn.MeetingLink, '') AS MeetingLink,
                ISNULL(bn.InquiryNotes, '') AS InquiryNotes,
                bn.NegotiationStatus,
                bn.CreatedAt
            FROM dbo.BookingNegotiation bn
            LEFT JOIN dbo.Customer c ON c.CustomerID = bn.CustomerID
            LEFT JOIN dbo.Employees e ON e.EmployeeID = bn.EmployeeID
            WHERE bn.NegotiationID = @NegId;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@NegId", negotiationId);

        ConsultationNegotiationDto? dto = null;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            dto = new ConsultationNegotiationDto
            {
                NegotiationId = reader.GetString(0),
                EventAppointmentId = reader.IsDBNull(1) ? null : reader.GetString(1),
                CustomerId = reader.GetString(2),
                CustomerName = reader.GetString(3),
                CustomerEmail = reader.GetString(4),
                CustomerPhone = reader.GetString(5),
                EmployeeId = reader.GetString(6),
                EmployeeName = reader.GetString(7),
                EmployeeEmail = reader.GetString(8),
                ConsultationDate = DateOnly.FromDateTime(reader.GetDateTime(9)),
                ConsultationTime = TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(10)),
                MeetingLink = reader.GetString(11),
                InquiryNotes = reader.GetString(12),
                NegotiationStatus = reader.GetString(13),
                CreatedAt = reader.GetDateTime(14)
            };
        }
        await reader.CloseAsync();

        if (dto != null && !string.IsNullOrWhiteSpace(dto.EventAppointmentId))
        {
            dto.Appointment = await GetAppointmentByIdAsync(dto.EventAppointmentId, cancellationToken);
        }

        return dto;
    }

    public async Task<bool> UpdateNegotiationOutcomeAsync(
        string negotiationId,
        UpdateNegotiationOutcomeRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);

        const string sql = @"
            UPDATE dbo.BookingNegotiation
            SET NegotiationStatus = @Status,
                ConsultationDate = CASE WHEN @NewDate IS NOT NULL THEN @NewDate ELSE ConsultationDate END,
                ConsultationTime = CASE WHEN @NewTime IS NOT NULL THEN @NewTime ELSE ConsultationTime END,
                InquiryNotes = CASE 
                    WHEN @Notes IS NOT NULL AND LEN(@Notes) > 0 
                    THEN ISNULL(InquiryNotes, '') + CHAR(13) + CHAR(10) + '[Outcome Note]: ' + @Notes 
                    ELSE InquiryNotes 
                END
            WHERE NegotiationID = @NegId;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Status", request.NegotiationStatus);
        cmd.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@NewDate", request.NewConsultationDate.HasValue ? request.NewConsultationDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        cmd.Parameters.AddWithValue("@NewTime", request.NewConsultationTime.HasValue ? request.NewConsultationTime.Value.ToTimeSpan() : DBNull.Value);
        cmd.Parameters.AddWithValue("@NegId", negotiationId);

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);

        if (rows > 0 && request.NegotiationStatus.Equals("Rescheduled", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var details = await GetNegotiationByIdAsync(negotiationId, cancellationToken);
                if (details != null && !string.IsNullOrWhiteSpace(details.CustomerEmail))
                {
                    var clientName = string.IsNullOrWhiteSpace(details.CustomerName) ? "Valued Client" : details.CustomerName;
                    var clerkName = string.IsNullOrWhiteSpace(details.EmployeeName) ? "Office Clerk" : details.EmployeeName;
                    var meetLink = details.MeetingLink;
                    var notesText = !string.IsNullOrWhiteSpace(request.Notes)
                        ? $"<p style=\"margin:8px 0;font-size:13px;color:#475569\"><strong>Clerk / Reschedule Notes:</strong> {WebUtility.HtmlEncode(request.Notes)}</p>"
                        : "";

                    var meetLinkHtml = string.IsNullOrWhiteSpace(meetLink) ? "" : $@"
                    <div style=""text-align:center;margin:28px 0"">
                        <a href=""{meetLink}"" style=""background:#0f172a;color:#ffffff;padding:12px 24px;border-radius:8px;text-decoration:none;font-weight:bold;display:inline-block"">
                            Join Google Meet Consultation
                        </a>
                    </div>
                    <p style=""font-size:12px;color:#64748b"">Direct Link: <a href=""{meetLink}"">{meetLink}</a></p>";

                    var rescheduleBody = $@"
<div style=""font-family:Arial,sans-serif;color:#1e293b;max-width:600px;margin:0 auto;padding:24px;border:1px solid #e2e8f0;border-radius:12px"">
    <h2 style=""color:#0f172a;margin-top:0"">Consultation Rescheduled</h2>
    <p>Dear {WebUtility.HtmlEncode(clientName)},</p>
    <p>Your online consultation (<strong>#{negotiationId}</strong>) with Photo Aevum Studio has been rescheduled.</p>
    <div style=""background:#f8fafc;padding:16px;border-radius:8px;margin:20px 0;border:1px solid #cbd5e1"">
        <p style=""margin:4px 0""><strong>New Date:</strong> {details.ConsultationDate:dddd, MMMM d, yyyy}</p>
        <p style=""margin:4px 0""><strong>New Time:</strong> {DateTime.Today.Add(details.ConsultationTime.ToTimeSpan()):h:mm tt}</p>
        <p style=""margin:4px 0""><strong>Assigned Clerk:</strong> {WebUtility.HtmlEncode(clerkName)}</p>
        <p style=""margin:4px 0""><strong>Platform:</strong> Google Meet</p>
        {notesText}
    </div>
    {meetLinkHtml}
    <hr style=""border:none;border-top:1px solid #e2e8f0;margin:20px 0"" />
    <p style=""font-size:12px;color:#94a3b8"">Photo Aevum Studio &bull; Transforming moments into timeless art.</p>
</div>";

                    await _emailService.SendEmailAsync(
                        details.CustomerEmail,
                        $"Consultation Rescheduled: #{negotiationId} - Photo Aevum Studio",
                        rescheduleBody);
                    _logger.LogInformation("Reschedule confirmation email sent to {Email} for negotiation {NegotiationId}", details.CustomerEmail, negotiationId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispatch consultation reschedule notification email for {NegotiationId}", negotiationId);
            }
        }

        return rows > 0;
    }

    public async Task<EventAppointmentDto> CreateEventAppointmentFromNegotiationAsync(
        CreateEventAppointmentFromNegotiationRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);

        // 1. Calculate pricing from selected services
        var serviceLines = new List<EventServiceLineDto>();
        if (request.ServiceIds.Count > 0)
        {
            var pList = string.Join(",", request.ServiceIds.Select((_, i) => $"@s{i}"));
            var qry = $"SELECT ServicesID, ServiceName, ServicePrice FROM dbo.Services WHERE ServicesID IN ({pList});";
            await using var sCmd = new SqlCommand(qry, conn);
            for (var i = 0; i < request.ServiceIds.Count; i++)
            {
                sCmd.Parameters.AddWithValue($"@s{i}", request.ServiceIds[i]);
            }
            await using var sReader = await sCmd.ExecuteReaderAsync(cancellationToken);
            while (await sReader.ReadAsync(cancellationToken))
            {
                serviceLines.Add(new EventServiceLineDto
                {
                    ServicesId = sReader.GetString(0),
                    ServiceName = sReader.GetString(1),
                    ServicePrice = sReader.GetDecimal(2)
                });
            }
            await sReader.CloseAsync();
        }

        var totalAmount = serviceLines.Sum(s => s.ServicePrice);
        if (totalAmount <= 0) totalAmount = 5000.00m; // reasonable fallback studio base rate if services unpriced

        var downpaymentPercentage = _xenditSettings.DownpaymentPercentage <= 0 ? 0.15m : _xenditSettings.DownpaymentPercentage;
        var downpaymentAmount = Math.Round(totalAmount * downpaymentPercentage, 2);
        var finalBalanceAmount = totalAmount - downpaymentAmount;

        var holdMinutes = _xenditSettings.HoldDurationMinutes <= 0 ? 30 : _xenditSettings.HoldDurationMinutes;
        var holdExpiresAt = DateTime.UtcNow.AddMinutes(holdMinutes);

        // 2. Generate EVT-#### ID
        var appointmentId = await GetNextIdAsync(conn, "dbo.EventAppointment", "EventAppointmentID", "EVT-", cancellationToken);

        // 3. Lookup customer name & email for checkout & notifications
        var custName = "Valued Client";
        var custEmail = "";
        {
            const string custSql = "SELECT CustomerFirstName, CustomerLastName, CustomerEmail FROM dbo.Customer WHERE CustomerID = @CustId;";
            await using var cCmd = new SqlCommand(custSql, conn);
            cCmd.Parameters.AddWithValue("@CustId", request.CustomerId);
            await using var cReader = await cCmd.ExecuteReaderAsync(cancellationToken);
            if (await cReader.ReadAsync(cancellationToken))
            {
                var fn = cReader.IsDBNull(0) ? "" : cReader.GetString(0);
                var ln = cReader.IsDBNull(1) ? "" : cReader.GetString(1);
                custName = $"{fn} {ln}".Trim();
                custEmail = cReader.IsDBNull(2) ? "" : cReader.GetString(2);
            }
        }

        // 4. Generate Xendit 25% Downpayment Invoice (with e-wallets GCash, Maya enabled)
        var successUrl = $"https://localhost:7128/my/bookings?checkout_success={appointmentId}";
        var cancelUrl = $"https://localhost:7128/my/bookings?checkout_cancelled={appointmentId}";

        var invoiceResult = await _xenditPaymentService.CreateInvoiceAsync(
            appointmentId,
            $"Photo Aevum Studio - 15% Reservation Deposit for {request.EventName}",
            downpaymentAmount,
            custName,
            custEmail,
            successUrl,
            cancelUrl,
            cancellationToken);

        // 5. Insert into EventAppointment and assign staff & equipment
        await using var tx = conn.BeginTransaction();
        try
        {
            const string insertEvtSql = @"
                INSERT INTO dbo.EventAppointment (
                    EventAppointmentID, CustomerID, EventName, EventDescription,
                    EventDate, EventTime, EventStatus, TotalAmount, DownpaymentAmount,
                    FinalBalanceAmount, DownpaymentStatus, HoldExpiresAt,
                    XenditInvoiceId, XenditExternalId, XenditInvoiceUrl, CreatedAt
                )
                VALUES (
                    @EvtId, @CustId, @Name, @Desc,
                    @Date, @Time, 'Awaiting_Downpayment', @Total, @Downpayment,
                    @FinalBal, 'Pending', @ExpiresAt,
                    @InvoiceId, @ExternalId, @InvoiceUrl, SYSUTCDATETIME()
                );";

            await using (var cmd = new SqlCommand(insertEvtSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@EvtId", appointmentId);
                cmd.Parameters.AddWithValue("@CustId", request.CustomerId);
                cmd.Parameters.AddWithValue("@Name", request.EventName);
                cmd.Parameters.AddWithValue("@Desc", (object?)request.EventDescription ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Date", request.EventDate.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.AddWithValue("@Time", request.EventTime.ToTimeSpan());
                cmd.Parameters.AddWithValue("@Total", totalAmount);
                cmd.Parameters.AddWithValue("@Downpayment", downpaymentAmount);
                cmd.Parameters.AddWithValue("@FinalBal", finalBalanceAmount);
                cmd.Parameters.AddWithValue("@ExpiresAt", holdExpiresAt);
                cmd.Parameters.AddWithValue("@InvoiceId", (object?)invoiceResult.InvoiceId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ExternalId", (object?)invoiceResult.ExternalId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@InvoiceUrl", (object?)invoiceResult.InvoiceUrl ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Assign staff in EventEmployee
            if (request.EmployeeIds.Count > 0)
            {
                foreach (var empId in request.EmployeeIds.Distinct())
                {
                    const string insEmp = @"
                        IF NOT EXISTS (SELECT 1 FROM dbo.EventEmployee WHERE EventAppointmentID = @EvtId AND EmployeeID = @EmpId)
                        BEGIN
                            INSERT INTO dbo.EventEmployee (EventAppointmentID, EmployeeID, IsTeamLeader)
                            VALUES (@EvtId, @EmpId, @IsTL);
                        END";
                    await using var eCmd = new SqlCommand(insEmp, conn, tx);
                    eCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                    eCmd.Parameters.AddWithValue("@EmpId", empId);
                    eCmd.Parameters.AddWithValue("@IsTL", empId == request.TeamLeaderEmployeeId ? 1 : 0);
                    await eCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            // Assign equipment in EventEquipment
            if (request.EquipmentIds.Count > 0)
            {
                foreach (var eqId in request.EquipmentIds.Distinct())
                {
                    const string insEq = @"
                        IF NOT EXISTS (SELECT 1 FROM dbo.EventEquipment WHERE EventAppointmentID = @EvtId AND EquipmentID = @EqId)
                        BEGIN
                            INSERT INTO dbo.EventEquipment (EventAppointmentID, EquipmentID)
                            VALUES (@EvtId, @EqId);
                        END";
                    await using var eqCmd = new SqlCommand(insEq, conn, tx);
                    eqCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                    eqCmd.Parameters.AddWithValue("@EqId", eqId);
                    await eqCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            // Link to BookingNegotiation
            if (!string.IsNullOrWhiteSpace(request.NegotiationId))
            {
                const string updateNeg = @"
                    UPDATE dbo.BookingNegotiation
                    SET EventAppointmentID = @EvtId,
                        NegotiationStatus = 'Deal_Won'
                    WHERE NegotiationID = @NegId;";
                await using var nCmd = new SqlCommand(updateNeg, conn, tx);
                nCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                nCmd.Parameters.AddWithValue("@NegId", request.NegotiationId);
                await nCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        // 6. Send transactional notification email to customer
        try
        {
            var emailBody = $"""
                <div style="font-family:Arial,sans-serif;color:#1e293b;max-width:600px;margin:0 auto;padding:24px;border:1px solid #e2e8f0;border-radius:12px">
                    <h2 style="color:#0f172a;margin-top:0">Action Required: 25% Deposit for {request.EventName}</h2>
                    <p>Dear {custName},</p>
                    <p>Your event photoshoot appointment (<strong>#{appointmentId}</strong>) has been set up following your consultation. To confirm your reservation, please settle the 25% downpayment within <strong>30 minutes</strong>.</p>
                    <div style="background:#f8fafc;padding:16px;border-radius:8px;margin:20px 0;border:1px solid #cbd5e1">
                        <p style="margin:4px 0"><strong>Event:</strong> {request.EventName}</p>
                        <p style="margin:4px 0"><strong>Schedule:</strong> {request.EventDate:dddd, MMMM d, yyyy} at {request.EventTime:h:mm tt}</p>
                        <p style="margin:4px 0"><strong>Venue:</strong> {(string.IsNullOrWhiteSpace(request.EventLocation) ? "Studio Main Hall" : request.EventLocation)}</p>
                        <p style="margin:4px 0"><strong>Total Package:</strong> ₱{totalAmount:N2}</p>
                        <p style="margin:4px 0;font-size:16px;color:#047857"><strong>25% Downpayment Due: ₱{downpaymentAmount:N2}</strong></p>
                        <p style="margin:4px 0;color:#b91c1c;font-size:13px"><strong>Reservation Hold Expires:</strong> {holdExpiresAt.ToLocalTime():h:mm tt} (in 30 mins)</p>
                    </div>
                    <div style="text-align:center;margin:28px 0">
                        <a href="{invoiceResult.InvoiceUrl}" style="background:#047857;color:#ffffff;padding:14px 28px;border-radius:8px;text-decoration:none;font-weight:bold;font-size:15px;display:inline-block">
                            Pay 25% Deposit Now (Xendit: GCash, Maya, Card, QR Ph)
                        </a>
                    </div>
                    <p style="font-size:12px;color:#64748b">If the timer expires, you may request an extension directly from your Aevum Dashboard.</p>
                </div>
                """;

            if (!string.IsNullOrWhiteSpace(custEmail))
            {
                await _emailService.SendEmailAsync(custEmail, $"Payment Required: 25% Deposit for {request.EventName} (#{appointmentId})", emailBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send downpayment email for {AppointmentId}", appointmentId);
        }

        return (await GetAppointmentByIdAsync(appointmentId, cancellationToken))!;
    }

    public async Task<EventAppointmentDto?> GetAppointmentByIdAsync(
        string appointmentId,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT 
                ea.EventAppointmentID,
                bn.NegotiationID,
                ea.CustomerID,
                COALESCE(c.CustomerFirstName + ' ' + c.CustomerLastName, ea.CustomerID) AS CustomerName,
                ISNULL(c.CustomerEmail, '') AS CustomerEmail,
                ea.EventName,
                ISNULL(ea.EventDescription, '') AS EventDescription,
                ea.EventDate,
                ea.EventTime,
                ea.EventStatus,
                ea.TotalAmount,
                ea.DownpaymentAmount,
                ea.FinalBalanceAmount,
                ea.DownpaymentStatus,
                ea.HoldExpiresAt,
                ea.XenditInvoiceId,
                ea.XenditExternalId,
                ea.XenditInvoiceUrl,
                ea.CreatedAt
            FROM dbo.EventAppointment ea
            LEFT JOIN dbo.Customer c ON c.CustomerID = ea.CustomerID
            LEFT JOIN dbo.BookingNegotiation bn ON bn.EventAppointmentID = ea.EventAppointmentID
            WHERE ea.EventAppointmentID = @EvtId;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@EvtId", appointmentId);

        EventAppointmentDto? dto = null;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var invoiceId = reader.IsDBNull(15) ? null : reader.GetString(15);
            var externalId = reader.IsDBNull(16) ? null : reader.GetString(16);
            var invoiceUrl = reader.IsDBNull(17) ? null : reader.GetString(17);
            if (string.IsNullOrWhiteSpace(invoiceUrl) && !string.IsNullOrWhiteSpace(invoiceId))
            {
                invoiceUrl = $"{_xenditSettings.InvoiceBaseUrl.TrimEnd('/')}/{invoiceId}";
            }

            dto = new EventAppointmentDto
            {
                EventAppointmentId = reader.GetString(0),
                NegotiationId = reader.IsDBNull(1) ? null : reader.GetString(1),
                CustomerId = reader.GetString(2),
                CustomerName = reader.GetString(3),
                CustomerEmail = reader.GetString(4),
                EventName = reader.GetString(5),
                EventDescription = reader.GetString(6),
                EventDate = DateOnly.FromDateTime(reader.GetDateTime(7)),
                EventTime = TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(8)),
                EventStatus = reader.GetString(9),
                TotalAmount = reader.GetDecimal(10),
                DownpaymentAmount = reader.GetDecimal(11),
                FinalBalanceAmount = reader.GetDecimal(12),
                DownpaymentStatus = reader.GetString(13),
                HoldExpiresAt = reader.IsDBNull(14) ? null : reader.GetDateTime(14),
                XenditInvoiceId = invoiceId,
                XenditExternalId = externalId,
                XenditInvoiceUrl = invoiceUrl,
                CreatedAt = reader.GetDateTime(18)
            };
        }
        await reader.CloseAsync();

        if (dto == null) return null;

        // Staff
        const string empSql = @"
            SELECT ee.EmployeeID, 
                   COALESCE(e.EmployeeFirstName + ' ' + e.EmployeeLastName, ee.EmployeeID) AS EmployeeName,
                   ISNULL(s.SpecializationName, 'Staff') AS Specialization,
                   ee.IsTeamLeader
            FROM dbo.EventEmployee ee
            LEFT JOIN dbo.Employees e ON e.EmployeeID = ee.EmployeeID
            LEFT JOIN dbo.EmployeeSpecialization es ON es.EmployeeID = ee.EmployeeID
            LEFT JOIN dbo.Specialization s ON s.SpecializationID = es.SpecializationID
            WHERE ee.EventAppointmentID = @EvtId;";
        await using var empCmd = new SqlCommand(empSql, conn);
        empCmd.Parameters.AddWithValue("@EvtId", appointmentId);
        await using var empReader = await empCmd.ExecuteReaderAsync(cancellationToken);
        while (await empReader.ReadAsync(cancellationToken))
        {
            dto.AssignedEmployees.Add(new AssignedEmployeeDto
            {
                EmployeeId = empReader.GetString(0),
                EmployeeName = empReader.GetString(1),
                Specialization = empReader.GetString(2),
                IsTeamLeader = empReader.GetBoolean(3)
            });
        }
        await empReader.CloseAsync();

        // Equipment
        const string eqSql = @"
            SELECT eq.EquipmentID, 
                   ISNULL(e.EquipmentName, eq.EquipmentID) AS EquipmentName,
                   ISNULL(et.TypeName, 'Equipment') AS EquipmentType
            FROM dbo.EventEquipment eq
            LEFT JOIN dbo.Equipment e ON e.EquipmentID = eq.EquipmentID
            LEFT JOIN dbo.EquipmentType et ON et.EquipmentTypeID = e.EquipmentTypeID
            WHERE eq.EventAppointmentID = @EvtId;";
        await using var eqCmd = new SqlCommand(eqSql, conn);
        eqCmd.Parameters.AddWithValue("@EvtId", appointmentId);
        await using var eqReader = await eqCmd.ExecuteReaderAsync(cancellationToken);
        while (await eqReader.ReadAsync(cancellationToken))
        {
            dto.AssignedEquipment.Add(new AssignedEquipmentDto
            {
                EquipmentId = eqReader.GetString(0),
                EquipmentName = eqReader.GetString(1),
                EquipmentType = eqReader.GetString(2)
            });
        }
        await eqReader.CloseAsync();

        // Overtime charges
        const string otSql = @"
            SELECT OvertimeID, EventAppointmentID, ExtraHours, HourlyRate, SurchargeAmount, Notes, LoggedByEmployeeID, CreatedAt
            FROM dbo.EventOvertimeCharges
            WHERE EventAppointmentID = @EvtId;";
        await using var otCmd = new SqlCommand(otSql, conn);
        otCmd.Parameters.AddWithValue("@EvtId", appointmentId);
        await using var otReader = await otCmd.ExecuteReaderAsync(cancellationToken);
        while (await otReader.ReadAsync(cancellationToken))
        {
            dto.OvertimeCharges.Add(new EventOvertimeDto
            {
                OvertimeId = otReader.GetInt32(0),
                EventAppointmentId = otReader.GetString(1),
                ExtraHours = otReader.GetDecimal(2),
                HourlyRate = otReader.GetDecimal(3),
                SurchargeAmount = otReader.GetDecimal(4),
                Notes = otReader.IsDBNull(5) ? null : otReader.GetString(5),
                LoggedByEmployeeId = otReader.GetString(6),
                CreatedAt = otReader.GetDateTime(7)
            });
        }
        await otReader.CloseAsync();

        // Transactions
        const string txSql = @"
            SELECT PaymentTransactionID, EventAppointmentID, PaymentType, PaymentMethod, Amount, Status, ExternalReference, ReceivedByEmployeeID, CreatedAt
            FROM dbo.PaymentTransactions
            WHERE EventAppointmentID = @EvtId
            ORDER BY CreatedAt DESC;";
        await using var txCmd = new SqlCommand(txSql, conn);
        txCmd.Parameters.AddWithValue("@EvtId", appointmentId);
        await using var txReader = await txCmd.ExecuteReaderAsync(cancellationToken);
        while (await txReader.ReadAsync(cancellationToken))
        {
            dto.Transactions.Add(new PaymentTransactionDto
            {
                TransactionId = txReader.GetString(0),
                EventAppointmentId = txReader.GetString(1),
                PaymentType = txReader.GetString(2),
                PaymentMethod = txReader.GetString(3),
                Amount = txReader.GetDecimal(4),
                Status = txReader.GetString(5),
                ExternalReference = txReader.IsDBNull(6) ? null : txReader.GetString(6),
                ReceivedByEmployeeId = txReader.IsDBNull(7) ? null : txReader.GetString(7),
                CreatedAt = txReader.GetDateTime(8)
            });
        }

        return dto;
    }

    public async Task<IReadOnlyList<EventAppointmentDto>> GetAppointmentsAsync(
        string? customerId = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var list = new List<EventAppointmentDto>();
        await using var conn = await OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT ea.EventAppointmentID
            FROM dbo.EventAppointment ea
            WHERE (@CustId IS NULL OR ea.CustomerID = @CustId)
              AND (@Status IS NULL OR ea.EventStatus = @Status)
            ORDER BY ea.EventDate DESC, ea.EventTime DESC;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CustId", (object?)customerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);

        var ids = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetString(0));
        }
        await reader.CloseAsync();

        foreach (var id in ids)
        {
            var appt = await GetAppointmentByIdAsync(id, cancellationToken);
            if (appt != null) list.Add(appt);
        }

        return list;
    }

    public async Task<EventAppointmentDto> VerifyDownpaymentAsync(
        string appointmentId,
        string? invoiceId = null,
        CancellationToken cancellationToken = default)
    {
        var appt = await GetAppointmentByIdAsync(appointmentId, cancellationToken);
        if (appt == null)
            throw new KeyNotFoundException($"Event appointment {appointmentId} not found.");

        var invId = invoiceId ?? appt.XenditInvoiceId ?? $"inv_mock_{appointmentId}";
        var statusResult = await _xenditPaymentService.CheckPaymentStatusAsync(invId, cancellationToken);

        if (statusResult.Status.Equals("paid", StringComparison.OrdinalIgnoreCase) ||
            statusResult.Status.Equals("settled", StringComparison.OrdinalIgnoreCase))
        {
            await using var conn = await OpenConnectionAsync(cancellationToken);

            var txnId = await GetNextIdAsync(conn, "dbo.PaymentTransactions", "PaymentTransactionID", "TXN-", cancellationToken);

            await using var tx = conn.BeginTransaction();
            try
            {
                const string updateSql = @"
                    UPDATE dbo.EventAppointment
                    SET EventStatus = 'Confirmed',
                        DownpaymentStatus = 'Paid'
                    WHERE EventAppointmentID = @EvtId;

                    INSERT INTO dbo.PaymentTransactions (
                        PaymentTransactionID, EventAppointmentID, PaymentType,
                        PaymentMethod, Amount, Status, ExternalReference,
                        XenditInvoiceId, XenditPaymentId, XenditPaymentChannel, CreatedAt
                    )
                    VALUES (
                        @TxnId, @EvtId, 'Downpayment_25',
                        @Method, @Amount, 'Paid', @Ref,
                        @XenditInvId, @XenditPayId, @XenditChannel, SYSUTCDATETIME()
                    );

                    IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                    BEGIN
                        INSERT INTO dbo.HistoryLog (
                            ActionType, EntityName, EntityID, ActionDetails,
                            EventAppointmentID, ChangedByType, LoggedAt
                        )
                        VALUES (
                            'STATUS_CHANGE', 'EventAppointment', @EvtId,
                            '25% Downpayment verified and paid via Xendit. Appointment confirmed.',
                            @EvtId, 'System', SYSUTCDATETIME()
                        );
                    END;";

                var paymentMethod = !string.IsNullOrWhiteSpace(statusResult.PaymentChannel)
                    ? $"Xendit_{statusResult.PaymentChannel}"
                    : (statusResult.PaymentMethod ?? "Xendit");

                await using var cmd = new SqlCommand(updateSql, conn, tx);
                cmd.Parameters.AddWithValue("@TxnId", txnId);
                cmd.Parameters.AddWithValue("@EvtId", appointmentId);
                cmd.Parameters.AddWithValue("@Method", paymentMethod);
                cmd.Parameters.AddWithValue("@Amount", appt.DownpaymentAmount);
                cmd.Parameters.AddWithValue("@Ref", statusResult.InvoiceId);
                cmd.Parameters.AddWithValue("@XenditInvId", (object?)statusResult.InvoiceId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@XenditPayId", (object?)statusResult.InvoiceId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@XenditChannel", (object?)statusResult.PaymentChannel ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }

            // Sync with assigned employee calendars and notify customer
            try
            {
                var body = $"""
                    <div style="font-family:Arial,sans-serif;color:#1e293b;max-width:600px;margin:0 auto;padding:24px;border:1px solid #e2e8f0;border-radius:12px">
                        <h2 style="color:#047857;margin-top:0">&#10004; Downpayment Confirmed - You're Booked!</h2>
                        <p>Dear {appt.CustomerName},</p>
                        <p>We have successfully received your 25% downpayment of <strong>₱{appt.DownpaymentAmount:N2}</strong> for <strong>{appt.EventName}</strong> (#{appointmentId}).</p>
                        <div style="background:#f0fdf4;padding:16px;border-radius:8px;margin:20px 0;border:1px solid #bbf7d0">
                            <p style="margin:4px 0"><strong>Date:</strong> {appt.EventDate:dddd, MMMM d, yyyy}</p>
                            <p style="margin:4px 0"><strong>Time:</strong> {appt.EventTime:h:mm tt}</p>
                            <p style="margin:4px 0"><strong>Status:</strong> <span style="color:#047857;font-weight:bold">Confirmed &amp; Calendar Locked</span></p>
                            <p style="margin:4px 0"><strong>Total Package:</strong> ₱{appt.TotalAmount:N2}</p>
                            <p style="margin:4px 0"><strong>Remaining Balance (Due on event day):</strong> ₱{appt.FinalBalanceAmount:N2}</p>
                        </div>
                        <p>Our creative team will prepare the necessary equipment and reach out prior to the shoot date.</p>
                    </div>
                    """;

                if (!string.IsNullOrWhiteSpace(appt.CustomerEmail))
                {
                    await _emailService.SendEmailAsync(appt.CustomerEmail, $"Downpayment Confirmed: #{appointmentId} - Photo Aevum Studio", body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send downpayment confirmation email for {AppointmentId}", appointmentId);
            }
        }

        return (await GetAppointmentByIdAsync(appointmentId, cancellationToken))!;
    }

    public async Task<bool> RequestExtensionAsync(
        string appointmentId,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);

        const string sql = @"
            UPDATE dbo.EventAppointment
            SET HoldExpiresAt = DATEADD(minute, 30, SYSUTCDATETIME())
            WHERE EventAppointmentID = @EvtId AND DownpaymentStatus = 'Pending';";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@EvtId", appointmentId);
        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("30-minute extension requested and granted for appointment {AppointmentId}", appointmentId);
        return rows > 0;
    }

    public async Task<bool> HandlePaymentExpiredAsync(
        string appointmentId,
        string? invoiceId = null,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);

        const string updateSql = @"
            UPDATE dbo.EventAppointment
            SET EventStatus = 'Expired',
                DownpaymentStatus = 'Expired'
            WHERE EventAppointmentID = @EvtId
              AND DownpaymentStatus = 'Pending';

            IF @@ROWCOUNT > 0 AND OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
            BEGIN
                INSERT INTO dbo.HistoryLog (
                    ActionType, EntityName, EntityID, ActionDetails,
                    EventAppointmentID, ChangedByType, LoggedAt
                )
                VALUES (
                    'STATUS_CHANGE', 'EventAppointment', @EvtId,
                    'Xendit payment invoice expired. Reservation hold released.',
                    @EvtId, 'System', SYSUTCDATETIME()
                );
            END;";

        await using var cmd = new SqlCommand(updateSql, conn);
        cmd.Parameters.AddWithValue("@EvtId", appointmentId);
        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Marked appointment {AppointmentId} as Expired via Xendit webhook callback (Rows: {Rows})", appointmentId, rows);
        return rows > 0;
    }

    private static volatile bool _schemaEnsured = false;
    private static readonly SemaphoreSlim _schemaLock = new(1, 1);

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var connStr = _db.Database.GetConnectionString();
        var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);
        await EnsureSchemaOnceAsync(conn, ct);
        return conn;
    }

    private static async Task EnsureSchemaOnceAsync(SqlConnection conn, CancellationToken ct)
    {
        if (_schemaEnsured) return;
        await _schemaLock.WaitAsync(ct);
        try
        {
            if (_schemaEnsured) return;
            await EnsureSchemaAsync(conn, ct);
            _schemaEnsured = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    private static async Task EnsureSchemaAsync(SqlConnection conn, CancellationToken ct)
    {
        const string schemaSql = @"
            -- 1. Ensure dbo.BookingNegotiation table & columns
            IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.BookingNegotiation ALTER COLUMN EventAppointmentID nvarchar(30) NULL;

                IF COL_LENGTH(N'dbo.BookingNegotiation', N'ConsultationDate') IS NULL
                    ALTER TABLE dbo.BookingNegotiation ADD ConsultationDate date NULL;

                IF COL_LENGTH(N'dbo.BookingNegotiation', N'ConsultationTime') IS NULL
                    ALTER TABLE dbo.BookingNegotiation ADD ConsultationTime time NULL;

                IF COL_LENGTH(N'dbo.BookingNegotiation', N'MeetingLink') IS NULL
                    ALTER TABLE dbo.BookingNegotiation ADD MeetingLink nvarchar(500) NULL;

                IF COL_LENGTH(N'dbo.BookingNegotiation', N'InquiryNotes') IS NULL
                    ALTER TABLE dbo.BookingNegotiation ADD InquiryNotes nvarchar(1000) NULL;

                IF COL_LENGTH(N'dbo.BookingNegotiation', N'NegotiationStatus') IS NULL
                    ALTER TABLE dbo.BookingNegotiation ADD NegotiationStatus nvarchar(50) NOT NULL CONSTRAINT DF_BookingNegotiation_Status DEFAULT 'Pending_Call';

                IF COL_LENGTH(N'dbo.BookingNegotiation', N'CreatedAt') IS NULL
                    ALTER TABLE dbo.BookingNegotiation ADD CreatedAt datetime2 NOT NULL CONSTRAINT DF_BookingNegotiation_Created DEFAULT SYSUTCDATETIME();
            END
            ELSE
            BEGIN
                CREATE TABLE dbo.BookingNegotiation (
                    NegotiationID nvarchar(30) NOT NULL PRIMARY KEY,
                    EventAppointmentID nvarchar(30) NULL,
                    CustomerID nvarchar(30) NOT NULL,
                    EmployeeID nvarchar(30) NOT NULL, 
                    ConsultationDate date NULL,
                    ConsultationTime time NULL,
                    MeetingLink nvarchar(500) NULL,
                    InquiryNotes nvarchar(1000) NULL,
                    NegotiationStatus nvarchar(50) NOT NULL CONSTRAINT DF_BookingNegotiation_Status DEFAULT 'Pending_Call',
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_BookingNegotiation_Created DEFAULT SYSUTCDATETIME()
                );
            END;

            -- 2. Ensure dbo.EventAppointment table & columns
            IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.EventAppointment', N'EventName') IS NULL
                    ALTER TABLE dbo.EventAppointment ADD EventName nvarchar(255) NOT NULL CONSTRAINT DF_EventAppointment_Name DEFAULT 'Photoshoot Event';
                IF COL_LENGTH(N'dbo.EventAppointment', N'EventDescription') IS NULL
                    ALTER TABLE dbo.EventAppointment ADD EventDescription nvarchar(255) NULL;
                IF COL_LENGTH(N'dbo.EventAppointment', N'TotalAmount') IS NULL
                    ALTER TABLE dbo.EventAppointment ADD TotalAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_Total DEFAULT 0.00;
                IF COL_LENGTH(N'dbo.EventAppointment', N'DownpaymentAmount') IS NULL
                    ALTER TABLE dbo.EventAppointment ADD DownpaymentAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_Downpayment DEFAULT 0.00;
                IF COL_LENGTH(N'dbo.EventAppointment', N'FinalBalanceAmount') IS NULL
                    ALTER TABLE dbo.EventAppointment ADD FinalBalanceAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_Balance DEFAULT 0.00;
                IF COL_LENGTH(N'dbo.EventAppointment', N'DownpaymentStatus') IS NULL
                    ALTER TABLE dbo.EventAppointment ADD DownpaymentStatus nvarchar(30) NOT NULL CONSTRAINT DF_EventAppointment_DownpaymentStatus DEFAULT 'Pending';
                IF COL_LENGTH(N'dbo.EventAppointment', N'DownpaymentPaidAt') IS NULL
                    ALTER TABLE dbo.EventAppointment ADD DownpaymentPaidAt datetime2 NULL;
                IF COL_LENGTH(N'dbo.EventAppointment', N'HoldExpiresAt') IS NULL
                    ALTER TABLE dbo.EventAppointment ADD HoldExpiresAt datetime2 NULL;
                IF COL_LENGTH(N'dbo.EventAppointment', N'XenditInvoiceId') IS NULL
                    ALTER TABLE dbo.EventAppointment ADD XenditInvoiceId nvarchar(100) NULL;
                IF COL_LENGTH(N'dbo.EventAppointment', N'XenditExternalId') IS NULL
                    ALTER TABLE dbo.EventAppointment ADD XenditExternalId nvarchar(100) NULL;
                IF COL_LENGTH(N'dbo.EventAppointment', N'XenditInvoiceUrl') IS NULL
                    ALTER TABLE dbo.EventAppointment ADD XenditInvoiceUrl nvarchar(500) NULL;
                IF COL_LENGTH(N'dbo.EventAppointment', N'CreatedAt') IS NULL
                    ALTER TABLE dbo.EventAppointment ADD CreatedAt datetime2 NOT NULL CONSTRAINT DF_EventAppointment_Created DEFAULT SYSUTCDATETIME();
            END
            ELSE
            BEGIN
                CREATE TABLE dbo.EventAppointment (
                    EventAppointmentID nvarchar(30) NOT NULL PRIMARY KEY,
                    EventName nvarchar(255) NOT NULL,
                    EventDescription nvarchar(255) NULL,
                    CustomerID nvarchar(30) NOT NULL,
                    EventDate date NOT NULL,
                    EventTime time NOT NULL,
                    EventStatus nvarchar(30) NOT NULL CONSTRAINT DF_EventAppointment_Status DEFAULT 'Pending',
                    TotalAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_Total DEFAULT 0.00,
                    DownpaymentAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_Downpayment DEFAULT 0.00,
                    FinalBalanceAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_Balance DEFAULT 0.00,
                    DownpaymentStatus nvarchar(30) NOT NULL CONSTRAINT DF_EventAppointment_DownpaymentStatus DEFAULT 'Pending',
                    DownpaymentPaidAt datetime2 NULL,
                    HoldExpiresAt datetime2 NULL,
                    XenditInvoiceId nvarchar(100) NULL,
                    XenditExternalId nvarchar(100) NULL,
                    XenditInvoiceUrl nvarchar(500) NULL,
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_EventAppointment_Created DEFAULT SYSUTCDATETIME()
                );
            END;

            -- 3. Ensure dbo.EventEmployee
            IF OBJECT_ID(N'dbo.EventEmployee', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.EventEmployee (
                    EventEmployeeID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    EventAppointmentID nvarchar(30) NOT NULL,
                    EmployeeID nvarchar(30) NOT NULL,
                    IsTeamLeader bit NOT NULL CONSTRAINT DF_EventEmployee_IsTeamLeader DEFAULT 0
                );
            END;

            -- 4. Ensure dbo.EventEquipment
            IF OBJECT_ID(N'dbo.EventEquipment', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.EventEquipment (
                    EventEquipmentID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    EventAppointmentID nvarchar(30) NOT NULL,
                    EquipmentID nvarchar(30) NOT NULL
                );
            END;

            -- 5. Ensure dbo.EventOvertimeCharges
            IF OBJECT_ID(N'dbo.EventOvertimeCharges', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.EventOvertimeCharges (
                    OvertimeID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    EventAppointmentID nvarchar(30) NOT NULL,
                    ExtraHours decimal(5,2) NOT NULL,
                    HourlyRate decimal(18,2) NOT NULL,
                    SurchargeAmount decimal(18,2) NOT NULL,
                    Notes nvarchar(500) NULL,
                    LoggedByEmployeeID nvarchar(30) NOT NULL,
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_EventOvertime_CreatedAt DEFAULT SYSUTCDATETIME()
                );
            END;

            -- 6. Ensure dbo.PaymentTransactions
            IF OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.PaymentTransactions', N'PaymentType') IS NULL
                    ALTER TABLE dbo.PaymentTransactions ADD PaymentType nvarchar(50) NOT NULL CONSTRAINT DF_PaymentTransactions_Type DEFAULT 'Downpayment_25';
                IF COL_LENGTH(N'dbo.PaymentTransactions', N'PaymentMethod') IS NULL
                    ALTER TABLE dbo.PaymentTransactions ADD PaymentMethod nvarchar(50) NOT NULL CONSTRAINT DF_PaymentTransactions_Method DEFAULT 'Xendit_Card';
                IF COL_LENGTH(N'dbo.PaymentTransactions', N'Amount') IS NULL
                    ALTER TABLE dbo.PaymentTransactions ADD Amount decimal(18,2) NOT NULL CONSTRAINT DF_PaymentTransactions_Amount DEFAULT 0.00;
                IF COL_LENGTH(N'dbo.PaymentTransactions', N'XenditInvoiceId') IS NULL
                    ALTER TABLE dbo.PaymentTransactions ADD XenditInvoiceId nvarchar(100) NULL;
                IF COL_LENGTH(N'dbo.PaymentTransactions', N'XenditPaymentId') IS NULL
                    ALTER TABLE dbo.PaymentTransactions ADD XenditPaymentId nvarchar(100) NULL;
                IF COL_LENGTH(N'dbo.PaymentTransactions', N'XenditPaymentChannel') IS NULL
                    ALTER TABLE dbo.PaymentTransactions ADD XenditPaymentChannel nvarchar(100) NULL;
                IF COL_LENGTH(N'dbo.PaymentTransactions', N'Status') IS NULL
                    ALTER TABLE dbo.PaymentTransactions ADD Status nvarchar(30) NOT NULL CONSTRAINT DF_PaymentTransactions_Status DEFAULT 'Pending';
                IF COL_LENGTH(N'dbo.PaymentTransactions', N'ExternalReference') IS NULL
                    ALTER TABLE dbo.PaymentTransactions ADD ExternalReference nvarchar(100) NULL;
                IF COL_LENGTH(N'dbo.PaymentTransactions', N'ReceivedByEmployeeID') IS NULL
                    ALTER TABLE dbo.PaymentTransactions ADD ReceivedByEmployeeID nvarchar(30) NULL;
                IF COL_LENGTH(N'dbo.PaymentTransactions', N'PaidAt') IS NULL
                    ALTER TABLE dbo.PaymentTransactions ADD PaidAt datetime2 NULL;
                IF COL_LENGTH(N'dbo.PaymentTransactions', N'CreatedAt') IS NULL
                    ALTER TABLE dbo.PaymentTransactions ADD CreatedAt datetime2 NOT NULL CONSTRAINT DF_PaymentTransactions_CreatedAt DEFAULT SYSUTCDATETIME();
            END
            ELSE
            BEGIN
                CREATE TABLE dbo.PaymentTransactions (
                    PaymentTransactionID nvarchar(50) NOT NULL PRIMARY KEY,
                    EventAppointmentID nvarchar(30) NOT NULL,
                    PaymentType nvarchar(50) NOT NULL,
                    PaymentMethod nvarchar(50) NOT NULL,
                    Amount decimal(18,2) NOT NULL,
                    XenditPaymentId nvarchar(100) NULL,
                    XenditInvoiceId nvarchar(100) NULL,
                    XenditPaymentChannel nvarchar(100) NULL,
                    Status nvarchar(30) NOT NULL CONSTRAINT DF_PaymentTransactions_Status DEFAULT 'Pending',
                    ExternalReference nvarchar(100) NULL,
                    ReceivedByEmployeeID nvarchar(30) NULL,
                    PaidAt datetime2 NULL,
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_PaymentTransactions_CreatedAt DEFAULT SYSUTCDATETIME()
                );
            END;

            -- 7. Ensure dbo.HistoryLog
            IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.HistoryLog (
                    LogID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    ActionType nvarchar(50) NOT NULL,
                    EntityName nvarchar(50) NOT NULL,
                    EntityID nvarchar(50) NOT NULL,
                    ActionDetails nvarchar(1000) NOT NULL,
                    NegotiationID nvarchar(30) NULL,
                    EventAppointmentID nvarchar(30) NULL,
                    CustomerID nvarchar(30) NULL,
                    EmployeeID nvarchar(30) NULL,
                    ChangedByType nvarchar(50) NOT NULL,
                    ChangedByCustomerID nvarchar(30) NULL,
                    ChangedByEmployeeID nvarchar(30) NULL,
                    LoggedAt datetime2 NOT NULL CONSTRAINT DF_HistoryLog_LoggedAt DEFAULT SYSUTCDATETIME()
                );
            END;

            -- 8. Ensure Services RateType & Rate Catalog
            IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.Services', N'RateType') IS NULL
                    ALTER TABLE dbo.Services ADD RateType nvarchar(50) NOT NULL CONSTRAINT DF_Services_RateType DEFAULT 'Per Hour';
            END;

            IF OBJECT_ID(N'dbo.RateType', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.RateType (
                    RateTypeID nvarchar(30) NOT NULL PRIMARY KEY,
                    RateTypeName nvarchar(50) NOT NULL,
                    RateTypeCode nvarchar(30) NOT NULL UNIQUE,
                    RateDescription nvarchar(255) NULL,
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_RateType_CreatedAt DEFAULT SYSUTCDATETIME()
                );
            END;

            IF OBJECT_ID(N'dbo.RateType', N'U') IS NOT NULL
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM dbo.RateType WHERE RateTypeID = 'RT-0001' OR RateTypeCode = 'per-hour')
                    INSERT INTO dbo.RateType (RateTypeID, RateTypeName, RateTypeCode, RateDescription) VALUES ('RT-0001', 'Per Hour', 'per-hour', 'Billed on an hourly rate');
                IF NOT EXISTS (SELECT 1 FROM dbo.RateType WHERE RateTypeID = 'RT-0002' OR RateTypeCode = 'per-picture')
                    INSERT INTO dbo.RateType (RateTypeID, RateTypeName, RateTypeCode, RateDescription) VALUES ('RT-0002', 'Per Picture', 'per-picture', 'Billed per edited picture delivered');
                IF NOT EXISTS (SELECT 1 FROM dbo.RateType WHERE RateTypeID = 'RT-0003' OR RateTypeCode = 'per-single-shot')
                    INSERT INTO dbo.RateType (RateTypeID, RateTypeName, RateTypeCode, RateDescription) VALUES ('RT-0003', 'Per Single Shot', 'per-single-shot', 'Billed for an individual studio shot');
                IF NOT EXISTS (SELECT 1 FROM dbo.RateType WHERE RateTypeID = 'RT-0004' OR RateTypeCode = 'multi-shot')
                    INSERT INTO dbo.RateType (RateTypeID, RateTypeName, RateTypeCode, RateDescription) VALUES ('RT-0004', 'Multi-Shot', 'multi-shot', 'Billed for a multi-shot sequence or burst bundle');
            END;

            IF OBJECT_ID(N'dbo.ServiceRate', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ServiceRate (
                    ServiceRateID nvarchar(30) NOT NULL PRIMARY KEY,
                    ServicesID nvarchar(30) NOT NULL,
                    RateTypeID nvarchar(30) NOT NULL,
                    RateTypeName nvarchar(50) NOT NULL,
                    Price decimal(18,2) NOT NULL CONSTRAINT DF_ServiceRate_Price DEFAULT 0.00,
                    UnitsIncluded int NOT NULL CONSTRAINT DF_ServiceRate_Units DEFAULT 1,
                    RateDescription nvarchar(255) NULL,
                    IsDefault bit NOT NULL CONSTRAINT DF_ServiceRate_IsDefault DEFAULT 0,
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_ServiceRate_CreatedAt DEFAULT SYSUTCDATETIME()
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_RateTypeID')
            BEGIN
                CREATE SEQUENCE dbo.Seq_RateTypeID AS INT START WITH 5 INCREMENT BY 1;
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_ServiceRateID')
            BEGIN
                CREATE SEQUENCE dbo.Seq_ServiceRateID AS INT START WITH 1 INCREMENT BY 1;
            END;
        ";

        try
        {
            await using var cmd = new SqlCommand(schemaSql, conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch { /* Best effort schema reconciliation */ }
    }

    public async Task<EventAppointmentDto> CompleteEventSettlementAsync(
        CompleteEventRequest request,
        CancellationToken cancellationToken = default)
    {
        var appt = await GetAppointmentByIdAsync(request.EventAppointmentId, cancellationToken);
        if (appt == null)
            throw new KeyNotFoundException($"Event appointment {request.EventAppointmentId} not found.");

        await using var conn = await OpenConnectionAsync(cancellationToken);

        var txnId = await GetNextIdAsync(conn, "dbo.PaymentTransactions", "PaymentTransactionID", "TXN-", cancellationToken);
        var finalBalance = appt.FinalBalanceAmount + request.SurchargeAmount;

        await using var tx = conn.BeginTransaction();
        try
        {
            // 1. Log overtime charge if applicable
            if (request.ExtraHours > 0 || request.SurchargeAmount > 0)
            {
                const string otSql = @"
                    INSERT INTO dbo.EventOvertimeCharges (
                        EventAppointmentID, ExtraHours, HourlyRate, SurchargeAmount, Notes, LoggedByEmployeeID, CreatedAt
                    )
                    VALUES (
                        @EvtId, @Hours, @Rate, @Surcharge, @Notes, @EmpId, SYSUTCDATETIME()
                    );";
                await using var otCmd = new SqlCommand(otSql, conn, tx);
                otCmd.Parameters.AddWithValue("@EvtId", request.EventAppointmentId);
                otCmd.Parameters.AddWithValue("@Hours", request.ExtraHours);
                otCmd.Parameters.AddWithValue("@Rate", request.HourlyRate);
                otCmd.Parameters.AddWithValue("@Surcharge", request.SurchargeAmount);
                otCmd.Parameters.AddWithValue("@Notes", (object?)request.OvertimeNotes ?? DBNull.Value);
                otCmd.Parameters.AddWithValue("@EmpId", request.TeamLeaderEmployeeId);
                await otCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // 2. Mark event as finished / shot
            const string updEvt = @"
                UPDATE dbo.EventAppointment
                SET EventStatus = 'Shot',
                    FinalBalanceAmount = @FinalBal
                WHERE EventAppointmentID = @EvtId;

                INSERT INTO dbo.PaymentTransactions (
                    PaymentTransactionID, EventAppointmentID, PaymentType,
                    PaymentMethod, Amount, Status, ReceivedByEmployeeID, CreatedAt
                )
                VALUES (
                    @TxnId, @EvtId, 'BalanceSettlement_85',
                    @Method, @Amount, 'Paid', @EmpId, SYSUTCDATETIME()
                );

                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (
                        ActionType, EntityName, EntityID, ActionDetails,
                        EventAppointmentID, ChangedByType, ChangedByEmployeeID, LoggedAt
                    )
                    VALUES (
                        'UPDATE', 'EventAppointment', @EvtId,
                        'Event photoshoot marked finished. Final balance settled via ' + @Method,
                        @EvtId, 'Employee', @EmpId, SYSUTCDATETIME()
                    );
                END;";

            await using var cmd = new SqlCommand(updEvt, conn, tx);
            cmd.Parameters.AddWithValue("@TxnId", txnId);
            cmd.Parameters.AddWithValue("@EvtId", request.EventAppointmentId);
            cmd.Parameters.AddWithValue("@FinalBal", finalBalance);
            cmd.Parameters.AddWithValue("@Method", request.PaymentMethod);
            cmd.Parameters.AddWithValue("@Amount", finalBalance);
            cmd.Parameters.AddWithValue("@EmpId", request.TeamLeaderEmployeeId);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        _logger.LogInformation("Event appointment {AppointmentId} completed by Team Leader {TeamLeaderId}. Final settlement of ₱{Amount:N2} via {Method}",
            request.EventAppointmentId, request.TeamLeaderEmployeeId, finalBalance, request.PaymentMethod);

        return (await GetAppointmentByIdAsync(request.EventAppointmentId, cancellationToken))!;
    }

    // Helper: Atomic ID generation
    private static async Task<string> GetNextIdAsync(
        SqlConnection conn,
        string tableName,
        string columnName,
        string prefix,
        CancellationToken ct)
    {
        var sql = $@"
            DECLARE @MaxNum INT = 0;
            SELECT @MaxNum = ISNULL(MAX(TRY_CAST(SUBSTRING({columnName}, {prefix.Length + 1}, 20) AS INT)), 0)
            FROM {tableName} WITH (UPDLOCK, HOLDLOCK)
            WHERE {columnName} LIKE '{prefix}%';
            SELECT '{prefix}' + RIGHT('0000' + CAST((@MaxNum + 1) AS VARCHAR(10)), 4) AS NextId;";

        await using var cmd = new SqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result?.ToString() ?? $"{prefix}0001";
    }

    // Helper: Least-recently-assigned clerk round robin (strictly Office Clerk specialization only)
    private static async Task<(string EmployeeId, string EmployeeName, string EmployeeEmail)> AssignOfficeClerkAsync(
        SqlConnection conn,
        CancellationToken ct)
    {
        // Query active employees who strictly have the verified 'Office Clerk' specialization
        const string clerkSql = @"
            SELECT TOP 1 
                e.EmployeeID, 
                e.EmployeeFirstName + ' ' + e.EmployeeLastName AS EmployeeName, 
                ISNULL(e.EmployeeEmail, '') AS EmployeeEmail
            FROM dbo.Employees e
            JOIN dbo.EmployeeJointSpecialization ejs ON e.EmployeeID = ejs.EmployeeID
            JOIN dbo.EmployeeSpecialization es ON ejs.SpecializationID = es.SpecializationID
            LEFT JOIN (
                SELECT EmployeeID, MAX(CreatedAt) AS LastAssignedAt
                FROM dbo.BookingNegotiation
                GROUP BY EmployeeID
            ) bn ON e.EmployeeID = bn.EmployeeID
            WHERE (e.EmployeeStatus = 'Active' OR e.Status = 'Active')
              AND UPPER(LTRIM(RTRIM(COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization)))) = 'OFFICE CLERK'
            ORDER BY COALESCE(bn.LastAssignedAt, '1900-01-01') ASC, e.EmployeeID ASC;";

        await using var cmd = new SqlCommand(clerkSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var id = reader.GetString(0);
            var name = reader.GetString(1);
            var email = reader.GetString(2);
            return (id, name, email);
        }
        await reader.CloseAsync();

        throw new InvalidOperationException("No active employee with the Office Clerk specialization is available to be assigned to this consultation.");
    }
}
