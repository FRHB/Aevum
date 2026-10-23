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
