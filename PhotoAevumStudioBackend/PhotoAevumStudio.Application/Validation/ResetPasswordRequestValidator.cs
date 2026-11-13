using FluentValidation;
using PhotoAevumStudio.Application.DTOs;

namespace PhotoAevumStudio.Application.Validation;

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Enter a valid email address.");

        RuleFor(x => x.VerificationCode)
            .NotEmpty().WithMessage("Verification code is required.")
            .Matches(@"^\d{6}$").WithMessage("Verification code must be exactly 6 digits.");

        RuleFor(x => x.NewPassword)
            .MeetsPasswordPolicy();
    }
}
