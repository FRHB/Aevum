using FluentValidation;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Application.Validation;

public class CustomerRegisterRequestValidator : AbstractValidator<CustomerRegisterRequest>
{
    public CustomerRegisterRequestValidator() : this(null) { }

    public CustomerRegisterRequestValidator(ISqlAuthService? sqlAuthService)
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).MeetsPasswordPolicy();
        RuleFor(x => x.VerificationCode).NotEmpty().Length(6);
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .WithMessage("Cellphone number is required.")
            .Matches(@"^09\d{9}$")
            .WithMessage("Cellphone number must be exactly 11 digits starting with 09.");

        if (sqlAuthService != null)
        {
            RuleFor(x => x.Email)
                .Must(email => !sqlAuthService.IsEmailInUseAcrossRoles(email))
                .WithMessage("This email address is already registered under another account role. Please use a different email.");
        }
    }
}
