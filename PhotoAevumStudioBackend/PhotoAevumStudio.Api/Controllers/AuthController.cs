using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Infrastructure.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;
    private readonly IMemoryCache _cache;
    private readonly IEmailService _emailService;
    private readonly ApplicationDbContext _dbContext;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration configuration,
        ILogger<AuthController> logger,
        IMemoryCache cache,
        IEmailService emailService,
        ApplicationDbContext dbContext)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _configuration = configuration;
        _logger = logger;
        _cache = cache;
        _emailService = emailService;
        _dbContext = dbContext;
    }

    [HttpPost("send-verification")]
    [AllowAnonymous]
    public async Task<IActionResult> SendVerificationCode([FromBody] SendVerificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email)) return BadRequest("Email is required.");

        // Cryptographically secure 6-digit code (System.Random is predictable).
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
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
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            MiddleName = request.MiddleName,
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
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.UserId)
                   ?? await _userManager.FindByNameAsync(request.UserId)
                   ?? await _userManager.Users.FirstOrDefaultAsync(u => u.StudioId == request.UserId);

        if (user is null)
        {
            _logger.LogWarning("Failed login attempt (unknown account).");
            return InvalidCredentials();
        }

        // lockoutOnFailure: true increments the failed-attempt counter and locks the
        // account once Lockout.MaxFailedAccessAttempts is reached (see Program.cs).
        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            if (result.IsLockedOut)
                _logger.LogWarning("Login blocked: account {UserId} is locked out.", user.Id);
            else
                _logger.LogWarning("Failed login attempt for account {UserId}.", user.Id);

            // Same response for wrong ID, wrong password and locked account,
            // so callers can't tell which accounts exist or are locked.
            return InvalidCredentials();
        }

        var roles = await _userManager.GetRolesAsync(user);
        var token = BuildJwt(user, roles);

        _logger.LogInformation("Successful login for {Email} ({Role})", user.Email, string.Join(',', roles));

        return Ok(new LoginResponse(
            Token: token.Token,
            Role: roles.FirstOrDefault() ?? string.Empty,
            FullName: user.FullName,
            ExpiresAt: token.ExpiresAt,
            MustChangePassword: user.MustChangePassword));
    }

    private IActionResult InvalidCredentials() =>
        Unauthorized(new ProblemDetails
        {
            Title = "Authentication failed",
            Detail = "Invalid credentials.",
            Status = StatusCodes.Status401Unauthorized
        });

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
                Title = "Invalid role",
                Detail = $"Role must be one of: {string.Join(", ", AppRoles.All)}",
                Status = StatusCodes.Status400BadRequest
            });

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            MiddleName = request.MiddleName,
            StudioId = request.StudioId,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description);
            return BadRequest(new ProblemDetails
            {
                Title = "Registration failed",
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
        var key = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var issuer = _configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
        var audience = _configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is not configured.");
        var minutes = _configuration.GetValue<int?>("Jwt:ExpiryMinutes") ?? 60;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier,     user.Id),
            new(ClaimTypes.Name,               user.UserName ?? string.Empty),
            new("full_name",                   user.FullName),
            new("studio_id",                   user.StudioId ?? string.Empty),
            new("must_change_password",        user.MustChangePassword ? "true" : "false"),
        };

        foreach (var role in roles) claims.Add(new Claim(ClaimTypes.Role, role));

        var expiresAt = DateTime.UtcNow.AddMinutes(minutes);
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(issuer, audience, claims,
            notBefore: DateTime.UtcNow, expires: expiresAt, signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(jwt), expiresAt);
    }

    [HttpPost("change-password")]
    [Authorize(Policy = "AllowPasswordChange")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var user = await _userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user is null) return Unauthorized();

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new ProblemDetails
            {
                Title = "Password change failed",
                Detail = string.Join("; ", result.Errors.Select(e => e.Description)),
                Status = StatusCodes.Status400BadRequest
            });

        user.MustChangePassword = false;
        user.PasswordChangedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        var roles = await _userManager.GetRolesAsync(user);
        var token = BuildJwt(user, roles);   // fresh token with must_change_password = "false"
        return Ok(new { token.Token, token.ExpiresAt, MustChangePassword = false });
    }
}