using PhotoAevumStudio.Application.DTOs;

namespace PhotoAevumStudio.Application.Interfaces;

public interface IPhotoService
{
    Task<IEnumerable<PhotoResponseDto>> GetAllAsync();
    Task<PhotoResponseDto> CreateAsync(CreatePhotoDto dto);
}