using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Infrastructure.Data;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Services;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/portfolio")]
public class PortfolioController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IR2StorageService _r2Service;
    private readonly ISqlAuthService _sqlAuthService;

    public PortfolioController(ApplicationDbContext db, IR2StorageService r2Service, ISqlAuthService sqlAuthService)
    {
        _db = db;
        _r2Service = r2Service;
        _sqlAuthService = sqlAuthService;
    }

    // ── Categories ────────────────────────────────────────────────────────────

    [HttpGet("categories")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken)
    {
        var categories = await _db.PortfolioCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
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

        string categoryId;
        if (!string.IsNullOrWhiteSpace(request.CategoryID))
        {
            categoryId = request.CategoryID.Trim().ToUpperInvariant();
        }
        else
        {
            var existing = await _db.PortfolioCategories
                .Where(c => c.CategoryID.StartsWith("PCAT-"))
                .Select(c => c.CategoryID)
                .ToListAsync(cancellationToken);

            var maxNum = 0;
            foreach (var id in existing)
            {
                var parts = id.Split('-');
                if (parts.Length > 1 && int.TryParse(parts[1], out var n) && n > maxNum)
                {
                    maxNum = n;
                }
            }
            categoryId = $"PCAT-{(maxNum + 1):D4}";
        }

        if (await _db.PortfolioCategories.AnyAsync(c => c.CategoryID == categoryId, cancellationToken))
            return BadRequest(new ProblemDetails { Title = "Category already exists", Detail = $"A category with ID '{categoryId}' already exists.", Status = 400 });

        var category = new PortfolioCategoryEntity
        {
            CategoryID = categoryId,
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
        await _sqlAuthService.LogActivityAsync("CREATE", "PortfolioCategory", category.CategoryID, $"Created portfolio category {category.CategoryName}", "Admin", actorId, cancellationToken);

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
            query = query.Where(p => p.CategoryID == category);
        }

        if (featuredOnly == true)
        {
            query = query.Where(p => p.IsFeatured);
        }

        var list = await query
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        var categories = await _db.PortfolioCategories.AsNoTracking().ToListAsync(cancellationToken);
        var catDict = categories.ToDictionary(c => c.CategoryID, c => c.CategoryName, StringComparer.OrdinalIgnoreCase);

        var result = list.Select(p => new
        {
            p.PortfolioId,
            p.PortfolioName,
            p.PortfolioDescription,
            p.CategoryID,
            CategoryName = catDict.TryGetValue(p.CategoryID, out var cName) ? cName : p.CategoryID,
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
        var catDict = categories.ToDictionary(c => c.CategoryID, c => c.CategoryName, StringComparer.OrdinalIgnoreCase);

        var result = list.Select(p => new
        {
            p.PortfolioId,
            p.PortfolioName,
            p.PortfolioDescription,
            p.CategoryID,
            CategoryName = catDict.TryGetValue(p.CategoryID, out var cName) ? cName : p.CategoryID,
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
        if (string.IsNullOrWhiteSpace(request.CategoryID))
            return BadRequest(new ProblemDetails { Title = "Portfolio category required", Status = 400 });

        var existing = await _db.Portfolios
            .Where(p => p.PortfolioId.StartsWith("PRF-"))
            .Select(p => p.PortfolioId)
            .ToListAsync(cancellationToken);

        var maxNum = 0;
        foreach (var id in existing)
        {
            var parts = id.Split('-');
            if (parts.Length > 1 && int.TryParse(parts[1], out var n) && n > maxNum)
            {
                maxNum = n;
            }
        }
        var portfolioId = $"PRF-{(maxNum + 1):D4}";

        var portfolio = new PortfolioEntity
        {
            PortfolioId = portfolioId,
            PortfolioName = request.PortfolioName.Trim(),
            PortfolioDescription = request.PortfolioDescription?.Trim(),
            CategoryID = request.CategoryID.Trim(),
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
        await _sqlAuthService.LogActivityAsync("CREATE", "Portfolio", portfolio.PortfolioId, $"Created portfolio folder {portfolio.PortfolioName} ({portfolio.PortfolioId})", "Admin", actorId, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new
        {
            portfolio.PortfolioId,
            portfolio.PortfolioName,
            portfolio.CategoryID,
            portfolio.IsFeatured
        });
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,SystemAdmin")]
    public async Task<IActionResult> UpdatePortfolio(string id, [FromBody] UpdatePortfolioRequest request, CancellationToken cancellationToken)
    {
        var portfolio = await _db.Portfolios.Include(p => p.Images).FirstOrDefaultAsync(p => p.PortfolioId == id, cancellationToken);
        if (portfolio == null)
            return NotFound(new ProblemDetails { Title = "Portfolio not found", Status = 404 });

        if (!string.IsNullOrWhiteSpace(request.PortfolioName))
            portfolio.PortfolioName = request.PortfolioName.Trim();
        if (request.PortfolioDescription != null)
            portfolio.PortfolioDescription = request.PortfolioDescription.Trim();
        if (!string.IsNullOrWhiteSpace(request.CategoryID))
            portfolio.CategoryID = request.CategoryID.Trim();

        if (request.NewImageFileNames != null && request.NewImageFileNames.Count > 0)
        {
            foreach (var fn in request.NewImageFileNames)
            {
                portfolio.Images.Add(new PortfolioImage
                {
                    PortfolioId = id,
                    FileName = fn,
                    UploadedAt = DateTime.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var actorId = User.FindFirstValue("studio_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Admin";
        await _sqlAuthService.LogActivityAsync("UPDATE", "Portfolio", id, $"Updated portfolio {portfolio.PortfolioName} ({id})", "Admin", actorId, cancellationToken);

        return Ok(portfolio);
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
        await _sqlAuthService.LogActivityAsync("DELETE", "Portfolio", id, $"Deleted portfolio {portfolio.PortfolioName} ({id})", "Admin", actorId, cancellationToken);

        return NoContent();
    }
}

public record CreatePortfolioCategoryRequest(string? CategoryID, string CategoryName, string? Description, string? ParentSection, string? ColorTag);
public record CreatePortfolioRequest(string PortfolioName, string? PortfolioDescription, string CategoryID, bool IsFeatured, string? HighlightImageFileName, List<string>? ImageFileNames);
public record UpdatePortfolioRequest(string? PortfolioName, string? PortfolioDescription, string? CategoryID, List<string>? NewImageFileNames);
