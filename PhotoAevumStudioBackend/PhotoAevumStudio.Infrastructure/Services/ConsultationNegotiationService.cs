using System.Net;



using System.Data;



using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;



using Microsoft.EntityFrameworkCore;



using Microsoft.Extensions.Logging;



using Microsoft.Extensions.Options;



using PhotoAevumStudio.Application;



using PhotoAevumStudio.Application.Interfaces;



using PhotoAevumStudio.Application.Models;



using PhotoAevumStudio.Infrastructure.Data;
using PhotoAevumStudio.Infrastructure.Scheduling;







namespace PhotoAevumStudio.Infrastructure.Services;







public class ConsultationNegotiationService : IConsultationNegotiationService



{



    private readonly ApplicationDbContext _db;



    private readonly IGoogleMeetAdapter _meetAdapter;



    private readonly IXenditPaymentService _xenditPaymentService;



    private readonly IEmailService _emailService;



    private readonly XenditSettings _xenditSettings;



    private readonly ILogger<ConsultationNegotiationService> _logger;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public ConsultationNegotiationService(
        ApplicationDbContext db,
        IGoogleMeetAdapter meetAdapter,
        IXenditPaymentService xenditPaymentService,
        IEmailService emailService,
        IOptions<XenditSettings> xenditOptions,
        ILogger<ConsultationNegotiationService> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _db = db;
        _meetAdapter = meetAdapter;
        _xenditPaymentService = xenditPaymentService;
        _emailService = emailService;
        _xenditSettings = xenditOptions.Value;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    private string ResolvePortalBaseUrl()
    {
        var configured = _xenditSettings.CustomerPortalBaseUrl?.Trim();
        var ctx = _httpContextAccessor?.HttpContext;

        if (!string.IsNullOrWhiteSpace(configured) &&
            !configured.Contains("localhost", StringComparison.OrdinalIgnoreCase) &&
            !configured.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return configured.TrimEnd('/');
        }

        if (ctx?.Request.Headers.TryGetValue("Origin", out var origin) == true &&
            Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var originUri) &&
            !originUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
            !originUri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return originUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }

        if (ctx?.Request.Host.HasValue == true &&
            !ctx.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
            !ctx.Request.Host.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
            !ctx.Request.Host.Host.StartsWith("api-", StringComparison.OrdinalIgnoreCase))
        {
            return $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}".TrimEnd('/');
        }

        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');

        return "http://photoaevum.tryasp.net";
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







        // 2. Select Office Clerk via least-recently-assigned round robin with schedule buffer verification
        var (clerkId, clerkName, clerkEmail) = await AssignOfficeClerkAsync(conn, request.ConsultationDate, request.ConsultationTime, cancellationToken);







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
        await conn.CloseAsync();

        _logger.LogInformation("Consultation {NegotiationId} booked by customer {CustomerId}. Assigned clerk: {ClerkId} ({ClerkName})",
            negotiationId, request.CustomerId, clerkId, clerkName);







        // 6. Send email notification to Customer



        try



        {



            var emailBody = StudioEmailTemplates.ConsultationScheduled(



                string.IsNullOrWhiteSpace(custName) ? "Valued Customer" : custName,



                negotiationId,



                request.ConsultationDate,



                request.ConsultationTime,



                clerkName,



                meetingLink,



                request.InquiryNotes);







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



                COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(c.CustomerFirstName, ' ', c.CustomerLastName))), ''), bn.CustomerID, '') AS CustomerName,



                ISNULL(c.CustomerEmail, '') AS CustomerEmail,



                ISNULL(c.CustomerPhoneNumber, '') AS CustomerPhone,



                ISNULL(bn.EmployeeID, '') AS EmployeeID,



                COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(e.EmployeeFirstName, ' ', e.EmployeeLastName))), ''), NULLIF(bn.EmployeeID, ''), 'Unassigned') AS EmployeeName,



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
            list.Add(ReadNegotiationDto(reader));
        }

        return list;
    }

    private static ConsultationNegotiationDto ReadNegotiationDto(SqlDataReader reader) => new()
    {
        NegotiationId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
        EventAppointmentId = reader.IsDBNull(1) ? null : reader.GetString(1),
        CustomerId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
        CustomerName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
        CustomerEmail = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
        CustomerPhone = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
        EmployeeId = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
        EmployeeName = reader.IsDBNull(7) ? "Unassigned" : reader.GetString(7),
        EmployeeEmail = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
        ConsultationDate = reader.IsDBNull(9) ? default : DateOnly.FromDateTime(reader.GetDateTime(9)),
        ConsultationTime = reader.IsDBNull(10) ? default : TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(10)),
        MeetingLink = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
        InquiryNotes = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
        NegotiationStatus = reader.IsDBNull(13) ? "Pending_Call" : reader.GetString(13),
        CreatedAt = reader.IsDBNull(14) ? DateTime.UtcNow : reader.GetDateTime(14)
    };







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



                COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(c.CustomerFirstName, ' ', c.CustomerLastName))), ''), bn.CustomerID, '') AS CustomerName,



                ISNULL(c.CustomerEmail, '') AS CustomerEmail,



                ISNULL(c.CustomerPhoneNumber, '') AS CustomerPhone,



                ISNULL(bn.EmployeeID, '') AS EmployeeID,



                COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(e.EmployeeFirstName, ' ', e.EmployeeLastName))), ''), NULLIF(bn.EmployeeID, ''), 'Unassigned') AS EmployeeName,



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
            dto = ReadNegotiationDto(reader);
        }

        await reader.CloseAsync();







        if (dto != null && !string.IsNullOrWhiteSpace(dto.EventAppointmentId))
        {
            dto.Appointment = await GetAppointmentByIdOnConnectionAsync(conn, dto.EventAppointmentId, cancellationToken);
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



                EmployeeID = CASE WHEN @NewEmployeeId IS NOT NULL AND LEN(@NewEmployeeId) > 0 THEN @NewEmployeeId ELSE EmployeeID END,



                AssignedEmployeeName = CASE 
                    WHEN @NewEmployeeName IS NOT NULL AND LEN(@NewEmployeeName) > 0 THEN @NewEmployeeName 
                    WHEN @NewEmployeeId IS NOT NULL AND LEN(@NewEmployeeId) > 0 THEN (SELECT TOP 1 LTRIM(RTRIM(CONCAT(ISNULL(EmployeeFirstName, ''), ' ', ISNULL(EmployeeLastName, '')))) FROM dbo.Employees WHERE EmployeeID = @NewEmployeeId)
                    ELSE AssignedEmployeeName 
                END,



                InquiryNotes = CASE 



                    WHEN @Notes IS NOT NULL AND LEN(@Notes) > 0 



                    THEN ISNULL(InquiryNotes, '') + CHAR(13) + CHAR(10) + '[Outcome Note]: ' + @Notes 



                    ELSE InquiryNotes 



                END



            WHERE NegotiationID = @NegId;







            UPDATE dbo.EventAppointment



            SET EventDate = CASE WHEN @NewDate IS NOT NULL THEN @NewDate ELSE EventDate END,



                EventTime = CASE WHEN @NewTime IS NOT NULL THEN @NewTime ELSE EventTime END,



                EmployeeID = CASE WHEN @NewEmployeeId IS NOT NULL AND LEN(@NewEmployeeId) > 0 THEN @NewEmployeeId ELSE EmployeeID END,



                AssignedEmployeeName = CASE 
                    WHEN @NewEmployeeName IS NOT NULL AND LEN(@NewEmployeeName) > 0 THEN @NewEmployeeName 
                    WHEN @NewEmployeeId IS NOT NULL AND LEN(@NewEmployeeId) > 0 THEN (SELECT TOP 1 LTRIM(RTRIM(CONCAT(ISNULL(EmployeeFirstName, ''), ' ', ISNULL(EmployeeLastName, '')))) FROM dbo.Employees WHERE EmployeeID = @NewEmployeeId)
                    ELSE AssignedEmployeeName 
                END



            WHERE EventAppointmentID IN (



                SELECT EventAppointmentID FROM dbo.BookingNegotiation 



                WHERE NegotiationID = @NegId AND EventAppointmentID IS NOT NULL



            );";







        await using var cmd = new SqlCommand(sql, conn);



        cmd.Parameters.AddWithValue("@Status", request.NegotiationStatus);



        cmd.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value);



        cmd.Parameters.AddWithValue("@NewDate", request.NewConsultationDate.HasValue ? request.NewConsultationDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);



        cmd.Parameters.AddWithValue("@NewTime", request.NewConsultationTime.HasValue ? request.NewConsultationTime.Value.ToTimeSpan() : DBNull.Value);



        cmd.Parameters.AddWithValue("@NewEmployeeId", (object?)request.NewEmployeeId ?? DBNull.Value);



        cmd.Parameters.AddWithValue("@NewEmployeeName", (object?)request.NewEmployeeName ?? DBNull.Value);



        cmd.Parameters.AddWithValue("@NegId", negotiationId);







        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        await conn.CloseAsync();

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







                    var rescheduleBody = StudioEmailTemplates.ConsultationRescheduled(



                        clientName,



                        negotiationId,



                        details.ConsultationDate,



                        details.ConsultationTime,



                        clerkName,



                        meetLink,



                        request.Notes);







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







    private static volatile bool _granularSchemaEnsured;

    private static async Task EnsureGranularEventColumnsAsync(SqlConnection conn, CancellationToken cancellationToken)
    {
        if (_granularSchemaEnsured) return;

        const string ddl = @"
            IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.EventAppointment', N'EventEndTime') IS NULL
                    ALTER TABLE dbo.EventAppointment ADD EventEndTime time NULL;
                IF COL_LENGTH(N'dbo.EventAppointment', N'DurationMinutes') IS NULL
                    ALTER TABLE dbo.EventAppointment ADD DurationMinutes int NOT NULL CONSTRAINT DF_EventAppointment_DurationMinutes DEFAULT 60;
                IF COL_LENGTH(N'dbo.EventAppointment', N'EventLocation') IS NULL
                    ALTER TABLE dbo.EventAppointment ADD EventLocation nvarchar(255) NULL;
            END;
            IF OBJECT_ID(N'dbo.EventLineItem', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.EventLineItem', N'Quantity') IS NULL
                    ALTER TABLE dbo.EventLineItem ADD Quantity int NOT NULL CONSTRAINT DF_EventLineItem_Quantity DEFAULT 1;
                IF COL_LENGTH(N'dbo.EventLineItem', N'UnitPrice') IS NULL
                    ALTER TABLE dbo.EventLineItem ADD UnitPrice decimal(18,2) NOT NULL CONSTRAINT DF_EventLineItem_UnitPrice DEFAULT 0.00;
                IF COL_LENGTH(N'dbo.EventLineItem', N'LineTotal') IS NULL
                    ALTER TABLE dbo.EventLineItem ADD LineTotal decimal(18,2) NOT NULL CONSTRAINT DF_EventLineItem_LineTotal DEFAULT 0.00;
                IF COL_LENGTH(N'dbo.EventLineItem', N'RateType') IS NULL
                    ALTER TABLE dbo.EventLineItem ADD RateType nvarchar(50) NULL;
                IF COL_LENGTH(N'dbo.EventLineItem', N'DurationHours') IS NULL
                    ALTER TABLE dbo.EventLineItem ADD DurationHours int NULL;
            END;";

        try
        {
            await using var cmd = new SqlCommand(ddl, conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            _granularSchemaEnsured = true;
        }
        catch
        {
            // Ignore DDL permission issues
        }
    }

    public static (int EffectiveQuantity, decimal LineTotal, int? EffectiveDurationHours) CalculateServiceLinePricing(
        decimal unitPrice,
        string? rateType,
        int quantity,
        int? durationHours)
    {
        var r = (rateType ?? "per hour").Trim();
        var isPerHour = r.Contains("hour", StringComparison.OrdinalIgnoreCase);
        var isPerPictureOrShot = r.Contains("picture", StringComparison.OrdinalIgnoreCase)
            || r.Contains("shot", StringComparison.OrdinalIgnoreCase);

        if (isPerHour)
        {
            var effectiveHours = durationHours.HasValue && durationHours.Value > 0
                ? durationHours.Value
                : Math.Max(1, quantity);
            var effectiveQty = quantity > 1 && (!durationHours.HasValue || durationHours.Value == quantity)
                ? quantity
                : effectiveHours;
            return (effectiveQty, unitPrice * effectiveQty, effectiveHours);
        }

        if (isPerPictureOrShot)
        {
            var effectiveCount = Math.Max(1, quantity);
            return (effectiveCount, unitPrice * effectiveCount, durationHours);
        }

        var sessionQty = Math.Max(1, quantity);
        return (sessionQty, unitPrice * sessionQty, durationHours);
    }

    public async Task<EventAppointmentDto> CreateEventAppointmentFromNegotiationAsync(
        CreateEventAppointmentFromNegotiationRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await EnsureGranularEventColumnsAsync(conn, cancellationToken);

        // 1. Calculate pricing & duration from selected services (Main, ComboPackage, AdditionalServices)
        var requestedItems = new List<EventServiceLineInput>();
        if (request.ServiceSelections != null && request.ServiceSelections.Count > 0)
        {
            foreach (var item in request.ServiceSelections)
            {
                if (!string.IsNullOrWhiteSpace(item.ServicesId) &&
                    !requestedItems.Any(x => string.Equals(x.ServicesId, item.ServicesId, StringComparison.OrdinalIgnoreCase)))
                {
                    requestedItems.Add(new EventServiceLineInput
                    {
                        ServicesId = item.ServicesId.Trim(),
                        Quantity = Math.Max(1, item.Quantity),
                        DurationHours = item.DurationHours,
                        RateType = item.RateType
                    });
                }
            }
        }
        foreach (var sid in request.ServiceIds ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(sid) &&
                !requestedItems.Any(x => string.Equals(x.ServicesId, sid, StringComparison.OrdinalIgnoreCase)))
            {
                requestedItems.Add(new EventServiceLineInput
                {
                    ServicesId = sid.Trim(),
                    Quantity = 1
                });
            }
        }

        var serviceLines = new List<(EventServiceLineDto Line, string CatalogKind)>();

        if (requestedItems.Count > 0)
        {
            var pList = string.Join(",", requestedItems.Select((_, i) => $"@s{i}"));
            try
            {
                await using var rtEnsureCmd = new SqlCommand(@"
                    IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Services', N'RateType') IS NULL
                        ALTER TABLE dbo.Services ADD RateType nvarchar(50) NOT NULL CONSTRAINT DF_Services_RateType DEFAULT 'Per Hour';
                    IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComboPackage', N'RateType') IS NULL
                        ALTER TABLE dbo.ComboPackage ADD RateType nvarchar(50) NOT NULL CONSTRAINT DF_ComboPackage_RateType DEFAULT 'Per Hour';
                    IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AdditionalServices', N'RateType') IS NULL
                        ALTER TABLE dbo.AdditionalServices ADD RateType nvarchar(50) NOT NULL CONSTRAINT DF_AdditionalServices_RateType DEFAULT 'Per Hour';", conn);
                await rtEnsureCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch { }

            var qry = $@"
                SELECT ServicesID AS Id, ServiceName AS Name, ServicePrice AS Price, ISNULL(RateType, 'Per Hour') AS RateType, N'Main' AS Kind
                FROM dbo.Services WHERE ServicesID IN ({pList})
                UNION ALL
                SELECT ComboPackageID AS Id, ComboPackageName AS Name, ComboPackagePrice AS Price, ISNULL(RateType, 'Per Hour') AS RateType, N'Combo' AS Kind
                FROM dbo.ComboPackage WHERE ComboPackageID IN ({pList})
                UNION ALL
                SELECT AdditionalServicesID AS Id, AdditionalServiceName AS Name, ServicePrice AS Price, ISNULL(RateType, 'Per Hour') AS RateType, N'Additional' AS Kind
                FROM dbo.AdditionalServices WHERE AdditionalServicesID IN ({pList});";

            await using var sCmd = new SqlCommand(qry, conn);
            for (var i = 0; i < requestedItems.Count; i++)
            {
                sCmd.Parameters.AddWithValue($"@s{i}", requestedItems[i].ServicesId);
            }

            var catalogRows = new Dictionary<string, (string Name, decimal Price, string RateType, string Kind)>(StringComparer.OrdinalIgnoreCase);
            await using var sReader = await sCmd.ExecuteReaderAsync(cancellationToken);
            while (await sReader.ReadAsync(cancellationToken))
            {
                catalogRows[sReader.GetString(0)] = (
                    sReader.GetString(1),
                    sReader.GetDecimal(2),
                    sReader.IsDBNull(3) ? "Per Session" : sReader.GetString(3),
                    sReader.GetString(4)
                );
            }
            await sReader.CloseAsync();

            foreach (var item in requestedItems)
            {
                if (catalogRows.TryGetValue(item.ServicesId, out var cat))
                {
                    var effectiveRate = !string.IsNullOrWhiteSpace(item.RateType) ? item.RateType! : cat.RateType;
                    var (qty, lineTotal, durHours) = CalculateServiceLinePricing(cat.Price, effectiveRate, item.Quantity, item.DurationHours);

                    serviceLines.Add((new EventServiceLineDto
                    {
                        ServicesId = item.ServicesId,
                        ServiceName = cat.Name,
                        ServicePrice = lineTotal,
                        UnitPrice = cat.Price,
                        Quantity = qty,
                        LineTotal = lineTotal,
                        RateType = effectiveRate,
                        DurationHours = durHours
                    }, cat.Kind));
                }
            }
        }

        int computedDurationMinutes;
        TimeOnly eventEndTime;
        if (request.EventEndTime.HasValue && request.EventEndTime.Value > request.EventTime)
        {
            eventEndTime = request.EventEndTime.Value;
            computedDurationMinutes = Math.Max(15, (int)Math.Round((eventEndTime.ToTimeSpan() - request.EventTime.ToTimeSpan()).TotalMinutes));
        }
        else if (request.DurationMinutes.HasValue && request.DurationMinutes.Value > 0)
        {
            computedDurationMinutes = request.DurationMinutes.Value;
            var rawEnd = request.EventTime.ToTimeSpan().Add(TimeSpan.FromMinutes(computedDurationMinutes));
            if (rawEnd > new TimeSpan(23, 59, 0)) rawEnd = new TimeSpan(23, 59, 0);
            eventEndTime = TimeOnly.FromTimeSpan(rawEnd);
        }
        else
        {
            var maxHours = serviceLines
                .Where(s => s.Line.DurationHours.HasValue && s.Line.DurationHours.Value > 0)
                .Select(s => s.Line.DurationHours!.Value)
                .DefaultIfEmpty(1)
                .Max();
            computedDurationMinutes = maxHours * 60;
            var rawEnd = request.EventTime.ToTimeSpan().Add(TimeSpan.FromMinutes(computedDurationMinutes));
            if (rawEnd > new TimeSpan(23, 59, 0)) rawEnd = new TimeSpan(23, 59, 0);
            eventEndTime = TimeOnly.FromTimeSpan(rawEnd);
        }

        var totalAmount = serviceLines.Sum(s => s.Line.LineTotal);
        if (totalAmount <= 0 && serviceLines.Count == 0) totalAmount = 5000.00m;







        var downpaymentPercentage = _xenditSettings.DownpaymentPercentage <= 0 ? 0.15m : _xenditSettings.DownpaymentPercentage;



        var downpaymentAmount = Math.Round(totalAmount * downpaymentPercentage, 2);



        var finalBalanceAmount = totalAmount - downpaymentAmount;







        var holdMinutes = _xenditSettings.HoldDurationMinutes <= 0 ? 30 : _xenditSettings.HoldDurationMinutes;



        var lockMinutes = _xenditSettings.LockDurationMinutes <= 0 ? 60 : _xenditSettings.LockDurationMinutes;
        var holdExpiresAt = DateTime.UtcNow.AddMinutes(lockMinutes); // LockExpiresAt: slot is reserved until payment or this time







        string? existingAppointmentId = null;
        string? existingDownpaymentStatus = null;
        decimal existingDownpaymentAmount = 0m;
        string? existingQrString = null;

        if (!string.IsNullOrWhiteSpace(request.NegotiationId))
        {
            const string findExistingSql = @"
                SELECT TOP 1
                    bn.EventAppointmentID,
                    ISNULL(ea.DownpaymentStatus, 'Pending'),
                    ISNULL(ea.DownpaymentAmount, 0),
                    ea.QrString
                FROM dbo.BookingNegotiation bn
                INNER JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = bn.EventAppointmentID
                WHERE bn.NegotiationID = @NegId
                  AND ISNULL(ea.EventStatus, '') NOT IN ('Cancelled', 'Expired');";

            await using var exCmd = new SqlCommand(findExistingSql, conn);
            exCmd.Parameters.AddWithValue("@NegId", request.NegotiationId);
            await using var exReader = await exCmd.ExecuteReaderAsync(cancellationToken);
            if (await exReader.ReadAsync(cancellationToken))
            {
                existingAppointmentId = exReader.GetString(0);
                existingDownpaymentStatus = exReader.GetString(1);
                existingDownpaymentAmount = exReader.GetDecimal(2);
                existingQrString = exReader.IsDBNull(3) ? null : exReader.GetString(3);
            }
        }

        var isExistingAppointment = !string.IsNullOrWhiteSpace(existingAppointmentId);

        // 2. Reuse active negotiation appointment if already created, otherwise generate EVT-#### ID
        var appointmentId = existingAppointmentId
            ?? await GetNextIdAsync(conn, "dbo.EventAppointment", "EventAppointmentID", "EVT-", cancellationToken);







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







        // 4. Verify the required staff & equipment are still free for the combined window (Prep 2h + Event + Post 1h)
        // Overlapping event times ARE allowed if different crews and equipment are assigned.
        await ValidateEmployeesAvailableAsync(conn, null, request.EmployeeIds, request.EventDate, cancellationToken);

        var newStartSpan = request.EventTime.ToTimeSpan();
        var newEndSpan = eventEndTime.ToTimeSpan();
        var newPrepStart = newStartSpan >= TimeSpan.FromHours(2) ? newStartSpan.Subtract(TimeSpan.FromHours(2)) : TimeSpan.Zero;
        var newPostEnd = newEndSpan.Add(TimeSpan.FromHours(1));
        if (newPostEnd > new TimeSpan(23, 59, 59)) newPostEnd = new TimeSpan(23, 59, 59);

        if (request.EmployeeIds.Count > 0)
        {
            var empParams = string.Join(",", request.EmployeeIds.Select((_, i) => $"@e{i}"));
            var conflictSql = $@"
                SELECT ea.EventAppointmentID, ea.EventName, ea.EventTime, ea.EventEndTime, ISNULL(ea.DurationMinutes, 60) AS DurationMinutes
                FROM dbo.EventAppointment ea
                JOIN dbo.EventEmployee ee ON ee.EventAppointmentID = ea.EventAppointmentID
                WHERE ee.EmployeeID IN ({empParams})
                  AND ea.EventDate = @CDate
                  AND ea.EventStatus NOT IN ('Cancelled','Expired')
                  AND ea.EventAppointmentID <> @CEvtId
                  AND NOT EXISTS (SELECT 1 FROM dbo.BookingNegotiation bn
                                  WHERE bn.EventAppointmentID = ea.EventAppointmentID
                                    AND (bn.NegotiationID = @CNeg OR bn.NegotiationStatus IN ('Deal_Lost','Cancelled')));";
            await using var conflictCmd = new SqlCommand(conflictSql, conn);
            for (var i = 0; i < request.EmployeeIds.Count; i++)
                conflictCmd.Parameters.AddWithValue($"@e{i}", request.EmployeeIds[i]);
            conflictCmd.Parameters.AddWithValue("@CDate", request.EventDate.ToDateTime(TimeOnly.MinValue));
            conflictCmd.Parameters.AddWithValue("@CEvtId", appointmentId);
            conflictCmd.Parameters.AddWithValue("@CNeg", (object?)request.NegotiationId ?? DBNull.Value);

            await using var cReader = await conflictCmd.ExecuteReaderAsync(cancellationToken);
            while (await cReader.ReadAsync(cancellationToken))
            {
                var exId = cReader.GetString(0);
                var exName = cReader.IsDBNull(1) ? exId : cReader.GetString(1);
                var exStart = cReader.GetFieldValue<TimeSpan>(2);
                var exDuration = cReader.IsDBNull(4) ? 60 : Math.Max(15, cReader.GetInt32(4));
                var exEnd = !cReader.IsDBNull(3) ? cReader.GetFieldValue<TimeSpan>(3) : exStart.Add(TimeSpan.FromMinutes(exDuration));
                if (exEnd <= exStart) exEnd = exStart.Add(TimeSpan.FromMinutes(exDuration));

                var exPrepStart = exStart >= TimeSpan.FromHours(2) ? exStart.Subtract(TimeSpan.FromHours(2)) : TimeSpan.Zero;
                var exPostEnd = exEnd.Add(TimeSpan.FromHours(1));
                if (exPostEnd > new TimeSpan(23, 59, 59)) exPostEnd = new TimeSpan(23, 59, 59);

                if (newPrepStart < exPostEnd && exPrepStart < newPostEnd)
                {
                    throw new InvalidOperationException(
                        $"Selected crew conflicts with '{exName}' ({exId}) on {request.EventDate:yyyy-MM-dd} including 2h prep & 1h post buffers. Please choose an available crew or adjust time.");
                }
            }
            await cReader.CloseAsync();
        }

        // Same-day calendar lock: an equipment item booked on any non-cancelled/non-expired event on the same calendar date is unavailable all day.
        await ValidateEquipmentSameDayAvailableAsync(
            conn,
            null,
            request.EquipmentIds,
            request.EventDate,
            appointmentId,
            request.NegotiationId,
            cancellationToken);

        var qrGeneratedOrRefreshed = false;







        // 5. Insert into EventAppointment and assign staff & equipment



        await using var tx = conn.BeginTransaction();



        try



        {



            var resolvedLocation = string.IsNullOrWhiteSpace(request.EventLocation)
                ? "Studio A (Main Hall)"
                : request.EventLocation.Trim();

            var cleanEmpIds = request.EmployeeIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var effectiveTeamLeaderId = !string.IsNullOrWhiteSpace(request.TeamLeaderEmployeeId) &&
                                        cleanEmpIds.Any(id => string.Equals(id, request.TeamLeaderEmployeeId.Trim(), StringComparison.OrdinalIgnoreCase))
                ? cleanEmpIds.First(id => string.Equals(id, request.TeamLeaderEmployeeId.Trim(), StringComparison.OrdinalIgnoreCase))
                : (cleanEmpIds.FirstOrDefault() ?? (string.IsNullOrWhiteSpace(request.TeamLeaderEmployeeId) ? string.Empty : request.TeamLeaderEmployeeId.Trim()));

            if (isExistingAppointment)
            {
                const string updateEvtSql = @"
                    UPDATE dbo.EventAppointment
                    SET EventName = @Name,
                        EventDescription = @Desc,
                        EventLocation = COALESCE(NULLIF(@Location, ''), EventLocation),
                        EventDate = @Date,
                        EventTime = @Time,
                        EventEndTime = @EndTime,
                        DurationMinutes = @DurationMinutes,
                        TotalAmount = @Total,
                        DownpaymentAmount = @Downpayment,
                        FinalBalanceAmount = @FinalBal,
                        EmployeeID = COALESCE(NULLIF(@LeadEmpId, ''), EmployeeID)
                    WHERE EventAppointmentID = @EvtId;";

                await using var uCmd = new SqlCommand(updateEvtSql, conn, tx);
                uCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                uCmd.Parameters.AddWithValue("@Name", request.EventName);
                uCmd.Parameters.AddWithValue("@Desc", (object?)request.EventDescription ?? DBNull.Value);
                uCmd.Parameters.AddWithValue("@Location", resolvedLocation);
                uCmd.Parameters.AddWithValue("@Date", request.EventDate.ToDateTime(TimeOnly.MinValue));
                uCmd.Parameters.AddWithValue("@Time", request.EventTime.ToTimeSpan());
                uCmd.Parameters.AddWithValue("@EndTime", eventEndTime.ToTimeSpan());
                uCmd.Parameters.AddWithValue("@DurationMinutes", computedDurationMinutes);
                uCmd.Parameters.AddWithValue("@Total", totalAmount);
                uCmd.Parameters.AddWithValue("@Downpayment", downpaymentAmount);
                uCmd.Parameters.AddWithValue("@FinalBal", finalBalanceAmount);
                uCmd.Parameters.AddWithValue("@LeadEmpId", effectiveTeamLeaderId);
                await uCmd.ExecuteNonQueryAsync(cancellationToken);

                var isAlreadyPaid = string.Equals(existingDownpaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase);
                if (!isAlreadyPaid && (string.IsNullOrWhiteSpace(existingQrString) || existingDownpaymentAmount != downpaymentAmount))
                {
                    var qr = await _xenditPaymentService.CreateQrCodeAsync(appointmentId, downpaymentAmount, holdExpiresAt, cancellationToken);
                    await using var qrCmd = new SqlCommand(
                        "UPDATE dbo.EventAppointment SET QrString = @Qr, XenditInvoiceId = @QrId, HoldExpiresAt = @ExpiresAt, LockExpiresAt = @ExpiresAt WHERE EventAppointmentID = @EvtId;",
                        conn, tx);
                    qrCmd.Parameters.AddWithValue("@Qr", qr.QrString);
                    qrCmd.Parameters.AddWithValue("@QrId", string.IsNullOrWhiteSpace(qr.QrId) ? DBNull.Value : qr.QrId);
                    qrCmd.Parameters.AddWithValue("@ExpiresAt", holdExpiresAt);
                    qrCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                    await qrCmd.ExecuteNonQueryAsync(cancellationToken);
                    qrGeneratedOrRefreshed = true;
                }
            }
            else
            {
                const string insertEvtSql = @"
                    INSERT INTO dbo.EventAppointment (
                        EventAppointmentID, CustomerID, EmployeeID, EventName, EventDescription, EventLocation,
                        EventDate, EventTime, EventEndTime, DurationMinutes, EventStatus, TotalAmount, DownpaymentAmount,
                        FinalBalanceAmount, DownpaymentStatus, HoldExpiresAt, LockExpiresAt,
                        XenditExternalId, CreatedAt
                    )
                    VALUES (
                        @EvtId, @CustId, NULLIF(@LeadEmpId, ''), @Name, @Desc, @Location,
                        @Date, @Time, @EndTime, @DurationMinutes, 'Pending Payment', @Total, @Downpayment,
                        @FinalBal, 'Pending', @ExpiresAt, @ExpiresAt,
                        @ExternalId, SYSUTCDATETIME()
                    );";

                await using (var cmd = new SqlCommand(insertEvtSql, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@EvtId", appointmentId);
                    cmd.Parameters.AddWithValue("@CustId", request.CustomerId);
                    cmd.Parameters.AddWithValue("@LeadEmpId", effectiveTeamLeaderId);
                    cmd.Parameters.AddWithValue("@Name", request.EventName);
                    cmd.Parameters.AddWithValue("@Desc", (object?)request.EventDescription ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Location", resolvedLocation);
                    cmd.Parameters.AddWithValue("@Date", request.EventDate.ToDateTime(TimeOnly.MinValue));
                    cmd.Parameters.AddWithValue("@Time", request.EventTime.ToTimeSpan());
                    cmd.Parameters.AddWithValue("@EndTime", eventEndTime.ToTimeSpan());
                    cmd.Parameters.AddWithValue("@DurationMinutes", computedDurationMinutes);
                    cmd.Parameters.AddWithValue("@Total", totalAmount);
                    cmd.Parameters.AddWithValue("@Downpayment", downpaymentAmount);
                    cmd.Parameters.AddWithValue("@FinalBal", finalBalanceAmount);
                    cmd.Parameters.AddWithValue("@ExpiresAt", holdExpiresAt);
                    cmd.Parameters.AddWithValue("@ExternalId", appointmentId);
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // 5b. Record exists -> immediately request the dynamic QR from Xendit /qr_codes (reference_id = EVT-###) and save the qr_string with the booking.
                var qr = await _xenditPaymentService.CreateQrCodeAsync(appointmentId, downpaymentAmount, holdExpiresAt, cancellationToken);
                await using (var qrCmd = new SqlCommand("UPDATE dbo.EventAppointment SET QrString = @Qr, XenditInvoiceId = @QrId WHERE EventAppointmentID = @EvtId;", conn, tx))
                {
                    qrCmd.Parameters.AddWithValue("@Qr", qr.QrString);
                    qrCmd.Parameters.AddWithValue("@QrId", string.IsNullOrWhiteSpace(qr.QrId) ? DBNull.Value : qr.QrId);
                    qrCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                    await qrCmd.ExecuteNonQueryAsync(cancellationToken);
                }
                qrGeneratedOrRefreshed = true;
            }

            // Persist selected service line items across Services, ComboPackage, and AdditionalServices
            if (serviceLines.Count > 0)
            {
                await using (var delLineCmd = new SqlCommand("DELETE FROM dbo.EventLineItem WHERE EventAppointmentID = @EvtId;", conn, tx))
                {
                    delLineCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                    await delLineCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                for (var idx = 0; idx < serviceLines.Count; idx++)
                {
                    var (line, kind) = serviceLines[idx];
                    var lineId = $"ELI-{appointmentId}-{idx + 1:D2}";
                    if (lineId.Length > 30) lineId = $"ELI-{Guid.NewGuid():N}"[..24];

                    const string insLineSql = @"
                        INSERT INTO dbo.EventLineItem (
                            EventLineItemID, EventAppointmentID, ServicesID, AdditionalServicesID, ComboPackageID,
                            Quantity, UnitPrice, LineTotal, RateType, DurationHours
                        )
                        VALUES (
                            @LineId, @EvtId, @SvcId, @AddId, @PkgId,
                            @Qty, @UnitPrice, @LineTotal, @RateType, @DurHours
                        );";
                    await using var lCmd = new SqlCommand(insLineSql, conn, tx);
                    lCmd.Parameters.AddWithValue("@LineId", lineId);
                    lCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                    lCmd.Parameters.AddWithValue("@SvcId", kind == "Main" ? line.ServicesId : DBNull.Value);
                    lCmd.Parameters.AddWithValue("@AddId", kind == "Additional" ? line.ServicesId : DBNull.Value);
                    lCmd.Parameters.AddWithValue("@PkgId", kind == "Combo" ? line.ServicesId : DBNull.Value);
                    lCmd.Parameters.AddWithValue("@Qty", line.Quantity);
                    lCmd.Parameters.AddWithValue("@UnitPrice", line.UnitPrice);
                    lCmd.Parameters.AddWithValue("@LineTotal", line.LineTotal);
                    lCmd.Parameters.AddWithValue("@RateType", (object?)line.RateType ?? DBNull.Value);
                    lCmd.Parameters.AddWithValue("@DurHours", (object?)line.DurationHours ?? DBNull.Value);
                    await lCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            // Replace assigned staff in EventEmployee when staff IDs are supplied
            if (cleanEmpIds.Count > 0)
            {
                await using (var delEmpCmd = new SqlCommand("DELETE FROM dbo.EventEmployee WHERE EventAppointmentID = @EvtId;", conn, tx))
                {
                    delEmpCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                    await delEmpCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                foreach (var empId in cleanEmpIds)
                {
                    var isTl = string.Equals(empId, effectiveTeamLeaderId, StringComparison.OrdinalIgnoreCase);
                    const string insEmp = @"
                        IF EXISTS (SELECT 1 FROM dbo.Employees WHERE EmployeeID = @EmpId)
                           AND NOT EXISTS (SELECT 1 FROM dbo.EventEmployee WHERE EventAppointmentID = @EvtId AND EmployeeID = @EmpId)
                        BEGIN
                            DECLARE @EmpFullName nvarchar(100) = (
                                SELECT TOP 1 NULLIF(LTRIM(RTRIM(ISNULL(EmployeeFirstName, '') + ' ' + ISNULL(EmployeeLastName, ''))), '')
                                FROM dbo.Employees WHERE EmployeeID = @EmpId
                            );
                            IF COL_LENGTH(N'dbo.EventEmployee', N'AssignedEmployeeName') IS NOT NULL
                                INSERT INTO dbo.EventEmployee (EventAppointmentID, EmployeeID, AssignedEmployeeName, IsTeamLeader)
                                VALUES (@EvtId, @EmpId, COALESCE(@EmpFullName, @EmpId), @IsTL);
                            ELSE
                                INSERT INTO dbo.EventEmployee (EventAppointmentID, EmployeeID, IsTeamLeader)
                                VALUES (@EvtId, @EmpId, @IsTL);

                            IF @IsTL = 1 AND COL_LENGTH(N'dbo.EventAppointment', N'AssignedEmployeeName') IS NOT NULL
                                UPDATE dbo.EventAppointment
                                SET AssignedEmployeeName = COALESCE(@EmpFullName, @EmpId)
                                WHERE EventAppointmentID = @EvtId;
                        END";
                    await using var eCmd = new SqlCommand(insEmp, conn, tx);
                    eCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                    eCmd.Parameters.AddWithValue("@EmpId", empId);
                    eCmd.Parameters.AddWithValue("@IsTL", isTl ? 1 : 0);
                    await eCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            // Replace assigned equipment in EventEquipment when equipment IDs are supplied
            if (request.EquipmentIds.Count > 0)
            {
                await using (var delEqCmd = new SqlCommand("DELETE FROM dbo.EventEquipment WHERE EventAppointmentID = @EvtId;", conn, tx))
                {
                    delEqCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                    await delEqCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                foreach (var eqId in request.EquipmentIds.Distinct())
                {
                    const string insEq = @"
                        IF EXISTS (SELECT 1 FROM dbo.Equipment WHERE EquipmentID = @EqId) AND NOT EXISTS (SELECT 1 FROM dbo.EventEquipment WHERE EventAppointmentID = @EvtId AND EquipmentID = @EqId)
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

            // Link to BookingNegotiation (preserve Appointment Booked / Deal_Won if already settled)
            if (!string.IsNullOrWhiteSpace(request.NegotiationId))
            {
                const string updateNeg = @"
                    UPDATE dbo.BookingNegotiation
                    SET EventAppointmentID = @EvtId
                    WHERE NegotiationID = @NegId;

                    UPDATE dbo.BookingNegotiation
                    SET NegotiationStatus = 'Pending Payment'
                    WHERE NegotiationID = @NegId
                      AND ISNULL(NegotiationStatus, '') NOT IN ('Deal_Won', 'Appointment Booked');";
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







        var createdAppointment = await GetAppointmentByIdOnConnectionAsync(conn, appointmentId, cancellationToken);
        await conn.CloseAsync();

        // 6. Send transactional notification email to customer (when newly created or QR generated/refreshed)
        if (qrGeneratedOrRefreshed)
        {
            try
            {
                var emailBody = StudioEmailTemplates.ReservationPaymentRequired(
                    custName,
                    appointmentId,
                    request.EventName,
                    request.EventDate,
                    request.EventTime,
                    string.IsNullOrWhiteSpace(request.EventLocation) ? "Studio Main Hall" : request.EventLocation,
                    totalAmount,
                    downpaymentAmount,
                    holdExpiresAt,
                    $"{ResolvePortalBaseUrl()}/checkout/confirm/{Uri.EscapeDataString(appointmentId)}",
                    string.IsNullOrWhiteSpace(request.PaymentMethod) ? "QR Ph / E-Wallet" : request.PaymentMethod
                );

                if (!string.IsNullOrWhiteSpace(custEmail))
                {
                    await _emailService.SendEmailAsync(custEmail, $"Payment Required: 15% Deposit for {request.EventName} (#{appointmentId})", emailBody);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send downpayment email for {AppointmentId}", appointmentId);
            }
        }

        return createdAppointment ?? (await GetAppointmentByIdAsync(appointmentId, cancellationToken))!;
    }

    public async Task<EventAppointmentDto?> GetAppointmentByIdAsync(
        string appointmentId,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        return await GetAppointmentByIdOnConnectionAsync(conn, appointmentId, cancellationToken);
    }

    private async Task<EventAppointmentDto?> GetAppointmentByIdOnConnectionAsync(
        SqlConnection conn,
        string appointmentId,
        CancellationToken cancellationToken = default)
    {
        await EnsureGranularEventColumnsAsync(conn, cancellationToken);

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
                ea.CreatedAt,
                ea.LockExpiresAt,
                ea.QrString,
                ea.FinalPaymentMethod,
                ea.FinalPaymentStatus,
                ea.FinalQrString,
                ea.FinalPaymentReferenceId,
                ea.FinalPaymentPaidAt,
                ea.EventEndTime,
                ISNULL(ea.DurationMinutes, 60) AS DurationMinutes,
                ea.EventLocation
            FROM dbo.EventAppointment ea
            LEFT JOIN dbo.Customer c ON c.CustomerID = ea.CustomerID
            LEFT JOIN dbo.BookingNegotiation bn ON bn.EventAppointmentID = ea.EventAppointmentID
            WHERE ea.EventAppointmentID = @EvtId;

            SELECT ee.EmployeeID,
                   COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(e.EmployeeFirstName, '') + ' ' + ISNULL(e.EmployeeLastName, ''))), ''), NULLIF(ee.AssignedEmployeeName, ''), ee.EmployeeID) AS EmployeeName,
                   COALESCE(NULLIF(spec.Specs, ''), 'Staff') AS Specialization,
                   ISNULL(ee.IsTeamLeader, 0) AS IsTeamLeader
            FROM dbo.EventEmployee ee
            LEFT JOIN dbo.Employees e ON e.EmployeeID = ee.EmployeeID
            OUTER APPLY (
                SELECT STUFF((
                    SELECT ', ' + COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization)
                    FROM dbo.EmployeeJointSpecialization ejs
                    INNER JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID
                    WHERE ejs.EmployeeID = ee.EmployeeID
                      AND NULLIF(COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization), '') IS NOT NULL
                    ORDER BY es.SpecializationID
                    FOR XML PATH(''), TYPE
                ).value('.', 'nvarchar(max)'), 1, 2, '') AS Specs
            ) spec
            WHERE ee.EventAppointmentID = @EvtId
            ORDER BY ISNULL(ee.IsTeamLeader, 0) DESC, ee.EmployeeID ASC;

            SELECT eq.EquipmentID, 
                   ISNULL(e.EquipmentName, eq.EquipmentID) AS EquipmentName,
                   COALESCE(et.EquipmentTypeName, e.EquipmentTypeID, 'Equipment') AS EquipmentType,
                   eq.EventEquipmentID,
                   ISNULL(eq.IsTaken, 0) AS IsTaken,
                   eq.TakenAt,
                   eq.TakenByEmployeeID,
                   ISNULL(eq.IsAdditionalRequest, 0) AS IsAdditionalRequest,
                   eq.RequestedNotes
            FROM dbo.EventEquipment eq
            LEFT JOIN dbo.Equipment e ON e.EquipmentID = eq.EquipmentID
            LEFT JOIN dbo.EquipmentType et ON et.EquipmentTypeID = e.EquipmentTypeID
            WHERE eq.EventAppointmentID = @EvtId;

            SELECT CAST($IDENTITY AS int) AS OvertimeID, EventAppointmentID, ExtraHours, HourlyRate, SurchargeAmount, Notes, LoggedByEmployeeID, CreatedAt
            FROM dbo.EventOvertimeCharges
            WHERE EventAppointmentID = @EvtId;

            SELECT PaymentTransactionID, EventAppointmentID, PaymentType, PaymentMethod, Amount, Status, ExternalReference, ReceivedByEmployeeID, CreatedAt
            FROM dbo.PaymentTransactions
            WHERE EventAppointmentID = @EvtId
            ORDER BY CreatedAt DESC;

            SELECT COALESCE(eli.ServicesID, eli.ComboPackageID, eli.AdditionalServicesID) AS ServicesID,
                   COALESCE(s.ServiceName, cp.ComboPackageName, ads.AdditionalServiceName, eli.EventLineItemID) AS ServiceName,
                   CASE WHEN ISNULL(eli.LineTotal, 0) > 0 THEN eli.LineTotal ELSE COALESCE(s.ServicePrice, cp.ComboPackagePrice, ads.ServicePrice, 0) END AS ServicePrice,
                   ISNULL(eli.Quantity, 1) AS Quantity,
                   CASE WHEN ISNULL(eli.UnitPrice, 0) > 0 THEN eli.UnitPrice ELSE COALESCE(s.ServicePrice, cp.ComboPackagePrice, ads.ServicePrice, 0) END AS UnitPrice,
                   CASE WHEN ISNULL(eli.LineTotal, 0) > 0 THEN eli.LineTotal ELSE COALESCE(s.ServicePrice, cp.ComboPackagePrice, ads.ServicePrice, 0) * ISNULL(eli.Quantity, 1) END AS LineTotal,
                   COALESCE(NULLIF(eli.RateType, ''), s.RateType, cp.RateType, ads.RateType, N'Per Hour') AS RateType,
                   eli.DurationHours
            FROM dbo.EventLineItem eli
            LEFT JOIN dbo.Services s ON s.ServicesID = eli.ServicesID
            LEFT JOIN dbo.ComboPackage cp ON cp.ComboPackageID = eli.ComboPackageID
            LEFT JOIN dbo.AdditionalServices ads ON ads.AdditionalServicesID = eli.AdditionalServicesID
            WHERE eli.EventAppointmentID = @EvtId;";

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
                if (!invoiceId.StartsWith("qr_", StringComparison.OrdinalIgnoreCase))
                {
                    invoiceUrl = $"{_xenditSettings.InvoiceBaseUrl.TrimEnd('/')}/{invoiceId}";
                }
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
                CreatedAt = reader.GetDateTime(18),
                LockExpiresAt = reader.IsDBNull(19) ? null : reader.GetDateTime(19),
                QrString = reader.IsDBNull(20) ? null : reader.GetString(20),
                FinalPaymentMethod = reader.IsDBNull(21) ? null : reader.GetString(21),
                FinalPaymentStatus = reader.IsDBNull(22) ? null : reader.GetString(22),
                FinalQrString = reader.IsDBNull(23) ? null : reader.GetString(23),
                FinalPaymentReferenceId = reader.IsDBNull(24) ? null : reader.GetString(24),
                FinalPaymentPaidAt = reader.IsDBNull(25) ? null : reader.GetDateTime(25),
                EventEndTime = reader.FieldCount > 26 && !reader.IsDBNull(26) ? TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(26)) : null,
                DurationMinutes = reader.FieldCount > 27 && !reader.IsDBNull(27) ? reader.GetInt32(27) : 60,
                EventLocation = reader.FieldCount > 28 && !reader.IsDBNull(28) && !string.IsNullOrWhiteSpace(reader.GetString(28))
                    ? reader.GetString(28)
                    : "Studio A (Main Hall)",
                PrepBufferMinutes = 120,
                PostBufferMinutes = 60
            };
        }

        if (dto == null) return null;

        // Result set 2: Staff
        if (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                dto.AssignedEmployees.Add(new AssignedEmployeeDto
                {
                    EmployeeId = reader.GetString(0),
                    EmployeeName = reader.GetString(1),
                    Specialization = reader.GetString(2),
                    IsTeamLeader = reader.GetBoolean(3)
                });
            }
        }

        // Result set 3: Equipment
        if (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                dto.AssignedEquipment.Add(new AssignedEquipmentDto
                {
                    EquipmentId = reader.GetString(0),
                    EquipmentName = reader.GetString(1),
                    EquipmentType = reader.GetString(2),
                    EventEquipmentId = reader.FieldCount > 3 && !reader.IsDBNull(3) ? reader.GetInt32(3) : 0,
                    IsTaken = reader.FieldCount > 4 && !reader.IsDBNull(4) && reader.GetBoolean(4),
                    TakenAt = reader.FieldCount > 5 && !reader.IsDBNull(5) ? reader.GetDateTime(5) : null,
                    TakenByEmployeeId = reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetString(6) : null,
                    IsAdditionalRequest = reader.FieldCount > 7 && !reader.IsDBNull(7) && reader.GetBoolean(7),
                    RequestedNotes = reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetString(8) : null
                });
            }
        }

        // Result set 4: Overtime charges
        if (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                dto.OvertimeCharges.Add(new EventOvertimeDto
                {
                    OvertimeId = reader.GetInt32(0),
                    EventAppointmentId = reader.GetString(1),
                    ExtraHours = reader.GetDecimal(2),
                    HourlyRate = reader.GetDecimal(3),
                    SurchargeAmount = reader.GetDecimal(4),
                    Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
                    LoggedByEmployeeId = reader.GetString(6),
                    CreatedAt = reader.GetDateTime(7)
                });
            }
        }

        // Result set 5: Transactions
        if (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                dto.Transactions.Add(new PaymentTransactionDto
                {
                    TransactionId = reader.GetString(0),
                    EventAppointmentId = reader.GetString(1),
                    PaymentType = reader.GetString(2),
                    PaymentMethod = reader.GetString(3),
                    Amount = reader.GetDecimal(4),
                    Status = reader.GetString(5),
                    ExternalReference = reader.IsDBNull(6) ? null : reader.GetString(6),
                    ReceivedByEmployeeId = reader.IsDBNull(7) ? null : reader.GetString(7),
                    CreatedAt = reader.GetDateTime(8)
                });
            }
        }

        // Result set 6: Selected Services
        if (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                dto.SelectedServices.Add(new EventServiceLineDto
                {
                    ServicesId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    ServiceName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    ServicePrice = reader.IsDBNull(2) ? 0m : reader.GetDecimal(2),
                    Quantity = reader.IsDBNull(3) ? 1 : reader.GetInt32(3),
                    UnitPrice = reader.IsDBNull(4) ? 0m : reader.GetDecimal(4),
                    LineTotal = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5),
                    RateType = reader.IsDBNull(6) ? "Per Session" : reader.GetString(6),
                    DurationHours = reader.IsDBNull(7) ? null : reader.GetInt32(7)
                });
            }
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
            var appt = await GetAppointmentByIdOnConnectionAsync(conn, id, cancellationToken);
            if (appt != null) list.Add(appt);
        }

        return list;
    }







    public Task<EventAppointmentDto> VerifyDownpaymentAsync(string appointmentId, string? invoiceId = null, CancellationToken cancellationToken = default)
        => VerifyDownpaymentAsync(appointmentId, invoiceId, null, cancellationToken);

    public async Task<EventAppointmentDto> VerifyDownpaymentAsync(



        string appointmentId,



        string? invoiceId = null, string? paymentChannel = null, CancellationToken cancellationToken = default)



    {



        var appt = await GetAppointmentByIdAsync(appointmentId, cancellationToken);



        if (appt == null)



            throw new KeyNotFoundException($"Event appointment {appointmentId} not found.");
        // QR bookings are confirmed only by the validated Xendit webhook; never self-verify them (the mock status fallback would otherwise mark them paid).
        if (appt.EventStatus == "Pending Payment")
            return appt;
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



                        LockExpiresAt = NULL,



                        DownpaymentStatus = 'Paid'



                    WHERE EventAppointmentID = @EvtId;

                    IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
                    BEGIN
                        UPDATE dbo.BookingNegotiation
                        SET NegotiationStatus = 'Appointment Booked',
                            EventAppointmentID = COALESCE(EventAppointmentID, @EvtId)
                        WHERE EventAppointmentID = @EvtId;
                    END;







                    INSERT INTO dbo.PaymentTransactions (



                        PaymentTransactionID, EventAppointmentID, PaymentType,



                        PaymentMethod, Amount, Status, ExternalReference,



                        XenditInvoiceId, XenditPaymentId, XenditPaymentChannel, CreatedAt



                    )



                    VALUES (



                        @TxnId, @EvtId, 'Downpayment_15',



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



                            '15% Downpayment verified and paid via Xendit. Appointment confirmed.',



                            @EvtId, 'System', SYSUTCDATETIME()



                        );



                    END;";







                var channel = !string.IsNullOrWhiteSpace(paymentChannel)
                    ? paymentChannel
                    : (!string.IsNullOrWhiteSpace(statusResult.PaymentChannel) ? statusResult.PaymentChannel : "GCASH");

                var paymentMethod = !string.IsNullOrWhiteSpace(channel)
                    ? $"Xendit_{channel}"
                    : (statusResult.PaymentMethod ?? "Xendit");







                await using var cmd = new SqlCommand(updateSql, conn, tx);



                cmd.Parameters.AddWithValue("@TxnId", txnId);



                cmd.Parameters.AddWithValue("@EvtId", appointmentId);



                cmd.Parameters.AddWithValue("@Method", paymentMethod);



                cmd.Parameters.AddWithValue("@Amount", appt.DownpaymentAmount);



                cmd.Parameters.AddWithValue("@Ref", statusResult.InvoiceId);



                cmd.Parameters.AddWithValue("@XenditInvId", (object?)statusResult.InvoiceId ?? DBNull.Value);



                cmd.Parameters.AddWithValue("@XenditPayId", (object?)statusResult.InvoiceId ?? DBNull.Value);



                cmd.Parameters.AddWithValue("@XenditChannel", (object?)channel ?? DBNull.Value);







                await cmd.ExecuteNonQueryAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }

            appt = await GetAppointmentByIdOnConnectionAsync(conn, appointmentId, cancellationToken) ?? appt;
            await conn.CloseAsync();

            // Sync with assigned employee calendars and notify customer
            try
            {
                var body = $"""
                    <div style="font-family:Arial,sans-serif;color:#1e293b;max-width:600px;margin:0 auto;padding:24px;border:1px solid #e2e8f0;border-radius:12px">
                        <h2 style="color:#047857;margin-top:0">&#10004; Downpayment Confirmed - You're Booked!</h2>
                        <p>Dear {appt.CustomerName},</p>
                        <p>We have successfully received your 15% downpayment of <strong>₱{appt.DownpaymentAmount:N2}</strong> for <strong>{appt.EventName}</strong> (#{appointmentId}).</p>
                        <div style="background:#f0fdf4;padding:16px;border-radius:8px;margin:20px 0;border:1px solid #bbf7d0">
                            <p style="margin:4px 0"><strong>Date:</strong> {appt.EventDate:dddd, MMMM d, yyyy}</p>
                            <p style="margin:4px 0"><strong>Time:</strong> {appt.EventTime:h:mm tt}</p>
                            <p style="margin:4px 0"><strong>Status:</strong> <span style="color:#047857;font-weight:bold">Confirmed &amp; Calendar Locked</span></p>
                            <p style="margin:4px 0"><strong>Total Package:</strong> ₱{appt.TotalAmount:N2}</p>
                            <p style="margin:4px 0"><strong>Remaining Balance (85% Due on event day):</strong> ₱{appt.FinalBalanceAmount:N2}</p>
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

            return appt;
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
            WHERE EventAppointmentID = @EvtId AND DownpaymentStatus = 'Pending' AND EventStatus <> 'Pending Payment';";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@EvtId", appointmentId);
        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        EventAppointmentDto? appt = rows > 0 ? await GetAppointmentByIdOnConnectionAsync(conn, appointmentId, cancellationToken) : null;
        await conn.CloseAsync();

        _logger.LogInformation("30-minute extension requested and granted for appointment {AppointmentId}", appointmentId);

        if (rows > 0)
        {
            try
            {
                if (appt != null && !string.IsNullOrWhiteSpace(appt.CustomerEmail))
                {
                    var emailBody = StudioEmailTemplates.ReservationPaymentRequired(
                        appt.CustomerName,
                        appt.EventAppointmentId,
                        appt.EventName,
                        appt.EventDate,
                        appt.EventTime,
                        "Studio A (Main Hall)",
                        appt.TotalAmount,
                        appt.DownpaymentAmount,
                        appt.HoldExpiresAt ?? DateTime.UtcNow.AddMinutes(30),
                        (!string.IsNullOrWhiteSpace(appt.XenditInvoiceUrl) && !appt.XenditInvoiceUrl.Contains("/qr_") && !appt.XenditInvoiceUrl.Contains("qr_"))
                            ? appt.XenditInvoiceUrl
                            : $"{ResolvePortalBaseUrl()}/checkout/confirm/{Uri.EscapeDataString(appointmentId)}",
                        "QR Ph / E-Wallet");

                    await _emailService.SendEmailAsync(appt.CustomerEmail, $"Payment Reminder: Reservation Fee for {appt.EventName} (#{appointmentId})", emailBody);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send reminder payment email for appointment {AppointmentId}", appointmentId);
            }
        }

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
        var conn = await SqlResilienceHelper.OpenConnectionAsync(connStr, ct);
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
            try
            {
                await PendingPaymentService.EnsureSchemaOnConnectionAsync(conn, ct);
            }
            catch { /* Best effort schema reconciliation (LockExpiresAt / QrString / Pending Payment status) */ }
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
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.BookingNegotiation') AND name = N'EventAppointmentID' AND is_nullable = 0)
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

            IF OBJECT_ID(N'dbo.EventOvertimeCharges', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.EventOvertimeCharges', N'Notes') IS NULL
                BEGIN
                    ALTER TABLE dbo.EventOvertimeCharges ADD Notes nvarchar(500) NULL;
                    IF COL_LENGTH(N'dbo.EventOvertimeCharges', N'ReasonNotes') IS NOT NULL
                    BEGIN
                        EXEC(N'UPDATE dbo.EventOvertimeCharges SET Notes = ReasonNotes WHERE Notes IS NULL AND ReasonNotes IS NOT NULL;');
                    END;
                END;
            END;







            -- 6. Ensure dbo.PaymentTransactions



            IF OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NOT NULL



            BEGIN



                IF COL_LENGTH(N'dbo.PaymentTransactions', N'PaymentType') IS NULL



                    ALTER TABLE dbo.PaymentTransactions ADD PaymentType nvarchar(50) NOT NULL CONSTRAINT DF_PaymentTransactions_Type DEFAULT 'Downpayment_15';



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

        if (string.Equals(appt.EventStatus, "Completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(appt.EventStatus, "Complete", StringComparison.OrdinalIgnoreCase)
            || string.Equals(appt.EventStatus, "Shot", StringComparison.OrdinalIgnoreCase)
            || string.Equals(appt.FinalPaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase)
            || string.Equals(appt.FinalPaymentStatus, "Payment Verification (QR:Paid)", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This event's final settlement has already been completed.");
        }







        await using var conn = await OpenConnectionAsync(cancellationToken);







        var txnId = await GetNextIdAsync(conn, "dbo.PaymentTransactions", "PaymentTransactionID", "TXN-", cancellationToken);



        var finalBalance = appt.FinalBalanceAmount + request.SurchargeAmount;

        var isQr = request.PaymentMethod.Contains("QR", StringComparison.OrdinalIgnoreCase)
                || request.PaymentMethod.Contains("Xendit", StringComparison.OrdinalIgnoreCase);
        var normalizedMethod = isQr ? "Xendit_QR" : "Cash_OnHand";
        var finalRefId = $"{request.EventAppointmentId}-FINAL";
        var qrResult = isQr
            ? await _xenditPaymentService.CreateQrCodeAsync(finalRefId, finalBalance, DateTime.UtcNow.AddDays(1), cancellationToken)
            : null;

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







            // 2. Branch final settlement workflow:
            // - Cash: moves event to 'Payment Verification (Cash)' (FinalPaymentStatus = 'Payment Verification (Cash)') and logs EmployeeRemittanceStatus = 'PendingAdminApproval'.
            // - QR: generates final QR ({id}-FINAL), moves event to 'Pending Payment' with FinalPaymentStatus = 'Payment Verification (QR:Pending Payment)', and emails customer.
            const string updEvt = @"
                UPDATE dbo.EventAppointment
                SET EventStatus = @NewStatus,
                    FinalBalanceAmount = @FinalBal,
                    FinalPaymentMethod = @FinalMethod,
                    FinalPaymentStatus = @FinalStatus,
                    FinalQrString = CASE WHEN @IsQr = 1 THEN @FinalQr ELSE FinalQrString END,
                    FinalPaymentReferenceId = CASE WHEN @IsQr = 1 THEN @FinalRef ELSE FinalPaymentReferenceId END,
                    LockExpiresAt = CASE WHEN @IsQr = 1 THEN NULL ELSE LockExpiresAt END,
                    HoldExpiresAt = CASE WHEN @IsQr = 1 THEN NULL ELSE HoldExpiresAt END
                WHERE EventAppointmentID = @EvtId;

                INSERT INTO dbo.PaymentTransactions (
                    PaymentTransactionID, EventAppointmentID, PaymentType,
                    PaymentMethod, Amount, Status, ExternalReference, XenditPaymentId,
                    CustomerPaymentStatus, EmployeeRemittanceStatus, ReceivedByEmployeeID, CreatedAt
                )
                VALUES (
                    @TxnId, @EvtId, 'BalanceSettlement_85',
                    @DisplayMethod, @Amount, 'Pending', @FinalRef, @QrId,
                    @CustPayStatus, @RemittanceStatus, @EmpId, SYSUTCDATETIME()
                );

                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (
                        ActionType, EntityName, EntityID, ActionDetails,
                        EventAppointmentID, ChangedByType, ChangedByEmployeeID, LoggedAt
                    )
                    VALUES (
                        'UPDATE', 'EventAppointment', @EvtId,
                        @HistoryNote,
                        @EvtId, 'Employee', @EmpId, SYSUTCDATETIME()
                    );
                END;";

            await using var cmd = new SqlCommand(updEvt, conn, tx);
            cmd.Parameters.AddWithValue("@TxnId", txnId);
            cmd.Parameters.AddWithValue("@EvtId", request.EventAppointmentId);
            cmd.Parameters.AddWithValue("@FinalBal", finalBalance);
            cmd.Parameters.AddWithValue("@NewStatus", isQr ? "Pending Payment" : "Payment Verification (Cash)");
            cmd.Parameters.AddWithValue("@FinalMethod", normalizedMethod);
            cmd.Parameters.AddWithValue("@FinalStatus", isQr ? "Payment Verification (QR:Pending Payment)" : "Payment Verification (Cash)");
            cmd.Parameters.AddWithValue("@IsQr", isQr ? 1 : 0);
            cmd.Parameters.AddWithValue("@FinalQr", isQr ? (object)(qrResult?.QrString ?? string.Empty) : DBNull.Value);
            cmd.Parameters.AddWithValue("@FinalRef", isQr ? (object)finalRefId : DBNull.Value);
            cmd.Parameters.AddWithValue("@QrId", isQr && !string.IsNullOrWhiteSpace(qrResult?.QrId) ? qrResult.QrId : DBNull.Value);
            cmd.Parameters.AddWithValue("@DisplayMethod", isQr ? "QR Ph" : "Cash_OnHand");
            cmd.Parameters.AddWithValue("@Amount", finalBalance);
            cmd.Parameters.AddWithValue("@CustPayStatus", isQr ? "Pending" : "Cleared");
            cmd.Parameters.AddWithValue("@RemittanceStatus", isQr ? "NotApplicable" : "PendingAdminApproval");
            cmd.Parameters.AddWithValue("@EmpId", request.TeamLeaderEmployeeId);
            cmd.Parameters.AddWithValue("@HistoryNote", isQr
                ? $"Team Leader initiated Final Balance QR payment ({finalRefId}) of PHP {finalBalance:N2}. Status: Payment Verification (QR:Pending Payment). Customer notified via email."
                : $"Team Leader logged on-site Cash final payment of PHP {finalBalance:N2}. Status: Payment Verification (Cash).");

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        // For Cash settlements, mirror a remittance row in EF Core dbo.Payments so AdminDashboard / BookingApprovals can approve it immediately
        if (!isQr)
        {
            try
            {
                _db.Payments.Add(new PhotoAevumStudio.Application.Entities.Payment
                {
                    EventAppointmentId = request.EventAppointmentId,
                    TotalAmount = appt.TotalAmount + request.SurchargeAmount,
                    DepositAmount = appt.DownpaymentAmount,
                    RemainingBalance = finalBalance,
                    Amount = finalBalance,
                    PaymentType = PhotoAevumStudio.Application.Entities.PaymentType.BalanceSettlement_85,
                    CustomerPaymentStatus = PhotoAevumStudio.Application.Entities.CustomerPaymentStatus.Cleared,
                    EmployeeRemittanceStatus = PhotoAevumStudio.Application.Entities.EmployeeRemittanceStatus.PendingAdminApproval,
                    Channel = PhotoAevumStudio.Application.Entities.PaymentChannel.Cash,
                    EmployeeId = request.TeamLeaderEmployeeId,
                    Notes = request.Notes ?? $"Team Leader Cash final balance collected on-site for {appt.EventName} ({request.EventAppointmentId}).",
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not mirror Cash final settlement to EF Payments table for {AppointmentId}", request.EventAppointmentId);
            }
        }
        else if (!string.IsNullOrWhiteSpace(appt.CustomerEmail))
        {
            try
            {
                var checkoutConfirmUrl = $"{ResolvePortalBaseUrl()}/checkout/confirm/{Uri.EscapeDataString(finalRefId)}";
                var emailBody = StudioEmailTemplates.FinalPaymentRequired(
                    appt.CustomerName,
                    finalRefId,
                    appt.EventAppointmentId,
                    appt.EventName,
                    appt.EventDate,
                    appt.EventTime,
                    finalBalance,
                    checkoutConfirmUrl);
                await _emailService.TrySendEmailAsync(
                    appt.CustomerEmail,
                    $"Final Balance Payment Required for {appt.EventName} (#{appt.EventAppointmentId})",
                    emailBody,
                    isHtml: true,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to email final QR payment instructions for {AppointmentId}", request.EventAppointmentId);
            }
        }







        _logger.LogInformation("Event appointment {AppointmentId} completed by Team Leader {TeamLeaderId}. Final settlement of ₱{Amount:N2} via {Method}",
            request.EventAppointmentId, request.TeamLeaderEmployeeId, finalBalance, request.PaymentMethod);

        return (await GetAppointmentByIdOnConnectionAsync(conn, request.EventAppointmentId, cancellationToken))!;
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
        DateOnly? consultationDate,
        TimeOnly? consultationTime,
        CancellationToken ct)
    {
        // Query active employees who strictly have the verified 'Office Clerk' specialization and are not on Day Off,
        // and are not restricted by existing bookings with 2 hours advance preparation and 1 hour post-event buffer.
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
              AND (
                  @EvtDate IS NULL
                  OR COL_LENGTH(N'dbo.Employees', N'DaysOff') IS NULL
                  OR CHARINDEX(DATENAME(dw, @EvtDate), ISNULL(e.DaysOff, 'Sunday')) = 0
              )
              AND (
                  @EvtDate IS NULL
                  OR OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NULL
                  OR NOT EXISTS (
                      SELECT 1 FROM dbo.EmployeeLeaves el
                      WHERE el.EmployeeStudioId = e.EmployeeID
                        AND el.Status = 'Approved'
                        AND @EvtDate BETWEEN el.StartDate AND el.EndDate
                  )
              )
              AND (
                  @EvtTime IS NULL
                  OR NOT EXISTS (
                      SELECT 1 FROM dbo.BookingNegotiation bn2
                      WHERE bn2.EmployeeID = e.EmployeeID
                        AND bn2.ConsultationDate = @EvtDate
                        AND bn2.NegotiationStatus NOT IN ('Cancelled', 'Deal_Lost')
                        AND bn2.ConsultationTime IS NOT NULL
                        AND @EvtTime < DATEADD(hour, 2, bn2.ConsultationTime)
                        AND DATEADD(hour, -2, bn2.ConsultationTime) < DATEADD(hour, 1, @EvtTime)
                  )
              )
              AND (
                  @EvtTime IS NULL
                  OR NOT EXISTS (
                      SELECT 1 FROM dbo.EventEmployee ee2
                      JOIN dbo.EventAppointment ea2 ON ee2.EventAppointmentID = ea2.EventAppointmentID
                      WHERE ee2.EmployeeID = e.EmployeeID
                        AND ea2.EventDate = @EvtDate
                        AND ea2.EventStatus <> 'Cancelled'
                        AND @EvtTime < DATEADD(hour, 2, ea2.EventTime)
                        AND DATEADD(hour, -2, ea2.EventTime) < DATEADD(hour, 1, @EvtTime)
                  )
              )
            ORDER BY COALESCE(bn.LastAssignedAt, '1900-01-01') ASC, e.EmployeeID ASC;";

        await using var cmd = new SqlCommand(clerkSql, conn);
        cmd.Parameters.AddWithValue("@EvtDate", consultationDate.HasValue ? (object)consultationDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        cmd.Parameters.AddWithValue("@EvtTime", consultationTime.HasValue ? (object)consultationTime.Value.ToTimeSpan() : DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var id = reader.GetString(0);
            var name = reader.GetString(1);
            var email = reader.GetString(2);
            return (id, name, email);
        }

        await reader.CloseAsync();

        if (consultationDate.HasValue && consultationTime.HasValue)
        {
            throw new InvalidOperationException($"Cannot schedule consultation: The assigned Office Clerk is unavailable at {consultationTime.Value:h:mm tt} on {consultationDate.Value:dddd, MMMM dd, yyyy} due to another booking or required buffer time.");
        }

        if (consultationDate.HasValue)
        {
            throw new InvalidOperationException($"Cannot schedule consultation: The Office Clerk has a scheduled Day Off on {consultationDate.Value:dddd, MMMM dd, yyyy}.");
        }

        throw new InvalidOperationException("No active employee with the Office Clerk specialization is available to be assigned to this consultation.");
    }



    public async Task<bool> UpdateAppointmentRosterAsync(
        string appointmentId,
        UpdateAppointmentRosterRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appointmentId))
            throw new ArgumentException("Appointment ID is required.", nameof(appointmentId));

        await using var conn = await OpenConnectionAsync(cancellationToken);
        await EnsureGranularEventColumnsAsync(conn, cancellationToken);

        await using var tx = conn.BeginTransaction();
        try
        {
            DateOnly evtDate;
            TimeSpan evtStart;
            TimeSpan evtEnd;
            string evtName;
            string evtStatus;

            const string evtMetaSql = @"
                SELECT EventDate, EventTime, EventEndTime, ISNULL(DurationMinutes, 60) AS DurationMinutes, EventName, EventStatus
                FROM dbo.EventAppointment
                WHERE EventAppointmentID = @EvtId;";
            await using (var mCmd = new SqlCommand(evtMetaSql, conn, tx))
            {
                mCmd.Parameters.AddWithValue("@EvtId", appointmentId.Trim());
                await using var mReader = await mCmd.ExecuteReaderAsync(cancellationToken);
                if (!await mReader.ReadAsync(cancellationToken))
                    throw new KeyNotFoundException($"Event appointment {appointmentId} not found.");

                evtDate = DateOnly.FromDateTime(mReader.GetDateTime(0));
                evtStart = mReader.GetFieldValue<TimeSpan>(1);
                var dur = mReader.IsDBNull(3) ? 60 : Math.Max(15, mReader.GetInt32(3));
                evtEnd = !mReader.IsDBNull(2) ? mReader.GetFieldValue<TimeSpan>(2) : evtStart.Add(TimeSpan.FromMinutes(dur));
                if (evtEnd <= evtStart) evtEnd = evtStart.Add(TimeSpan.FromMinutes(dur));
                evtName = mReader.IsDBNull(4) ? appointmentId : mReader.GetString(4);
                evtStatus = mReader.IsDBNull(5) ? string.Empty : mReader.GetString(5);
            }

            if (string.Equals(evtStatus, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(evtStatus, "Expired", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Cannot modify crew or equipment for a {evtStatus} event.");
            }

            var newEmpIds = (request.EmployeeIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (newEmpIds.Count == 0)
                throw new InvalidOperationException("At least one crew member must be assigned to the event.");

            var newEqIds = (request.EquipmentIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Snapshot existing crew & equipment assignments for readable HistoryLog diffing
            var oldEmpMap = new Dictionary<string, (string Name, bool IsTeamLeader)>(StringComparer.OrdinalIgnoreCase);
            const string oldEmpSql = @"
                SELECT ee.EmployeeID,
                       COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(e.EmployeeFirstName, '') + ' ' + ISNULL(e.EmployeeLastName, ''))), ''), NULLIF(ee.AssignedEmployeeName, ''), ee.EmployeeID) AS EmpName,
                       ISNULL(ee.IsTeamLeader, 0) AS IsTeamLeader
                FROM dbo.EventEmployee ee
                LEFT JOIN dbo.Employees e ON e.EmployeeID = ee.EmployeeID
                WHERE ee.EventAppointmentID = @EvtId AND ee.EmployeeID IS NOT NULL;";
            await using (var oeCmd = new SqlCommand(oldEmpSql, conn, tx))
            {
                oeCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                await using var oeReader = await oeCmd.ExecuteReaderAsync(cancellationToken);
                while (await oeReader.ReadAsync(cancellationToken))
                {
                    var empId = oeReader.GetString(0).Trim();
                    var empName = oeReader.IsDBNull(1) ? empId : oeReader.GetString(1).Trim();
                    var isTl = !oeReader.IsDBNull(2) && oeReader.GetBoolean(2);
                    oldEmpMap[empId] = (empName, isTl);
                }
            }

            var oldEqMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            const string oldEqSql = @"
                SELECT eeq.EquipmentID,
                       COALESCE(NULLIF(eq.EquipmentName, ''), eeq.EquipmentID) AS EqName
                FROM dbo.EventEquipment eeq
                LEFT JOIN dbo.Equipment eq ON eq.EquipmentID = eeq.EquipmentID
                WHERE eeq.EventAppointmentID = @EvtId;";
            await using (var oqCmd = new SqlCommand(oldEqSql, conn, tx))
            {
                oqCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                await using var oqReader = await oqCmd.ExecuteReaderAsync(cancellationToken);
                while (await oqReader.ReadAsync(cancellationToken))
                {
                    var eqId = oqReader.GetString(0).Trim();
                    var eqName = oqReader.IsDBNull(1) ? eqId : oqReader.GetString(1).Trim();
                    oldEqMap[eqId] = eqName;
                }
            }

            // 1. Validate crew: Office Clerk exclusion + Approved Leaves + Scheduled Days Off
            await ValidateEmployeesAvailableAsync(conn, tx, newEmpIds, evtDate, cancellationToken);

            // 2. Validate crew buffer overlaps (2h prep + event + 1h post) against other active appointments on evtDate
            var newPrepStart = evtStart >= TimeSpan.FromHours(2) ? evtStart.Subtract(TimeSpan.FromHours(2)) : TimeSpan.Zero;
            var newPostEnd = evtEnd.Add(TimeSpan.FromHours(1));
            if (newPostEnd > new TimeSpan(23, 59, 59)) newPostEnd = new TimeSpan(23, 59, 59);

            var empParams = string.Join(",", newEmpIds.Select((_, i) => $"@rEmp{i}"));
            var crewConflictSql = $@"
                SELECT ea.EventAppointmentID, ea.EventName, ea.EventTime, ea.EventEndTime, ISNULL(ea.DurationMinutes, 60) AS DurationMinutes,
                       ee.EmployeeID, COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(e.EmployeeFirstName, '') + ' ' + ISNULL(e.EmployeeLastName, ''))), ''), ee.EmployeeID) AS EmpName
                FROM dbo.EventAppointment ea
                INNER JOIN dbo.EventEmployee ee ON ee.EventAppointmentID = ea.EventAppointmentID
                LEFT JOIN dbo.Employees e ON e.EmployeeID = ee.EmployeeID
                WHERE ee.EmployeeID IN ({empParams})
                  AND CAST(ea.EventDate AS date) = @CDate
                  AND ISNULL(ea.EventStatus, '') NOT IN ('Cancelled', 'Expired')
                  AND ea.EventAppointmentID <> @CEvtId
                  AND NOT EXISTS (
                      SELECT 1 FROM dbo.BookingNegotiation bn
                      WHERE bn.EventAppointmentID = ea.EventAppointmentID
                        AND bn.NegotiationStatus IN ('Deal_Lost', 'Cancelled')
                  );";
            await using (var ccCmd = new SqlCommand(crewConflictSql, conn, tx))
            {
                for (var i = 0; i < newEmpIds.Count; i++)
                    ccCmd.Parameters.AddWithValue($"@rEmp{i}", newEmpIds[i]);
                ccCmd.Parameters.AddWithValue("@CDate", evtDate.ToDateTime(TimeOnly.MinValue));
                ccCmd.Parameters.AddWithValue("@CEvtId", appointmentId);

                await using var ccReader = await ccCmd.ExecuteReaderAsync(cancellationToken);
                while (await ccReader.ReadAsync(cancellationToken))
                {
                    var exId = ccReader.GetString(0);
                    var exName = ccReader.IsDBNull(1) ? exId : ccReader.GetString(1);
                    var exStart = ccReader.GetFieldValue<TimeSpan>(2);
                    var exDur = ccReader.IsDBNull(4) ? 60 : Math.Max(15, ccReader.GetInt32(4));
                    var exEnd = !ccReader.IsDBNull(3) ? ccReader.GetFieldValue<TimeSpan>(3) : exStart.Add(TimeSpan.FromMinutes(exDur));
                    if (exEnd <= exStart) exEnd = exStart.Add(TimeSpan.FromMinutes(exDur));

                    var exPrepStart = exStart >= TimeSpan.FromHours(2) ? exStart.Subtract(TimeSpan.FromHours(2)) : TimeSpan.Zero;
                    var exPostEnd = exEnd.Add(TimeSpan.FromHours(1));
                    if (exPostEnd > new TimeSpan(23, 59, 59)) exPostEnd = new TimeSpan(23, 59, 59);

                    if (newPrepStart < exPostEnd && exPrepStart < newPostEnd)
                    {
                        var conflictEmpName = ccReader.IsDBNull(6) ? ccReader.GetString(5) : ccReader.GetString(6);
                        throw new InvalidOperationException(
                            $"Crew member '{conflictEmpName}' conflicts with '{exName}' ({exId}) on {evtDate:yyyy-MM-dd} within the 2h prep & 1h post buffer window.");
                    }
                }
            }

            // 3. Validate same-day equipment lock + Maintenance/Retired checks
            await ValidateEquipmentSameDayAvailableAsync(conn, tx, newEqIds, evtDate, appointmentId, null, cancellationToken);

            var effectiveTeamLeaderId = !string.IsNullOrWhiteSpace(request.TeamLeaderEmployeeId) &&
                                        newEmpIds.Contains(request.TeamLeaderEmployeeId.Trim(), StringComparer.OrdinalIgnoreCase)
                ? newEmpIds.First(id => string.Equals(id, request.TeamLeaderEmployeeId.Trim(), StringComparison.OrdinalIgnoreCase))
                : newEmpIds[0];

            // Resolve display names for new crew & equipment
            var empNames = new Dictionary<string, string>(oldEmpMap.ToDictionary(k => k.Key, v => v.Value.Name, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            var lookupEmpSql = $@"
                SELECT EmployeeID,
                       COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(EmployeeFirstName, '') + ' ' + ISNULL(EmployeeLastName, ''))), ''), EmployeeID) AS EmpName
                FROM dbo.Employees
                WHERE EmployeeID IN ({empParams});";
            await using (var leCmd = new SqlCommand(lookupEmpSql, conn, tx))
            {
                for (var i = 0; i < newEmpIds.Count; i++)
                    leCmd.Parameters.AddWithValue($"@rEmp{i}", newEmpIds[i]);
                await using var leReader = await leCmd.ExecuteReaderAsync(cancellationToken);
                while (await leReader.ReadAsync(cancellationToken))
                {
                    var id = leReader.GetString(0).Trim();
                    empNames[id] = leReader.IsDBNull(1) ? id : leReader.GetString(1).Trim();
                }
            }

            var eqNames = new Dictionary<string, string>(oldEqMap, StringComparer.OrdinalIgnoreCase);
            if (newEqIds.Count > 0)
            {
                var eqParams = string.Join(",", newEqIds.Select((_, i) => $"@rEq{i}"));
                var lookupEqSql = $"SELECT EquipmentID, COALESCE(NULLIF(EquipmentName, ''), EquipmentID) AS EqName FROM dbo.Equipment WHERE EquipmentID IN ({eqParams});";
                await using var lqCmd = new SqlCommand(lookupEqSql, conn, tx);
                for (var i = 0; i < newEqIds.Count; i++)
                    lqCmd.Parameters.AddWithValue($"@rEq{i}", newEqIds[i]);
                await using var lqReader = await lqCmd.ExecuteReaderAsync(cancellationToken);
                while (await lqReader.ReadAsync(cancellationToken))
                {
                    var id = lqReader.GetString(0).Trim();
                    eqNames[id] = lqReader.IsDBNull(1) ? id : lqReader.GetString(1).Trim();
                }
            }

            // 4. Replace EventEmployee assignments and update EventAppointment lead snapshot
            const string delEmp = "DELETE FROM dbo.EventEmployee WHERE EventAppointmentID = @EvtId;";
            await using (var dEmpCmd = new SqlCommand(delEmp, conn, tx))
            {
                dEmpCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                await dEmpCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var empId in newEmpIds)
            {
                var snapName = empNames.TryGetValue(empId, out var n) ? n : empId;
                var isTl = string.Equals(empId, effectiveTeamLeaderId, StringComparison.OrdinalIgnoreCase);
                const string insEmp = @"
                    IF EXISTS (SELECT 1 FROM dbo.Employees WHERE EmployeeID = @EmpId)
                       AND NOT EXISTS (SELECT 1 FROM dbo.EventEmployee WHERE EventAppointmentID = @EvtId AND EmployeeID = @EmpId)
                    BEGIN
                        IF COL_LENGTH(N'dbo.EventEmployee', N'AssignedEmployeeName') IS NOT NULL
                            INSERT INTO dbo.EventEmployee (EventAppointmentID, EmployeeID, AssignedEmployeeName, IsTeamLeader)
                            VALUES (@EvtId, @EmpId, @EmpName, @IsTL);
                        ELSE
                            INSERT INTO dbo.EventEmployee (EventAppointmentID, EmployeeID, IsTeamLeader)
                            VALUES (@EvtId, @EmpId, @IsTL);
                    END;";
                await using var eCmd = new SqlCommand(insEmp, conn, tx);
                eCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                eCmd.Parameters.AddWithValue("@EmpId", empId);
                eCmd.Parameters.AddWithValue("@EmpName", snapName);
                eCmd.Parameters.AddWithValue("@IsTL", isTl ? 1 : 0);
                await eCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            var tlName = empNames.TryGetValue(effectiveTeamLeaderId, out var resolvedTlName) ? resolvedTlName : effectiveTeamLeaderId;
            const string updApptLeadSql = @"
                UPDATE dbo.EventAppointment
                SET EmployeeID = @LeadEmpId,
                    AssignedEmployeeName = CASE
                        WHEN COL_LENGTH(N'dbo.EventAppointment', N'AssignedEmployeeName') IS NOT NULL THEN @LeadEmpName
                        ELSE AssignedEmployeeName
                    END
                WHERE EventAppointmentID = @EvtId;";
            await using (var uaCmd = new SqlCommand(updApptLeadSql, conn, tx))
            {
                uaCmd.Parameters.AddWithValue("@LeadEmpId", effectiveTeamLeaderId);
                uaCmd.Parameters.AddWithValue("@LeadEmpName", tlName);
                uaCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                await uaCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // 5. Sync EventEquipment preserving IsTaken/RequestedNotes on retained items
            var removedEqIds = oldEqMap.Keys.Except(newEqIds, StringComparer.OrdinalIgnoreCase).ToList();
            var addedEqIds = newEqIds.Except(oldEqMap.Keys, StringComparer.OrdinalIgnoreCase).ToList();

            if (removedEqIds.Count > 0)
            {
                var remParams = string.Join(",", removedEqIds.Select((_, i) => $"@delEq{i}"));
                var delEqSql = $"DELETE FROM dbo.EventEquipment WHERE EventAppointmentID = @EvtId AND EquipmentID IN ({remParams});";
                await using var dEqCmd = new SqlCommand(delEqSql, conn, tx);
                dEqCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                for (var i = 0; i < removedEqIds.Count; i++)
                    dEqCmd.Parameters.AddWithValue($"@delEq{i}", removedEqIds[i]);
                await dEqCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var eqId in addedEqIds)
            {
                const string insEq = @"
                    IF EXISTS (SELECT 1 FROM dbo.Equipment WHERE EquipmentID = @EqId)
                       AND NOT EXISTS (SELECT 1 FROM dbo.EventEquipment WHERE EventAppointmentID = @EvtId AND EquipmentID = @EqId)
                    BEGIN
                        INSERT INTO dbo.EventEquipment (EventAppointmentID, EquipmentID)
                        VALUES (@EvtId, @EqId);
                    END;";
                await using var eqCmd = new SqlCommand(insEq, conn, tx);
                eqCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                eqCmd.Parameters.AddWithValue("@EqId", eqId);
                await eqCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // 6. Build HistoryLog diff summary & insert audit record
            var crewAdded = newEmpIds.Except(oldEmpMap.Keys, StringComparer.OrdinalIgnoreCase)
                .Select(id => $"{(empNames.TryGetValue(id, out var n) ? n : id)} ({id})").ToList();
            var crewRemoved = oldEmpMap.Keys.Except(newEmpIds, StringComparer.OrdinalIgnoreCase)
                .Select(id => $"{(oldEmpMap.TryGetValue(id, out var oldInfo) ? oldInfo.Name : id)} ({id})").ToList();
            var eqAddedLabels = addedEqIds
                .Select(id => $"{(eqNames.TryGetValue(id, out var n) ? n : id)} ({id})").ToList();
            var eqRemovedLabels = removedEqIds
                .Select(id => $"{(oldEqMap.TryGetValue(id, out var n) ? n : id)} ({id})").ToList();

            var diffParts = new List<string>();
            if (crewAdded.Count > 0) diffParts.Add($"Crew added: {string.Join(", ", crewAdded)}");
            if (crewRemoved.Count > 0) diffParts.Add($"Crew removed: {string.Join(", ", crewRemoved)}");
            diffParts.Add($"Team Leader: {tlName} ({effectiveTeamLeaderId})");
            if (eqAddedLabels.Count > 0) diffParts.Add($"Equipment added: {string.Join(", ", eqAddedLabels)}");
            if (eqRemovedLabels.Count > 0) diffParts.Add($"Equipment removed: {string.Join(", ", eqRemovedLabels)}");

            var details = $"Updated event roster for '{evtName}' (#{appointmentId}) on {evtDate:yyyy-MM-dd} — {string.Join("; ", diffParts)}.";
            var changedByType = request.ActorRole?.Trim() switch
            {
                "Admin" => "Admin",
                "SystemAdmin" => "SystemAdmin",
                "Employee" or "Office Clerk" => "Employee",
                "Customer" => "Customer",
                _ => "System"
            };

            const string logSql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    DECLARE @SafeChangedByType nvarchar(20) = CASE
                        WHEN @ChangedByType IN ('Admin', 'SystemAdmin', 'Employee', 'Customer', 'System') THEN @ChangedByType
                        ELSE 'System'
                    END;
                    DECLARE @SafeEmpId nvarchar(30) = CASE
                        WHEN @SafeChangedByType = 'Employee' AND @ActorId IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.Employees WHERE EmployeeID = @ActorId)
                        THEN @ActorId ELSE NULL END;
                    DECLARE @SafeAdminId nvarchar(30) = CASE
                        WHEN @SafeChangedByType IN ('Admin', 'SystemAdmin') AND @ActorId IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.Admin WHERE AdminID = @ActorId)
                        THEN @ActorId ELSE NULL END;

                    INSERT INTO dbo.HistoryLog (
                        ActionType, EntityName, EntityID, ActionDetails,
                        EventAppointmentID, ChangedByType, ChangedByEmployeeID, ChangedByAdminID, LoggedAt
                    )
                    VALUES (
                        'UPDATE', 'EventAppointment', @EvtId, @Details,
                        @EvtId, @SafeChangedByType, @SafeEmpId, @SafeAdminId, SYSUTCDATETIME()
                    );
                END;";

            await using (var logCmd = new SqlCommand(logSql, conn, tx))
            {
                logCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                logCmd.Parameters.AddWithValue("@Details", details);
                logCmd.Parameters.AddWithValue("@ChangedByType", changedByType);
                logCmd.Parameters.AddWithValue("@ActorId", string.IsNullOrWhiteSpace(request.ActorId) ? DBNull.Value : request.ActorId.Trim());
                await logCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }








    public async Task<EventAppointmentDto> UpdateBookingStatusFromPaymentAsync(
        string appointmentId,
        UpdatePaymentBookingStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var appt = await GetAppointmentByIdAsync(appointmentId, cancellationToken);
        if (appt == null)
            throw new KeyNotFoundException($"Event appointment {appointmentId} not found.");

        if (string.Equals(request.Status, "Deal_Won", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(request.Status, "Appointment Booked", StringComparison.OrdinalIgnoreCase))
        {
            await using var connWon = await OpenConnectionAsync(cancellationToken);
            await using var cmdWon = connWon.CreateCommand();
            cmdWon.CommandText = @"
                UPDATE dbo.EventAppointment
                SET EventStatus = 'Confirmed',
                    DownpaymentStatus = 'Paid',
                    DownpaymentPaidAt = ISNULL(DownpaymentPaidAt, SYSUTCDATETIME()),
                    LockExpiresAt = NULL,
                    HoldExpiresAt = NULL
                WHERE EventAppointmentID = @EvtId;

                IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.BookingNegotiation
                    SET NegotiationStatus = 'Appointment Booked',
                        EventAppointmentID = COALESCE(EventAppointmentID, @EvtId)
                    WHERE EventAppointmentID = @EvtId OR NegotiationID = @NegId;
                END";
            cmdWon.Parameters.AddWithValue("@EvtId", appointmentId);
            cmdWon.Parameters.AddWithValue("@NegId", (object?)appt.NegotiationId ?? DBNull.Value);
            await cmdWon.ExecuteNonQueryAsync(cancellationToken);

            var invoiceId = appt.XenditInvoiceId ?? $"inv_sim_{appointmentId}_{Guid.NewGuid():N[..6]}";
            var updated = await VerifyDownpaymentAsync(appointmentId, invoiceId, request.PaymentMethod ?? "QR Ph", cancellationToken);
            return updated;
        }

        await using var conn = await OpenConnectionAsync(cancellationToken);

        if (string.Equals(request.Status, "Deal_Lost", StringComparison.OrdinalIgnoreCase))
        {
            await using var cmdLost = conn.CreateCommand();
            cmdLost.CommandText = @"
                IF OBJECT_ID(N'dbo.EventEmployee', N'U') IS NOT NULL
                    DELETE FROM dbo.EventEmployee WHERE EventAppointmentID = @EvtId;

                IF OBJECT_ID(N'dbo.EventEquipment', N'U') IS NOT NULL
                    DELETE FROM dbo.EventEquipment WHERE EventAppointmentID = @EvtId;

                IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.EventAppointment
                    SET EventStatus = 'Cancelled',
                        DownpaymentStatus = 'Cancelled',
                        LockExpiresAt = NULL,
                        HoldExpiresAt = NULL,
                        QrString = NULL
                    WHERE EventAppointmentID = @EvtId;
                END

                IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.BookingNegotiation
                    SET NegotiationStatus = 'Deal_Lost'
                    WHERE EventAppointmentID = @EvtId OR NegotiationID = @NegId;
                END

                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (
                        ActionType, EntityName, EntityID, ActionDetails,
                        EventAppointmentID, ChangedByType, LoggedAt
                    )
                    VALUES (
                        'STATUS_CHANGE', 'EventAppointment', @EvtId,
                        @Details,
                        @EvtId, 'Customer', SYSUTCDATETIME()
                    );
                END";
            cmdLost.Parameters.AddWithValue("@EvtId", appointmentId);
            cmdLost.Parameters.AddWithValue("@NegId", (object?)appt.NegotiationId ?? DBNull.Value);
            cmdLost.Parameters.AddWithValue("@Details", $"Customer marked deal lost during payment: {request.Reason ?? "Customer declined offer"}");
            await cmdLost.ExecuteNonQueryAsync(cancellationToken);

            appt.EventStatus = "Cancelled";
            appt.DownpaymentStatus = "Cancelled";
            return appt;
        }
        else if (string.Equals(request.Status, "Rescheduled", StringComparison.OrdinalIgnoreCase))
        {
            var newDate = request.NewDate ?? appt.EventDate;
            var newTime = request.NewTime ?? appt.EventTime;

            // Check if designated clerk has day off on newDate
            await using var chkCmd = conn.CreateCommand();
            chkCmd.CommandText = @"
                SELECT TOP 1 e.DaysOff
                FROM dbo.BookingNegotiation bn
                JOIN dbo.Employees e ON bn.EmployeeID = e.EmployeeID
                WHERE bn.EventAppointmentID = @EvtId OR bn.NegotiationID = @NegId;";
            chkCmd.Parameters.AddWithValue("@EvtId", appointmentId);
            chkCmd.Parameters.AddWithValue("@NegId", (object?)appt.NegotiationId ?? DBNull.Value);
            var daysOffObj = await chkCmd.ExecuteScalarAsync(cancellationToken);
            var daysOff = daysOffObj is string dStr ? dStr : "Sunday";
            var dayName = newDate.DayOfWeek.ToString();
            if (daysOff.Contains(dayName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Cannot reschedule: The Office Clerk has a scheduled Day Off on {newDate:dddd, MMMM dd, yyyy}.");
            }

            // Check if designated clerk has an approved leave covering newDate
            await using var lvCmd = conn.CreateCommand();
            lvCmd.CommandText = @"
                IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NOT NULL
                BEGIN
                    SELECT TOP 1 el.LeaveType
                    FROM dbo.BookingNegotiation bn
                    JOIN dbo.EmployeeLeaves el ON bn.EmployeeID = el.EmployeeStudioId
                    WHERE (bn.EventAppointmentID = @EvtId OR bn.NegotiationID = @NegId)
                      AND el.Status = 'Approved'
                      AND @NewDate BETWEEN el.StartDate AND el.EndDate;
                END";
            lvCmd.Parameters.AddWithValue("@EvtId", appointmentId);
            lvCmd.Parameters.AddWithValue("@NegId", (object?)appt.NegotiationId ?? DBNull.Value);
            lvCmd.Parameters.AddWithValue("@NewDate", newDate.ToDateTime(TimeOnly.MinValue));
            var leaveTypeObj = await lvCmd.ExecuteScalarAsync(cancellationToken);
            if (leaveTypeObj is string leaveType && !string.IsNullOrWhiteSpace(leaveType))
            {
                throw new InvalidOperationException($"Cannot reschedule: The Office Clerk is on approved {leaveType} on {newDate:dddd, MMMM dd, yyyy}.");
            }

            await using var cmdResched = conn.CreateCommand();
            cmdResched.CommandText = @"
                IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.EventAppointment
                    SET EventDate = @NewDate,
                        EventTime = @NewTime,
                        EventStatus = 'Pending',
                        HoldExpiresAt = DATEADD(minute, 30, SYSUTCDATETIME())
                    WHERE EventAppointmentID = @EvtId;
                END

                IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.BookingNegotiation
                    SET NegotiationStatus = 'Rescheduled',
                        ConsultationDate = @NewDate,
                        ConsultationTime = @NewTime,
                        InquiryNotes = ISNULL(InquiryNotes, '') + ' [Rescheduled: ' + @Reason + ']'
                    WHERE EventAppointmentID = @EvtId OR NegotiationID = @NegId;
                END

                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (
                        ActionType, EntityName, EntityID, ActionDetails,
                        EventAppointmentID, ChangedByType, LoggedAt
                    )
                    VALUES (
                        'STATUS_CHANGE', 'EventAppointment', @EvtId,
                        @Details,
                        @EvtId, 'Customer', SYSUTCDATETIME()
                    );
                END";
            cmdResched.Parameters.AddWithValue("@EvtId", appointmentId);
            cmdResched.Parameters.AddWithValue("@NegId", (object?)appt.NegotiationId ?? DBNull.Value);
            cmdResched.Parameters.AddWithValue("@NewDate", newDate.ToDateTime(TimeOnly.MinValue));
            cmdResched.Parameters.AddWithValue("@NewTime", newTime.ToTimeSpan());
            cmdResched.Parameters.AddWithValue("@Reason", request.Reason ?? "Customer requested reschedule");
            cmdResched.Parameters.AddWithValue("@Details", $"Appointment rescheduled to {newDate:yyyy-MM-dd} {newTime:HH:mm} by customer during payment. Reason: {request.Reason}");
            await cmdResched.ExecuteNonQueryAsync(cancellationToken);

            appt.EventDate = newDate;
            appt.EventTime = newTime;
            appt.EventStatus = "Pending";
            appt.HoldExpiresAt = DateTime.UtcNow.AddMinutes(30);
            return appt;
        }

        throw new ArgumentException($"Unsupported status: {request.Status}");
    }

    public async Task<IReadOnlyList<TeamLeaderEventDto>> GetTeamLeaderEventsAsync(
        string? employeeId = null,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await AutoTriggerPostEventsOnConnectionAsync(conn, cancellationToken);

        const string sql = @"
            SELECT DISTINCT
                ea.EventAppointmentID,
                ea.EventDate,
                ea.EventTime,
                ISNULL(c.CustomerPhoneNumber, '') AS CustomerPhone
            FROM dbo.EventAppointment ea
            LEFT JOIN dbo.EventEmployee ee ON ee.EventAppointmentID = ea.EventAppointmentID
            LEFT JOIN dbo.Customer c ON c.CustomerID = ea.CustomerID
            WHERE (
                    (ISNULL(ee.IsTeamLeader, 0) = 1
                     AND (@EmpId IS NULL OR LTRIM(RTRIM(@EmpId)) = '' OR LTRIM(RTRIM(ee.EmployeeID)) = LTRIM(RTRIM(@EmpId))))
                    OR (NOT EXISTS (
                            SELECT 1 FROM dbo.EventEmployee eeTl
                            WHERE eeTl.EventAppointmentID = ea.EventAppointmentID
                              AND ISNULL(eeTl.IsTeamLeader, 0) = 1
                        )
                        AND (@EmpId IS NULL OR LTRIM(RTRIM(@EmpId)) = '' OR LTRIM(RTRIM(ea.EmployeeID)) = LTRIM(RTRIM(@EmpId))))
                  )
              AND ISNULL(ea.EventStatus, '') NOT IN ('Cancelled', 'Expired', 'Delivered', 'Complete', 'Completed', 'Shot')
              AND ISNULL(ea.FinalPaymentStatus, '') NOT IN ('Paid', 'Payment Verification (QR:Paid)')
              AND ((ea.EventStatus NOT IN ('Pending Payment', 'Awaiting_Downpayment'))
                   OR ISNULL(ea.DownpaymentStatus, 'Pending') = 'Paid'
                   OR NULLIF(ea.FinalPaymentReferenceId, '') IS NOT NULL)
            ORDER BY
                CASE ea.EventStatus
                    WHEN 'Ongoing' THEN 1
                    WHEN 'On Going' THEN 1
                    WHEN 'In Execution' THEN 1
                    WHEN 'Dispatch' THEN 2
                    WHEN 'Preparation' THEN 3
                    WHEN 'Preparing' THEN 3
                    WHEN 'Ready' THEN 3
                    WHEN 'Event Done' THEN 4
                    WHEN 'Post Event' THEN 4
                    WHEN 'Pending Payment' THEN 5
                    WHEN 'Pending Verification' THEN 5
                    WHEN 'Paid Cash Pending Verification' THEN 5
                    WHEN 'Payment Verification (Cash)' THEN 5
                    WHEN 'Confirmed' THEN 6
                    ELSE 7
                END,
                ea.EventDate ASC,
                ea.EventTime ASC;";

        var rows = new List<(string AppointmentId, string Phone)>();
        await using (var cmd = new SqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("@EmpId", (object?)employeeId ?? DBNull.Value);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((reader.GetString(0), reader.IsDBNull(3) ? string.Empty : reader.GetString(3)));
            }
        }

        var result = new List<TeamLeaderEventDto>();
        foreach (var (apptId, phone) in rows)
        {
            var detail = await GetAppointmentByIdOnConnectionAsync(conn, apptId, cancellationToken);
            if (detail == null) continue;

            var triggerAt = detail.EventDate.ToDateTime(detail.EventTime).AddHours(1);
            result.Add(new TeamLeaderEventDto
            {
                EventAppointmentId = detail.EventAppointmentId,
                NegotiationId = detail.NegotiationId,
                CustomerId = detail.CustomerId,
                CustomerName = detail.CustomerName,
                CustomerEmail = detail.CustomerEmail,
                CustomerPhone = phone,
                EventName = detail.EventName,
                EventDescription = detail.EventDescription,
                EventDate = detail.EventDate,
                EventTime = detail.EventTime,
                EventEndTime = detail.EventEndTime,
                DurationMinutes = detail.DurationMinutes,
                PrepBufferMinutes = detail.PrepBufferMinutes,
                PostBufferMinutes = detail.PostBufferMinutes,
                EventLocation = detail.EventLocation,
                EventStatus = detail.EventStatus,
                TotalAmount = detail.TotalAmount,
                DownpaymentAmount = detail.DownpaymentAmount,
                FinalBalanceAmount = detail.FinalBalanceAmount,
                DownpaymentStatus = detail.DownpaymentStatus,
                HoldExpiresAt = detail.HoldExpiresAt,
                LockExpiresAt = detail.LockExpiresAt,
                QrString = detail.QrString,
                XenditInvoiceUrl = detail.XenditInvoiceUrl,
                XenditInvoiceId = detail.XenditInvoiceId,
                XenditExternalId = detail.XenditExternalId,
                FinalPaymentMethod = detail.FinalPaymentMethod,
                FinalPaymentStatus = detail.FinalPaymentStatus,
                FinalQrString = detail.FinalQrString,
                FinalPaymentReferenceId = detail.FinalPaymentReferenceId,
                FinalPaymentPaidAt = detail.FinalPaymentPaidAt,
                AssignedEmployees = detail.AssignedEmployees,
                AssignedEquipment = detail.AssignedEquipment,
                SelectedServices = detail.SelectedServices,
                OvertimeCharges = detail.OvertimeCharges,
                Transactions = detail.Transactions,
                CreatedAt = detail.CreatedAt,
                IsTeamLeader = true,
                PostEventTriggerAt = triggerAt,
                IsPostEventAutoTriggered = string.Equals(detail.EventStatus, "Post Event", StringComparison.OrdinalIgnoreCase)
            });
        }

        return TeamLeaderMonitorSelector.SelectUpcomingAndOngoing(result, GetStudioLocalNow());
    }

    public async Task<EventAppointmentDto> UpdateTeamLeaderEventStatusAsync(
        string appointmentId,
        UpdateTeamLeaderStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var rawStatus = (request.Status ?? string.Empty).Trim();
        var normalized = rawStatus switch
        {
            var s when s.Equals("Early", StringComparison.OrdinalIgnoreCase) => "Preparing",
            var s when s.Equals("Late", StringComparison.OrdinalIgnoreCase) => "Preparing",
            var s when s.Equals("Confirmed", StringComparison.OrdinalIgnoreCase) => "Confirmed",
            var s when s.Equals("Preparation", StringComparison.OrdinalIgnoreCase) => "Preparation",
            var s when s.Equals("Preparing", StringComparison.OrdinalIgnoreCase) => "Preparing",
            var s when s.Equals("Dispatch", StringComparison.OrdinalIgnoreCase) => "Dispatch",
            var s when s.Equals("Ongoing", StringComparison.OrdinalIgnoreCase) || s.Equals("Event Started", StringComparison.OrdinalIgnoreCase) => "Ongoing",
            var s when s.Equals("On Going", StringComparison.OrdinalIgnoreCase) => "On Going",
            var s when s.Equals("Event Done", StringComparison.OrdinalIgnoreCase) || s.Equals("EventDone", StringComparison.OrdinalIgnoreCase) => "Event Done",
            var s when s.Equals("Post Event", StringComparison.OrdinalIgnoreCase) || s.Equals("PostEvent", StringComparison.OrdinalIgnoreCase) => "Post Event",
            var s when s.Equals("Pending Payment", StringComparison.OrdinalIgnoreCase) || s.Equals("PendingPayment", StringComparison.OrdinalIgnoreCase) => "Pending Payment",
            var s when s.Equals("Complete", StringComparison.OrdinalIgnoreCase) => "Complete",
            var s when s.Equals("Completed", StringComparison.OrdinalIgnoreCase) => "Completed",
            _ => throw new ArgumentException($"Unsupported Team Leader event stage: '{request.Status}'. Allowed stages: Early, Late, Preparing, Ongoing, Event Done, Pending Payment.")
        };

        var effectiveArrival = !string.IsNullOrWhiteSpace(request.ArrivalStatus)
            ? request.ArrivalStatus.Trim()
            : (rawStatus.Equals("Early", StringComparison.OrdinalIgnoreCase) ? "Early"
                : rawStatus.Equals("Late", StringComparison.OrdinalIgnoreCase) ? "Late"
                : null);

        var historyNote = !string.IsNullOrWhiteSpace(effectiveArrival)
            ? $"Team Leader marked arrival ({effectiveArrival}) and updated event status to {normalized}."
            : $"Team Leader updated event execution status to {normalized}.";

        await using var conn = await OpenConnectionAsync(cancellationToken);

        const string sql = @"
            UPDATE dbo.EventAppointment
            SET EventStatus = @Status,
                LockExpiresAt = CASE WHEN @Status = 'Pending Payment' AND ISNULL(DownpaymentStatus, 'Pending') = 'Paid' THEN NULL ELSE LockExpiresAt END
            WHERE EventAppointmentID = @EvtId;

            IF @@ROWCOUNT > 0 AND OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
            BEGIN
                INSERT INTO dbo.HistoryLog (
                    ActionType, EntityName, EntityID, ActionDetails,
                    EventAppointmentID, ChangedByType, ChangedByEmployeeID, LoggedAt
                )
                VALUES (
                    'STATUS_CHANGE', 'EventAppointment', @EvtId,
                    @HistoryNote,
                    @EvtId, 'Employee', NULLIF(@EmpId, ''), SYSUTCDATETIME()
                );
            END;";

        await using (var cmd = new SqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("@Status", normalized);
            cmd.Parameters.AddWithValue("@HistoryNote", historyNote);
            cmd.Parameters.AddWithValue("@EvtId", appointmentId);
            cmd.Parameters.AddWithValue("@EmpId", request.EmployeeId ?? string.Empty);
            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
            {
                var existing = await GetAppointmentByIdOnConnectionAsync(conn, appointmentId, cancellationToken);
                if (existing == null)
                    throw new KeyNotFoundException($"Event appointment {appointmentId} not found.");
            }
        }

        return (await GetAppointmentByIdOnConnectionAsync(conn, appointmentId, cancellationToken))!;
    }

    public async Task NotifyTeamLeaderFinalQrPaidAsync(
        string appointmentId,
        decimal amountPaid,
        string? paymentChannel,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT TOP 1
                ea.EventName,
                ea.EventDate,
                ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(c.CustomerFirstName, '') + ' ' + ISNULL(c.CustomerLastName, ''))), ''), ea.CustomerID) AS CustomerName,
                ISNULL(c.CustomerEmail, '') AS CustomerEmail,
                ISNULL(ea.FinalPaymentReferenceId, ea.EventAppointmentID + '-FINAL') AS RefId,
                ISNULL(tl.EmployeeEmail, '') AS LeaderEmail,
                ISNULL(tl.LeaderName, 'Team Leader') AS LeaderName
            FROM dbo.EventAppointment ea
            LEFT JOIN dbo.Customer c ON c.CustomerID = ea.CustomerID
            OUTER APPLY (
                SELECT TOP 1
                    e.EmployeeEmail,
                    ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(e.EmployeeFirstName, '') + ' ' + ISNULL(e.EmployeeLastName, ''))), ''), 'Team Leader') AS LeaderName
                FROM dbo.EventEmployee ee
                INNER JOIN dbo.Employees e ON e.EmployeeID = ee.EmployeeID
                WHERE ee.EventAppointmentID = ea.EventAppointmentID
                  AND NULLIF(LTRIM(RTRIM(ISNULL(e.EmployeeEmail, ''))), '') IS NOT NULL
                ORDER BY ISNULL(ee.IsTeamLeader, 0) DESC, ee.EmployeeID ASC
            ) tl
            WHERE ea.EventAppointmentID = @EvtId;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@EvtId", appointmentId);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
            return;

        var eventName = r.GetString(0);
        var eventDate = DateOnly.FromDateTime(r.GetDateTime(1));
        var customerName = r.GetString(2);
        var customerEmail = r.IsDBNull(3) ? null : r.GetString(3);
        var refId = r.GetString(4);
        var leaderEmail = r.IsDBNull(5) ? null : r.GetString(5);
        var leaderName = r.GetString(6);

        var channelLabel = string.IsNullOrWhiteSpace(paymentChannel) ? "QR Ph" : paymentChannel;
        var portalBase = ResolvePortalBaseUrl();

        if (!string.IsNullOrWhiteSpace(leaderEmail))
        {
            try
            {
                var body = StudioEmailTemplates.TeamLeaderQrPaymentPaidNotification(
                    leaderName,
                    appointmentId,
                    refId,
                    eventName,
                    customerName,
                    eventDate,
                    amountPaid,
                    channelLabel,
                    $"{portalBase}/staff");

                await _emailService.TrySendEmailAsync(
                    leaderEmail,
                    $"Paid: Customer QR Final Payment Received for {eventName} (#{appointmentId})",
                    body,
                    isHtml: true,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispatch Team Leader final QR confirmation email for {AppointmentId}", appointmentId);
            }
        }

        if (!string.IsNullOrWhiteSpace(customerEmail))
        {
            try
            {
                var customerBody = StudioEmailTemplates.CustomerFinalQrPaymentConfirmation(
                    customerName,
                    appointmentId,
                    refId,
                    eventName,
                    eventDate,
                    amountPaid,
                    channelLabel,
                    $"{portalBase}/my/bookings");

                await _emailService.TrySendEmailAsync(
                    customerEmail,
                    $"Payment Confirmed & Event Completed: {eventName} (#{appointmentId})",
                    customerBody,
                    isHtml: true,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispatch Customer final QR confirmation email for {AppointmentId}", appointmentId);
            }
        }
    }

    private static async Task ValidateEmployeesAvailableAsync(
        SqlConnection conn,
        SqlTransaction? tx,
        IEnumerable<string>? employeeIds,
        DateOnly eventDate,
        CancellationToken cancellationToken)
    {
        var ids = employeeIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids == null || ids.Count == 0)
            return;

        var empParams = string.Join(",", ids.Select((_, i) => $"@vEmp{i}"));
        var eventDateValue = eventDate.ToDateTime(TimeOnly.MinValue);

        // 0. Reject any employee holding the Office Clerk role/specialization from crew dispatch
        var clerkCheckSql = $@"
            SELECT TOP 1
                e.EmployeeID,
                COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(e.EmployeeFirstName, '') + ' ' + ISNULL(e.EmployeeLastName, ''))), ''), e.EmployeeID) AS EmpName
            FROM dbo.Employees e
            LEFT JOIN dbo.EmployeeJointSpecialization ejs ON ejs.EmployeeID = e.EmployeeID
            LEFT JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID
            WHERE e.EmployeeID IN ({empParams})
              AND (
                  e.EmployeeID LIKE 'CLK-%'
                  OR UPPER(LTRIM(RTRIM(COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization, '')))) = 'OFFICE CLERK'
                  OR UPPER(ISNULL(e.Specializations, '')) LIKE '%OFFICE CLERK%'
              );";

        await using (var clkCmd = new SqlCommand(clerkCheckSql, conn, tx))
        {
            for (var i = 0; i < ids.Count; i++)
                clkCmd.Parameters.AddWithValue($"@vEmp{i}", ids[i]);

            await using var clkReader = await clkCmd.ExecuteReaderAsync(cancellationToken);
            if (await clkReader.ReadAsync(cancellationToken))
            {
                var empId = clkReader.GetString(0);
                var empName = clkReader.IsDBNull(1) ? empId : clkReader.GetString(1);
                throw new InvalidOperationException(
                    $"Cannot assign {empName} ({empId}) to event crew: Office Clerks are disabled from event crew dispatch.");
            }
        }

        // 1. Check approved leaves covering eventDate
        var leaveSql = $@"
            IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NOT NULL
            BEGIN
                SELECT TOP 1
                    el.EmployeeStudioId,
                    COALESCE(NULLIF(el.EmployeeName, ''), LTRIM(RTRIM(ISNULL(e.EmployeeFirstName, '') + ' ' + ISNULL(e.EmployeeLastName, ''))), el.EmployeeStudioId) AS EmpName,
                    el.LeaveType,
                    el.StartDate,
                    el.EndDate
                FROM dbo.EmployeeLeaves el
                LEFT JOIN dbo.Employees e ON e.EmployeeID = el.EmployeeStudioId
                WHERE el.EmployeeStudioId IN ({empParams})
                  AND el.Status = 'Approved'
                  AND @VDate BETWEEN el.StartDate AND el.EndDate;
            END";

        await using (var lvCmd = new SqlCommand(leaveSql, conn, tx))
        {
            for (var i = 0; i < ids.Count; i++)
                lvCmd.Parameters.AddWithValue($"@vEmp{i}", ids[i]);
            lvCmd.Parameters.AddWithValue("@VDate", eventDateValue);

            await using var lvReader = await lvCmd.ExecuteReaderAsync(cancellationToken);
            if (await lvReader.ReadAsync(cancellationToken))
            {
                var empId = lvReader.GetString(0);
                var empName = lvReader.IsDBNull(1) ? empId : lvReader.GetString(1);
                var leaveType = lvReader.IsDBNull(2) ? "Leave" : lvReader.GetString(2);
                var start = DateOnly.FromDateTime(lvReader.GetDateTime(3));
                var end = DateOnly.FromDateTime(lvReader.GetDateTime(4));
                throw new InvalidOperationException(
                    $"Cannot assign {empName} ({empId}): Employee is on approved {leaveType} ({start:MMM d, yyyy} - {end:MMM d, yyyy}).");
            }
        }

        // 2. Check scheduled DaysOff
        var dayOffSql = $@"
            IF COL_LENGTH(N'dbo.Employees', N'DaysOff') IS NOT NULL
            BEGIN
                SELECT TOP 1
                    e.EmployeeID,
                    COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(e.EmployeeFirstName, '') + ' ' + ISNULL(e.EmployeeLastName, ''))), ''), e.EmployeeID) AS EmpName,
                    ISNULL(e.DaysOff, 'Sunday') AS DaysOff
                FROM dbo.Employees e
                WHERE e.EmployeeID IN ({empParams})
                  AND CHARINDEX(DATENAME(dw, @VDate), ISNULL(e.DaysOff, 'Sunday')) > 0;
            END";

        await using (var doCmd = new SqlCommand(dayOffSql, conn, tx))
        {
            for (var i = 0; i < ids.Count; i++)
                doCmd.Parameters.AddWithValue($"@vEmp{i}", ids[i]);
            doCmd.Parameters.AddWithValue("@VDate", eventDateValue);

            await using var doReader = await doCmd.ExecuteReaderAsync(cancellationToken);
            if (await doReader.ReadAsync(cancellationToken))
            {
                var empId = doReader.GetString(0);
                var empName = doReader.IsDBNull(1) ? empId : doReader.GetString(1);
                throw new InvalidOperationException(
                    $"Cannot assign {empName} ({empId}): {eventDate:dddd, MMM d, yyyy} is their scheduled Day Off.");
            }
        }
    }

    private static async Task ValidateEquipmentSameDayAvailableAsync(
        SqlConnection conn,
        SqlTransaction? tx,
        IEnumerable<string>? equipmentIds,
        DateOnly eventDate,
        string? excludeAppointmentId,
        string? excludeNegotiationId,
        CancellationToken cancellationToken)
    {
        var ids = equipmentIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids == null || ids.Count == 0)
            return;

        var eqParams = string.Join(",", ids.Select((_, i) => $"@vEq{i}"));
        var eventDateValue = eventDate.ToDateTime(TimeOnly.MinValue);

        // 1. Ensure equipment is not in Maintenance or Retired
        var statusSql = $@"
            SELECT TOP 1 EquipmentID, EquipmentName, ISNULL(EquipmentStatus, 'Available') AS EquipmentStatus
            FROM dbo.Equipment
            WHERE EquipmentID IN ({eqParams})
              AND UPPER(ISNULL(EquipmentStatus, '')) IN ('MAINTENANCE', 'RETIRED', 'NEEDS REPLACEMENT');";

        await using (var sCmd = new SqlCommand(statusSql, conn, tx))
        {
            for (var i = 0; i < ids.Count; i++)
                sCmd.Parameters.AddWithValue($"@vEq{i}", ids[i]);

            await using var sReader = await sCmd.ExecuteReaderAsync(cancellationToken);
            if (await sReader.ReadAsync(cancellationToken))
            {
                var eqId = sReader.GetString(0);
                var eqName = sReader.IsDBNull(1) ? eqId : sReader.GetString(1);
                var status = sReader.IsDBNull(2) ? "Unavailable" : sReader.GetString(2);
                throw new InvalidOperationException($"Equipment '{eqName}' ({eqId}) is currently '{status}' and cannot be assigned.");
            }
        }

        // 2. Enforce entire calendar-day lock across all non-cancelled, non-expired events on eventDate
        var conflictSql = $@"
            SELECT TOP 1
                eeq.EquipmentID,
                COALESCE(NULLIF(eq.EquipmentName, ''), eeq.EquipmentID) AS EquipmentName,
                ea.EventAppointmentID,
                COALESCE(NULLIF(ea.EventName, ''), ea.EventAppointmentID) AS EventName
            FROM dbo.EventEquipment eeq
            INNER JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = eeq.EventAppointmentID
            LEFT JOIN dbo.Equipment eq ON eq.EquipmentID = eeq.EquipmentID
            WHERE eeq.EquipmentID IN ({eqParams})
              AND CAST(ea.EventDate AS date) = @CDate
              AND ISNULL(ea.EventStatus, '') NOT IN ('Cancelled', 'Expired')
              AND (@CEvtId IS NULL OR ea.EventAppointmentID <> @CEvtId)
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.BookingNegotiation bn
                  WHERE bn.EventAppointmentID = ea.EventAppointmentID
                    AND ((@CNeg IS NOT NULL AND bn.NegotiationID = @CNeg) OR bn.NegotiationStatus IN ('Deal_Lost', 'Cancelled'))
              );";

        await using (var cCmd = new SqlCommand(conflictSql, conn, tx))
        {
            for (var i = 0; i < ids.Count; i++)
                cCmd.Parameters.AddWithValue($"@vEq{i}", ids[i]);
            cCmd.Parameters.AddWithValue("@CDate", eventDateValue);
            cCmd.Parameters.AddWithValue("@CEvtId", string.IsNullOrWhiteSpace(excludeAppointmentId) ? DBNull.Value : excludeAppointmentId.Trim());
            cCmd.Parameters.AddWithValue("@CNeg", string.IsNullOrWhiteSpace(excludeNegotiationId) ? DBNull.Value : excludeNegotiationId.Trim());

            await using var cReader = await cCmd.ExecuteReaderAsync(cancellationToken);
            if (await cReader.ReadAsync(cancellationToken))
            {
                var eqId = cReader.GetString(0);
                var eqName = cReader.IsDBNull(1) ? eqId : cReader.GetString(1);
                var exId = cReader.GetString(2);
                var exName = cReader.IsDBNull(3) ? exId : cReader.GetString(3);
                throw new InvalidOperationException(
                    $"Equipment '{eqName}' ({eqId}) is already booked for '{exName}' ({exId}) on {eventDate:yyyy-MM-dd} and is unavailable for the entire calendar day.");
            }
        }
    }

    public async Task<bool> IsTeamLeaderForEventAsync(
        string appointmentId,
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appointmentId) || string.IsNullOrWhiteSpace(employeeId))
            return false;

        await using var conn = await OpenConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT COUNT(1)
            FROM dbo.EventEmployee
            WHERE EventAppointmentID = @EvtId
              AND EmployeeID = @EmpId
              AND ISNULL(IsTeamLeader, 0) = 1;";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@EvtId", appointmentId);
        cmd.Parameters.AddWithValue("@EmpId", employeeId);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0);
        return count > 0;
    }

    public async Task<int> AutoTriggerPostEventsAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        return await AutoTriggerPostEventsOnConnectionAsync(conn, cancellationToken);
    }

    private static async Task<int> AutoTriggerPostEventsOnConnectionAsync(SqlConnection conn, CancellationToken cancellationToken)
    {
        try
        {
            const string sql = @"
                DECLARE @PhtNow datetime2 = DATEADD(HOUR, 8, SYSUTCDATETIME());
                DECLARE @Triggered TABLE (EventAppointmentID nvarchar(30));

                UPDATE dbo.EventAppointment
                SET EventStatus = 'Post Event'
                OUTPUT inserted.EventAppointmentID INTO @Triggered
                WHERE EventStatus IN ('Ongoing', 'On Going')
                  AND DATEADD(HOUR, 1, DATEADD(SECOND, DATEDIFF(SECOND, CAST('00:00:00' AS time), EventTime), CAST(EventDate AS datetime2))) <= @PhtNow;

                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ActionDetails, EventAppointmentID, ChangedByType, LoggedAt)
                    SELECT 'STATUS_CHANGE', 'EventAppointment', t.EventAppointmentID,
                           'Automatically transitioned event to Post Event 1 hour after scheduled start.',
                           t.EventAppointmentID, 'System', SYSUTCDATETIME()
                    FROM @Triggered t;

                SELECT COUNT(1) FROM @Triggered;";

            await using var cmd = new SqlCommand(sql, conn);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0);
        }
        catch
        {
            return 0;
        }
    }

    public async Task<EventAppointmentDto> MarkEquipmentTakenAsync(
        string appointmentId,
        MarkEquipmentTakenRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            var existing = await GetAppointmentByIdOnConnectionAsync(conn, appointmentId, cancellationToken);
            if (existing == null)
                throw new KeyNotFoundException($"Event appointment {appointmentId} not found.");

            var markIds = request.EventEquipmentIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
            if (markIds.Count > 0)
            {
                var paramNames = markIds.Select((_, idx) => $"@ee{idx}").ToArray();
                var updItemsSql = $@"
                    UPDATE dbo.EventEquipment
                    SET IsTaken = @Taken,
                        TakenAt = CASE WHEN @Taken = 1 THEN SYSUTCDATETIME() ELSE NULL END,
                        TakenByEmployeeID = CASE WHEN @Taken = 1 THEN NULLIF(@EmpId, '') ELSE NULL END
                    WHERE EventAppointmentID = @EvtId
                      AND EventEquipmentID IN ({string.Join(",", paramNames)});";
                await using var cmd = new SqlCommand(updItemsSql, conn, tx);
                cmd.Parameters.AddWithValue("@Taken", request.IsTaken ? 1 : 0);
                cmd.Parameters.AddWithValue("@EmpId", request.EmployeeId ?? string.Empty);
                cmd.Parameters.AddWithValue("@EvtId", appointmentId);
                for (int i = 0; i < markIds.Count; i++)
                    cmd.Parameters.AddWithValue(paramNames[i], markIds[i]);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                const string updAllSql = @"
                    UPDATE dbo.EventEquipment
                    SET IsTaken = @Taken,
                        TakenAt = CASE WHEN @Taken = 1 THEN SYSUTCDATETIME() ELSE NULL END,
                        TakenByEmployeeID = CASE WHEN @Taken = 1 THEN NULLIF(@EmpId, '') ELSE NULL END
                    WHERE EventAppointmentID = @EvtId;";
                await using var cmd = new SqlCommand(updAllSql, conn, tx);
                cmd.Parameters.AddWithValue("@Taken", request.IsTaken ? 1 : 0);
                cmd.Parameters.AddWithValue("@EmpId", request.EmployeeId ?? string.Empty);
                cmd.Parameters.AddWithValue("@EvtId", appointmentId);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            if (request.TransitionToDispatch && request.IsTaken)
            {
                const string checkUntakenSql = @"
                    SELECT COUNT(1)
                    FROM dbo.EventEquipment
                    WHERE EventAppointmentID = @EvtId
                      AND ISNULL(IsTaken, 0) = 0;";
                await using var checkCmd = new SqlCommand(checkUntakenSql, conn, tx);
                checkCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                var remainingUntaken = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(cancellationToken) ?? 0);

                if (remainingUntaken == 0)
                {
                    const string dispatchSql = @"
                        UPDATE dbo.EventAppointment
                        SET EventStatus = 'Dispatch'
                        WHERE EventAppointmentID = @EvtId;

                        IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                        BEGIN
                            INSERT INTO dbo.HistoryLog (
                                ActionType, EntityName, EntityID, ActionDetails,
                                EventAppointmentID, ChangedByType, ChangedByEmployeeID, LoggedAt
                            )
                            VALUES (
                                'STATUS_CHANGE', 'EventAppointment', @EvtId,
                                'Equipment checklist marked taken by Team Leader. Event status updated to Dispatch.',
                                @EvtId, 'Employee', NULLIF(@EmpId, ''), SYSUTCDATETIME()
                            );
                        END;";
                    await using var dCmd = new SqlCommand(dispatchSql, conn, tx);
                    dCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                    dCmd.Parameters.AddWithValue("@EmpId", request.EmployeeId ?? string.Empty);
                    await dCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return (await GetAppointmentByIdOnConnectionAsync(conn, appointmentId, cancellationToken))!;
    }

    public async Task<EventAppointmentDto> AddAdditionalEquipmentToEventAsync(
        string appointmentId,
        AddEventEquipmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.EquipmentId))
            throw new ArgumentException("EquipmentId is required.");

        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            DateOnly evtDate;
            string evtStatus;
            const string existsApptSql = "SELECT EventDate, ISNULL(EventStatus, '') FROM dbo.EventAppointment WHERE EventAppointmentID = @EvtId;";
            await using (var aCmd = new SqlCommand(existsApptSql, conn, tx))
            {
                aCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                await using var aReader = await aCmd.ExecuteReaderAsync(cancellationToken);
                if (!await aReader.ReadAsync(cancellationToken))
                    throw new KeyNotFoundException($"Event appointment {appointmentId} not found.");
                evtDate = DateOnly.FromDateTime(aReader.GetDateTime(0));
                evtStatus = aReader.IsDBNull(1) ? string.Empty : aReader.GetString(1);
            }

            if (string.Equals(evtStatus, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(evtStatus, "Expired", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Cannot add equipment to a {evtStatus} event.");

            await ValidateEquipmentSameDayAvailableAsync(
                conn,
                tx,
                new[] { request.EquipmentId.Trim() },
                evtDate,
                appointmentId,
                null,
                cancellationToken);

            const string eqInfoSql = "SELECT EquipmentName, EquipmentStatus FROM dbo.Equipment WHERE EquipmentID = @EqId;";
            string? eqName = null;
            string? eqStatus = null;
            await using (var eCmd = new SqlCommand(eqInfoSql, conn, tx))
            {
                eCmd.Parameters.AddWithValue("@EqId", request.EquipmentId.Trim());
                await using var r = await eCmd.ExecuteReaderAsync(cancellationToken);
                if (await r.ReadAsync(cancellationToken))
                {
                    eqName = r.GetString(0);
                    eqStatus = r.IsDBNull(1) ? "Available" : r.GetString(1);
                }
            }

            if (eqName == null)
                throw new KeyNotFoundException($"Equipment '{request.EquipmentId}' not found.");
            if (string.Equals(eqStatus, "Maintenance", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(eqStatus, "Retired", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(eqStatus, "Needs Replacement", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Equipment '{eqName}' is currently '{eqStatus}' and cannot be requested.");

            const string upsertSql = @"
                IF NOT EXISTS (SELECT 1 FROM dbo.EventEquipment WHERE EventAppointmentID = @EvtId AND EquipmentID = @EqId)
                BEGIN
                    INSERT INTO dbo.EventEquipment (EventAppointmentID, EquipmentID, IsTaken, IsAdditionalRequest, RequestedNotes)
                    VALUES (@EvtId, @EqId, 0, 1, NULLIF(@Notes, ''));
                END
                ELSE
                BEGIN
                    UPDATE dbo.EventEquipment
                    SET IsAdditionalRequest = 1,
                        RequestedNotes = COALESCE(NULLIF(@Notes, ''), RequestedNotes)
                    WHERE EventAppointmentID = @EvtId AND EquipmentID = @EqId;
                END

                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (
                        ActionType, EntityName, EntityID, ActionDetails,
                        EventAppointmentID, ChangedByType, ChangedByEmployeeID, LoggedAt
                    )
                    VALUES (
                        'INSERT', 'EventEquipment', @EqId,
                        'Team Leader requested/added additional equipment (' + @EqName + ') for event.',
                        @EvtId, 'Employee', NULLIF(@EmpId, ''), SYSUTCDATETIME()
                    );
                END;";

            await using (var uCmd = new SqlCommand(upsertSql, conn, tx))
            {
                uCmd.Parameters.AddWithValue("@EvtId", appointmentId);
                uCmd.Parameters.AddWithValue("@EqId", request.EquipmentId.Trim());
                uCmd.Parameters.AddWithValue("@EqName", eqName);
                uCmd.Parameters.AddWithValue("@Notes", request.Notes ?? string.Empty);
                uCmd.Parameters.AddWithValue("@EmpId", request.EmployeeId ?? string.Empty);
                await uCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return (await GetAppointmentByIdOnConnectionAsync(conn, appointmentId, cancellationToken))!;
    }

    public async Task<IReadOnlyList<LiveEventOverviewDto>> GetLiveEventsOverviewAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await AutoTriggerPostEventsOnConnectionAsync(conn, cancellationToken);

        const string sql = @"
            SELECT
                ea.EventAppointmentID,
                ISNULL(c.CustomerPhoneNumber, '') AS CustomerPhone,
                pay.PendingPaymentId
            FROM dbo.EventAppointment ea
            LEFT JOIN dbo.Customer c ON c.CustomerID = ea.CustomerID
            OUTER APPLY (
                SELECT TOP 1 p.Id AS PendingPaymentId
                FROM dbo.Payments p
                WHERE p.EventAppointmentId = ea.EventAppointmentID
                  AND p.EmployeeRemittanceStatus = N'PendingAdminApproval'
                ORDER BY p.CreatedAt DESC
            ) pay
            WHERE ISNULL(ea.EventStatus, '') NOT IN ('Cancelled', 'Expired', 'Delivered')
              AND (ea.EventStatus <> 'Pending Payment'
                   OR ISNULL(ea.DownpaymentStatus, 'Pending') = 'Paid'
                   OR NULLIF(ea.FinalPaymentReferenceId, '') IS NOT NULL)
            ORDER BY
                CASE ea.EventStatus
                    WHEN 'Pending Verification' THEN 1
                    WHEN 'Paid Cash Pending Verification' THEN 1
                    WHEN 'Payment Verification (Cash)' THEN 1
                    WHEN 'Ongoing' THEN 2
                    WHEN 'On Going' THEN 2
                    WHEN 'In Execution' THEN 2
                    WHEN 'Event Done' THEN 3
                    WHEN 'Post Event' THEN 3
                    WHEN 'Dispatch' THEN 4
                    WHEN 'Ready' THEN 5
                    WHEN 'Preparation' THEN 6
                    WHEN 'Preparing' THEN 6
                    WHEN 'Pending Payment' THEN 7
                    WHEN 'Confirmed' THEN 8
                    WHEN 'Completed' THEN 9
                    WHEN 'Complete' THEN 9
                    WHEN 'Shot' THEN 9
                    ELSE 10
                END,
                ea.EventDate ASC,
                ea.EventTime ASC;";

        var rows = new List<(string AppointmentId, string Phone, int? PaymentId)>();
        await using (var cmd = new SqlCommand(sql, conn))
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    reader.GetString(0),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2)));
            }
        }

        var list = new List<LiveEventOverviewDto>();
        foreach (var (apptId, phone, payId) in rows)
        {
            var detail = await GetAppointmentByIdOnConnectionAsync(conn, apptId, cancellationToken);
            if (detail == null) continue;

            list.Add(new LiveEventOverviewDto
            {
                EventAppointmentId = detail.EventAppointmentId,
                NegotiationId = detail.NegotiationId,
                CustomerId = detail.CustomerId,
                CustomerName = detail.CustomerName,
                CustomerEmail = detail.CustomerEmail,
                CustomerPhone = phone,
                EventName = detail.EventName,
                EventDescription = detail.EventDescription,
                EventDate = detail.EventDate,
                EventTime = detail.EventTime,
                EventEndTime = detail.EventEndTime,
                DurationMinutes = detail.DurationMinutes,
                EventStatus = detail.EventStatus,
                DisplayStage = MapDisplayStage(detail.EventStatus, detail.FinalPaymentMethod, detail.FinalPaymentStatus, detail.FinalPaymentReferenceId),
                PaymentVerificationStatus = MapPaymentVerificationStatus(detail.EventStatus, detail.FinalPaymentMethod, detail.FinalPaymentStatus, detail.FinalPaymentReferenceId),
                TotalAmount = detail.TotalAmount,
                DownpaymentAmount = detail.DownpaymentAmount,
                FinalBalanceAmount = detail.FinalBalanceAmount,
                DownpaymentStatus = detail.DownpaymentStatus,
                HoldExpiresAt = detail.HoldExpiresAt,
                LockExpiresAt = detail.LockExpiresAt,
                QrString = detail.QrString,
                XenditInvoiceUrl = detail.XenditInvoiceUrl,
                XenditInvoiceId = detail.XenditInvoiceId,
                XenditExternalId = detail.XenditExternalId,
                FinalPaymentMethod = detail.FinalPaymentMethod,
                FinalPaymentStatus = detail.FinalPaymentStatus,
                FinalQrString = detail.FinalQrString,
                FinalPaymentReferenceId = detail.FinalPaymentReferenceId,
                FinalPaymentPaidAt = detail.FinalPaymentPaidAt,
                AssignedEmployees = detail.AssignedEmployees,
                AssignedEquipment = detail.AssignedEquipment,
                SelectedServices = detail.SelectedServices,
                OvertimeCharges = detail.OvertimeCharges,
                Transactions = detail.Transactions,
                CreatedAt = detail.CreatedAt,
                PendingRemittancePaymentId = payId
            });
        }

        return LiveEventOverviewSelector.SelectForOverview(list, GetStudioLocalNow());
    }

    private static DateTime GetStudioLocalNow()
    {
        foreach (var tzId in new[] { "Asia/Manila", "Singapore Standard Time" })
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            }
            catch
            {
            }
        }
        return DateTime.UtcNow.AddHours(8);
    }

    public static string MapPaymentVerificationStatus(
        string eventStatus,
        string? finalMethod,
        string? finalStatus,
        string? finalRefId = null)
    {
        if (string.Equals(finalStatus, "Payment Verification (QR:Paid)", StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(finalMethod, "Xendit_QR", StringComparison.OrdinalIgnoreCase) &&
             (string.Equals(finalStatus, "Paid", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(eventStatus, "Completed", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(eventStatus, "Complete", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(eventStatus, "Shot", StringComparison.OrdinalIgnoreCase))))
            return "Payment Verification (QR:Paid)";

        if (string.Equals(eventStatus, "Payment Verification (Cash)", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventStatus, "Pending Verification", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventStatus, "Paid Cash Pending Verification", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(finalStatus, "Payment Verification (Cash)", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(finalStatus, "Pending Verification", StringComparison.OrdinalIgnoreCase))
            return "Payment Verification (Cash)";

        if (string.Equals(finalStatus, "Payment Verification (QR:Pending Payment)", StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(finalMethod, "Xendit_QR", StringComparison.OrdinalIgnoreCase) &&
             (string.Equals(eventStatus, "Pending Payment", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(finalStatus, "Pending", StringComparison.OrdinalIgnoreCase) ||
              !string.IsNullOrWhiteSpace(finalRefId))))
            return "Payment Verification (QR:Pending Payment)";

        return string.Empty;
    }

    public static string MapDisplayStage(
        string eventStatus,
        string? finalMethod,
        string? finalStatus,
        string? finalRefId = null)
    {
        if (string.Equals(eventStatus, "Completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventStatus, "Complete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventStatus, "Shot", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(finalStatus, "Paid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(finalStatus, "Payment Verification (QR:Paid)", StringComparison.OrdinalIgnoreCase))
            return "Completed";

        if (string.Equals(eventStatus, "Payment Verification (Cash)", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventStatus, "Pending Verification", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventStatus, "Paid Cash Pending Verification", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(finalStatus, "Payment Verification (Cash)", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(finalStatus, "Pending Verification", StringComparison.OrdinalIgnoreCase))
            return "Payment Verification (Cash)";

        if (string.Equals(finalStatus, "Payment Verification (QR:Pending Payment)", StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(finalMethod, "Xendit_QR", StringComparison.OrdinalIgnoreCase) &&
             (string.Equals(eventStatus, "Pending Payment", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(finalStatus, "Pending", StringComparison.OrdinalIgnoreCase) ||
              !string.IsNullOrWhiteSpace(finalRefId))))
            return "Payment Verification (QR:Pending Payment)";

        if (string.Equals(eventStatus, "Event Done", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventStatus, "Post Event", StringComparison.OrdinalIgnoreCase))
            return "Event Done";

        if (string.Equals(eventStatus, "In Execution", StringComparison.OrdinalIgnoreCase))
            return "In Execution";

        if (string.Equals(eventStatus, "Ongoing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventStatus, "On Going", StringComparison.OrdinalIgnoreCase))
            return "Ongoing";

        if (string.Equals(eventStatus, "Dispatch", StringComparison.OrdinalIgnoreCase))
            return "Dispatch";

        if (string.Equals(eventStatus, "Ready", StringComparison.OrdinalIgnoreCase))
            return "Ready";

        if (string.Equals(eventStatus, "Preparation", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventStatus, "Preparing", StringComparison.OrdinalIgnoreCase))
            return "Preparing";

        return eventStatus;
    }

    public async Task<EventAppointmentDto> VerifyCashSettlementAsync(
        string appointmentId,
        string verifierId,
        string? verifierName = null,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            const string updSql = @"
                UPDATE dbo.EventAppointment
                SET EventStatus = 'Completed',
                    FinalPaymentStatus = 'Paid',
                    FinalPaymentPaidAt = COALESCE(FinalPaymentPaidAt, SYSUTCDATETIME())
                WHERE EventAppointmentID = @EvtId;

                IF @@ROWCOUNT = 0
                    THROW 50030, 'Event appointment not found.', 1;

                IF OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.PaymentTransactions
                    SET EmployeeRemittanceStatus = 'Cleared',
                        Status = 'Paid',
                        AdminApproverID = @VerifierId,
                        ApprovalTimestamp = SYSUTCDATETIME()
                    WHERE EventAppointmentID = @EvtId
                      AND (EmployeeRemittanceStatus = 'PendingAdminApproval' OR Status <> 'Paid');
                END

                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (
                        ActionType, EntityName, EntityID, ActionDetails,
                        EventAppointmentID, ChangedByType, ChangedBy, LoggedAt
                    )
                    VALUES (
                        'APPROVAL', 'CashRemittance', @EvtId,
                        @ActionDetails,
                        @EvtId, 'Admin', @VerifierId, SYSUTCDATETIME()
                    );
                END;";

            await using (var cmd = new SqlCommand(updSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@EvtId", appointmentId);
                cmd.Parameters.AddWithValue("@VerifierId", string.IsNullOrWhiteSpace(verifierId) ? "Admin" : verifierId);
                cmd.Parameters.AddWithValue("@ActionDetails",
                    string.IsNullOrWhiteSpace(notes)
                        ? $"Cash verified by {verifierName ?? verifierId}. Employee event workflow finalized as Completed."
                        : notes);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == 50030)
        {
            await tx.RollbackAsync(cancellationToken);
            throw new KeyNotFoundException($"Event appointment {appointmentId} not found.");
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        try
        {
            var pendingEfPayments = await _db.Payments
                .Where(p => p.EventAppointmentId == appointmentId &&
                            p.EmployeeRemittanceStatus == PhotoAevumStudio.Application.Entities.EmployeeRemittanceStatus.PendingAdminApproval)
                .ToListAsync(cancellationToken);
            foreach (var p in pendingEfPayments)
            {
                p.EmployeeRemittanceStatus = PhotoAevumStudio.Application.Entities.EmployeeRemittanceStatus.Cleared;
                p.AdminApproverId = verifierId;
                p.AdminApproverName = verifierName;
                p.ApprovalTimestamp = DateTime.UtcNow;
            }
            if (pendingEfPayments.Count > 0)
            {
                _db.AuditLogs.Add(new PhotoAevumStudio.Application.Entities.AuditLog
                {
                    UserId = verifierId,
                    UserEmail = verifierName ?? verifierId,
                    Action = "Verify Cash Payment",
                    Resource = "EventAppointment",
                    ResourceId = appointmentId,
                    Timestamp = DateTime.UtcNow,
                    Details = $"Verified cash payment for Event #{appointmentId}. Employee workflow marked Completed."
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not sync EF Core Payments remittance status for {AppointmentId}", appointmentId);
        }

        return (await GetAppointmentByIdOnConnectionAsync(conn, appointmentId, cancellationToken))!;
    }

    public async Task<CustomerAppointmentActionResultDto> CustomerCancelAppointmentAsync(
        string appointmentId,
        string customerId,
        string? reason = null,
        bool isPrivileged = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appointmentId))
            throw new ArgumentException("Appointment ID is required.", nameof(appointmentId));

        await using var conn = await OpenConnectionAsync(cancellationToken);
        var appt = await GetAppointmentByIdOnConnectionAsync(conn, appointmentId, cancellationToken);
        if (appt == null)
            throw new KeyNotFoundException($"Event appointment {appointmentId} not found.");

        if (!isPrivileged && !string.Equals(appt.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("You can only cancel your own appointments.");

        var nonCancellable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Completed", "Complete", "Shot", "Editing", "Delivered",
            "Ongoing", "On Going", "In Execution", "Post Event",
            "Paid Cash Pending Verification", "Pending Verification",
            "Cancelled", "Expired"
        };
        if (nonCancellable.Contains(appt.EventStatus ?? string.Empty))
            throw new InvalidOperationException($"Appointment #{appointmentId} is currently '{appt.EventStatus}' and cannot be cancelled.");

        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            const string cancelSql = @"
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

                IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.BookingNegotiation
                    SET NegotiationStatus = 'Cancelled',
                        EmployeeID = NULL,
                        AssignedEmployeeName = NULL
                    WHERE EventAppointmentID = @EvtId
                       OR (@NegId IS NOT NULL AND NegotiationID = @NegId);
                END;

                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (
                        ActionType, EntityName, EntityID, ActionDetails,
                        EventAppointmentID, CustomerID, ChangedByType, ChangedByCustomerID, LoggedAt
                    )
                    VALUES (
                        'STATUS_CHANGE', 'EventAppointment', @EvtId,
                        @Details,
                        @EvtId, @CustId, @ChangedByType, @ChangedCustId, SYSUTCDATETIME()
                    );
                END;";

            var cancelNote = string.IsNullOrWhiteSpace(reason)
                ? $"Appointment #{appointmentId} cancelled by customer. Locked slot, Office Clerk, crew, and equipment released."
                : $"Appointment #{appointmentId} cancelled by customer. Reason: {reason.Trim()}. Locked slot, Office Clerk, crew, and equipment released.";

            await using (var cmd = new SqlCommand(cancelSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@EvtId", appointmentId);
                cmd.Parameters.AddWithValue("@NegId", (object?)appt.NegotiationId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Details", cancelNote);
                cmd.Parameters.AddWithValue("@CustId", appt.CustomerId);
                cmd.Parameters.AddWithValue("@ChangedByType", isPrivileged ? "Admin" : "Customer");
                cmd.Parameters.AddWithValue("@ChangedCustId", isPrivileged ? DBNull.Value : appt.CustomerId);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(appt.NegotiationId) &&
                appt.NegotiationId.StartsWith("NEG-", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(appt.NegotiationId[4..], out var schedApptId))
            {
                const string cancelSchedSql = @"
                    IF OBJECT_ID(N'dbo.Appointment', N'U') IS NOT NULL
                    BEGIN
                        UPDATE dbo.Appointment
                        SET Status = 'Cancelled'
                        WHERE AppointmentID = @SchedId AND Status <> 'Cancelled';
                    END;";
                await using var sCmd = new SqlCommand(cancelSchedSql, conn, tx);
                sCmd.Parameters.AddWithValue("@SchedId", schedApptId);
                await sCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        var updated = await GetAppointmentByIdOnConnectionAsync(conn, appointmentId, cancellationToken);
        return new CustomerAppointmentActionResultDto
        {
            Success = true,
            EventAppointmentId = appointmentId,
            NegotiationId = appt.NegotiationId,
            EventStatus = "Cancelled",
            SlotReleased = true,
            ClerkReleased = true,
            EquipmentReleased = true,
            Message = $"Appointment #{appointmentId} has been cancelled and its time slot, Office Clerk, crew, and equipment have been released.",
            Appointment = updated
        };
    }

    public async Task<CustomerAppointmentActionResultDto> CustomerRescheduleAppointmentAsync(
        string appointmentId,
        CustomerRescheduleAppointmentRequest request,
        string customerId,
        bool isPrivileged = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appointmentId))
            throw new ArgumentException("Appointment ID is required.", nameof(appointmentId));

        await using var conn = await OpenConnectionAsync(cancellationToken);
        await EnsureGranularEventColumnsAsync(conn, cancellationToken);

        var appt = await GetAppointmentByIdOnConnectionAsync(conn, appointmentId, cancellationToken);
        if (appt == null)
            throw new KeyNotFoundException($"Event appointment {appointmentId} not found.");

        if (!isPrivileged && !string.Equals(appt.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("You can only reschedule your own appointments.");

        var allowedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Pending", "Pending Payment", "Awaiting_Downpayment", "Confirmed", "Ready", "Preparation", "Preparing"
        };
        if (!allowedStatuses.Contains(appt.EventStatus ?? string.Empty))
            throw new InvalidOperationException($"Appointment #{appointmentId} is '{appt.EventStatus}' and can no longer be rescheduled.");

        var studioNow = GetStudioLocalNow();
        if (request.NewDate.ToDateTime(request.NewTime) <= studioNow)
            throw new InvalidOperationException("Please choose a future date and time slot.");

        var durationMinutes = Math.Max(15, appt.DurationMinutes <= 0 ? 60 : appt.DurationMinutes);
        var rawEndSpan = request.NewTime.ToTimeSpan().Add(TimeSpan.FromMinutes(durationMinutes));
        if (rawEndSpan > new TimeSpan(23, 59, 0))
            rawEndSpan = new TimeSpan(23, 59, 0);
        var newEndTime = TimeOnly.FromTimeSpan(rawEndSpan);

        // 1. Validate existing crew & equipment remain free on the requested date/time (with 2h prep & 1h post buffers)
        var crewIds = appt.AssignedEmployees.Select(e => e.EmployeeId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        await ValidateEmployeesAvailableAsync(conn, null, crewIds, request.NewDate, cancellationToken);

        var newStartSpan = request.NewTime.ToTimeSpan();
        var newPrepStart = newStartSpan >= TimeSpan.FromHours(2) ? newStartSpan.Subtract(TimeSpan.FromHours(2)) : TimeSpan.Zero;
        var newPostEnd = rawEndSpan.Add(TimeSpan.FromHours(1));
        if (newPostEnd > new TimeSpan(23, 59, 59)) newPostEnd = new TimeSpan(23, 59, 59);

        if (crewIds.Count > 0)
        {
            var empParams = string.Join(",", crewIds.Select((_, i) => $"@rEmp{i}"));
            var crewConflictSql = $@"
                SELECT ea.EventAppointmentID, ea.EventName, ea.EventTime, ea.EventEndTime, ISNULL(ea.DurationMinutes, 60) AS DurationMinutes
                FROM dbo.EventAppointment ea
                JOIN dbo.EventEmployee ee ON ee.EventAppointmentID = ea.EventAppointmentID
                WHERE ee.EmployeeID IN ({empParams})
                  AND ea.EventDate = @RDate
                  AND ea.EventStatus NOT IN ('Cancelled', 'Expired')
                  AND ea.EventAppointmentID <> @REvtId;";
            await using var ccCmd = new SqlCommand(crewConflictSql, conn);
            for (var i = 0; i < crewIds.Count; i++)
                ccCmd.Parameters.AddWithValue($"@rEmp{i}", crewIds[i]);
            ccCmd.Parameters.AddWithValue("@RDate", request.NewDate.ToDateTime(TimeOnly.MinValue));
            ccCmd.Parameters.AddWithValue("@REvtId", appointmentId);

            await using var ccReader = await ccCmd.ExecuteReaderAsync(cancellationToken);
            while (await ccReader.ReadAsync(cancellationToken))
            {
                var exId = ccReader.GetString(0);
                var exName = ccReader.IsDBNull(1) ? exId : ccReader.GetString(1);
                var exStart = ccReader.GetFieldValue<TimeSpan>(2);
                var exDuration = ccReader.IsDBNull(4) ? 60 : Math.Max(15, ccReader.GetInt32(4));
                var exEnd = !ccReader.IsDBNull(3) ? ccReader.GetFieldValue<TimeSpan>(3) : exStart.Add(TimeSpan.FromMinutes(exDuration));
                if (exEnd <= exStart) exEnd = exStart.Add(TimeSpan.FromMinutes(exDuration));

                var exPrepStart = exStart >= TimeSpan.FromHours(2) ? exStart.Subtract(TimeSpan.FromHours(2)) : TimeSpan.Zero;
                var exPostEnd = exEnd.Add(TimeSpan.FromHours(1));
                if (exPostEnd > new TimeSpan(23, 59, 59)) exPostEnd = new TimeSpan(23, 59, 59);

                if (newPrepStart < exPostEnd && exPrepStart < newPostEnd)
                {
                    throw new InvalidOperationException(
                        $"Selected date/time conflicts with existing crew assignment for '{exName}' ({exId}) including buffer windows.");
                }
            }
        }

        var eqIds = appt.AssignedEquipment.Select(e => e.EquipmentId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (eqIds.Count > 0)
        {
            var eqParams = string.Join(",", eqIds.Select((_, i) => $"@rEq{i}"));
            var eqConflictSql = $@"
                SELECT ea.EventAppointmentID, ea.EventName, ea.EventTime, ea.EventEndTime, ISNULL(ea.DurationMinutes, 60) AS DurationMinutes
                FROM dbo.EventAppointment ea
                JOIN dbo.EventEquipment eeq ON eeq.EventAppointmentID = ea.EventAppointmentID
                WHERE eeq.EquipmentID IN ({eqParams})
                  AND ea.EventDate = @RDate
                  AND ea.EventStatus NOT IN ('Cancelled', 'Expired')
                  AND ea.EventAppointmentID <> @REvtId;";
            await using var ecCmd = new SqlCommand(eqConflictSql, conn);
            for (var i = 0; i < eqIds.Count; i++)
                ecCmd.Parameters.AddWithValue($"@rEq{i}", eqIds[i]);
            ecCmd.Parameters.AddWithValue("@RDate", request.NewDate.ToDateTime(TimeOnly.MinValue));
            ecCmd.Parameters.AddWithValue("@REvtId", appointmentId);

            await using var ecReader = await ecCmd.ExecuteReaderAsync(cancellationToken);
            while (await ecReader.ReadAsync(cancellationToken))
            {
                var exId = ecReader.GetString(0);
                var exName = ecReader.IsDBNull(1) ? exId : ecReader.GetString(1);
                var exStart = ecReader.GetFieldValue<TimeSpan>(2);
                var exDuration = ecReader.IsDBNull(4) ? 60 : Math.Max(15, ecReader.GetInt32(4));
                var exEnd = !ecReader.IsDBNull(3) ? ecReader.GetFieldValue<TimeSpan>(3) : exStart.Add(TimeSpan.FromMinutes(exDuration));
                if (exEnd <= exStart) exEnd = exStart.Add(TimeSpan.FromMinutes(exDuration));

                var exPrepStart = exStart >= TimeSpan.FromHours(2) ? exStart.Subtract(TimeSpan.FromHours(2)) : TimeSpan.Zero;
                var exPostEnd = exEnd.Add(TimeSpan.FromHours(1));
                if (exPostEnd > new TimeSpan(23, 59, 59)) exPostEnd = new TimeSpan(23, 59, 59);

                if (newPrepStart < exPostEnd && exPrepStart < newPostEnd)
                {
                    throw new InvalidOperationException(
                        $"Selected date/time conflicts with reserved equipment for '{exName}' ({exId}) including buffer windows.");
                }
            }
        }

        // 2. Resolve original Office Clerk and retain if available, or auto-assign another available Office Clerk
        string? originalClerkId = null;
        const string origClerkSql = @"
            SELECT COALESCE(
                (SELECT TOP 1 bn.EmployeeID FROM dbo.BookingNegotiation bn WHERE (bn.EventAppointmentID = @EvtId OR (@NegId IS NOT NULL AND bn.NegotiationID = @NegId)) AND bn.EmployeeID IS NOT NULL),
                (SELECT TOP 1 ea.EmployeeID FROM dbo.EventAppointment ea WHERE ea.EventAppointmentID = @EvtId)
            );";
        await using (var ocCmd = new SqlCommand(origClerkSql, conn))
        {
            ocCmd.Parameters.AddWithValue("@EvtId", appointmentId);
            ocCmd.Parameters.AddWithValue("@NegId", (object?)appt.NegotiationId ?? DBNull.Value);
            originalClerkId = await ocCmd.ExecuteScalarAsync(cancellationToken) as string;
        }

        var clerkSelection = await ResolveOfficeClerkForDateAsync(
            conn,
            originalClerkId,
            request.NewDate,
            request.NewTime,
            newEndTime,
            excludeSourceId: appt.NegotiationId ?? appointmentId,
            cancellationToken);

        if (!clerkSelection.IsAvailable || string.IsNullOrWhiteSpace(clerkSelection.ClerkId))
        {
            throw new InvalidOperationException(clerkSelection.FailureReason ?? "No Office Clerk is available for the requested date and time.");
        }

        var assignedClerkId = clerkSelection.ClerkId!;
        var assignedClerkName = clerkSelection.ClerkName ?? assignedClerkId;

        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            const string updateSql = @"
                UPDATE dbo.EventAppointment
                SET EventDate = @NewDate,
                    EventTime = @NewTime,
                    EventEndTime = @NewEndTime,
                    EmployeeID = @ClerkId,
                    AssignedEmployeeName = @ClerkName,
                    HoldExpiresAt = CASE WHEN EventStatus IN ('Pending Payment', 'Awaiting_Downpayment') AND ISNULL(DownpaymentStatus, 'Pending') <> 'Paid' THEN DATEADD(minute, 60, SYSUTCDATETIME()) ELSE HoldExpiresAt END,
                    LockExpiresAt = CASE WHEN EventStatus IN ('Pending Payment', 'Awaiting_Downpayment') AND ISNULL(DownpaymentStatus, 'Pending') <> 'Paid' THEN DATEADD(minute, 60, SYSUTCDATETIME()) ELSE LockExpiresAt END
                WHERE EventAppointmentID = @EvtId;

                IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.BookingNegotiation
                    SET EmployeeID = @ClerkId,
                        AssignedEmployeeName = @ClerkName
                    WHERE EventAppointmentID = @EvtId
                       OR (@NegId IS NOT NULL AND NegotiationID = @NegId);
                END;

                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (
                        ActionType, EntityName, EntityID, ActionDetails,
                        EventAppointmentID, CustomerID, ChangedByType, ChangedByCustomerID, LoggedAt
                    )
                    VALUES (
                        'UPDATE', 'EventAppointment', @EvtId,
                        @Details,
                        @EvtId, @CustId, @ChangedByType, @ChangedCustId, SYSUTCDATETIME()
                    );
                END;";

            var clerkActionNote = clerkSelection.RetainedOriginalClerk
                ? $"Retained original Office Clerk {assignedClerkName} ({assignedClerkId})."
                : $"Original Office Clerk unavailable; automatically reassigned to {assignedClerkName} ({assignedClerkId}).";
            var details = $"Customer rescheduled appointment #{appointmentId} to {request.NewDate:yyyy-MM-dd} {request.NewTime:HH:mm}. {clerkActionNote}" +
                          (string.IsNullOrWhiteSpace(request.Reason) ? string.Empty : $" Reason: {request.Reason.Trim()}");

            await using (var cmd = new SqlCommand(updateSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@NewDate", request.NewDate.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.AddWithValue("@NewTime", request.NewTime.ToTimeSpan());
                cmd.Parameters.AddWithValue("@NewEndTime", newEndTime.ToTimeSpan());
                cmd.Parameters.AddWithValue("@ClerkId", assignedClerkId);
                cmd.Parameters.AddWithValue("@ClerkName", assignedClerkName);
                cmd.Parameters.AddWithValue("@EvtId", appointmentId);
                cmd.Parameters.AddWithValue("@NegId", (object?)appt.NegotiationId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Details", details);
                cmd.Parameters.AddWithValue("@CustId", appt.CustomerId);
                cmd.Parameters.AddWithValue("@ChangedByType", isPrivileged ? "Admin" : "Customer");
                cmd.Parameters.AddWithValue("@ChangedCustId", isPrivileged ? DBNull.Value : appt.CustomerId);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        var updatedAppt = await GetAppointmentByIdOnConnectionAsync(conn, appointmentId, cancellationToken);
        await conn.CloseAsync();

        try
        {
            if (!string.IsNullOrWhiteSpace(appt.CustomerEmail))
            {
                var body = StudioEmailTemplates.ConsultationRescheduled(
                    string.IsNullOrWhiteSpace(appt.CustomerName) ? "Valued Client" : appt.CustomerName,
                    appointmentId,
                    request.NewDate,
                    request.NewTime,
                    assignedClerkName,
                    string.Empty,
                    request.Reason);
                await _emailService.SendEmailAsync(
                    appt.CustomerEmail,
                    $"Appointment Rescheduled: #{appointmentId} - Photo Aevum Studio",
                    body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch customer reschedule email for {AppointmentId}", appointmentId);
        }

        var uiMessage = clerkSelection.RetainedOriginalClerk
            ? $"Rescheduled to {request.NewDate:MMM d, yyyy} at {request.NewTime:h:mm tt}. Your originally assigned Office Clerk ({assignedClerkName}) remains assigned."
            : $"Rescheduled to {request.NewDate:MMM d, yyyy} at {request.NewTime:h:mm tt}. Your original clerk was unavailable, so {assignedClerkName} was automatically assigned.";

        return new CustomerAppointmentActionResultDto
        {
            Success = true,
            EventAppointmentId = appointmentId,
            NegotiationId = appt.NegotiationId,
            EventStatus = updatedAppt?.EventStatus ?? appt.EventStatus ?? string.Empty,
            AssignedClerkId = assignedClerkId,
            AssignedClerkName = assignedClerkName,
            RetainedOriginalClerk = clerkSelection.RetainedOriginalClerk,
            Message = uiMessage,
            Appointment = updatedAppt
        };
    }

    private static async Task<ClerkRescheduleSelection> ResolveOfficeClerkForDateAsync(
        SqlConnection conn,
        string? originalClerkId,
        DateOnly targetDate,
        TimeOnly targetStart,
        TimeOnly targetEnd,
        string? excludeSourceId,
        CancellationToken ct)
    {
        var clerks = new List<ClerkCandidate>();
        const string clerksSql = @"
            SELECT DISTINCT
                e.EmployeeID,
                COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(ISNULL(e.EmployeeFirstName, ''), ' ', ISNULL(e.EmployeeLastName, '')))), ''), e.EmployeeID) AS FullName,
                ISNULL(e.DaysOff, 'Sunday') AS DaysOff,
                bn.LastAssignedAt
            FROM dbo.Employees e
            LEFT JOIN dbo.EmployeeJointSpecialization ejs ON e.EmployeeID = ejs.EmployeeID
            LEFT JOIN dbo.EmployeeSpecialization es ON ejs.SpecializationID = es.SpecializationID
            LEFT JOIN (
                SELECT EmployeeID, MAX(CreatedAt) AS LastAssignedAt
                FROM dbo.BookingNegotiation
                WHERE EmployeeID IS NOT NULL
                GROUP BY EmployeeID
            ) bn ON e.EmployeeID = bn.EmployeeID
            WHERE (e.EmployeeStatus = 'Active' OR e.Status = 'Active')
              AND (
                  UPPER(LTRIM(RTRIM(COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization, '')))) = 'OFFICE CLERK'
                  OR ISNULL(e.Specializations, '') LIKE '%Office Clerk%'
              );";
        await using (var cmd = new SqlCommand(clerksSql, conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                clerks.Add(new ClerkCandidate(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetDateTime(3)));
            }
        }

        var leaves = new List<ClerkLeaveWindow>();
        const string leavesSql = @"
            IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NOT NULL
            BEGIN
                SELECT EmployeeStudioId, StartDate, EndDate
                FROM dbo.EmployeeLeaves
                WHERE Status = 'Approved' AND @TDate BETWEEN StartDate AND EndDate;
            END;";
        await using (var lvCmd = new SqlCommand(leavesSql, conn))
        {
            lvCmd.Parameters.AddWithValue("@TDate", targetDate.ToDateTime(TimeOnly.MinValue));
            await using var lvReader = await lvCmd.ExecuteReaderAsync(ct);
            while (await lvReader.ReadAsync(ct))
            {
                leaves.Add(new ClerkLeaveWindow(
                    lvReader.GetString(0),
                    DateOnly.FromDateTime(lvReader.GetDateTime(1)),
                    DateOnly.FromDateTime(lvReader.GetDateTime(2))));
            }
        }

        var busy = new List<ClerkBusyWindow>();
        const string busySql = @"
            IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
            BEGIN
                SELECT EmployeeID, ConsultationDate, ConsultationTime, NegotiationID
                FROM dbo.BookingNegotiation
                WHERE ConsultationDate = @TDate
                  AND EmployeeID IS NOT NULL
                  AND ConsultationTime IS NOT NULL
                  AND NegotiationStatus NOT IN ('Cancelled', 'Deal_Lost');
            END;
            IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
            BEGIN
                SELECT ee.EmployeeID, ea.EventDate, ea.EventTime, ea.EventEndTime, ISNULL(ea.DurationMinutes, 60), ea.EventAppointmentID
                FROM dbo.EventEmployee ee
                JOIN dbo.EventAppointment ea ON ee.EventAppointmentID = ea.EventAppointmentID
                WHERE ea.EventDate = @TDate
                  AND ee.EmployeeID IS NOT NULL
                  AND ea.EventStatus NOT IN ('Cancelled', 'Expired');
            END;";
        await using (var bCmd = new SqlCommand(busySql, conn))
        {
            bCmd.Parameters.AddWithValue("@TDate", targetDate.ToDateTime(TimeOnly.MinValue));
            await using var bReader = await bCmd.ExecuteReaderAsync(ct);
            while (await bReader.ReadAsync(ct))
            {
                var start = TimeOnly.FromTimeSpan(bReader.GetFieldValue<TimeSpan>(2));
                busy.Add(new ClerkBusyWindow(
                    bReader.GetString(0),
                    DateOnly.FromDateTime(bReader.GetDateTime(1)),
                    start,
                    start.AddHours(1),
                    bReader.IsDBNull(3) ? null : bReader.GetString(3)));
            }
            if (await bReader.NextResultAsync(ct))
            {
                while (await bReader.ReadAsync(ct))
                {
                    var start = TimeOnly.FromTimeSpan(bReader.GetFieldValue<TimeSpan>(2));
                    var dur = bReader.IsDBNull(4) ? 60 : Math.Max(15, bReader.GetInt32(4));
                    var end = !bReader.IsDBNull(3) ? TimeOnly.FromTimeSpan(bReader.GetFieldValue<TimeSpan>(3)) : start.AddMinutes(dur);
                    if (end <= start) end = start.AddMinutes(dur);
                    busy.Add(new ClerkBusyWindow(
                        bReader.GetString(0),
                        DateOnly.FromDateTime(bReader.GetDateTime(1)),
                        start,
                        end,
                        bReader.IsDBNull(5) ? null : bReader.GetString(5)));
                }
            }
        }

        return OfficeClerkAssignmentResolver.ResolveClerkForReschedule(
            originalClerkId,
            clerks,
            leaves,
            busy,
            targetDate,
            targetStart,
            targetEnd,
            excludeSourceId);
    }
}




