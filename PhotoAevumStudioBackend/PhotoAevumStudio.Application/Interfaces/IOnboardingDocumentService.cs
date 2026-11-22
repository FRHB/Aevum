namespace PhotoAevumStudio.Application.Interfaces;

public record OnboardingDocumentResult(
    bool Found,
    EmailAttachment? Attachment,
    string? DownloadUrl,
    string? ErrorMessage);

/// <summary>
/// Retrieves protected company onboarding documents from configured local or cloud storage for backend services.
/// </summary>
public interface IOnboardingDocumentService
{
    Task<OnboardingDocumentResult> GetEmployeeHandbookAsync(CancellationToken cancellationToken = default);
}
