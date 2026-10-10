using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Infrastructure.Services;

public class PhotoService : IPhotoService
{
	private readonly ApplicationDbContext _context;

	public PhotoService(ApplicationDbContext context)
	{
		_context = context;
	}

	public async Task<IEnumerable<PhotoResponseDto>> GetAllAsync(string? customerId = null)
	{
        var query = _context.Photos.AsQueryable();
        if (!string.IsNullOrEmpty(customerId))
        {
            query = query.Where(p => p.Booking != null && p.Booking.CustomerId == customerId);
        }

		return await query
			.Select(p => new PhotoResponseDto(p.Id, p.Title, p.Url, p.UploadedAt))
			.ToListAsync();
	}

	public async Task<PhotoResponseDto> CreateAsync(CreatePhotoDto dto)
	{
		var photo = new Photo { Title = dto.Title, Url = dto.Url };
		_context.Photos.Add(photo);
		await _context.SaveChangesAsync();

		return new PhotoResponseDto(photo.Id, photo.Title, photo.Url, photo.UploadedAt);
	}
}