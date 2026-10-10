using PhotoAevumStudio.Application.DTOs;

namespace PhotoAevumStudio.Application.Interfaces;

public interface IPhotoService
{
    Task<IEnumerable<PhotoResponseDto>> GetAllAsync(string? customerId = null);
    Task<PhotoResponseDto> CreateAsync(CreatePhotoDto dto);
}