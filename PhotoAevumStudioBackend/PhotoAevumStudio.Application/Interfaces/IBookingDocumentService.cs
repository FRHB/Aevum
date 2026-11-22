using System.Data.Common;

namespace PhotoAevumStudio.Application.Interfaces;

public interface IBookingDocumentService
{
    Task EnsureSchemaAsync(CancellationToken ct = default);

    Task<TermsVersionDto> GetActiveTermsVersionAsync(CancellationToken ct = default);
    Task<TermsVersionDto?> GetTermsVersionByIdAsync(int termsVersionId, CancellationToken ct = default);

    Task<BookingContractDto> GenerateOrUpdateDraftContractAsync(
        string eventAppointmentId,
        ConfigureBookingContractRequest? overrides = null,
        CancellationToken ct = default);

    Task<BookingDocumentsBundleDto?> GetBookingDocumentsBundleAsync(
        string eventAppointmentId,
        CancellationToken ct = default);

    Task<BookingContractDto> AcceptContractAsync(
        string eventAppointmentId,
        string callerCustomerId,
        bool isPrivilegedStaff,
        AcceptContractRequest request,
        string? clientIpAddress,
        string? userAgent,
        CancellationToken ct = default);

    Task<(BookingPaymentLedgerDto Payment, BookingReceiptDto Receipt)> RecordStagePaymentAndReceiptAsync(
        DbConnection conn,
        DbTransaction tx,
        string eventAppointmentId,
        string paymentStage,
        string paymentMethod,
        decimal amount,
        decimal overtimeAmount,
        string gatewayReferenceId,
        string? externalReference,
        string? verifiedByEmployeeId,
        string? notes,
        CancellationToken ct = default);

    Task FinalizeStageDocumentsAndEmailAsync(
        string eventAppointmentId,
        string receiptType,
        CancellationToken ct = default);

    Task<(byte[] PdfBytes, string FileName, string ContentType)> GetContractPdfAsync(
        string eventAppointmentId,
        CancellationToken ct = default);

    Task<(byte[] PdfBytes, string FileName, string ContentType)> GetReceiptPdfAsync(
        string eventAppointmentId,
        string receiptType,
        CancellationToken ct = default);

    Task<(byte[] PdfBytes, string FileName, string ContentType)> GetActiveTermsPdfAsync(
        int? termsVersionId = null,
        CancellationToken ct = default);

    Task<(bool Allowed, decimal RemainingBalance, string? BlockReason)> CheckDeliverablesAccessAsync(
        string eventAppointmentId,
        CancellationToken ct = default);
}
