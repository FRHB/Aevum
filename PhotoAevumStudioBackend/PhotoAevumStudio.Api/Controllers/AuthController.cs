using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ILogger<AuthController> logger)
    {
        _userManager   = userManager;
        _configuration = configuration;
        _logger        = logger;
    }

    // ── POST /api/auth/login ─────────────────────────────────────────────────

    /// <summary>
    /// Authenticates an employee or admin by their StudioId or email + password.
    /// Returns a signed JWT on success.
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // Allow login by either email or StudioId
        var user = await _userManager.FindByEmailAsync(request.UserId)
                   ?? await _userManager.FindByNameAsync(request.UserId);

        // If not found by email/username, try StudioId
        if (user is null)
        {
            user = _userManager.Users
                .FirstOrDefault(u => u.StudioId == request.UserId);
        }

        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            _logger.LogWarning("Failed login attempt for UserId: {UserId}", request.UserId);
            return Unauthorized(new ProblemDetails
            {
                Title  = "Authentication failed",
                Detail = "Invalid ID or password.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        var roles = await _userManager.GetRolesAsync(user);
        var token = BuildJwt(user, roles);

        _logger.LogInformation("Successful login for {Email} ({Role})",
            user.Email, string.Join(',', roles));

        return Ok(new LoginResponse(
            Token:    token.Token,
            Role:     roles.FirstOrDefault() ?? string.Empty,
            FullName: user.FullName,
            ExpiresAt: token.ExpiresAt));
    }

    // ── POST /api/auth/register ──────────────────────────────────────────────

    /// <summary>
    /// Creates a new Employee or Customer account.
    /// Restricted to Admins only.
    /// </summary>
    [HttpPost("register")]
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!AppRoles.All.Contains(request.Role))
            return BadRequest(new ProblemDetails
            {
                Title  = "Invalid role",
                Detail = $"Role must be one of: {string.Join(", ", AppRoles.All)}",
                Status = StatusCodes.Status400BadRequest
            });

        var user = new ApplicationUser
        {
            UserName       = request.Email,
            Email          = request.Email,
            FirstName      = request.FirstName,
            LastName       = request.LastName,
            MiddleName     = request.MiddleName,
            StudioId       = request.StudioId,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description);
            return BadRequest(new ProblemDetails
            {
                Title  = "Registration failed",
                Detail = string.Join("; ", errors),
                Status = StatusCodes.Status400BadRequest
            });
        }

        await _userManager.AddToRoleAsync(user, request.Role);
        _logger.LogInformation("Created {Role} account for {Email}", request.Role, user.Email);

        return StatusCode(StatusCodes.Status201Created,
            new { user.Id, user.Email, user.FullName, Role = request.Role });
    }

    // ── JWT Builder ──────────────────────────────────────────────────────────

    private (string Token, DateTime ExpiresAt) BuildJwt(
        ApplicationUser user, IList<string> roles)
    {
        var key    = _configuration["Jwt:Key"]
                     ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var issuer = _configuration["Jwt:Issuer"] ?? "PhotoAevumStudioApi";
        var audience = _configuration["Jwt:Audience"] ?? "PhotoAevumStudioClients";

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
            new(ClaimTypes.Name,               user.UserName ?? string.Empty),
            new("full_name",                   user.FullName),
            new("studio_id",                   user.StudioId ?? string.Empty),
        };

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var expiresAt = DateTime.UtcNow.AddHours(8);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));

        var jwt = new JwtSecurityToken(
            issuer:             issuer,
            audience:           audience,
            claims:             claims,
            notBefore:          DateTime.UtcNow,
            expires:            expiresAt,
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(jwt), expiresAt);
    }
}
