namespace PhotoAevumStudio.Application.DTOs;

// Photos
public record PhotoResponseDto(int Id, string Title, string Url, DateTime UploadedAt);
public record CreatePhotoDto(string Title, string Url);

// Services & Catalog
public record ServiceSummaryDto(
    string Id,
    string Name,
    string Description,
    decimal Price,
    bool HasImage);

public record ServiceDetailDto(
    string Id,
    string Name,
    string Description,
    decimal Price,
    byte[]? Image,
    string? MimeType,
    bool HasImage);

public record UpdateServiceImageRequest(
    byte[] ImageBytes,
    string MimeType);

// Categories
public record CategoryDto(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string>? AssociatedServices = null,
    IReadOnlyList<CategoryAssociationDetailDto>? Associations = null);

public record CategoryAssociationDetailDto(
    string JoinId,
    string? ServiceId = null,
    string? ServiceName = null,
    string? AdditionalServiceId = null,
    string? AdditionalServiceName = null,
    string? ComboPackageId = null,
    string? ComboPackageName = null);

public record CreateCategoryDto(
    string Name,
    string Description,
    IReadOnlyList<string>? AssociatedServiceIds = null,
    IReadOnlyList<string>? AssociatedAdditionalServiceIds = null,
    IReadOnlyList<string>? AssociatedComboPackageIds = null,
    IReadOnlyList<string>? Associations = null);

// Service Wizard Creation Models
public record CreateServiceDto(
    string Name,
    string Description,
    decimal Price,
    string CategoryId,
    byte[]? ImageBytes,
    string? ImageMimeType,
    string? ImageFileName = null);

public record ServiceCreatedResponseDto(
    string Id,
    string Name,
    string Description,
    decimal Price,
    string CategoryId,
    string? CategoryName,
    bool HasImage);

// Auto-generated ID Response
public record GeneratedIdResponseDto(string NextId);
public record NextIdResponseDto(string NextId);

// Additional Services / Add-On Creation DTOs
public record CreateAdditionalServiceDto(
    string ServiceName,
    string ServiceDescription,
    string ServiceCategory,
    decimal ServicePrice,
    byte[]? ImageBytes,
    string? ImageMimeType);

public record AdditionalServiceResponseDto(
    string Id,
    string Name,
    string Description,
    string Category,
    decimal Price,
    bool HasImage,
    string? ImageUrl,
    DateTime CreatedAt);

// History Log
public record HistoryLogDto(
    long Id,
    string ActionType,
    string EntityName,
    string? EntityId,
    string? ActionDetails,
    string? EventAppointmentId,
    string? CustomerId,
    string? EmployeeId,
    string ChangedByType,
    string? ChangedBy,
    DateTime LoggedAt);

// ── Combo Package Wizard & Bundling DTOs ─────────────────────────────────────
public record CreateComboPackageModel(
    string Name,
    string? Description,
    string CategoryId,
    decimal Price,
    IReadOnlyList<string> SelectedServiceIds);

public record BundledServiceSelectionItem(
    string Id,
    string Name,
    string? Description,
    decimal Price,
    string ServiceType, // "Main" or "Additional"
    bool HasImage);

public record ComboPackageCreatedResponseDto(
    string PackageId,
    string Name,
    string? Description,
    decimal Price,
    string? CategoryId,
    string? CategoryName,
    int BundledServicesCount,
    IReadOnlyList<string> BundledServiceNames,
    bool HasImage,
    DateTime CreatedAt);

public record PackageNextIdDto(string PackageId);

