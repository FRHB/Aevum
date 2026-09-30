using Microsoft.AspNetCore.Mvc;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PhotosController : ControllerBase
{
    private readonly IPhotoService _photoService;

    public PhotosController(IPhotoService photoService)
    {
        _photoService = photoService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var photos = await _photoService.GetAllAsync();
        return Ok(photos);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePhotoDto dto)
    {
        var result = await _photoService.CreateAsync(dto);
        return CreatedAtAction(nameof(GetAll), new { id = result.Id }, result);
    }
}