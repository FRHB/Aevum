namespace PhotoAevumStudio.Application;

public class StudioProfileSettings
{
    public const string SectionName = "StudioProfile";

    public string StudioName { get; set; } = "Photo Aevum Studio";
    public string StudioAddress { get; set; } = "Metro Manila, Philippines";
    public string StudioPhone { get; set; } = "(+63) 919-578-93124";
    public string StudioEmail { get; set; } = "photoaevumstudio@gmail.com";
    public string RepresentativeName { get; set; } = "Authorized Studio Representative";
    public int DefaultDeliveryBusinessDays { get; set; } = 7;
    public decimal DefaultOvertimeHourlyRate { get; set; } = 1500.00m;
}

public class TermsVersionDto
{
    public int TermsVersionId { get; set; }
    public string VersionCode { get; set; } = "v1.0";
    public string Title { get; set; } = "Standard Terms and Conditions";
    public DateOnly EffectiveDate { get; set; }
    public int DeliveryBusinessDays { get; set; }
    public decimal OvertimeHourlyRate { get; set; }
    public string TermsBodyHtml { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class BookingContractDto
{
    public int BookingContractId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string EventAppointmentId { get; set; } = string.Empty;
    public string? NegotiationId { get; set; }
    public string CustomerId { get; set; } = string.Empty;

    public int TermsVersionId { get; set; }
    public string TermsVersionCode { get; set; } = "v1.0";
    public int DeliveryBusinessDays { get; set; } = 7;

    public string StudioName { get; set; } = string.Empty;
    public string StudioAddress { get; set; } = string.Empty;
    public string StudioPhone { get; set; } = string.Empty;
    public string StudioEmail { get; set; } = string.Empty;

    public string ClientName { get; set; } = string.Empty;
    public string? ClientAddress { get; set; }
    public string? ClientPhone { get; set; }
    public string ClientEmail { get; set; } = string.Empty;

    public string SessionType { get; set; } = string.Empty;
    public DateOnly DateOfShoot { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string Venue { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string PackageInclusions { get; set; } = string.Empty;

    public decimal TotalPackageAmount { get; set; }
    public decimal ReservationFeeAmount { get; set; }
    public decimal RemainingBalanceAmount { get; set; }
    public string RepresentativeName { get; set; } = string.Empty;

    public string ContractStatus { get; set; } = "PendingAcceptance";
    public bool IsAccepted => AcceptedAtUtc.HasValue;
    public DateTime? AcceptedAtUtc { get; set; }
    public string? ClientIpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? ClickwrapReference { get; set; }

    public string RenderedContractHtml { get; set; } = string.Empty;
    public string? PdfBucketName { get; set; }
    public string? PdfObjectKey { get; set; }
    public string? PdfUri { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class BookingPaymentLedgerDto
{
    public int BookingPaymentId { get; set; }
    public string EventAppointmentId { get; set; } = string.Empty;
    public int? BookingContractId { get; set; }
    public string PaymentStage { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal OvertimeAmount { get; set; }
    public string GatewayReferenceId { get; set; } = string.Empty;
    public string? ExternalReference { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime PaidAtUtc { get; set; }
    public string? VerifiedByEmployeeId { get; set; }
    public string? Notes { get; set; }
}

public class BookingReceiptDto
{
    public int BookingReceiptId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public string EventAppointmentId { get; set; } = string.Empty;
    public int? BookingPaymentId { get; set; }
    public string ReceiptType { get; set; } = string.Empty;
    public decimal TotalContractCharges { get; set; }
    public decimal Stage1PaidAmount { get; set; }
    public decimal OvertimeAddOnAmount { get; set; }
    public decimal Stage2PaidAmount { get; set; }
    public decimal RemainingBalanceDue { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? GatewayReferenceId { get; set; }
    public DateTime IssuedAtUtc { get; set; }
    public string RenderedReceiptHtml { get; set; } = string.Empty;
    public string? PdfBucketName { get; set; }
    public string? PdfObjectKey { get; set; }
    public string? PdfUri { get; set; }
}

public class BookingDocumentsBundleDto
{
    public string EventAppointmentId { get; set; } = string.Empty;
    public string EventStatus { get; set; } = string.Empty;
    public decimal TotalPackageAmount { get; set; }
    public decimal ReservationFeeAmount { get; set; }
    public decimal OvertimeAmount { get; set; }
    public decimal RemainingBalance { get; set; }
    public bool IsDeliverablesUnlocked { get; set; }
    public BookingContractDto? Contract { get; set; }
    public TermsVersionDto? TermsVersion { get; set; }
    public List<BookingPaymentLedgerDto> Payments { get; set; } = new();
    public List<BookingReceiptDto> Receipts { get; set; } = new();
}

public class AcceptContractRequest
{
    public bool AgreeToServicesAgreement { get; set; }
    public bool AgreeToStandardTerms { get; set; }
    public int? TermsVersionId { get; set; }
    public string? ClickwrapReference { get; set; }
}

public class ConfigureBookingContractRequest
{
    public decimal? TotalPackageAmount { get; set; }
    public decimal? ReservationFeeAmount { get; set; }
    public string? PackageName { get; set; }
    public string? PackageInclusions { get; set; }
    public int? DeliveryBusinessDays { get; set; }
    public string? RepresentativeName { get; set; }
}
