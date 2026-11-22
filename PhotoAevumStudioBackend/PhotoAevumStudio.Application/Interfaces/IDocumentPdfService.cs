namespace PhotoAevumStudio.Application.Interfaces;

public interface IDocumentPdfService
{
    byte[] GenerateServicesAgreementPdf(BookingContractDto contract, TermsVersionDto terms);
    byte[] GenerateStandardTermsPdf(TermsVersionDto terms, StudioProfileSettings? studio = null);
    byte[] GenerateReservationReceiptPdf(BookingReceiptDto receipt, BookingContractDto contract, TermsVersionDto terms);
    byte[] GenerateFinalConsolidatedReceiptPdf(
        BookingReceiptDto receipt,
        BookingContractDto contract,
        TermsVersionDto terms,
        string? stage1DateRef,
        string? stage2DateRef,
        string? overtimeNote);

    string RenderServicesAgreementHtml(BookingContractDto contract, TermsVersionDto terms);
    string RenderStandardTermsHtml(TermsVersionDto terms, StudioProfileSettings? studio = null);
    string RenderReservationReceiptHtml(BookingReceiptDto receipt, BookingContractDto contract, TermsVersionDto terms);
    string RenderFinalConsolidatedReceiptHtml(
        BookingReceiptDto receipt,
        BookingContractDto contract,
        TermsVersionDto terms,
        string? stage1DateRef,
        string? stage2DateRef,
        string? overtimeNote);
}
