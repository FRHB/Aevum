using System.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Infrastructure.Services;

public class BookingDocumentService : IBookingDocumentService
{
    private static volatile bool _schemaEnsured;
    private static volatile bool _bucketEnsured;
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);

    private readonly ApplicationDbContext _db;
    private readonly IDocumentPdfService _pdfService;
    private readonly IR2StorageService _r2Storage;
    private readonly IEmailService _emailService;
    private readonly StudioProfileSettings _studioProfile;
    private readonly IWebHostEnvironment? _env;
    private readonly ILogger<BookingDocumentService> _logger;

    public BookingDocumentService(
        ApplicationDbContext db,
        IDocumentPdfService pdfService,
        IR2StorageService r2Storage,
        IEmailService emailService,
        IOptions<StudioProfileSettings> studioOptions,
        ILogger<BookingDocumentService> logger,
        IWebHostEnvironment? env = null)
    {
        _db = db;
        _pdfService = pdfService;
        _r2Storage = r2Storage;
        _emailService = emailService;
        _studioProfile = studioOptions.Value;
        _logger = logger;
        _env = env;
    }

    internal static readonly string[] SchemaBatches =
    {
        @"IF COL_LENGTH(N'dbo.EventAppointment', N'OvertimeAmount') IS NULL
              ALTER TABLE dbo.EventAppointment ADD OvertimeAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_OvertimeAmount DEFAULT 0.00;
          IF COL_LENGTH(N'dbo.EventAppointment', N'RemainingBalance') IS NULL
              ALTER TABLE dbo.EventAppointment ADD RemainingBalance decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_RemainingBalance DEFAULT 0.00;
          IF COL_LENGTH(N'dbo.EventAppointment', N'IsDeliverablesUnlocked') IS NULL
              ALTER TABLE dbo.EventAppointment ADD IsDeliverablesUnlocked bit NOT NULL CONSTRAINT DF_EventAppointment_IsDeliverablesUnlocked DEFAULT 0;
          IF COL_LENGTH(N'dbo.EventAppointment', N'ContractAcceptedAtUtc') IS NULL
              ALTER TABLE dbo.EventAppointment ADD ContractAcceptedAtUtc datetime2 NULL;",

        @"IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
                         WHERE name = N'CK_EventAppointment_Status'
                           AND definition LIKE N'%Pending Agreement%'
                           AND definition LIKE N'%Awaiting Settlement%'
                           AND definition LIKE N'%In Progress%')
          BEGIN
              IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_EventAppointment_Status')
                  ALTER TABLE dbo.EventAppointment DROP CONSTRAINT CK_EventAppointment_Status;
              ALTER TABLE dbo.EventAppointment ADD CONSTRAINT CK_EventAppointment_Status
                  CHECK (EventStatus IN ('Pending','Pending Agreement','Pending Reservation Fee','Awaiting_Downpayment','Pending Payment','Ready','In Execution','In Progress','Preparation','Preparing','Dispatch','Ongoing','On Going','Event Done','Post Event','Awaiting Settlement','Pending Verification','Paid Cash Pending Verification','Payment Verification (Cash)','Confirmed','Complete','Completed','Shot','Editing','Delivered','Cancelled','Expired'));
          END;",

        @"IF OBJECT_ID(N'dbo.TermsVersions', N'U') IS NULL
          BEGIN
              CREATE TABLE dbo.TermsVersions (
                  TermsVersionId int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                  VersionCode nvarchar(30) NOT NULL,
                  Title nvarchar(200) NOT NULL CONSTRAINT DF_TermsVersions_Title DEFAULT 'Standard Terms and Conditions',
                  EffectiveDate date NOT NULL,
                  DeliveryBusinessDays int NOT NULL CONSTRAINT DF_TermsVersions_DeliveryDays DEFAULT 7,
                  OvertimeHourlyRate decimal(18,2) NOT NULL CONSTRAINT DF_TermsVersions_OvertimeRate DEFAULT 1500.00,
                  TermsBodyHtml nvarchar(max) NOT NULL,
                  IsActive bit NOT NULL CONSTRAINT DF_TermsVersions_IsActive DEFAULT 1,
                  CreatedAtUtc datetime2 NOT NULL CONSTRAINT DF_TermsVersions_CreatedAt DEFAULT SYSUTCDATETIME(),
                  CONSTRAINT UQ_TermsVersions_VersionCode UNIQUE (VersionCode)
              );
          END;",

        @"IF OBJECT_ID(N'dbo.BookingContracts', N'U') IS NULL
          BEGIN
              CREATE TABLE dbo.BookingContracts (
                  BookingContractId int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                  ContractNumber nvarchar(40) NOT NULL,
                  EventAppointmentID nvarchar(30) NOT NULL,
                  NegotiationID nvarchar(30) NULL,
                  CustomerID nvarchar(30) NOT NULL,
                  TermsVersionId int NOT NULL,
                  TermsVersionCode nvarchar(30) NOT NULL,
                  DeliveryBusinessDays int NOT NULL CONSTRAINT DF_BookingContracts_DeliveryDays DEFAULT 7,
                  StudioName nvarchar(200) NOT NULL,
                  StudioAddress nvarchar(500) NOT NULL,
                  StudioPhone nvarchar(100) NOT NULL,
                  StudioEmail nvarchar(200) NOT NULL,
                  ClientName nvarchar(200) NOT NULL,
                  ClientAddress nvarchar(500) NULL,
                  ClientPhone nvarchar(100) NULL,
                  ClientEmail nvarchar(200) NOT NULL,
                  SessionType nvarchar(150) NOT NULL,
                  DateOfShoot date NOT NULL,
                  StartTime time NOT NULL,
                  EndTime time NOT NULL,
                  Venue nvarchar(255) NOT NULL,
                  PackageName nvarchar(255) NOT NULL,
                  PackageInclusions nvarchar(1000) NOT NULL,
                  TotalPackageAmount decimal(18,2) NOT NULL,
                  ReservationFeeAmount decimal(18,2) NOT NULL,
                  RemainingBalanceAmount decimal(18,2) NOT NULL,
                  RepresentativeName nvarchar(200) NOT NULL,
                  ContractStatus nvarchar(30) NOT NULL CONSTRAINT DF_BookingContracts_Status DEFAULT 'PendingAcceptance',
                  AcceptedAtUtc datetime2 NULL,
                  ClientIpAddress nvarchar(100) NULL,
                  UserAgent nvarchar(500) NULL,
                  ClickwrapReference nvarchar(100) NULL,
                  RenderedContractHtml nvarchar(max) NOT NULL,
                  PdfBucketName nvarchar(100) NULL,
                  PdfObjectKey nvarchar(500) NULL,
                  PdfUri nvarchar(1000) NULL,
                  CreatedAtUtc datetime2 NOT NULL CONSTRAINT DF_BookingContracts_CreatedAt DEFAULT SYSUTCDATETIME(),
                  UpdatedAtUtc datetime2 NOT NULL CONSTRAINT DF_BookingContracts_UpdatedAt DEFAULT SYSUTCDATETIME(),
                  CONSTRAINT UQ_BookingContracts_ContractNumber UNIQUE (ContractNumber)
              );
              CREATE INDEX IX_BookingContracts_EventAppointmentID ON dbo.BookingContracts (EventAppointmentID);
          END;
          IF OBJECT_ID(N'dbo.BookingContracts', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.FK_BookingContracts_EventAppointment', N'F') IS NULL
              ALTER TABLE dbo.BookingContracts ADD CONSTRAINT FK_BookingContracts_EventAppointment
                  FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;
          IF OBJECT_ID(N'dbo.BookingContracts', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.TermsVersions', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.FK_BookingContracts_TermsVersions', N'F') IS NULL
              ALTER TABLE dbo.BookingContracts ADD CONSTRAINT FK_BookingContracts_TermsVersions
                  FOREIGN KEY (TermsVersionId) REFERENCES dbo.TermsVersions(TermsVersionId) ON DELETE NO ACTION;",

        @"IF OBJECT_ID(N'dbo.BookingPayments', N'U') IS NULL
          BEGIN
              CREATE TABLE dbo.BookingPayments (
                  BookingPaymentId int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                  EventAppointmentID nvarchar(30) NOT NULL,
                  BookingContractId int NULL,
                  PaymentStage nvarchar(30) NOT NULL,
                  PaymentMethod nvarchar(50) NOT NULL,
                  Amount decimal(18,2) NOT NULL,
                  OvertimeAmount decimal(18,2) NOT NULL CONSTRAINT DF_BookingPayments_Overtime DEFAULT 0.00,
                  GatewayReferenceId nvarchar(150) NOT NULL,
                  ExternalReference nvarchar(150) NULL,
                  Status nvarchar(30) NOT NULL CONSTRAINT DF_BookingPayments_Status DEFAULT 'Paid',
                  PaidAtUtc datetime2 NOT NULL CONSTRAINT DF_BookingPayments_PaidAt DEFAULT SYSUTCDATETIME(),
                  VerifiedByEmployeeID nvarchar(30) NULL,
                  Notes nvarchar(1000) NULL,
                  CreatedAtUtc datetime2 NOT NULL CONSTRAINT DF_BookingPayments_CreatedAt DEFAULT SYSUTCDATETIME(),
                  CONSTRAINT CK_BookingPayments_Stage CHECK (PaymentStage IN ('ReservationFee', 'FinalSettlement'))
              );
              CREATE INDEX IX_BookingPayments_Event_Stage ON dbo.BookingPayments (EventAppointmentID, PaymentStage);
          END;
          IF OBJECT_ID(N'dbo.BookingPayments', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.FK_BookingPayments_EventAppointment', N'F') IS NULL
              ALTER TABLE dbo.BookingPayments ADD CONSTRAINT FK_BookingPayments_EventAppointment
                  FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;
          IF OBJECT_ID(N'dbo.BookingPayments', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.BookingContracts', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.FK_BookingPayments_BookingContract', N'F') IS NULL
              ALTER TABLE dbo.BookingPayments ADD CONSTRAINT FK_BookingPayments_BookingContract
                  FOREIGN KEY (BookingContractId) REFERENCES dbo.BookingContracts(BookingContractId) ON DELETE NO ACTION;",

        @"IF OBJECT_ID(N'dbo.BookingReceipts', N'U') IS NULL
          BEGIN
              CREATE TABLE dbo.BookingReceipts (
                  BookingReceiptId int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                  ReceiptNumber nvarchar(50) NOT NULL,
                  EventAppointmentID nvarchar(30) NOT NULL,
                  BookingPaymentId int NULL,
                  ReceiptType nvarchar(40) NOT NULL,
                  TotalContractCharges decimal(18,2) NOT NULL,
                  Stage1PaidAmount decimal(18,2) NOT NULL CONSTRAINT DF_BookingReceipts_Stage1 DEFAULT 0.00,
                  OvertimeAddOnAmount decimal(18,2) NOT NULL CONSTRAINT DF_BookingReceipts_Overtime DEFAULT 0.00,
                  Stage2PaidAmount decimal(18,2) NOT NULL CONSTRAINT DF_BookingReceipts_Stage2 DEFAULT 0.00,
                  RemainingBalanceDue decimal(18,2) NOT NULL,
                  PaymentMethod nvarchar(50) NOT NULL,
                  GatewayReferenceId nvarchar(150) NULL,
                  IssuedAtUtc datetime2 NOT NULL CONSTRAINT DF_BookingReceipts_IssuedAt DEFAULT SYSUTCDATETIME(),
                  RenderedReceiptHtml nvarchar(max) NOT NULL,
                  PdfBucketName nvarchar(100) NULL,
                  PdfObjectKey nvarchar(500) NULL,
                  PdfUri nvarchar(1000) NULL,
                  CreatedAtUtc datetime2 NOT NULL CONSTRAINT DF_BookingReceipts_CreatedAt DEFAULT SYSUTCDATETIME(),
                  CONSTRAINT UQ_BookingReceipts_ReceiptNumber UNIQUE (ReceiptNumber),
                  CONSTRAINT UQ_BookingReceipts_Appt_Type UNIQUE (EventAppointmentID, ReceiptType),
                  CONSTRAINT CK_BookingReceipts_Type CHECK (ReceiptType IN ('ReservationAcknowledgment', 'FinalConsolidated'))
              );
          END;
          IF OBJECT_ID(N'dbo.BookingReceipts', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.FK_BookingReceipts_EventAppointment', N'F') IS NULL
              ALTER TABLE dbo.BookingReceipts ADD CONSTRAINT FK_BookingReceipts_EventAppointment
                  FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;
          IF OBJECT_ID(N'dbo.BookingReceipts', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.BookingPayments', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.FK_BookingReceipts_BookingPayment', N'F') IS NULL
              ALTER TABLE dbo.BookingReceipts ADD CONSTRAINT FK_BookingReceipts_BookingPayment
                  FOREIGN KEY (BookingPaymentId) REFERENCES dbo.BookingPayments(BookingPaymentId) ON DELETE NO ACTION;"
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

            await EnsureInitialTermsSeededAsync(conn, ct);
            _schemaEnsured = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reconcile BookingContracts / TermsVersions / BookingReceipts schema.");
            throw;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    private async Task EnsureInitialTermsSeededAsync(SqlConnection conn, CancellationToken ct)
    {
        const string checkSql = "SELECT COUNT(1) FROM dbo.TermsVersions;";
        await using var chk = new SqlCommand(checkSql, conn);
        var count = Convert.ToInt32(await chk.ExecuteScalarAsync(ct));
        if (count > 0) return;

        var eff = new DateOnly(2026, 1, 1);
        var days = _studioProfile.DefaultDeliveryBusinessDays > 0 ? _studioProfile.DefaultDeliveryBusinessDays : 7;
        var html = DocumentPdfService.BuildDefaultStandardTermsHtml(_studioProfile.StudioName, "v1.0", eff, days);

        const string insSql = @"
            INSERT INTO dbo.TermsVersions (
                VersionCode, Title, EffectiveDate, DeliveryBusinessDays, OvertimeHourlyRate, TermsBodyHtml, IsActive, CreatedAtUtc
            )
            VALUES (
                'v1.0', 'Standard Terms and Conditions', @EffDate, @DeliveryDays, @OtRate, @BodyHtml, 1, SYSUTCDATETIME()
            );";
        await using var ins = new SqlCommand(insSql, conn);
        ins.Parameters.AddWithValue("@EffDate", eff.ToDateTime(TimeOnly.MinValue));
        ins.Parameters.AddWithValue("@DeliveryDays", days);
        ins.Parameters.AddWithValue("@OtRate", _studioProfile.DefaultOvertimeHourlyRate > 0 ? _studioProfile.DefaultOvertimeHourlyRate : 1500.00m);
        ins.Parameters.AddWithValue("@BodyHtml", html);
        await ins.ExecuteNonQueryAsync(ct);
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = await SqlResilienceHelper.OpenConnectionAsync(_db.Database.GetConnectionString(), ct);
        await EnsureSchemaOnConnectionAsync(conn, ct);
        return conn;
    }

    public async Task<TermsVersionDto> GetActiveTermsVersionAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        return await GetActiveTermsVersionOnConnectionAsync(conn, null, ct);
    }

    public async Task<TermsVersionDto?> GetTermsVersionByIdAsync(int termsVersionId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        return await GetTermsVersionByIdOnConnectionAsync(conn, null, termsVersionId, ct);
    }

    private async Task<TermsVersionDto> GetActiveTermsVersionOnConnectionAsync(SqlConnection conn, SqlTransaction? tx, CancellationToken ct)
    {
        const string sql = @"
            SELECT TOP 1 TermsVersionId, VersionCode, Title, EffectiveDate, DeliveryBusinessDays, OvertimeHourlyRate, TermsBodyHtml, IsActive, CreatedAtUtc
            FROM dbo.TermsVersions
            WHERE IsActive = 1
            ORDER BY TermsVersionId DESC;";
        await using var cmd = new SqlCommand(sql, conn, tx);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (await r.ReadAsync(ct))
            return ReadTerms(r);

        await r.CloseAsync();
        await EnsureInitialTermsSeededAsync(conn, ct);
        await using var retry = new SqlCommand(sql, conn, tx);
        await using var rr = await retry.ExecuteReaderAsync(ct);
        if (await rr.ReadAsync(ct))
            return ReadTerms(rr);

        throw new InvalidOperationException("No active Standard Terms & Conditions version is configured.");
    }

    private static async Task<TermsVersionDto?> GetTermsVersionByIdOnConnectionAsync(SqlConnection conn, SqlTransaction? tx, int id, CancellationToken ct)
    {
        const string sql = @"
            SELECT TOP 1 TermsVersionId, VersionCode, Title, EffectiveDate, DeliveryBusinessDays, OvertimeHourlyRate, TermsBodyHtml, IsActive, CreatedAtUtc
            FROM dbo.TermsVersions
            WHERE TermsVersionId = @Id;";
        await using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("@Id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadTerms(r) : null;
    }

    public async Task<BookingContractDto> GenerateOrUpdateDraftContractAsync(
        string eventAppointmentId,
        ConfigureBookingContractRequest? overrides = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventAppointmentId))
            throw new ArgumentException("EventAppointmentID is required.", nameof(eventAppointmentId));

        await using var conn = await OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            var contract = await GenerateOrUpdateDraftContractOnConnectionAsync(conn, tx, eventAppointmentId.Trim(), overrides, ct);
            await tx.CommitAsync(ct);
            return contract;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    internal async Task<BookingContractDto> GenerateOrUpdateDraftContractOnConnectionAsync(
        SqlConnection conn,
        SqlTransaction tx,
        string eventAppointmentId,
        ConfigureBookingContractRequest? overrides,
        CancellationToken ct)
    {
        await EnsureSchemaOnConnectionAsync(conn, ct);

        // Check existing contract
        var existing = await GetContractByAppointmentOnConnectionAsync(conn, tx, eventAppointmentId, ct);
        if (existing is { AcceptedAtUtc: not null })
        {
            if (overrides != null)
                throw new InvalidOperationException("This agreement has already been electronically accepted by the client and is locked.");
            return existing;
        }

        // Load appointment, customer, negotiation, and selected line items
        const string apptSql = @"
            SELECT TOP 1
                ea.EventAppointmentID,
                ea.CustomerID,
                ea.EventName,
                ISNULL(ea.EventDescription, '') AS EventDescription,
                ISNULL(ea.EventLocation, 'Main Studio') AS EventLocation,
                ea.EventDate,
                ea.EventTime,
                ea.EventEndTime,
                ea.DurationMinutes,
                ea.TotalAmount,
                ea.DownpaymentAmount,
                ea.FinalBalanceAmount,
                ISNULL(ea.AssignedEmployeeName, '') AS AssignedEmployeeName,
                LTRIM(RTRIM(ISNULL(c.CustomerFirstName, '') + ' ' + ISNULL(c.CustomerLastName, ''))) AS CustomerName,
                ISNULL(c.CustomerEmail, '') AS CustomerEmail,
                ISNULL(c.CustomerPhoneNumber, '') AS CustomerPhone,
                bn.NegotiationID
            FROM dbo.EventAppointment ea
            LEFT JOIN dbo.Customer c ON c.CustomerID = ea.CustomerID
            LEFT JOIN dbo.BookingNegotiation bn ON bn.EventAppointmentID = ea.EventAppointmentID
            WHERE ea.EventAppointmentID = @EvtId;";

        string custId = string.Empty, eventName = "Photoshoot Event", eventDesc = string.Empty, venue = "Main Studio";
        string repName = _studioProfile.RepresentativeName, custName = "Valued Client", custEmail = string.Empty, custPhone = string.Empty;
        string? negId = null;
        DateOnly shootDate = DateOnly.FromDateTime(DateTime.Today);
        TimeSpan startTs = new(9, 0, 0), endTs = new(10, 0, 0);
        decimal total = 0m, resFee = 0m, remBal = 0m;

        await using (var cmd = new SqlCommand(apptSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("@EvtId", eventAppointmentId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
                throw new KeyNotFoundException($"Event appointment '{eventAppointmentId}' not found.");

            custId = r.GetString(1);
            eventName = r.GetString(2);
            eventDesc = r.GetString(3);
            venue = r.GetString(4);
            shootDate = DateOnly.FromDateTime(r.GetDateTime(5));
            startTs = r.GetFieldValue<TimeSpan>(6);
            var dur = r.IsDBNull(8) ? 60 : Math.Max(15, r.GetInt32(8));
            endTs = !r.IsDBNull(7) ? r.GetFieldValue<TimeSpan>(7) : startTs.Add(TimeSpan.FromMinutes(dur));
            if (endTs <= startTs) endTs = startTs.Add(TimeSpan.FromMinutes(dur));
            total = r.GetDecimal(9);
            resFee = r.GetDecimal(10);
            remBal = r.GetDecimal(11);
            var assignedLead = r.GetString(12);
            if (!string.IsNullOrWhiteSpace(assignedLead)) repName = assignedLead;
            custName = string.IsNullOrWhiteSpace(r.GetString(13)) ? custId : r.GetString(13);
            custEmail = r.GetString(14);
            custPhone = r.GetString(15);
            negId = r.IsDBNull(16) ? null : r.GetString(16);
        }

        // Gather line items to format package & inclusions
        const string linesSql = @"
            SELECT
                COALESCE(s.ServiceName, p.ComboPackageName, a.AdditionalServiceName, eli.ServiceNameSnapshot, 'Studio Service') AS ItemName,
                eli.Quantity,
                ISNULL(eli.RateType, 'Per Session') AS RateType,
                eli.DurationHours
            FROM dbo.EventLineItem eli
            LEFT JOIN dbo.Services s ON s.ServicesID = eli.ServicesID
            LEFT JOIN dbo.ComboPackage p ON p.ComboPackageID = eli.ComboPackageID
            LEFT JOIN dbo.AdditionalServices a ON a.AdditionalServicesID = eli.AdditionalServicesID
            WHERE eli.EventAppointmentID = @EvtId;";

        var itemNames = new List<string>();
        var inclusionsList = new List<string>();
        await using (var lCmd = new SqlCommand(linesSql, conn, tx))
        {
            lCmd.Parameters.AddWithValue("@EvtId", eventAppointmentId);
            await using var lr = await lCmd.ExecuteReaderAsync(ct);
            while (await lr.ReadAsync(ct))
            {
                var name = lr.GetString(0);
                var qty = lr.GetInt32(1);
                var rate = lr.GetString(2);
                var hrs = lr.IsDBNull(3) ? (int?)null : lr.GetInt32(3);
                itemNames.Add(name);
                inclusionsList.Add(hrs.HasValue ? $"{name} ({qty} x {hrs} hr(s), {rate})" : $"{name} ({qty} x {rate})");
            }
        }

        if (overrides?.TotalPackageAmount is > 0)
            total = Math.Round(overrides.TotalPackageAmount.Value, 2);
        if (overrides?.ReservationFeeAmount is > 0)
            resFee = Math.Round(overrides.ReservationFeeAmount.Value, 2);
        else if (resFee <= 0 && total > 0)
            resFee = Math.Round(total * 0.15m, 2);

        if (resFee > total && total > 0)
            throw new InvalidOperationException("Reservation fee cannot exceed the total package amount.");

        remBal = Math.Max(0m, Math.Round(total - resFee, 2));

        if (overrides != null && (overrides.TotalPackageAmount.HasValue || overrides.ReservationFeeAmount.HasValue))
        {
            const string syncApptAmtSql = @"
                UPDATE dbo.EventAppointment
                SET TotalAmount = @Total,
                    DownpaymentAmount = @ResFee,
                    FinalBalanceAmount = @RemBal,
                    RemainingBalance = CASE WHEN ISNULL(DownpaymentStatus, 'Pending') = 'Paid' THEN @RemBal ELSE @Total END
                WHERE EventAppointmentID = @EvtId;";
            await using var sCmd = new SqlCommand(syncApptAmtSql, conn, tx);
            sCmd.Parameters.AddWithValue("@Total", total);
            sCmd.Parameters.AddWithValue("@ResFee", resFee);
            sCmd.Parameters.AddWithValue("@RemBal", remBal);
            sCmd.Parameters.AddWithValue("@EvtId", eventAppointmentId);
            await sCmd.ExecuteNonQueryAsync(ct);
        }

        var terms = await GetActiveTermsVersionOnConnectionAsync(conn, tx, ct);
        var deliveryDays = overrides?.DeliveryBusinessDays is > 0
            ? overrides.DeliveryBusinessDays.Value
            : (terms.DeliveryBusinessDays > 0 ? terms.DeliveryBusinessDays : _studioProfile.DefaultDeliveryBusinessDays);
        if (!string.IsNullOrWhiteSpace(overrides?.RepresentativeName))
            repName = overrides.RepresentativeName.Trim();

        var pkgName = !string.IsNullOrWhiteSpace(overrides?.PackageName)
            ? overrides.PackageName.Trim()
            : (itemNames.Count > 0 ? string.Join(" + ", itemNames) : eventName);

        var pkgInclusions = !string.IsNullOrWhiteSpace(overrides?.PackageInclusions)
            ? overrides.PackageInclusions.Trim()
            : (inclusionsList.Count > 0
                ? string.Join("; ", inclusionsList)
                : (!string.IsNullOrWhiteSpace(eventDesc) ? eventDesc : $"{Math.Round((endTs - startTs).TotalHours, 1)} hour(s) coverage & post-processing"));

        var contractNumber = existing?.ContractNumber ?? $"CTR-{eventAppointmentId}";
        var dto = new BookingContractDto
        {
            BookingContractId = existing?.BookingContractId ?? 0,
            ContractNumber = contractNumber,
            EventAppointmentId = eventAppointmentId,
            NegotiationId = negId,
            CustomerId = custId,
            TermsVersionId = terms.TermsVersionId,
            TermsVersionCode = terms.VersionCode,
            DeliveryBusinessDays = deliveryDays,
            StudioName = _studioProfile.StudioName,
            StudioAddress = _studioProfile.StudioAddress,
            StudioPhone = _studioProfile.StudioPhone,
            StudioEmail = _studioProfile.StudioEmail,
            ClientName = custName,
            ClientAddress = "On File with Studio",
            ClientPhone = custPhone,
            ClientEmail = custEmail,
            SessionType = eventName,
            DateOfShoot = shootDate,
            StartTime = TimeOnly.FromTimeSpan(startTs),
            EndTime = TimeOnly.FromTimeSpan(endTs),
            Venue = venue,
            PackageName = pkgName,
            PackageInclusions = pkgInclusions,
            TotalPackageAmount = total,
            ReservationFeeAmount = resFee,
            RemainingBalanceAmount = remBal,
            RepresentativeName = repName,
            ContractStatus = existing?.ContractStatus ?? "PendingAcceptance",
            CreatedAtUtc = existing?.CreatedAtUtc ?? DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        dto.RenderedContractHtml = _pdfService.RenderServicesAgreementHtml(dto, terms);

        if (existing == null)
        {
            const string insContractSql = @"
                INSERT INTO dbo.BookingContracts (
                    ContractNumber, EventAppointmentID, NegotiationID, CustomerID,
                    TermsVersionId, TermsVersionCode, DeliveryBusinessDays,
                    StudioName, StudioAddress, StudioPhone, StudioEmail,
                    ClientName, ClientAddress, ClientPhone, ClientEmail,
                    SessionType, DateOfShoot, StartTime, EndTime, Venue, PackageName, PackageInclusions,
                    TotalPackageAmount, ReservationFeeAmount, RemainingBalanceAmount, RepresentativeName,
                    ContractStatus, RenderedContractHtml, CreatedAtUtc, UpdatedAtUtc
                )
                OUTPUT inserted.BookingContractId
                VALUES (
                    @Num, @EvtId, @NegId, @CustId,
                    @TermsId, @TermsCode, @DelDays,
                    @SName, @SAddr, @SPhone, @SEmail,
                    @CName, @CAddr, @CPhone, @CEmail,
                    @SessType, @ShootDate, @StartT, @EndT, @Venue, @PkgName, @PkgInc,
                    @Total, @ResFee, @RemBal, @RepName,
                    'PendingAcceptance', @Html, SYSUTCDATETIME(), SYSUTCDATETIME()
                );";

            await using var insCmd = new SqlCommand(insContractSql, conn, tx);
            BindContractParameters(insCmd, dto);
            dto.BookingContractId = Convert.ToInt32(await insCmd.ExecuteScalarAsync(ct));
        }
        else
        {
            const string updContractSql = @"
                UPDATE dbo.BookingContracts
                SET TermsVersionId = @TermsId,
                    TermsVersionCode = @TermsCode,
                    DeliveryBusinessDays = @DelDays,
                    ClientName = @CName,
                    ClientPhone = @CPhone,
                    ClientEmail = @CEmail,
                    SessionType = @SessType,
                    DateOfShoot = @ShootDate,
                    StartTime = @StartT,
                    EndTime = @EndT,
                    Venue = @Venue,
                    PackageName = @PkgName,
                    PackageInclusions = @PkgInc,
                    TotalPackageAmount = @Total,
                    ReservationFeeAmount = @ResFee,
                    RemainingBalanceAmount = @RemBal,
                    RepresentativeName = @RepName,
                    RenderedContractHtml = @Html,
                    UpdatedAtUtc = SYSUTCDATETIME()
                WHERE BookingContractId = @Id AND AcceptedAtUtc IS NULL;";

            await using var updCmd = new SqlCommand(updContractSql, conn, tx);
            updCmd.Parameters.AddWithValue("@Id", existing.BookingContractId);
            BindContractParameters(updCmd, dto);
            await updCmd.ExecuteNonQueryAsync(ct);
        }

        return dto;
    }

    public async Task<BookingDocumentsBundleDto?> GetBookingDocumentsBundleAsync(
        string eventAppointmentId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventAppointmentId))
            return null;

        await using var conn = await OpenAsync(ct);

        const string apptSql = @"
            SELECT TOP 1 EventAppointmentID, EventStatus, TotalAmount, DownpaymentAmount, OvertimeAmount, FinalBalanceAmount, RemainingBalance, IsDeliverablesUnlocked
            FROM dbo.EventAppointment
            WHERE EventAppointmentID = @EvtId;";

        BookingDocumentsBundleDto? bundle = null;
        await using (var cmd = new SqlCommand(apptSql, conn))
        {
            cmd.Parameters.AddWithValue("@EvtId", eventAppointmentId.Trim());
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;

            var total = r.GetDecimal(2);
            var down = r.GetDecimal(3);
            var ot = r.GetDecimal(4);
            var finalBal = r.GetDecimal(5);
            var rem = r.GetDecimal(6);
            var unlocked = r.GetBoolean(7);
            var status = r.GetString(1);

            if (rem <= 0 && !unlocked && total > 0 &&
                !string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(status, "Complete", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(status, "Delivered", StringComparison.OrdinalIgnoreCase))
            {
                rem = finalBal > 0 ? finalBal : Math.Max(0m, total + ot - down);
            }

            bundle = new BookingDocumentsBundleDto
            {
                EventAppointmentId = r.GetString(0),
                EventStatus = status,
                TotalPackageAmount = total,
                ReservationFeeAmount = down,
                OvertimeAmount = ot,
                RemainingBalance = unlocked ? 0m : rem,
                IsDeliverablesUnlocked = unlocked
            };
        }

        var contract = await GetContractByAppointmentOnConnectionAsync(conn, null, eventAppointmentId.Trim(), ct);
        if (contract == null)
        {
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                contract = await GenerateOrUpdateDraftContractOnConnectionAsync(conn, tx, eventAppointmentId.Trim(), null, ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
            }
        }

        bundle.Contract = contract;
        bundle.TermsVersion = contract != null
            ? await GetTermsVersionByIdOnConnectionAsync(conn, null, contract.TermsVersionId, ct)
              ?? await GetActiveTermsVersionOnConnectionAsync(conn, null, ct)
            : await GetActiveTermsVersionOnConnectionAsync(conn, null, ct);

        bundle.Payments = await GetPaymentsOnConnectionAsync(conn, null, eventAppointmentId.Trim(), ct);
        bundle.Receipts = await GetReceiptsOnConnectionAsync(conn, null, eventAppointmentId.Trim(), ct);
        return bundle;
    }

    public async Task<BookingContractDto> AcceptContractAsync(
        string eventAppointmentId,
        string callerCustomerId,
        bool isPrivilegedStaff,
        AcceptContractRequest request,
        string? clientIpAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        if (!request.AgreeToServicesAgreement || !request.AgreeToStandardTerms)
            throw new InvalidOperationException("You must explicitly accept both the Photo Studio Services Agreement and the Standard Terms and Conditions.");

        await using var conn = await OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        BookingContractDto contract;
        TermsVersionDto terms;

        try
        {
            contract = await GetContractByAppointmentOnConnectionAsync(conn, tx, eventAppointmentId.Trim(), ct)
                       ?? await GenerateOrUpdateDraftContractOnConnectionAsync(conn, tx, eventAppointmentId.Trim(), null, ct);

            if (!isPrivilegedStaff && !string.Equals(contract.CustomerId, callerCustomerId?.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("You are not authorized to accept the contract for this booking.");

            if (contract.AcceptedAtUtc.HasValue)
            {
                await tx.CommitAsync(ct);
                return contract;
            }

            terms = await GetTermsVersionByIdOnConnectionAsync(conn, tx, contract.TermsVersionId, ct)
                    ?? await GetActiveTermsVersionOnConnectionAsync(conn, tx, ct);

            var acceptedAt = DateTime.UtcNow;
            var clickRef = !string.IsNullOrWhiteSpace(request.ClickwrapReference)
                ? request.ClickwrapReference.Trim()
                : $"CW-{eventAppointmentId.Trim()}-{acceptedAt:yyyyMMddHHmmss}";

            contract.AcceptedAtUtc = acceptedAt;
            contract.ClientIpAddress = string.IsNullOrWhiteSpace(clientIpAddress) ? "Unknown" : clientIpAddress.Trim();
            contract.UserAgent = string.IsNullOrWhiteSpace(userAgent) ? "Browser" : (userAgent.Length > 480 ? userAgent[..480] : userAgent);
            contract.ClickwrapReference = clickRef;
            contract.ContractStatus = "Accepted";
            contract.UpdatedAtUtc = acceptedAt;
            contract.RenderedContractHtml = _pdfService.RenderServicesAgreementHtml(contract, terms);

            const string acceptSql = @"
                UPDATE dbo.BookingContracts
                SET ContractStatus = 'Accepted',
                    AcceptedAtUtc = @AcceptedAt,
                    ClientIpAddress = @Ip,
                    UserAgent = @Ua,
                    ClickwrapReference = @Ref,
                    RenderedContractHtml = @Html,
                    UpdatedAtUtc = @AcceptedAt
                WHERE BookingContractId = @Id AND AcceptedAtUtc IS NULL;

                UPDATE dbo.EventAppointment
                SET ContractAcceptedAtUtc = COALESCE(ContractAcceptedAtUtc, @AcceptedAt)
                WHERE EventAppointmentID = @EvtId;

                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (
                        ActionType, EntityName, EntityID, ActionDetails,
                        EventAppointmentID, CustomerID, ChangedByType, ChangedByCustomerID, LoggedAt
                    )
                    VALUES (
                        'UPDATE', 'BookingContracts', @ContractNum,
                        'Client electronically accepted Services Agreement (' + @TermsCode + ') via clickwrap. Ref: ' + @Ref,
                        @EvtId, @CustId, 'Customer', @CustId, @AcceptedAt
                    );
                END;";

            await using var cmd = new SqlCommand(acceptSql, conn, tx);
            cmd.Parameters.AddWithValue("@AcceptedAt", acceptedAt);
            cmd.Parameters.AddWithValue("@Ip", contract.ClientIpAddress);
            cmd.Parameters.AddWithValue("@Ua", contract.UserAgent);
            cmd.Parameters.AddWithValue("@Ref", contract.ClickwrapReference);
            cmd.Parameters.AddWithValue("@Html", contract.RenderedContractHtml);
            cmd.Parameters.AddWithValue("@Id", contract.BookingContractId);
            cmd.Parameters.AddWithValue("@EvtId", contract.EventAppointmentId);
            cmd.Parameters.AddWithValue("@ContractNum", contract.ContractNumber);
            cmd.Parameters.AddWithValue("@TermsCode", contract.TermsVersionCode);
            cmd.Parameters.AddWithValue("@CustId", contract.CustomerId);
            await cmd.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        // Generate & upload Signed Services Agreement PDF immediately
        try
        {
            var pdfBytes = _pdfService.GenerateServicesAgreementPdf(contract, terms);
            var objectKey = $"contracts/{SanitizeSegment(contract.CustomerId)}/{SanitizeSegment(contract.EventAppointmentId)}/{contract.ContractNumber}.pdf";
            var (bucket, uri) = await PersistPdfAsync(objectKey, pdfBytes, ct);
            await UpdateContractPdfMetadataAsync(contract.BookingContractId, bucket, objectKey, uri, ct);
            contract.PdfBucketName = bucket;
            contract.PdfObjectKey = objectKey;
            contract.PdfUri = uri;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Non-fatal Signed Agreement PDF upload warning for {AppointmentId}", eventAppointmentId);
        }

        return contract;
    }

    public async Task<(BookingPaymentLedgerDto Payment, BookingReceiptDto Receipt)> RecordStagePaymentAndReceiptAsync(
        System.Data.Common.DbConnection dbConn,
        System.Data.Common.DbTransaction dbTx,
        string eventAppointmentId,
        string paymentStage,
        string paymentMethod,
        decimal amount,
        decimal overtimeAmount,
        string gatewayReferenceId,
        string? externalReference,
        string? verifiedByEmployeeId,
        string? notes,
        CancellationToken ct = default)
    {
        var conn = (SqlConnection)dbConn;
        var tx = (SqlTransaction)dbTx;
        await EnsureSchemaOnConnectionAsync(conn, ct);

        var contract = await GetContractByAppointmentOnConnectionAsync(conn, tx, eventAppointmentId, ct)
                       ?? await GenerateOrUpdateDraftContractOnConnectionAsync(conn, tx, eventAppointmentId, null, ct);

        var terms = await GetTermsVersionByIdOnConnectionAsync(conn, tx, contract.TermsVersionId, ct)
                    ?? await GetActiveTermsVersionOnConnectionAsync(conn, tx, ct);

        var isStage1 = string.Equals(paymentStage, "ReservationFee", StringComparison.OrdinalIgnoreCase);
        var normalizedStage = isStage1 ? "ReservationFee" : "FinalSettlement";
        var receiptType = isStage1 ? "ReservationAcknowledgment" : "FinalConsolidated";

        // Idempotency guard: if receipt for this stage already exists, return existing payment + receipt without duplicating
        var existingReceipt = await GetReceiptByTypeOnConnectionAsync(conn, tx, eventAppointmentId, receiptType, ct);
        if (existingReceipt != null)
        {
            var existingPayments = await GetPaymentsOnConnectionAsync(conn, tx, eventAppointmentId, ct);
            var matchedPay = existingPayments.FirstOrDefault(p => string.Equals(p.PaymentStage, normalizedStage, StringComparison.OrdinalIgnoreCase))
                             ?? new BookingPaymentLedgerDto
                             {
                                 BookingPaymentId = existingReceipt.BookingPaymentId ?? 0,
                                 EventAppointmentId = eventAppointmentId,
                                 BookingContractId = contract.BookingContractId,
                                 PaymentStage = normalizedStage,
                                 PaymentMethod = paymentMethod,
                                 Amount = amount,
                                 OvertimeAmount = overtimeAmount,
                                 GatewayReferenceId = gatewayReferenceId,
                                 Status = "Paid",
                                 PaidAtUtc = existingReceipt.IssuedAtUtc
                             };
            return (matchedPay, existingReceipt);
        }

        var nowUtc = DateTime.UtcNow;
        var payDto = new BookingPaymentLedgerDto
        {
            EventAppointmentId = eventAppointmentId,
            BookingContractId = contract.BookingContractId,
            PaymentStage = normalizedStage,
            PaymentMethod = paymentMethod,
            Amount = amount,
            OvertimeAmount = overtimeAmount,
            GatewayReferenceId = string.IsNullOrWhiteSpace(gatewayReferenceId) ? eventAppointmentId : gatewayReferenceId,
            ExternalReference = externalReference,
            Status = "Paid",
            PaidAtUtc = nowUtc,
            VerifiedByEmployeeId = verifiedByEmployeeId,
            Notes = notes
        };

        const string insPaySql = @"
            INSERT INTO dbo.BookingPayments (
                EventAppointmentID, BookingContractId, PaymentStage, PaymentMethod,
                Amount, OvertimeAmount, GatewayReferenceId, ExternalReference, Status,
                PaidAtUtc, VerifiedByEmployeeID, Notes, CreatedAtUtc
            )
            OUTPUT inserted.BookingPaymentId
            VALUES (
                @EvtId, @ContractId, @Stage, @Method,
                @Amount, @OtAmount, @GwRef, @ExtRef, 'Paid',
                @PaidAt, @VerEmpId, @Notes, @PaidAt
            );";

        await using (var pCmd = new SqlCommand(insPaySql, conn, tx))
        {
            pCmd.Parameters.AddWithValue("@EvtId", payDto.EventAppointmentId);
            pCmd.Parameters.AddWithValue("@ContractId", contract.BookingContractId);
            pCmd.Parameters.AddWithValue("@Stage", payDto.PaymentStage);
            pCmd.Parameters.AddWithValue("@Method", payDto.PaymentMethod);
            pCmd.Parameters.AddWithValue("@Amount", payDto.Amount);
            pCmd.Parameters.AddWithValue("@OtAmount", payDto.OvertimeAmount);
            pCmd.Parameters.AddWithValue("@GwRef", payDto.GatewayReferenceId);
            pCmd.Parameters.AddWithValue("@ExtRef", (object?)payDto.ExternalReference ?? DBNull.Value);
            pCmd.Parameters.AddWithValue("@PaidAt", nowUtc);
            pCmd.Parameters.AddWithValue("@VerEmpId", (object?)payDto.VerifiedByEmployeeId ?? DBNull.Value);
            pCmd.Parameters.AddWithValue("@Notes", (object?)payDto.Notes ?? DBNull.Value);
            payDto.BookingPaymentId = Convert.ToInt32(await pCmd.ExecuteScalarAsync(ct));
        }

        // Compute ledger totals for receipt
        var allPayments = await GetPaymentsOnConnectionAsync(conn, tx, eventAppointmentId, ct);
        var stage1Paid = allPayments.Where(p => p.PaymentStage == "ReservationFee").Sum(p => p.Amount);
        if (stage1Paid <= 0 && !isStage1) stage1Paid = contract.ReservationFeeAmount;
        var stage2Paid = allPayments.Where(p => p.PaymentStage == "FinalSettlement").Sum(p => p.Amount);

        decimal totalOt = overtimeAmount;
        if (totalOt <= 0)
        {
            const string otSumSql = "SELECT ISNULL(SUM(ExtraHours * HourlyRate + SurchargeAmount), 0) FROM dbo.EventOvertimeCharges WHERE EventAppointmentID = @EvtId;";
            await using var otCmd = new SqlCommand(otSumSql, conn, tx);
            otCmd.Parameters.AddWithValue("@EvtId", eventAppointmentId);
            totalOt = Convert.ToDecimal(await otCmd.ExecuteScalarAsync(ct));
        }

        var totalContractCharges = contract.TotalPackageAmount + totalOt;
        var remainingDue = isStage1
            ? Math.Max(0m, totalContractCharges - stage1Paid)
            : 0m;

        var receiptNum = isStage1
            ? $"REC-S1-{eventAppointmentId}"
            : $"REC-S2-{eventAppointmentId}";

        var receipt = new BookingReceiptDto
        {
            ReceiptNumber = receiptNum,
            EventAppointmentId = eventAppointmentId,
            BookingPaymentId = payDto.BookingPaymentId,
            ReceiptType = receiptType,
            TotalContractCharges = totalContractCharges,
            Stage1PaidAmount = stage1Paid,
            OvertimeAddOnAmount = totalOt,
            Stage2PaidAmount = stage2Paid,
            RemainingBalanceDue = remainingDue,
            PaymentMethod = paymentMethod,
            GatewayReferenceId = payDto.GatewayReferenceId,
            IssuedAtUtc = nowUtc
        };

        var stage1Ref = allPayments.FirstOrDefault(p => p.PaymentStage == "ReservationFee") is { } s1
            ? $"{s1.PaidAtUtc:yyyy-MM-dd} • {s1.GatewayReferenceId}"
            : "Stage 1 Settled";
        var stage2Ref = $"{nowUtc:yyyy-MM-dd} • {payDto.GatewayReferenceId}";

        receipt.RenderedReceiptHtml = isStage1
            ? _pdfService.RenderReservationReceiptHtml(receipt, contract, terms)
            : _pdfService.RenderFinalConsolidatedReceiptHtml(receipt, contract, terms, stage1Ref, stage2Ref, notes);

        const string insRecSql = @"
            INSERT INTO dbo.BookingReceipts (
                ReceiptNumber, EventAppointmentID, BookingPaymentId, ReceiptType,
                TotalContractCharges, Stage1PaidAmount, OvertimeAddOnAmount, Stage2PaidAmount,
                RemainingBalanceDue, PaymentMethod, GatewayReferenceId, IssuedAtUtc,
                RenderedReceiptHtml, CreatedAtUtc
            )
            OUTPUT inserted.BookingReceiptId
            VALUES (
                @RecNum, @EvtId, @PayId, @RecType,
                @TotalCharges, @Stage1, @OtAdd, @Stage2,
                @RemDue, @Method, @GwRef, @IssuedAt,
                @Html, @IssuedAt
            );";

        await using (var rCmd = new SqlCommand(insRecSql, conn, tx))
        {
            rCmd.Parameters.AddWithValue("@RecNum", receipt.ReceiptNumber);
            rCmd.Parameters.AddWithValue("@EvtId", receipt.EventAppointmentId);
            rCmd.Parameters.AddWithValue("@PayId", payDto.BookingPaymentId);
            rCmd.Parameters.AddWithValue("@RecType", receipt.ReceiptType);
            rCmd.Parameters.AddWithValue("@TotalCharges", receipt.TotalContractCharges);
            rCmd.Parameters.AddWithValue("@Stage1", receipt.Stage1PaidAmount);
            rCmd.Parameters.AddWithValue("@OtAdd", receipt.OvertimeAddOnAmount);
            rCmd.Parameters.AddWithValue("@Stage2", receipt.Stage2PaidAmount);
            rCmd.Parameters.AddWithValue("@RemDue", receipt.RemainingBalanceDue);
            rCmd.Parameters.AddWithValue("@Method", receipt.PaymentMethod);
            rCmd.Parameters.AddWithValue("@GwRef", (object?)receipt.GatewayReferenceId ?? DBNull.Value);
            rCmd.Parameters.AddWithValue("@IssuedAt", nowUtc);
            rCmd.Parameters.AddWithValue("@Html", receipt.RenderedReceiptHtml);
            receipt.BookingReceiptId = Convert.ToInt32(await rCmd.ExecuteScalarAsync(ct));
        }

        // Synchronize EventAppointment and BookingContracts balances/status
        const string syncStatusSql = @"
            UPDATE dbo.EventAppointment
            SET OvertimeAmount = @Ot,
                RemainingBalance = @RemDue,
                IsDeliverablesUnlocked = @Unlocked
            WHERE EventAppointmentID = @EvtId;

            UPDATE dbo.BookingContracts
            SET ContractStatus = CASE WHEN @IsStage1 = 1 THEN 'Confirmed' ELSE 'Completed' END,
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE BookingContractId = @ContractId;";

        await using (var sCmd = new SqlCommand(syncStatusSql, conn, tx))
        {
            sCmd.Parameters.AddWithValue("@Ot", totalOt);
            sCmd.Parameters.AddWithValue("@RemDue", remainingDue);
            sCmd.Parameters.AddWithValue("@Unlocked", isStage1 ? 0 : 1);
            sCmd.Parameters.AddWithValue("@EvtId", eventAppointmentId);
            sCmd.Parameters.AddWithValue("@IsStage1", isStage1 ? 1 : 0);
            sCmd.Parameters.AddWithValue("@ContractId", contract.BookingContractId);
            await sCmd.ExecuteNonQueryAsync(ct);
        }

        return (payDto, receipt);
    }

    public async Task FinalizeStageDocumentsAndEmailAsync(
        string eventAppointmentId,
        string receiptType,
        CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var contract = await GetContractByAppointmentOnConnectionAsync(conn, null, eventAppointmentId, ct);
        if (contract == null) return;

        var terms = await GetTermsVersionByIdOnConnectionAsync(conn, null, contract.TermsVersionId, ct)
                    ?? await GetActiveTermsVersionOnConnectionAsync(conn, null, ct);

        var receipt = await GetReceiptByTypeOnConnectionAsync(conn, null, eventAppointmentId, receiptType, ct);
        if (receipt == null) return;

        var isStage1 = string.Equals(receiptType, "ReservationAcknowledgment", StringComparison.OrdinalIgnoreCase);
        var attachments = new List<EmailAttachment>();

        // 1. Ensure Agreement PDF is uploaded (Stage 1)
        if (isStage1)
        {
            var contractPdf = _pdfService.GenerateServicesAgreementPdf(contract, terms);
            var contractKey = $"contracts/{SanitizeSegment(contract.CustomerId)}/{SanitizeSegment(contract.EventAppointmentId)}/{contract.ContractNumber}.pdf";
            var (cBucket, cUri) = await PersistPdfAsync(contractKey, contractPdf, ct);
            await UpdateContractPdfMetadataAsync(contract.BookingContractId, cBucket, contractKey, cUri, ct);
            attachments.Add(new EmailAttachment($"{contract.ContractNumber}.pdf", contractPdf));
        }

        // 2. Generate & upload Receipt PDF
        byte[] receiptPdf;
        if (isStage1)
        {
            receiptPdf = _pdfService.GenerateReservationReceiptPdf(receipt, contract, terms);
        }
        else
        {
            var payments = await GetPaymentsOnConnectionAsync(conn, null, eventAppointmentId, ct);
            var s1 = payments.FirstOrDefault(p => p.PaymentStage == "ReservationFee");
            var s2 = payments.FirstOrDefault(p => p.PaymentStage == "FinalSettlement");
            var s1Ref = s1 != null ? $"{s1.PaidAtUtc:yyyy-MM-dd} • {s1.GatewayReferenceId}" : "Stage 1 Settled";
            var s2Ref = s2 != null ? $"{s2.PaidAtUtc:yyyy-MM-dd} • {s2.GatewayReferenceId}" : $"{receipt.IssuedAtUtc:yyyy-MM-dd} • {receipt.GatewayReferenceId}";
            var otNote = s2?.Notes;
            receiptPdf = _pdfService.GenerateFinalConsolidatedReceiptPdf(receipt, contract, terms, s1Ref, s2Ref, otNote);
        }

        var receiptKey = $"receipts/{SanitizeSegment(contract.CustomerId)}/{SanitizeSegment(contract.EventAppointmentId)}/{receipt.ReceiptNumber}.pdf";
        var (rBucket, rUri) = await PersistPdfAsync(receiptKey, receiptPdf, ct);
        await UpdateReceiptPdfMetadataAsync(receipt.BookingReceiptId, rBucket, receiptKey, rUri, ct);
        attachments.Add(new EmailAttachment($"{receipt.ReceiptNumber}.pdf", receiptPdf));

        // 3. Email client with PDF attachments
        if (!string.IsNullOrWhiteSpace(contract.ClientEmail))
        {
            var subject = isStage1
                ? $"Booking Confirmed & Reservation Receipt (#{contract.EventAppointmentId}) – {_studioProfile.StudioName}"
                : $"Final Settlement Receipt & Deliverables Unlocked (#{contract.EventAppointmentId}) – {_studioProfile.StudioName}";

            var body = isStage1
                ? StudioEmailTemplates.ReservationAcknowledgmentEmail(
                    contract.ClientName,
                    contract.EventAppointmentId,
                    contract.SessionType,
                    contract.DateOfShoot,
                    contract.StartTime,
                    contract.EndTime,
                    contract.Venue,
                    receipt.ReceiptNumber,
                    receipt.TotalContractCharges,
                    receipt.Stage1PaidAmount,
                    receipt.RemainingBalanceDue,
                    contract.TermsVersionCode)
                : StudioEmailTemplates.FinalSettlementReceiptEmail(
                    contract.ClientName,
                    contract.EventAppointmentId,
                    contract.SessionType,
                    contract.DateOfShoot,
                    receipt.ReceiptNumber,
                    receipt.TotalContractCharges,
                    receipt.Stage1PaidAmount,
                    receipt.OvertimeAddOnAmount,
                    receipt.Stage2PaidAmount,
                    contract.DeliveryBusinessDays);

            await _emailService.TrySendEmailWithAttachmentsAsync(
                contract.ClientEmail,
                subject,
                body,
                attachments,
                isHtml: true,
                cancellationToken: ct);
        }
    }

    public async Task<(byte[] PdfBytes, string FileName, string ContentType)> GetContractPdfAsync(
        string eventAppointmentId,
        CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var contract = await GetContractByAppointmentOnConnectionAsync(conn, null, eventAppointmentId.Trim(), ct);
        if (contract == null)
        {
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            contract = await GenerateOrUpdateDraftContractOnConnectionAsync(conn, tx, eventAppointmentId.Trim(), null, ct);
            await tx.CommitAsync(ct);
        }

        if (!string.IsNullOrWhiteSpace(contract.PdfBucketName) && !string.IsNullOrWhiteSpace(contract.PdfObjectKey))
        {
            var stored = await TryLoadStoredPdfAsync(contract.PdfBucketName, contract.PdfObjectKey, ct);
            if (stored is { Length: > 0 })
                return (stored, $"{contract.ContractNumber}.pdf", "application/pdf");
        }

        var terms = await GetTermsVersionByIdOnConnectionAsync(conn, null, contract.TermsVersionId, ct)
                    ?? await GetActiveTermsVersionOnConnectionAsync(conn, null, ct);
        var pdf = _pdfService.GenerateServicesAgreementPdf(contract, terms);
        return (pdf, $"{contract.ContractNumber}.pdf", "application/pdf");
    }

    public async Task<(byte[] PdfBytes, string FileName, string ContentType)> GetReceiptPdfAsync(
        string eventAppointmentId,
        string receiptType,
        CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var receipt = await GetReceiptByTypeOnConnectionAsync(conn, null, eventAppointmentId.Trim(), receiptType, ct)
                      ?? throw new KeyNotFoundException($"Receipt '{receiptType}' for booking '{eventAppointmentId}' not found.");

        if (!string.IsNullOrWhiteSpace(receipt.PdfBucketName) && !string.IsNullOrWhiteSpace(receipt.PdfObjectKey))
        {
            var stored = await TryLoadStoredPdfAsync(receipt.PdfBucketName, receipt.PdfObjectKey, ct);
            if (stored is { Length: > 0 })
                return (stored, $"{receipt.ReceiptNumber}.pdf", "application/pdf");
        }

        var contract = await GetContractByAppointmentOnConnectionAsync(conn, null, eventAppointmentId.Trim(), ct)
                       ?? throw new KeyNotFoundException($"Contract for booking '{eventAppointmentId}' not found.");
        var terms = await GetTermsVersionByIdOnConnectionAsync(conn, null, contract.TermsVersionId, ct)
                    ?? await GetActiveTermsVersionOnConnectionAsync(conn, null, ct);

        byte[] pdf;
        if (string.Equals(receipt.ReceiptType, "ReservationAcknowledgment", StringComparison.OrdinalIgnoreCase))
        {
            pdf = _pdfService.GenerateReservationReceiptPdf(receipt, contract, terms);
        }
        else
        {
            var payments = await GetPaymentsOnConnectionAsync(conn, null, eventAppointmentId.Trim(), ct);
            var s1 = payments.FirstOrDefault(p => p.PaymentStage == "ReservationFee");
            var s2 = payments.FirstOrDefault(p => p.PaymentStage == "FinalSettlement");
            pdf = _pdfService.GenerateFinalConsolidatedReceiptPdf(
                receipt,
                contract,
                terms,
                s1 != null ? $"{s1.PaidAtUtc:yyyy-MM-dd} • {s1.GatewayReferenceId}" : null,
                s2 != null ? $"{s2.PaidAtUtc:yyyy-MM-dd} • {s2.GatewayReferenceId}" : null,
                s2?.Notes);
        }

        return (pdf, $"{receipt.ReceiptNumber}.pdf", "application/pdf");
    }

    public async Task<(byte[] PdfBytes, string FileName, string ContentType)> GetActiveTermsPdfAsync(
        int? termsVersionId = null,
        CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var terms = termsVersionId.HasValue
            ? await GetTermsVersionByIdOnConnectionAsync(conn, null, termsVersionId.Value, ct)
              ?? throw new KeyNotFoundException($"Terms version {termsVersionId.Value} not found.")
            : await GetActiveTermsVersionOnConnectionAsync(conn, null, ct);

        var pdf = _pdfService.GenerateStandardTermsPdf(terms, _studioProfile);
        return (pdf, $"Standard-Terms-and-Conditions-{terms.VersionCode}.pdf", "application/pdf");
    }

    public async Task<(bool Allowed, decimal RemainingBalance, string? BlockReason)> CheckDeliverablesAccessAsync(
        string eventAppointmentId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventAppointmentId))
            return (false, 0m, "Event appointment ID is required.");

        await using var conn = await OpenAsync(ct);
        const string sql = @"
            SELECT TOP 1
                ea.EventStatus,
                ea.TotalAmount,
                ea.DownpaymentAmount,
                ea.OvertimeAmount,
                ea.FinalBalanceAmount,
                ea.RemainingBalance,
                ea.FinalPaymentStatus,
                ea.IsDeliverablesUnlocked,
                ISNULL((SELECT SUM(bp.Amount) FROM dbo.BookingPayments bp WHERE bp.EventAppointmentID = ea.EventAppointmentID AND bp.Status IN ('Paid', 'Verified')), 0) AS LedgerPaid
            FROM dbo.EventAppointment ea
            WHERE ea.EventAppointmentID = @EvtId;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@EvtId", eventAppointmentId.Trim());
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            return (false, 0m, "Booking not found.");

        var status = r.IsDBNull(0) ? string.Empty : r.GetString(0);
        var total = r.GetDecimal(1);
        var down = r.GetDecimal(2);
        var ot = r.GetDecimal(3);
        var finalBal = r.GetDecimal(4);
        var storedRemaining = r.GetDecimal(5);
        var finalPayStatus = r.IsDBNull(6) ? null : r.GetString(6);
        var unlocked = r.GetBoolean(7);
        var ledgerPaid = r.GetDecimal(8);

        if (unlocked && storedRemaining <= 0m)
            return (true, 0m, null);

        var finalSettled = string.Equals(finalPayStatus, "Paid", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(finalPayStatus, "Payment Verification (QR:Paid)", StringComparison.OrdinalIgnoreCase);

        if (finalSettled && storedRemaining <= 0m)
            return (true, 0m, null);

        var totalCharges = total + ot;
        var remaining = storedRemaining > 0m
            ? storedRemaining
            : (ledgerPaid > 0m ? Math.Max(0m, totalCharges - ledgerPaid) : (finalBal > 0m ? finalBal : Math.Max(0m, totalCharges - down)));

        if (totalCharges <= 0m && (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "Delivered", StringComparison.OrdinalIgnoreCase)))
            return (true, 0m, null);

        var reason = remaining > 0m
            ? $"Deliverables are locked until the remaining balance of PHP {remaining:N2} is settled in full (0.00 balance due)."
            : "Deliverables are locked until final settlement is verified and completed.";
        return (false, remaining, reason);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // INTERNAL HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<(string Bucket, string Uri)> PersistPdfAsync(string objectKey, byte[] pdfBytes, CancellationToken ct)
    {
        if (_r2Storage.IsConfigured)
        {
            try
            {
                if (!_bucketEnsured)
                {
                    await _r2Storage.CreateBucketIfNotExistsAsync(_r2Storage.DocumentsBucket);
                    _bucketEnsured = true;
                }
                using var ms = new MemoryStream(pdfBytes);
                var uri = await _r2Storage.UploadFileAsync(_r2Storage.DocumentsBucket, objectKey, ms, "application/pdf");
                if (!string.IsNullOrWhiteSpace(uri))
                    return (_r2Storage.DocumentsBucket, uri);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cloudflare R2 PDF upload failed for {ObjectKey}; falling back to local document store.", objectKey);
            }
        }

        // Local fallback when R2 is unconfigured in local dev/testing
        var webRoot = !string.IsNullOrWhiteSpace(_env?.WebRootPath)
            ? _env.WebRootPath
            : Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var localPath = Path.Combine(webRoot, "uploads", "documents", objectKey.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await File.WriteAllBytesAsync(localPath, pdfBytes, ct);
        return ("local", $"/uploads/documents/{objectKey}");
    }

    private async Task<byte[]?> TryLoadStoredPdfAsync(string bucket, string objectKey, CancellationToken ct)
    {
        if (string.Equals(bucket, "local", StringComparison.OrdinalIgnoreCase))
        {
            var webRoot = !string.IsNullOrWhiteSpace(_env?.WebRootPath)
                ? _env.WebRootPath
                : Path.Combine(AppContext.BaseDirectory, "wwwroot");
            var localPath = Path.Combine(webRoot, "uploads", "documents", objectKey.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(localPath) ? await File.ReadAllBytesAsync(localPath, ct) : null;
        }

        if (_r2Storage.IsConfigured)
        {
            try
            {
                await using var stream = await _r2Storage.DownloadFileAsync(bucket, objectKey);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to read PDF '{ObjectKey}' from R2 bucket '{Bucket}'; regenerating from snapshot.", objectKey, bucket);
            }
        }

        return null;
    }

    private async Task UpdateContractPdfMetadataAsync(int contractId, string bucket, string key, string uri, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        const string sql = "UPDATE dbo.BookingContracts SET PdfBucketName = @B, PdfObjectKey = @K, PdfUri = @U, UpdatedAtUtc = SYSUTCDATETIME() WHERE BookingContractId = @Id;";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@B", bucket);
        cmd.Parameters.AddWithValue("@K", key);
        cmd.Parameters.AddWithValue("@U", uri);
        cmd.Parameters.AddWithValue("@Id", contractId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task UpdateReceiptPdfMetadataAsync(int receiptId, string bucket, string key, string uri, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        const string sql = "UPDATE dbo.BookingReceipts SET PdfBucketName = @B, PdfObjectKey = @K, PdfUri = @U WHERE BookingReceiptId = @Id;";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@B", bucket);
        cmd.Parameters.AddWithValue("@K", key);
        cmd.Parameters.AddWithValue("@U", uri);
        cmd.Parameters.AddWithValue("@Id", receiptId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Trim().Select(ch => invalid.Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "unknown" : cleaned;
    }

    private static void BindContractParameters(SqlCommand cmd, BookingContractDto dto)
    {
        cmd.Parameters.AddWithValue("@Num", dto.ContractNumber);
        cmd.Parameters.AddWithValue("@EvtId", dto.EventAppointmentId);
        cmd.Parameters.AddWithValue("@NegId", (object?)dto.NegotiationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CustId", dto.CustomerId);
        cmd.Parameters.AddWithValue("@TermsId", dto.TermsVersionId);
        cmd.Parameters.AddWithValue("@TermsCode", dto.TermsVersionCode);
        cmd.Parameters.AddWithValue("@DelDays", dto.DeliveryBusinessDays);
        cmd.Parameters.AddWithValue("@SName", dto.StudioName);
        cmd.Parameters.AddWithValue("@SAddr", dto.StudioAddress);
        cmd.Parameters.AddWithValue("@SPhone", dto.StudioPhone);
        cmd.Parameters.AddWithValue("@SEmail", dto.StudioEmail);
        cmd.Parameters.AddWithValue("@CName", dto.ClientName);
        cmd.Parameters.AddWithValue("@CAddr", (object?)dto.ClientAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CPhone", (object?)dto.ClientPhone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CEmail", dto.ClientEmail);
        cmd.Parameters.AddWithValue("@SessType", dto.SessionType);
        cmd.Parameters.AddWithValue("@ShootDate", dto.DateOfShoot.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@StartT", dto.StartTime.ToTimeSpan());
        cmd.Parameters.AddWithValue("@EndT", dto.EndTime.ToTimeSpan());
        cmd.Parameters.AddWithValue("@Venue", dto.Venue);
        cmd.Parameters.AddWithValue("@PkgName", dto.PackageName);
        cmd.Parameters.AddWithValue("@PkgInc", dto.PackageInclusions);
        cmd.Parameters.AddWithValue("@Total", dto.TotalPackageAmount);
        cmd.Parameters.AddWithValue("@ResFee", dto.ReservationFeeAmount);
        cmd.Parameters.AddWithValue("@RemBal", dto.RemainingBalanceAmount);
        cmd.Parameters.AddWithValue("@RepName", dto.RepresentativeName);
        cmd.Parameters.AddWithValue("@Html", dto.RenderedContractHtml);
    }

    private static async Task<BookingContractDto?> GetContractByAppointmentOnConnectionAsync(
        SqlConnection conn, SqlTransaction? tx, string eventAppointmentId, CancellationToken ct)
    {
        const string sql = @"
            SELECT TOP 1
                BookingContractId, ContractNumber, EventAppointmentID, NegotiationID, CustomerID,
                TermsVersionId, TermsVersionCode, DeliveryBusinessDays,
                StudioName, StudioAddress, StudioPhone, StudioEmail,
                ClientName, ClientAddress, ClientPhone, ClientEmail,
                SessionType, DateOfShoot, StartTime, EndTime, Venue, PackageName, PackageInclusions,
                TotalPackageAmount, ReservationFeeAmount, RemainingBalanceAmount, RepresentativeName,
                ContractStatus, AcceptedAtUtc, ClientIpAddress, UserAgent, ClickwrapReference,
                RenderedContractHtml, PdfBucketName, PdfObjectKey, PdfUri, CreatedAtUtc, UpdatedAtUtc
            FROM dbo.BookingContracts
            WHERE EventAppointmentID = @EvtId
            ORDER BY BookingContractId DESC;";
        await using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("@EvtId", eventAppointmentId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;

        return new BookingContractDto
        {
            BookingContractId = r.GetInt32(0),
            ContractNumber = r.GetString(1),
            EventAppointmentId = r.GetString(2),
            NegotiationId = r.IsDBNull(3) ? null : r.GetString(3),
            CustomerId = r.GetString(4),
            TermsVersionId = r.GetInt32(5),
            TermsVersionCode = r.GetString(6),
            DeliveryBusinessDays = r.GetInt32(7),
            StudioName = r.GetString(8),
            StudioAddress = r.GetString(9),
            StudioPhone = r.GetString(10),
            StudioEmail = r.GetString(11),
            ClientName = r.GetString(12),
            ClientAddress = r.IsDBNull(13) ? null : r.GetString(13),
            ClientPhone = r.IsDBNull(14) ? null : r.GetString(14),
            ClientEmail = r.GetString(15),
            SessionType = r.GetString(16),
            DateOfShoot = DateOnly.FromDateTime(r.GetDateTime(17)),
            StartTime = TimeOnly.FromTimeSpan(r.GetFieldValue<TimeSpan>(18)),
            EndTime = TimeOnly.FromTimeSpan(r.GetFieldValue<TimeSpan>(19)),
            Venue = r.GetString(20),
            PackageName = r.GetString(21),
            PackageInclusions = r.GetString(22),
            TotalPackageAmount = r.GetDecimal(23),
            ReservationFeeAmount = r.GetDecimal(24),
            RemainingBalanceAmount = r.GetDecimal(25),
            RepresentativeName = r.GetString(26),
            ContractStatus = r.GetString(27),
            AcceptedAtUtc = r.IsDBNull(28) ? null : r.GetDateTime(28),
            ClientIpAddress = r.IsDBNull(29) ? null : r.GetString(29),
            UserAgent = r.IsDBNull(30) ? null : r.GetString(30),
            ClickwrapReference = r.IsDBNull(31) ? null : r.GetString(31),
            RenderedContractHtml = r.GetString(32),
            PdfBucketName = r.IsDBNull(33) ? null : r.GetString(33),
            PdfObjectKey = r.IsDBNull(34) ? null : r.GetString(34),
            PdfUri = r.IsDBNull(35) ? null : r.GetString(35),
            CreatedAtUtc = r.GetDateTime(36),
            UpdatedAtUtc = r.GetDateTime(37)
        };
    }

    private static async Task<List<BookingPaymentLedgerDto>> GetPaymentsOnConnectionAsync(
        SqlConnection conn, SqlTransaction? tx, string eventAppointmentId, CancellationToken ct)
    {
        const string sql = @"
            SELECT BookingPaymentId, EventAppointmentID, BookingContractId, PaymentStage, PaymentMethod,
                   Amount, OvertimeAmount, GatewayReferenceId, ExternalReference, Status, PaidAtUtc,
                   VerifiedByEmployeeID, Notes
            FROM dbo.BookingPayments
            WHERE EventAppointmentID = @EvtId
            ORDER BY BookingPaymentId ASC;";
        var list = new List<BookingPaymentLedgerDto>();
        await using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("@EvtId", eventAppointmentId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new BookingPaymentLedgerDto
            {
                BookingPaymentId = r.GetInt32(0),
                EventAppointmentId = r.GetString(1),
                BookingContractId = r.IsDBNull(2) ? null : r.GetInt32(2),
                PaymentStage = r.GetString(3),
                PaymentMethod = r.GetString(4),
                Amount = r.GetDecimal(5),
                OvertimeAmount = r.GetDecimal(6),
                GatewayReferenceId = r.GetString(7),
                ExternalReference = r.IsDBNull(8) ? null : r.GetString(8),
                Status = r.GetString(9),
                PaidAtUtc = r.GetDateTime(10),
                VerifiedByEmployeeId = r.IsDBNull(11) ? null : r.GetString(11),
                Notes = r.IsDBNull(12) ? null : r.GetString(12)
            });
        }
        return list;
    }

    private static async Task<List<BookingReceiptDto>> GetReceiptsOnConnectionAsync(
        SqlConnection conn, SqlTransaction? tx, string eventAppointmentId, CancellationToken ct)
    {
        const string sql = @"
            SELECT BookingReceiptId, ReceiptNumber, EventAppointmentID, BookingPaymentId, ReceiptType,
                   TotalContractCharges, Stage1PaidAmount, OvertimeAddOnAmount, Stage2PaidAmount,
                   RemainingBalanceDue, PaymentMethod, GatewayReferenceId, IssuedAtUtc,
                   RenderedReceiptHtml, PdfBucketName, PdfObjectKey, PdfUri
            FROM dbo.BookingReceipts
            WHERE EventAppointmentID = @EvtId
            ORDER BY BookingReceiptId ASC;";
        var list = new List<BookingReceiptDto>();
        await using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("@EvtId", eventAppointmentId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(ReadReceipt(r));
        }
        return list;
    }

    private static async Task<BookingReceiptDto?> GetReceiptByTypeOnConnectionAsync(
        SqlConnection conn, SqlTransaction? tx, string eventAppointmentId, string receiptType, CancellationToken ct)
    {
        const string sql = @"
            SELECT TOP 1 BookingReceiptId, ReceiptNumber, EventAppointmentID, BookingPaymentId, ReceiptType,
                   TotalContractCharges, Stage1PaidAmount, OvertimeAddOnAmount, Stage2PaidAmount,
                   RemainingBalanceDue, PaymentMethod, GatewayReferenceId, IssuedAtUtc,
                   RenderedReceiptHtml, PdfBucketName, PdfObjectKey, PdfUri
            FROM dbo.BookingReceipts
            WHERE EventAppointmentID = @EvtId AND ReceiptType = @Type;";
        await using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("@EvtId", eventAppointmentId);
        cmd.Parameters.AddWithValue("@Type", receiptType);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadReceipt(r) : null;
    }

    private static TermsVersionDto ReadTerms(SqlDataReader r) => new()
    {
        TermsVersionId = r.GetInt32(0),
        VersionCode = r.GetString(1),
        Title = r.GetString(2),
        EffectiveDate = DateOnly.FromDateTime(r.GetDateTime(3)),
        DeliveryBusinessDays = r.GetInt32(4),
        OvertimeHourlyRate = r.GetDecimal(5),
        TermsBodyHtml = r.GetString(6),
        IsActive = r.GetBoolean(7),
        CreatedAtUtc = r.GetDateTime(8)
    };

    private static BookingReceiptDto ReadReceipt(SqlDataReader r) => new()
    {
        BookingReceiptId = r.GetInt32(0),
        ReceiptNumber = r.GetString(1),
        EventAppointmentId = r.GetString(2),
        BookingPaymentId = r.IsDBNull(3) ? null : r.GetInt32(3),
        ReceiptType = r.GetString(4),
        TotalContractCharges = r.GetDecimal(5),
        Stage1PaidAmount = r.GetDecimal(6),
        OvertimeAddOnAmount = r.GetDecimal(7),
        Stage2PaidAmount = r.GetDecimal(8),
        RemainingBalanceDue = r.GetDecimal(9),
        PaymentMethod = r.GetString(10),
        GatewayReferenceId = r.IsDBNull(11) ? null : r.GetString(11),
        IssuedAtUtc = r.GetDateTime(12),
        RenderedReceiptHtml = r.GetString(13),
        PdfBucketName = r.IsDBNull(14) ? null : r.GetString(14),
        PdfObjectKey = r.IsDBNull(15) ? null : r.GetString(15),
        PdfUri = r.IsDBNull(16) ? null : r.GetString(16)
    };
}
