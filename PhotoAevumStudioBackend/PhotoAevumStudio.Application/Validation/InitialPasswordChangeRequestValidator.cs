using FluentValidation;
using PhotoAevumStudio.Application.Auth;

namespace PhotoAevumStudio.Application.Validation;

public class InitialPasswordChangeRequestValidator : AbstractValidator<InitialPasswordChangeRequest>
{
    public InitialPasswordChangeRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required.");

        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required.");

        RuleFor(x => x.NewPassword)
            .MeetsPasswordPolicy()
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("New password cannot be the same as current password.");

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Confirm password is required.")
            .Equal(x => x.NewPassword)
            .WithMessage("Passwords do not match.");
    }
}
