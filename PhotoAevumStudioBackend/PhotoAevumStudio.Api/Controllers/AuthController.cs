using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;
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
    private readonly ISqlAuthService _sqlAuthService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;
    private readonly IMemoryCache _cache;
    private readonly IEmailService _emailService;

    public AuthController(
        ISqlAuthService sqlAuthService,
        IConfiguration configuration,
        ILogger<AuthController> logger,
        IMemoryCache cache,
        IEmailService emailService)
    {
        _sqlAuthService = sqlAuthService;
        _configuration = configuration;
        _logger = logger;
        _cache = cache;
        _emailService = emailService;
    }

    [HttpPost("send-verification")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> SendVerificationCode([FromBody] SendVerificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email)) return BadRequest("Email is required.");

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        _cache.Set($"VerificationCode_{request.Email}", code, TimeSpan.FromMinutes(10));

        _logger.LogInformation("Verification code for {Email}: {Code}", request.Email, code);

        await _emailService.SendEmailAsync(
            request.Email,
            "Your Photo Aevum Studio Verification Code",
            $"<p>Your verification code is: <strong>{code}</strong></p><p>This code will expire in 10 minutes.</p>",
            isHtml: true);

        return Ok(new { Message = "Verification code sent." });
    }

    [HttpPost("verify-code")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
    public IActionResult VerifyCode([FromBody] VerifyCodeRequest request)
    {
        if (!_cache.TryGetValue($"VerificationCode_{request.Email}", out string? storedCode) || storedCode != request.VerificationCode)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid or expired verification code", Status = 400 });
        }
        return Ok(new { Message = "Verification code is valid." });
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> ForgotPassword([FromBody] SendVerificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email)) return BadRequest("Email is required.");

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        _cache.Set($"ResetCode_{request.Email}", code, TimeSpan.FromMinutes(10));
        _logger.LogInformation("Password reset code for {Email}: {Code}", request.Email, code);

        await _emailService.SendEmailAsync(
            request.Email,
            "Photo Aevum Studio - Password Reset Code",
            $"<p>Your password reset code is: <strong>{code}</strong></p><p>This code will expire in 10 minutes.</p>",
            isHtml: true);

        return Ok(new { Message = "If an account with that email exists, a reset code was sent." });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (!_cache.TryGetValue($"ResetCode_{request.Email}", out string? storedCode) || storedCode != request.VerificationCode)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid or expired reset code", Status = 400 });
        }

        var (success, message) = await _sqlAuthService.ResetPasswordAsync(request.Email, request.NewPassword);
        if (!success)
            return BadRequest(new ProblemDetails
            {
                Title = "Password reset failed",
                Detail = message,
                Status = 400
            });

        _cache.Remove($"ResetCode_{request.Email}");
        return Ok(new { Message = "Password has been successfully reset." });
    }

    [HttpPost("register-customer")]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterCustomer([FromBody] CustomerRegisterRequest request)
    {
        if (!_cache.TryGetValue($"VerificationCode_{request.Email}", out string? storedCode) || storedCode != request.VerificationCode)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid or expired verification code", Status = 400 });
        }

        var (success, message, customerId) = await _sqlAuthService.RegisterCustomerAsync(
            request.FirstName, request.LastName, request.MiddleName, request.Email, request.PhoneNumber, request.Password);

        if (!success)
            return BadRequest(new ProblemDetails { Title = "Registration failed", Detail = message, Status = 400 });

        _cache.Remove($"VerificationCode_{request.Email}");
        return Ok(new { Message = "Customer registered successfully.", CustomerId = customerId });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var authResult = await _sqlAuthService.AuthenticateAsync(request.UserId, request.Password);

        if (!authResult.Success)
        {
            _logger.LogWarning("Failed login attempt for {UserId}: {Error}", request.UserId, authResult.ErrorMessage);
            return InvalidCredentials();
        }

        var user = authResult.User!;

        if (authResult.RequiresPasswordChange || authResult.IsFirstLogin)
        {
            _logger.LogInformation("Account {UserId} requires initial password change. JWT withheld.", user.Id);
            return Ok(new LoginResponse(
                Token: null,
                Role: user.Role,
                FullName: user.FullName,
                ExpiresAt: null,
                MustChangePassword: true,
                RequiresPasswordChange: true,
                UserId: user.Id,
                IsFirstLogin: true));
        }

        var roles = new List<string> { user.Role };
        if (user.Role == AppRoles.SystemAdmin)
        {
            roles.Add(AppRoles.Admin);
        }
        if (user.Role == AppRoles.OfficeClerk)
        {
            roles.Add(AppRoles.Employee);
        }

        var token = BuildJwt(user.Id, user.Email, user.FullName, user.Id, roles);
        await _sqlAuthService.LogActivityAsync("LOGIN", "Session", user.Id, "Successful login session established", user.Role, user.Id);

        _logger.LogInformation("Successful login for {UserId} ({Role})", user.Id, user.Role);

        return Ok(new LoginResponse(
            Token: token.Token,
            Role: user.Role,
            FullName: user.FullName,
            ExpiresAt: token.ExpiresAt,
            MustChangePassword: false,
            RequiresPasswordChange: false,
            UserId: user.Id,
            IsFirstLogin: false));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        string? userId = User.FindFirstValue("studio_id")
                         ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? User.FindFirstValue(ClaimTypes.Name);

        string role = User.FindFirstValue(ClaimTypes.Role) ?? "User";

        if (!string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogInformation("Global logout requested for {UserId} ({Role})", userId, role);
            await _sqlAuthService.LogActivityAsync("LOGOUT", "Session", userId, "Global user sign-out and session revocation", role, userId);
        }

        return Ok(new { Success = true, Message = "Global session successfully terminated." });
    }

    private IActionResult InvalidCredentials() =>
        Unauthorized(new ProblemDetails
        {
            Title = "Authentication failed",
            Detail = "Invalid credentials.",
            Status = StatusCodes.Status401Unauthorized
        });

    [HttpPost("initial-password-change")]
    [AllowAnonymous]
    public async Task<IActionResult> InitialPasswordChange([FromBody] InitialPasswordChangeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest(new ProblemDetails { Title = "Validation Error", Detail = "All fields are required.", Status = 400 });

        if (request.NewPassword.Length < 8)
            return BadRequest(new ProblemDetails { Title = "Validation Error", Detail = "Password must be at least 8 characters long.", Status = 400 });

        if (!string.IsNullOrEmpty(request.ConfirmPassword) && request.NewPassword != request.ConfirmPassword)
            return BadRequest(new ProblemDetails { Title = "Validation Error", Detail = "Passwords do not match.", Status = 400 });

        var (success, message) = await _sqlAuthService.ChangeInitialPasswordAsync(
            request.UserId, request.CurrentPassword, request.NewPassword);

        if (!success)
            return BadRequest(new ProblemDetails
            {
                Title = "Password change failed",
                Detail = message,
                Status = 400
            });

        return Ok(new { Success = true, Message = message });
    }

    [HttpPost("first-login/change-password")]
    [AllowAnonymous]
    public Task<IActionResult> FirstLoginChangePassword([FromBody] InitialPasswordChangeRequest request)
        => InitialPasswordChange(request);

    [HttpPost("register")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.SystemAdmin}")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (request.Role == AppRoles.Admin && !System.Text.RegularExpressions.Regex.IsMatch(request.StudioId ?? "", @"^(SADM|ADM)-\d{3}$"))
        {
            return BadRequest(new ProblemDetails { Title = "Invalid Admin ID format", Detail = "Admin ID must be SADM-### or ADM-###" });
        }
        if ((request.Role == AppRoles.Employee || request.Role == AppRoles.OfficeClerk) && !System.Text.RegularExpressions.Regex.IsMatch(request.StudioId ?? "", @"^(EMP|CLK)-\d{3}$"))
        {
            return BadRequest(new ProblemDetails { Title = "Invalid Staff ID format", Detail = "Staff ID must be EMP-### or CLK-###" });
        }

        if (!AppRoles.All.Contains(request.Role))
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid role",
                Detail = $"Role must be one of: {string.Join(", ", AppRoles.All)}",
                Status = StatusCodes.Status400BadRequest
            });

        string? requesterId = User.FindFirstValue("studio_id")
                              ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                              ?? User.FindFirstValue(ClaimTypes.Name);

        // RESTRICT CREATION OF NEW ADMIN ACCOUNTS EXCLUSIVELY TO SYSTEMADMIN ROLE
        if (request.Role == AppRoles.Admin || request.Role == AppRoles.SystemAdmin)
        {
            bool isSystemAdmin = User.IsInRole(AppRoles.SystemAdmin)
                                 || (!string.IsNullOrEmpty(requesterId) && requesterId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase));

            if (!isSystemAdmin)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
                {
                    Title = "Forbidden",
                    Detail = "Permission Denied: Only a SystemAdmin (SADM-###) is authorized to create new Admin accounts.",
                    Status = StatusCodes.Status403Forbidden
                });
            }

            var (adminSuccess, adminMsg) = await _sqlAuthService.RegisterAdminAsync(
                request.StudioId!,
                request.Role,
                request.FirstName,
                request.LastName,
                request.MiddleName,
                request.Email,
                request.Password,
                requesterId ?? "SADM-001");

            if (!adminSuccess)
                return BadRequest(new ProblemDetails { Title = "Registration failed", Detail = adminMsg, Status = 400 });

            _logger.LogInformation("SystemAdmin {RequesterId} created new Admin account {AdminId}", requesterId, request.StudioId);
            return StatusCode(StatusCodes.Status201Created,
                new { Id = request.StudioId, Email = request.Email, Role = request.Role });
        }

        if (request.Role == AppRoles.Employee || request.Role == AppRoles.OfficeClerk)
        {
            var (success, message) = await _sqlAuthService.RegisterEmployeeAsync(
                request.StudioId!, request.FirstName, request.LastName, request.MiddleName, request.Email, request.Password);

            if (!success)
                return BadRequest(new ProblemDetails { Title = "Registration failed", Detail = message, Status = 400 });

            _logger.LogInformation("Created Employee account {EmployeeId} ({Email})", request.StudioId, request.Email);

            var fullName = string.Join(" ", new[] { request.FirstName, request.MiddleName, request.LastName }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

            try
            {
                await _emailService.SendEmployeeWelcomeEmailAsync(
                    toEmail: request.Email.Trim(),
                    employeeName: string.IsNullOrWhiteSpace(fullName) ? request.Email : fullName,
                    employeeId: request.StudioId!,
                    initialPassword: request.Password,
                    cancellationToken: HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch employee welcome email to {Email}. Employee account was preserved.", request.Email);
            }

            return StatusCode(StatusCodes.Status201Created,
                new { Id = request.StudioId, Email = request.Email, Role = request.Role });
        }

        if (request.Role == AppRoles.Customer)
        {
            var (success, message, customerId) = await _sqlAuthService.RegisterCustomerAsync(
                request.FirstName, request.LastName, request.MiddleName, request.Email, null, request.Password);

            if (!success)
                return BadRequest(new ProblemDetails { Title = "Registration failed", Detail = message, Status = 400 });

            _logger.LogInformation("Created Customer account {CustomerId} ({Email})", customerId, request.Email);
            return StatusCode(StatusCodes.Status201Created,
                new { Id = customerId, Email = request.Email, Role = request.Role });
        }

        return BadRequest(new ProblemDetails { Title = "Unsupported role registration", Status = 400 });
    }

    private (string Token, DateTime ExpiresAt) BuildJwt(string userId, string email, string fullName, string studioId, IList<string> roles)
    {
        var key = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var issuer = _configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
        var audience = _configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is not configured.");
        var minutes = _configuration.GetValue<int?>("Jwt:ExpiryMinutes") ?? 60;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   userId),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier,     userId),
            new(ClaimTypes.Name,               email),
            new("full_name",                   fullName),
            new("studio_id",                   studioId),
            new("must_change_password",        "false"),
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
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        string? userId = User.FindFirstValue("studio_id")
                         ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? User.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var (success, message) = await _sqlAuthService.ChangeInitialPasswordAsync(
            userId, request.CurrentPassword, request.NewPassword);

        if (!success)
            return BadRequest(new ProblemDetails
            {
                Title = "Password change failed",
                Detail = message,
                Status = StatusCodes.Status400BadRequest
            });

        return Ok(new { Success = true, Message = message });
    }
}
