<<<<<<< HEAD
using PhotoAevumStudio.Application.DTOs;

namespace PhotoAevumStudio.Application.Interfaces;

public interface IPhotoService
{
    Task<IEnumerable<PhotoResponseDto>> GetAllAsync(string? customerId = null);
    Task<PhotoResponseDto> CreateAsync(CreatePhotoDto dto);
=======
using PhotoAevumStudio.Application.DTOs;

namespace PhotoAevumStudio.Application.Interfaces;

public interface IPhotoService
{
    Task<IEnumerable<PhotoResponseDto>> GetAllAsync(string? customerId = null);
    Task<PhotoResponseDto> CreateAsync(CreatePhotoDto dto);
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
}