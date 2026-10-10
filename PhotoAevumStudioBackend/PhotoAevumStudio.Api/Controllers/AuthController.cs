using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Infrastructure.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;
    private readonly IMemoryCache _cache;
    private readonly IEmailService _emailService;
    private readonly ApplicationDbContext _dbContext;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ILogger<AuthController> logger,
        IMemoryCache cache,
        IEmailService emailService,
        ApplicationDbContext dbContext)
    {
        _userManager   = userManager;
        _configuration = configuration;
        _logger        = logger;
        _cache         = cache;
        _emailService  = emailService;
        _dbContext     = dbContext;
    }

    [HttpPost("send-verification")]
    [AllowAnonymous]
    public async Task<IActionResult> SendVerificationCode([FromBody] SendVerificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email)) return BadRequest("Email is required.");
        
        var code = new Random().Next(100000, 999999).ToString();
        _cache.Set($"VerificationCode_{request.Email}", code, TimeSpan.FromMinutes(10));
        
        await _emailService.SendEmailAsync(
            request.Email,
            "Your Photo Aevum Studio Verification Code",
            $"<p>Your verification code is: <strong>{code}</strong></p><p>This code will expire in 10 minutes.</p>",
            isHtml: true);
            
        return Ok(new { Message = "Verification code sent." });
    }

    [HttpPost("register-customer")]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterCustomer([FromBody] CustomerRegisterRequest request)
    {
        if (!_cache.TryGetValue($"VerificationCode_{request.Email}", out string? storedCode) || storedCode != request.VerificationCode)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid or expired verification code", Status = 400 });
        }
        
        var user = new ApplicationUser
        {
            UserName       = request.Email,
            Email          = request.Email,
            FirstName      = request.FirstName,
            LastName       = request.LastName,
            MiddleName     = request.MiddleName,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(new ProblemDetails { Title = "Registration failed", Detail = string.Join("; ", result.Errors.Select(e => e.Description)) });
            
        await _userManager.AddToRoleAsync(user, AppRoles.Customer);
        _cache.Remove($"VerificationCode_{request.Email}");
        return Ok(new { Message = "Customer registered successfully." });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.UserId)
                   ?? await _userManager.FindByNameAsync(request.UserId);

        if (user is null)
        {
            user = _userManager.Users.FirstOrDefault(u => u.StudioId == request.UserId);
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

        _logger.LogInformation("Successful login for {Email} ({Role})", user.Email, string.Join(',', roles));

        return Ok(new LoginResponse(
            Token:    token.Token,
            Role:     roles.FirstOrDefault() ?? string.Empty,
            FullName: user.FullName,
            ExpiresAt: token.ExpiresAt));
    }

    [HttpPost("register")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (request.Role == AppRoles.Admin && !System.Text.RegularExpressions.Regex.IsMatch(request.StudioId ?? "", @"^(SADM|ADM)-\d{3}$"))
        {
            return BadRequest(new ProblemDetails { Title = "Invalid Admin ID format", Detail = "Admin ID must be SADM-### or ADM-###" });
        }
        if (request.Role == AppRoles.Employee && !System.Text.RegularExpressions.Regex.IsMatch(request.StudioId ?? "", @"^EMP-\d{3}$"))
        {
            return BadRequest(new ProblemDetails { Title = "Invalid Employee ID format", Detail = "Employee ID must be EMP-###" });
        }

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
        if (request.Role == AppRoles.Employee)
        {
            _dbContext.Employees.Add(new Employee { IdentityUserId = user.Id });
            await _dbContext.SaveChangesAsync();
        }
        _logger.LogInformation("Created {Role} account for {Email}", request.Role, user.Email);

        return StatusCode(StatusCodes.Status201Created,
            new { user.Id, user.Email, user.FullName, Role = request.Role });
    }

    private (string Token, DateTime ExpiresAt) BuildJwt(ApplicationUser user, IList<string> roles)
    {
        var key    = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
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

        foreach (var role in roles) claims.Add(new Claim(ClaimTypes.Role, role));

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



