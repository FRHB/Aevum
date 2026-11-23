using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Validation;
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

    private static readonly TimeSpan VerificationCodeExpiry = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ResetCodeExpiry = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ResetResendCooldown = TimeSpan.FromSeconds(60);
    private const int MaxVerificationAttempts = 5;
    private const string GenericResetCodeSentMessage =
        "If an active customer account is registered with that email address, a 6-digit verification code has been sent.";

    private sealed record CachedHashedCode(byte[] Salt, byte[] Hash);

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
        var normalizedEmail = request?.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedEmail) || !System.Net.Mail.MailAddress.TryCreate(normalizedEmail, out _))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid email address",
                Detail = "Please enter a valid email address.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (await _sqlAuthService.IsEmailInUseAcrossRolesAsync(normalizedEmail))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Registration failed",
                Detail = "This email address is already registered under another account role. Please use a different email.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        StoreHashedCode(
            $"VerificationCode_{normalizedEmail}",
            $"VerificationAttempts_{normalizedEmail}",
            code,
            VerificationCodeExpiry);

        _logger.LogInformation("Verification code generated for {Email}.", normalizedEmail);

        var htmlBody = PhotoAevumStudio.Infrastructure.Services.StudioEmailTemplates.EmailVerificationCode(normalizedEmail, code, 10);
        var sent = await _emailService.TrySendEmailAsync(
            normalizedEmail,
            "Photo Aevum Studio – Your Verification Code",
            htmlBody,
            isHtml: true);

        if (!sent)
        {
            _cache.Remove($"VerificationCode_{normalizedEmail}");
            _cache.Remove($"VerificationAttempts_{normalizedEmail}");
            _logger.LogWarning("Email delivery failed for verification code to {Email}", normalizedEmail);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "Email delivery service unavailable",
                Detail = "Could not deliver the verification email at this time. Please verify email configuration or try again later.",
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }

        return Ok(new { Message = "Verification code sent." });
    }

    [HttpGet("check-email")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> CheckEmail([FromQuery] string email, [FromQuery] string? excludeId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { IsUnique = false, Message = "Email is required." });
        }

        bool inUse = await _sqlAuthService.IsEmailInUseAcrossRolesAsync(email, excludeId, ct);
        if (inUse)
        {
            return Ok(new
            {
                IsUnique = false,
                Message = "This email address is already registered under another account role. Please use a different email."
            });
        }

        return Ok(new { IsUnique = true, Message = (string?)null });
    }

    [HttpPost("verify-code")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
    public IActionResult VerifyCode([FromBody] VerifyCodeRequest request)
    {
        var normalizedEmail = request?.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var code = request?.VerificationCode?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrWhiteSpace(code))
            return BadRequest(new ProblemDetails { Title = "Invalid or expired verification code", Status = 400 });

        if (!VerifyCachedCode($"VerificationCode_{normalizedEmail}", $"VerificationAttempts_{normalizedEmail}", code, invalidateOnMaxAttempts: true, out _))
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
        var normalizedEmail = request?.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedEmail) || !System.Net.Mail.MailAddress.TryCreate(normalizedEmail, out _))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid email address",
                Detail = "Please enter a valid customer email address.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var (isEligible, canonicalEmail, errorMessage, statusCode) =
            await _sqlAuthService.CheckCustomerForgotPasswordEligibilityAsync(normalizedEmail, HttpContext.RequestAborted);

        if (!isEligible && statusCode == StatusCodes.Status403Forbidden)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Password recovery unavailable",
                Detail = errorMessage ?? "Password recovery is only available for customer accounts.",
                Status = StatusCodes.Status403Forbidden
            });
        }

        if (!isEligible && statusCode == StatusCodes.Status400BadRequest)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid email address",
                Detail = errorMessage ?? "Please enter a valid customer email address.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var cooldownKey = BuildResetCooldownKey(normalizedEmail);
        if (_cache.TryGetValue(cooldownKey, out DateTimeOffset cooldownUntil) && cooldownUntil > DateTimeOffset.UtcNow)
        {
            var remainingSeconds = Math.Max(1, (int)Math.Ceiling((cooldownUntil - DateTimeOffset.UtcNow).TotalSeconds));
            return StatusCode(StatusCodes.Status429TooManyRequests, new ProblemDetails
            {
                Title = "Please wait before requesting another code",
                Detail = $"Please wait {remainingSeconds} seconds before requesting a new verification code.",
                Status = StatusCodes.Status429TooManyRequests
            });
        }

        if (!isEligible)
        {
            // Prevent customer email enumeration while enforcing identical per-email resend cooldown.
            _cache.Set(cooldownKey, DateTimeOffset.UtcNow.Add(ResetResendCooldown), ResetResendCooldown);
            _logger.LogInformation("Password reset requested for unregistered customer email {Email}; returning generic response.", normalizedEmail);
            return Ok(new { Message = GenericResetCodeSentMessage });
        }

        var targetEmail = string.IsNullOrWhiteSpace(canonicalEmail) ? normalizedEmail : canonicalEmail.Trim();
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var resetCodeKey = BuildResetCodeKey(normalizedEmail);
        var resetAttemptsKey = BuildResetAttemptsKey(normalizedEmail);

        StoreHashedCode(resetCodeKey, resetAttemptsKey, code, ResetCodeExpiry);
        _logger.LogInformation("Customer password reset code generated for {Email}", normalizedEmail);

        var htmlBody = PhotoAevumStudio.Infrastructure.Services.StudioEmailTemplates.PasswordResetCode(targetEmail, code, (int)ResetCodeExpiry.TotalMinutes);
        var sent = await _emailService.TrySendEmailAsync(
            targetEmail,
            "Photo Aevum Studio – Password Reset Code",
            htmlBody,
            isHtml: true);

        if (!sent)
        {
            _cache.Remove(resetCodeKey);
            _cache.Remove(resetAttemptsKey);
            _logger.LogWarning("Email delivery failed for password reset code to {Email}", normalizedEmail);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "Email delivery service unavailable",
                Detail = "Could not deliver the password reset email at this time. Please try again later.",
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }

        _cache.Set(cooldownKey, DateTimeOffset.UtcNow.Add(ResetResendCooldown), ResetResendCooldown);
        return Ok(new { Message = GenericResetCodeSentMessage });
    }

    [HttpPost("verify-reset-code")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> VerifyResetCode([FromBody] VerifyCodeRequest request)
    {
        var normalizedEmail = request?.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var code = request?.VerificationCode?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation error",
                Detail = "Email and verification code are required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var resetCodeKey = BuildResetCodeKey(normalizedEmail);
        var resetAttemptsKey = BuildResetAttemptsKey(normalizedEmail);

        if (!VerifyCachedCode(resetCodeKey, resetAttemptsKey, code, invalidateOnMaxAttempts: true, out var maxAttemptsExceeded))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid or expired verification code",
                Detail = maxAttemptsExceeded
                    ? "Too many failed verification attempts. Your code has been invalidated; please request a new one."
                    : "That verification code doesn't match or has expired. Please try again or request a new one.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var (isEligible, _, errorMessage, statusCode) =
            await _sqlAuthService.CheckCustomerForgotPasswordEligibilityAsync(normalizedEmail, HttpContext.RequestAborted);

        if (!isEligible)
        {
            _cache.Remove(resetCodeKey);
            _cache.Remove(resetAttemptsKey);
            return StatusCode(statusCode, new ProblemDetails
            {
                Title = "Password recovery unavailable",
                Detail = errorMessage ?? "Forgot Password is only available for customer accounts.",
                Status = statusCode
            });
        }

        return Ok(new { Message = "Verification code is valid." });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var normalizedEmail = request?.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var code = request?.VerificationCode?.Trim() ?? string.Empty;
        var newPassword = request?.NewPassword ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedEmail) || !System.Net.Mail.MailAddress.TryCreate(normalizedEmail, out _))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation error",
                Detail = "Please enter a valid customer email address.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation error",
                Detail = "Verification code is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (PasswordPolicy.Validate(newPassword) is { } pwdError)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation error",
                Detail = pwdError,
                Status = StatusCodes.Status400BadRequest
            });
        }

        var resetCodeKey = BuildResetCodeKey(normalizedEmail);
        var resetAttemptsKey = BuildResetAttemptsKey(normalizedEmail);

        if (!VerifyCachedCode(resetCodeKey, resetAttemptsKey, code, invalidateOnMaxAttempts: true, out var maxAttemptsExceeded))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid or expired reset code",
                Detail = maxAttemptsExceeded
                    ? "Too many failed verification attempts. Your reset code has been invalidated; please request a new one."
                    : "Your verification code is invalid or has expired. Please request a new one.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var (success, message) = await _sqlAuthService.ResetPasswordAsync(normalizedEmail, newPassword, HttpContext.RequestAborted);
        if (!success)
            return BadRequest(new ProblemDetails
            {
                Title = "Password reset failed",
                Detail = message,
                Status = StatusCodes.Status400BadRequest
            });

        _cache.Remove(resetCodeKey);
        _cache.Remove(resetAttemptsKey);
        RevokeUserSessions(normalizedEmail);
        return Ok(new { Message = "Password has been successfully reset." });
    }

    [HttpPost("register-customer")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> RegisterCustomer([FromBody] CustomerRegisterRequest request)
    {
        var normalizedEmail = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var code = request.VerificationCode?.Trim() ?? string.Empty;
        if (!VerifyCachedCode($"VerificationCode_{normalizedEmail}", $"VerificationAttempts_{normalizedEmail}", code, invalidateOnMaxAttempts: true, out _))
        {
            return BadRequest(new ProblemDetails { Title = "Invalid or expired verification code", Status = 400 });
        }

        var (success, message, customerId) = await _sqlAuthService.RegisterCustomerAsync(
            request.FirstName, request.LastName, request.MiddleName, normalizedEmail, request.PhoneNumber, request.Password);

        if (!success)
            return BadRequest(new ProblemDetails { Title = "Registration failed", Detail = message, Status = 400 });

        _cache.Remove($"VerificationCode_{normalizedEmail}");
        _cache.Remove($"VerificationAttempts_{normalizedEmail}");
        return Ok(new { Message = "Customer registered successfully.", CustomerId = customerId });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation error",
                Detail = "Account ID or Email and password are required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var trimmedUserId = request.UserId.Trim();
        var lockoutKey = BuildAuthLockoutKey(trimmedUserId);
        if (IsAuthLockedOut(lockoutKey))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new ProblemDetails
            {
                Title = "Too many failed attempts",
                Detail = "Too many failed sign-in attempts. Please wait 15 minutes before trying again.",
                Status = StatusCodes.Status429TooManyRequests
            });
        }

        var authResult = await _sqlAuthService.AuthenticateAsync(trimmedUserId, request.Password);

        if (!authResult.Success)
        {
            RecordAuthFailure(lockoutKey);
            _logger.LogWarning("Failed login attempt for {UserId}: {Error}", trimmedUserId, authResult.ErrorMessage);
            if (!string.IsNullOrWhiteSpace(authResult.ErrorMessage) &&
                authResult.ErrorMessage.Contains("inactive", StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized(new ProblemDetails
                {
                    Title = "Account inactive",
                    Detail = authResult.ErrorMessage,
                    Status = StatusCodes.Status401Unauthorized
                });
            }
            return InvalidCredentials();
        }

        _cache.Remove(lockoutKey);

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
        _cache.Set($"SessionActivity_{user.Id.ToUpperInvariant()}", DateTime.UtcNow, TimeSpan.FromHours(4));
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
            RevokeUserSessions(userId);
            if (!string.IsNullOrWhiteSpace(User.FindFirstValue(ClaimTypes.Name)))
            {
                RevokeUserSessions(User.FindFirstValue(ClaimTypes.Name)!);
            }
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
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> InitialPasswordChange([FromBody] InitialPasswordChangeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest(new ProblemDetails { Title = "Validation Error", Detail = "All fields are required.", Status = 400 });

        var lockoutKey = BuildAuthLockoutKey($"initial_{request.UserId}");
        if (IsAuthLockedOut(lockoutKey))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new ProblemDetails
            {
                Title = "Too many failed attempts",
                Detail = "Too many failed password attempts. Please wait 15 minutes before trying again.",
                Status = StatusCodes.Status429TooManyRequests
            });
        }

        if (PasswordPolicy.Validate(request.NewPassword) is { } pwdError)
            return BadRequest(new ProblemDetails { Title = "Validation Error", Detail = pwdError, Status = 400 });

        if (!string.IsNullOrEmpty(request.ConfirmPassword) && request.NewPassword != request.ConfirmPassword)
            return BadRequest(new ProblemDetails { Title = "Validation Error", Detail = "Passwords do not match.", Status = 400 });

        var (success, message) = await _sqlAuthService.ChangeInitialPasswordAsync(
            request.UserId, request.CurrentPassword, request.NewPassword);

        if (!success)
        {
            RecordAuthFailure(lockoutKey);
            return BadRequest(new ProblemDetails
            {
                Title = "Password change failed",
                Detail = message,
                Status = 400
            });
        }

        _cache.Remove(lockoutKey);
        RevokeUserSessions(request.UserId);
        return Ok(new { Success = true, Message = message });
    }

    [HttpPost("first-login/change-password")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
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
                requesterId ?? string.Empty);

            if (!adminSuccess)
                return BadRequest(new ProblemDetails { Title = "Registration failed", Detail = adminMsg, Status = 400 });

            _logger.LogInformation("SystemAdmin {RequesterId} created new Admin account {AdminId}", requesterId, request.StudioId);

            var adminFullName = string.Join(" ", new[] { request.FirstName, request.MiddleName, request.LastName }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            var adminDisplayRole = request.Role == AppRoles.SystemAdmin ? "System Admin" : "Admin";

            WelcomeEmailDispatchResult adminDispatch;
            try
            {
                adminDispatch = await _emailService.TrySendAdminWelcomeEmailAsync(
                    toEmail: request.Email.Trim(),
                    adminName: string.IsNullOrWhiteSpace(adminFullName) ? request.Email : adminFullName,
                    adminId: request.StudioId!,
                    temporaryPassword: request.Password,
                    role: adminDisplayRole,
                    cancellationToken: HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch admin welcome email to {Email}. Admin account {AdminId} was preserved.", request.Email, request.StudioId);
                adminDispatch = new WelcomeEmailDispatchResult(
                    EmailSent: false,
                    HandbookAttached: false,
                    WarningMessage: "Administrator account was created, but the welcome email and Employee Handbook PDF could not be delivered. Please retry sending credentials.");
            }

            return StatusCode(StatusCodes.Status201Created, new
            {
                Id = request.StudioId,
                Email = request.Email,
                Role = request.Role,
                WelcomeEmailSent = adminDispatch.EmailSent,
                HandbookAttached = adminDispatch.HandbookAttached,
                EmailWarning = adminDispatch.WarningMessage
            });
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

            var assignedRole = !string.IsNullOrWhiteSpace(request.AssignedRole)
                ? request.AssignedRole.Trim()
                : (request.Specializations is { Count: > 0 }
                    ? string.Join(", ", request.Specializations.Where(s => !string.IsNullOrWhiteSpace(s)))
                    : request.Role);

            WelcomeEmailDispatchResult employeeDispatch;
            try
            {
                employeeDispatch = await _emailService.TrySendEmployeeWelcomeEmailAsync(
                    toEmail: request.Email.Trim(),
                    employeeName: string.IsNullOrWhiteSpace(fullName) ? request.Email : fullName,
                    employeeId: request.StudioId!,
                    initialPassword: request.Password,
                    cancellationToken: HttpContext.RequestAborted,
                    assignedRole: assignedRole);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch employee welcome email to {Email}. Employee account {EmployeeId} was preserved.", request.Email, request.StudioId);
                employeeDispatch = new WelcomeEmailDispatchResult(
                    EmailSent: false,
                    HandbookAttached: false,
                    WarningMessage: "Employee account was created, but the welcome email and Employee Handbook PDF could not be delivered. Please retry sending the welcome email.");
            }

            return StatusCode(StatusCodes.Status201Created, new
            {
                Id = request.StudioId,
                Email = request.Email,
                Role = request.Role,
                WelcomeEmailSent = employeeDispatch.EmailSent,
                HandbookAttached = employeeDispatch.HandbookAttached,
                EmailWarning = employeeDispatch.WarningMessage
            });
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

        var nowOffset = DateTimeOffset.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   userId),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat,   nowOffset.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("iat_ms",                      nowOffset.ToUnixTimeMilliseconds().ToString()),
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
    [EnableRateLimiting("LoginPolicy")]
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

        RevokeUserSessions(userId);
        if (!string.IsNullOrWhiteSpace(User.FindFirstValue(ClaimTypes.Name)))
        {
            RevokeUserSessions(User.FindFirstValue(ClaimTypes.Name)!);
        }

        return Ok(new { Success = true, Message = message });
    }

    private static string BuildResetCodeKey(string normalizedEmail) => $"ResetCode_{normalizedEmail}";
    private static string BuildResetAttemptsKey(string normalizedEmail) => $"ResetAttempts_{normalizedEmail}";
    private static string BuildResetCooldownKey(string normalizedEmail) => $"ResetCooldown_{normalizedEmail}";

    private void StoreHashedCode(string codeCacheKey, string attemptCacheKey, string code, TimeSpan expiry)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = ComputeCodeHash(code, salt);
        _cache.Set(codeCacheKey, new CachedHashedCode(salt, hash), expiry);
        _cache.Remove(attemptCacheKey);
    }

    private static byte[] ComputeCodeHash(string code, byte[] salt)
    {
        var codeBytes = Encoding.UTF8.GetBytes(code.Trim());
        return HMACSHA256.HashData(salt, codeBytes);
    }

    private bool VerifyCachedCode(
        string codeCacheKey,
        string attemptCacheKey,
        string providedCode,
        bool invalidateOnMaxAttempts,
        out bool maxAttemptsExceeded)
    {
        maxAttemptsExceeded = false;

        if (string.IsNullOrWhiteSpace(providedCode) ||
            !_cache.TryGetValue(codeCacheKey, out CachedHashedCode? storedEntry) ||
            storedEntry is null)
        {
            return false;
        }

        var actualHash = ComputeCodeHash(providedCode, storedEntry.Salt);
        bool matched = actualHash.Length == storedEntry.Hash.Length &&
                       CryptographicOperations.FixedTimeEquals(actualHash, storedEntry.Hash);

        if (matched)
        {
            _cache.Remove(attemptCacheKey);
            return true;
        }

        if (invalidateOnMaxAttempts)
        {
            int attempts = (_cache.TryGetValue(attemptCacheKey, out int current) ? current : 0) + 1;
            if (attempts >= MaxVerificationAttempts)
            {
                _cache.Remove(codeCacheKey);
                _cache.Remove(attemptCacheKey);
                maxAttemptsExceeded = true;
            }
            else
            {
                _cache.Set(attemptCacheKey, attempts, ResetCodeExpiry);
            }
        }

        return false;
    }

    private string BuildAuthLockoutKey(string? identifier)
        => $"AuthFailures_{(identifier ?? string.Empty).Trim().ToUpperInvariant()}";

    private bool IsAuthLockedOut(string lockoutKey)
        => _cache.TryGetValue(lockoutKey, out int count) && count >= 5;

    private void RecordAuthFailure(string lockoutKey)
    {
        int attempts = (_cache.TryGetValue(lockoutKey, out int count) ? count : 0) + 1;
        _cache.Set(lockoutKey, attempts, TimeSpan.FromMinutes(15));
    }

    private void RevokeUserSessions(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return;

        var normalized = identifier.Trim().ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;
        _cache.Remove($"SessionActivity_{normalized}");
        _cache.Set($"SessionRevokedBefore_{normalized}", now.ToUnixTimeSeconds(), TimeSpan.FromHours(4));
        _cache.Set($"SessionRevokedBeforeMs_{normalized}", now.ToUnixTimeMilliseconds(), TimeSpan.FromHours(4));
    }
}

