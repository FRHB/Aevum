<<<<<<< HEAD
using FluentValidation;
using PhotoAevumStudio.Application.Auth;

namespace PhotoAevumStudio.Application.Validation;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.Password).MeetsPasswordPolicy();
        RuleFor(x => x.Role).Must(role => AppRoles.All.Contains(role))
            .WithMessage($"Role must be one of: {string.Join(", ", AppRoles.All)}");
    }
}
=======
using FluentValidation;
using PhotoAevumStudio.Application.Auth;

namespace PhotoAevumStudio.Application.Validation;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.Password).MeetsPasswordPolicy();
        RuleFor(x => x.Role).Must(role => AppRoles.All.Contains(role))
            .WithMessage($"Role must be one of: {string.Join(", ", AppRoles.All)}");
    }
}
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
