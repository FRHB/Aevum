namespace PhotoAevumStudio.Application.Models;

/// <summary>
/// Configurable storage settings for company onboarding documents (such as the Employee Handbook & Operations Manual).
/// Values can be bound from appsettings ("OnboardingDocuments") or environment variables.
/// </summary>
public class OnboardingDocumentsSettings
{
    public const string SectionName = "OnboardingDocuments";

    public string StoragePath { get; set; } = "storage/documents/Employee_Handbook_and_Manual_PHOTO_AEVUM_STUDIO.pdf";
    public string HandbookFileName { get; set; } = "Employee_Handbook_and_Manual_PHOTO_AEVUM_STUDIO.pdf";
    public string R2Bucket { get; set; } = "documents";
    public string R2ObjectKey { get; set; } = "onboarding/Employee_Handbook_and_Manual_PHOTO_AEVUM_STUDIO.pdf";
    public bool PreferR2 { get; set; } = false;
    public int MaxAttachmentBytes { get; set; } = 10 * 1024 * 1024;
    public int PresignedUrlExpiryMinutes { get; set; } = 7 * 24 * 60;
}
