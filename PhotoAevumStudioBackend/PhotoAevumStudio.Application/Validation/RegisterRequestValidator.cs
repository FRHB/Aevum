using FluentValidation;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Application.Validation;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator() : this(null) { }

    public RegisterRequestValidator(ISqlAuthService? sqlAuthService)
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).MeetsPasswordPolicy();
        RuleFor(x => x.Role).Must(role => AppRoles.All.Contains(role))
            .WithMessage($"Role must be one of: {string.Join(", ", AppRoles.All)}");

        if (sqlAuthService != null)
        {
            RuleFor(x => x.Email)
                .Must((req, email) => !sqlAuthService.IsEmailInUseAcrossRoles(email, req.StudioId))
                .WithMessage("This email address is already registered under another account role. Please use a different email.");
        }
    }
}
