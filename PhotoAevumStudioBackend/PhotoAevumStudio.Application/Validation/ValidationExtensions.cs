using FluentValidation;

namespace PhotoAevumStudio.Application.Validation;

public static class ValidationExtensions
{
    /// <summary>
    /// Enforces the studio password policy:
    /// - Minimum 8 characters
    /// - At least one letter
    /// - At least one digit
    /// </summary>
    public static IRuleBuilderOptions<T, string> MeetsPasswordPolicy<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters long.")
            .Matches(@"[A-Za-z]").WithMessage("Password must contain at least one letter.")
            .Matches(@"[0-9]").WithMessage("Password must contain at least one digit.");
    }
}
