using FluentValidation;
using PhotoAevumStudio.Application.DTOs;

namespace PhotoAevumStudio.Application.Validation;

public class CustomerRegisterRequestValidator : AbstractValidator<CustomerRegisterRequest>
{
    public CustomerRegisterRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).MeetsPasswordPolicy();
        RuleFor(x => x.VerificationCode).NotEmpty().Length(6);
    }
}
