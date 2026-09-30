namespace PhotoAevumStudio.Application.DTOs;

// What the frontend receives
public record PhotoResponseDto(int Id, string Title, string Url, DateTime UploadedAt);

// What the frontend sends when creating a photo                            
public record CreatePhotoDto(string Title, string Url);