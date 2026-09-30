using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PhotosController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public PhotosController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Photo>>> GetPhotos()
    {
        return await _context.Photos.ToListAsync();
    }
}