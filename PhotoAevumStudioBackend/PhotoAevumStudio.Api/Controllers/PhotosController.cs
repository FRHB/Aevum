using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;
using System.Security.Claims;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PhotosController : ControllerBase
{
    private readonly IPhotoService _photoService;

    public PhotosController(IPhotoService photoService)
    {
        _photoService = photoService;
    }

    /// <summary>Returns all photos. Available to all authenticated users.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var photos = await _photoService.GetAllAsync();
        return Ok(photos);
    }

    /// <summary>Creates a photo record. Admin and Employee only.</summary>
    [HttpPost]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Employee}")]
    public async Task<IActionResult> Create([FromBody] CreatePhotoDto dto)
    {
        var result = await _photoService.CreateAsync(dto);
        return CreatedAtAction(nameof(GetAll), new { id = result.Id }, result);
    }
}
