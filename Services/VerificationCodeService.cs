using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;
using System.Security.Cryptography;

namespace PhotoAevumStudio.Infrastructure.Services;

public class VerificationCodeService : IVerificationCodeService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<VerificationCodeService> _logger;
    private const int MaxFailedAttempts = 5;

    public VerificationCodeService(ApplicationDbContext dbContext, ILogger<VerificationCodeService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<string> GenerateCodeAsync(string email, string purpose, TimeSpan? validity = null, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var validityDuration = validity ?? TimeSpan.FromMinutes(10);
        var now = DateTime.UtcNow;

        // Invalidate any existing active codes for this email and purpose
        var existingCodes = await _dbContext.VerificationCodes
            .Where(c => c.Email == normalizedEmail && c.Purpose == purpose && !c.IsUsed && c.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        foreach (var existing in existingCodes)
        {
            existing.IsUsed = true;
        }

        // Generate cryptographically secure 6-digit numeric code
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        var record = new VerificationCode
        {
            Email = normalizedEmail,
            Code = code,
            Purpose = purpose,
            CreatedAt = now,
            ExpiresAt = now.Add(validityDuration),
            FailedAttempts = 0,
            IsUsed = false
        };

        _dbContext.VerificationCodes.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Generated verification code for {Email} with purpose {Purpose}.", normalizedEmail, purpose);
        return code;
    }

    public async Task<bool> ValidateCodeAsync(string email, string code, string purpose, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;

        var record = await _dbContext.VerificationCodes
            .Where(c => c.Email == normalizedEmail && c.Purpose == purpose && !c.IsUsed && c.ExpiresAt > now)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (record == null)
        {
            _logger.LogWarning("No active verification code found for {Email} ({Purpose}).", normalizedEmail, purpose);
            return false;
        }

        if (record.FailedAttempts >= MaxFailedAttempts)
        {
            record.IsUsed = true; // Burned
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Verification code for {Email} ({Purpose}) has exceeded maximum failed attempts and was burned.", normalizedEmail, purpose);
            return false;
        }

        if (!string.Equals(record.Code, code?.Trim(), StringComparison.Ordinal))
        {
            record.FailedAttempts++;
            if (record.FailedAttempts >= MaxFailedAttempts)
            {
                record.IsUsed = true; // Burned after 5 failed attempts
                _logger.LogWarning("Verification code for {Email} reached {Attempts} failed attempts and has been burned.", normalizedEmail, record.FailedAttempts);
            }
            else
            {
                _logger.LogWarning("Invalid verification code attempt for {Email} ({Attempts}/{MaxAttempts}).", normalizedEmail, record.FailedAttempts, MaxFailedAttempts);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }

        _logger.LogInformation("Verification code for {Email} successfully validated.", normalizedEmail);
        return true;
    }

    public async Task InvalidateCodeAsync(string email, string purpose, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var codes = await _dbContext.VerificationCodes
            .Where(c => c.Email == normalizedEmail && c.Purpose == purpose && !c.IsUsed)
            .ToListAsync(cancellationToken);

        foreach (var c in codes)
        {
            c.IsUsed = true;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
