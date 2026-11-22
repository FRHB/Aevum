namespace PhotoAevumStudio.Application.Entities;

public class TermsVersion
{
    public int TermsVersionId { get; set; }
    public string VersionCode { get; set; } = "v1.0";
    public string Title { get; set; } = "Standard Terms and Conditions";
    public DateOnly EffectiveDate { get; set; }
    public int DeliveryBusinessDays { get; set; } = 7;
    public decimal OvertimeHourlyRate { get; set; } = 1500.00m;
    public string TermsBodyHtml { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class BookingContract
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
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string Venue { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string PackageInclusions { get; set; } = string.Empty;

    public decimal TotalPackageAmount { get; set; }
    public decimal ReservationFeeAmount { get; set; }
    public decimal RemainingBalanceAmount { get; set; }
    public string RepresentativeName { get; set; } = string.Empty;

    public string ContractStatus { get; set; } = "PendingAcceptance";
    public DateTime? AcceptedAtUtc { get; set; }
    public string? ClientIpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? ClickwrapReference { get; set; }

    public string RenderedContractHtml { get; set; } = string.Empty;
    public string? PdfBucketName { get; set; }
    public string? PdfObjectKey { get; set; }
    public string? PdfUri { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public TermsVersion? TermsVersion { get; set; }
}

public class BookingPayment
{
    public int BookingPaymentId { get; set; }
    public string EventAppointmentId { get; set; } = string.Empty;
    public int? BookingContractId { get; set; }

    /// <summary>'ReservationFee' or 'FinalSettlement'</summary>
    public string PaymentStage { get; set; } = "ReservationFee";
    public string PaymentMethod { get; set; } = "QR Ph";
    public decimal Amount { get; set; }
    public decimal OvertimeAmount { get; set; }
    public string GatewayReferenceId { get; set; } = string.Empty;
    public string? ExternalReference { get; set; }
    public string Status { get; set; } = "Paid";
    public DateTime PaidAtUtc { get; set; } = DateTime.UtcNow;
    public string? VerifiedByEmployeeId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public BookingContract? BookingContract { get; set; }
}

public class BookingReceipt
{
    public int BookingReceiptId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public string EventAppointmentId { get; set; } = string.Empty;
    public int? BookingPaymentId { get; set; }

    /// <summary>'ReservationAcknowledgment' or 'FinalConsolidated'</summary>
    public string ReceiptType { get; set; } = "ReservationAcknowledgment";

    public decimal TotalContractCharges { get; set; }
    public decimal Stage1PaidAmount { get; set; }
    public decimal OvertimeAddOnAmount { get; set; }
    public decimal Stage2PaidAmount { get; set; }
    public decimal RemainingBalanceDue { get; set; }

    public string PaymentMethod { get; set; } = string.Empty;
    public string? GatewayReferenceId { get; set; }
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;

    public string RenderedReceiptHtml { get; set; } = string.Empty;
    public string? PdfBucketName { get; set; }
    public string? PdfObjectKey { get; set; }
    public string? PdfUri { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public BookingPayment? BookingPayment { get; set; }
}
