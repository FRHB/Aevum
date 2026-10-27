namespace PhotoAevumStudio.Application.Interfaces;

public class XenditInvoiceResult
{
    public string InvoiceId { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string InvoiceUrl { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";
    public decimal Amount { get; set; }
}

public class XenditPaymentStatusResult
{
    public string InvoiceId { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string Status { get; set; } = "PAID"; // 'PAID', 'PENDING', 'EXPIRED', 'SETTLED'
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "XENDIT";
    public string? PaymentChannel { get; set; }
}

public interface IXenditPaymentService
{
    Task<XenditInvoiceResult> CreateInvoiceAsync(
        string externalId,
        string description,
        decimal amount,
        string customerName,
        string customerEmail,
        string successUrl,
        string failureUrl,
        CancellationToken cancellationToken = default);

    Task<XenditPaymentStatusResult> CheckPaymentStatusAsync(
        string invoiceId,
        CancellationToken cancellationToken = default);

    Task<XenditPaymentStatusResult> CheckPaymentStatusByExternalIdAsync(
        string externalId,
        CancellationToken cancellationToken = default);
}
