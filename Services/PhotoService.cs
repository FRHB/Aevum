using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Infrastructure.Services;

public class PhotoService : IPhotoService
{
    private readonly ApplicationDbContext _context;
    private readonly IStorageProvider _storageProvider;

    public PhotoService(ApplicationDbContext context, IStorageProvider storageProvider)
    {
        _context = context;
        _storageProvider = storageProvider;
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

    public async Task<PhotoResponseDto> UploadAsync(
        string fileName,
        Stream stream,
        string contentType,
        string? title = null,
        int? bookingId = null,
        CancellationToken cancellationToken = default)
    {
        var fileIdentifier = await _storageProvider.UploadFileAsync(fileName, stream, contentType, cancellationToken);
        var secureUrl = await _storageProvider.GetSecureUrlAsync(fileIdentifier, null, cancellationToken);

        var photoTitle = !string.IsNullOrWhiteSpace(title)
            ? title.Trim()
            : Path.GetFileNameWithoutExtension(fileName);

        var photo = new Photo
        {
            Title = photoTitle,
            Url = secureUrl,
            BookingId = bookingId,
            UploadedAt = DateTime.UtcNow
        };

        _context.Photos.Add(photo);
        await _context.SaveChangesAsync(cancellationToken);

        return new PhotoResponseDto(photo.Id, photo.Title, photo.Url, photo.UploadedAt);
    }

    public async Task<(Stream Stream, string ContentType, string FileName)?> GetPhotoFileAsync(
        string fileIdentifier,
        string? customerId = null,
        CancellationToken cancellationToken = default)
    {
        var cleanIdentifier = Path.GetFileName(fileIdentifier);
        var expectedSuffix = $"/{cleanIdentifier}";

        // If customerId is provided, enforce access control
        if (!string.IsNullOrEmpty(customerId))
        {
            var isAllowed = await _context.Photos
                .AnyAsync(p => (p.Url.EndsWith(expectedSuffix) || p.Url == cleanIdentifier)
                            && p.Booking != null
                            && p.Booking.CustomerId == customerId, cancellationToken);

            if (!isAllowed)
                return null;
        }

        var fileResult = await _storageProvider.GetFileAsync(cleanIdentifier, cancellationToken);
        if (fileResult == null)
            return null;

        return (fileResult.Value.Stream, fileResult.Value.ContentType, cleanIdentifier);
    }

    public async Task<(Stream Stream, string ContentType, string FileName)?> GetPhotoFileByIdAsync(
        int id,
        string? customerId = null,
        CancellationToken cancellationToken = default)
    {
        var photo = await _context.Photos
            .Include(p => p.Booking)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (photo == null)
            return null;

        if (!string.IsNullOrEmpty(customerId))
        {
            if (photo.Booking == null || photo.Booking.CustomerId != customerId)
                return null;
        }

        var fileIdentifier = Path.GetFileName(photo.Url);
        var fileResult = await _storageProvider.GetFileAsync(fileIdentifier, cancellationToken);
        if (fileResult == null)
            return null;

        return (fileResult.Value.Stream, fileResult.Value.ContentType, fileIdentifier);
    }
}