using FluentValidation;

namespace PhotoAevumStudio.Application.Validation;

public static class ValidationExtensions
{
    /// <summary>
    /// Enforces the studio password policy:
    /// - Minimum 8 characters
    /// - At least one number (0–9)
    /// - At least one special character (e.g., @, #, $, !, %, etc.)
    /// </summary>
    public static IRuleBuilderOptions<T, string> MeetsPasswordPolicy<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters long.")
            .Matches(@"[0-9]").WithMessage("Password must contain at least one number (0–9).")
            .Matches(@"[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character (e.g., @, #, $, !, %, etc.).");
    }
}
