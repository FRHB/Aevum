using FluentValidation;
using PhotoAevumStudio.Application.Auth;

namespace PhotoAevumStudio.Application.Validation;

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();

        RuleFor(x => x.NewPassword)
            .MeetsPasswordPolicy()
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("New password must be different from the current password.");

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.NewPassword)
            .WithMessage("Passwords do not match.");
    }
}