using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PortfolioController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IR2StorageService _r2Service;
    private readonly ISqlAuthService _sqlAuthService;
    private readonly ILogger<PortfolioController> _logger;

    public PortfolioController(
        ApplicationDbContext db,
        IR2StorageService r2Service,
        ISqlAuthService sqlAuthService,
        ILogger<PortfolioController> logger)
    {
        _db = db;
        _r2Service = r2Service;
        _sqlAuthService = sqlAuthService;
        _logger = logger;
    }

    // ── Categories ────────────────────────────────────────────────────────────

    [HttpGet("categories")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken)
    {
        var categories = await _db.PortfolioCategories
            .AsNoTracking()
            .OrderBy(c => c.CategoryName)
            .ToListAsync(cancellationToken);

        return Ok(categories);
    }

    [HttpPost("categories")]
    [Authorize(Roles = "Admin,SystemAdmin,Office Clerk")]
    public async Task<IActionResult> CreateCategory([FromBody] CreatePortfolioCategoryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CategoryName))
            return BadRequest(new ProblemDetails { Title = "Category name required", Status = 400 });

        var code = string.IsNullOrWhiteSpace(request.CategoryCode)
            ? request.CategoryName.Trim().ToUpperInvariant().Replace(" ", "_")
            : request.CategoryCode.Trim().ToUpperInvariant();

        if (code.Length > 50) code = code[..50];

        if (await _db.PortfolioCategories.AnyAsync(c => c.CategoryCode == code, cancellationToken))
            return BadRequest(new ProblemDetails { Title = "Category already exists", Detail = $"A category with code '{code}' already exists.", Status = 400 });

        var category = new PortfolioCategoryEntity
        {
            CategoryCode = code,
            CategoryName = request.CategoryName.Trim(),
            Description = request.Description?.Trim(),
            ParentSection = string.IsNullOrWhiteSpace(request.ParentSection) ? "Photography" : request.ParentSection.Trim(),
            ColorTag = string.IsNullOrWhiteSpace(request.ColorTag) ? "Blue" : request.ColorTag.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.PortfolioCategories.Add(category);
        await _db.SaveChangesAsync(cancellationToken);

        var actorId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Admin";
        await _sqlAuthService.LogActivityAsync("CREATE", "PortfolioCategory", category.CategoryCode, $"Created portfolio category {category.CategoryName}", "Admin", actorId, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, category);
    }

    // ── Portfolios ────────────────────────────────────────────────────────────

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetPortfolios([FromQuery] string? category, [FromQuery] bool? featuredOnly, CancellationToken cancellationToken)
    {
        var query = _db.Portfolios
            .Include(p => p.Images)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => p.CategoryCode == category);
        }

        if (featuredOnly == true)
        {
            query = query.Where(p => p.IsFeatured);
        }

        var list = await query
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        var result = list.Select(p => new
        {
            p.PortfolioId,
            p.PortfolioName,
            p.PortfolioDescription,
            p.CategoryCode,
            p.IsFeatured,
            p.HighlightImageFileName,
            HighlightImageUrl = !string.IsNullOrEmpty(p.HighlightImageFileName) ? _r2Service.GetPresignedUrl(p.HighlightImageFileName) : (p.Images.FirstOrDefault() != null ? _r2Service.GetPresignedUrl(p.Images.First().FileName) : string.Empty),
            p.CreatedAt,
            Images = p.Images.Select(img => new
            {
                img.Id,
                img.FileName,
                Url = _r2Service.GetPresignedUrl(img.FileName),
                img.UploadedAt
            }).ToList()
        });

        return Ok(result);
    }

    [HttpGet("featured")]
    [AllowAnonymous]
    public async Task<IActionResult> GetFeaturedPortfolios(CancellationToken cancellationToken)
    {
        var list = await _db.Portfolios
            .Include(p => p.Images)
            .AsNoTracking()
            .Where(p => p.IsFeatured)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        var categories = await _db.PortfolioCategories.AsNoTracking().ToListAsync(cancellationToken);
        var catDict = categories.ToDictionary(c => c.CategoryCode, c => c.CategoryName, StringComparer.OrdinalIgnoreCase);

        var result = list.Select(p => new
        {
            p.PortfolioId,
            p.PortfolioName,
            p.PortfolioDescription,
            p.CategoryCode,
            CategoryName = catDict.TryGetValue(p.CategoryCode, out var cName) ? cName : p.CategoryCode,
            p.IsFeatured,
            HighlightImageUrl = !string.IsNullOrEmpty(p.HighlightImageFileName) ? _r2Service.GetPresignedUrl(p.HighlightImageFileName) : (p.Images.FirstOrDefault() != null ? _r2Service.GetPresignedUrl(p.Images.First().FileName) : string.Empty),
            Images = p.Images.Select(img => new
            {
                img.Id,
                img.FileName,
                Url = _r2Service.GetPresignedUrl(img.FileName)
            }).ToList()
        });

        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,SystemAdmin")]
    public async Task<IActionResult> CreatePortfolio([FromBody] CreatePortfolioRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PortfolioName))
            return BadRequest(new ProblemDetails { Title = "Portfolio name required", Status = 400 });
        if (string.IsNullOrWhiteSpace(request.CategoryCode))
            return BadRequest(new ProblemDetails { Title = "Portfolio category required", Status = 400 });

        var portfolioId = $"PORT-{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        var portfolio = new PortfolioEntity
        {
            PortfolioId = portfolioId,
            PortfolioName = request.PortfolioName.Trim(),
            PortfolioDescription = request.PortfolioDescription?.Trim(),
            CategoryCode = request.CategoryCode.Trim(),
            IsFeatured = request.IsFeatured,
            HighlightImageFileName = request.HighlightImageFileName,
            CreatedAt = DateTime.UtcNow
        };

        if (request.ImageFileNames != null && request.ImageFileNames.Count > 0)
        {
            foreach (var fn in request.ImageFileNames)
            {
                portfolio.Images.Add(new PortfolioImage
                {
                    PortfolioId = portfolioId,
                    FileName = fn,
                    UploadedAt = DateTime.UtcNow
                });
            }

            if (string.IsNullOrWhiteSpace(portfolio.HighlightImageFileName))
            {
                portfolio.HighlightImageFileName = request.ImageFileNames[0];
            }
        }

        _db.Portfolios.Add(portfolio);
        await _db.SaveChangesAsync(cancellationToken);

        var actorId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Admin";
        await _sqlAuthService.LogActivityAsync("CREATE", "Portfolio", portfolio.PortfolioId, $"Created portfolio folder {portfolio.PortfolioName}", "Admin", actorId, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new
        {
            portfolio.PortfolioId,
            portfolio.PortfolioName,
            portfolio.CategoryCode,
            portfolio.IsFeatured
        });
    }

    [HttpPatch("{id}/toggle-feature")]
    [Authorize(Roles = "Admin,SystemAdmin")]
    public async Task<IActionResult> ToggleFeature(string id, CancellationToken cancellationToken)
    {
        var portfolio = await _db.Portfolios.FindAsync([id], cancellationToken);
        if (portfolio == null)
            return NotFound(new ProblemDetails { Title = "Portfolio not found", Status = 404 });

        portfolio.IsFeatured = !portfolio.IsFeatured;
        await _db.SaveChangesAsync(cancellationToken);

        var actorId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Admin";
        await _sqlAuthService.LogActivityAsync("UPDATE", "Portfolio", portfolio.PortfolioId, $"Toggled feature status of {portfolio.PortfolioName} to {portfolio.IsFeatured}", "Admin", actorId, cancellationToken);

        return Ok(new { portfolio.PortfolioId, portfolio.IsFeatured });
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,SystemAdmin")]
    public async Task<IActionResult> DeletePortfolio(string id, CancellationToken cancellationToken)
    {
        var portfolio = await _db.Portfolios.Include(p => p.Images).FirstOrDefaultAsync(p => p.PortfolioId == id, cancellationToken);
        if (portfolio == null)
            return NotFound();

        _db.Portfolios.Remove(portfolio);
        await _db.SaveChangesAsync(cancellationToken);

        var actorId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Admin";
        await _sqlAuthService.LogActivityAsync("DELETE", "Portfolio", id, $"Deleted portfolio {portfolio.PortfolioName}", "Admin", actorId, cancellationToken);

        return NoContent();
    }
}

public record CreatePortfolioCategoryRequest(string CategoryCode, string CategoryName, string? Description, string? ParentSection, string? ColorTag);
public record CreatePortfolioRequest(string PortfolioName, string? PortfolioDescription, string CategoryCode, bool IsFeatured, string? HighlightImageFileName, List<string>? ImageFileNames);
