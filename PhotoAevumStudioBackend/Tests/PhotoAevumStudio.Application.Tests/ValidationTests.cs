using FluentValidation.TestHelper;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Validation;
using Xunit;

namespace PhotoAevumStudio.Application.Tests;

public class ValidationTests
{
    private readonly RegisterRequestValidator _registerValidator = new();
    private readonly ChangePasswordRequestValidator _changePasswordValidator = new();
    private readonly CustomerRegisterRequestValidator _customerRegisterValidator = new();
    private readonly InitialPasswordChangeRequestValidator _initialPasswordValidator = new();

    [Theory]
    [InlineData("Valid1Pass")]
    [InlineData("Passw0rdSecure!")]
    public void RegisterValidator_Accepts_Strong_Password(string password)
    {
        var model = new RegisterRequest("Jane", "Doe", null, "jane@example.com", password, AppRoles.Employee, "EMP-001");
        var result = _registerValidator.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Password);
    }

    [Theory]
    [InlineData("short1")]
    [InlineData("allletters")]
    [InlineData("12345678")]
    public void RegisterValidator_Rejects_Weak_Password(string password)
    {
        var model = new RegisterRequest("Jane", "Doe", null, "jane@example.com", password, AppRoles.Employee, "EMP-001");
        var result = _registerValidator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void ChangePasswordValidator_Rejects_Identical_Current_And_New_Password()
    {
        var model = new ChangePasswordRequest("OldPass123", "OldPass123", "OldPass123");
        var result = _changePasswordValidator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public void CustomerRegisterValidator_Validates_Correct_Data()
    {
        var model = new CustomerRegisterRequest("Alice", "Wonder", null, "alice@example.com", "Password123", "123456");
        var result = _customerRegisterValidator.TestValidate(model);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void InitialPasswordValidator_Accepts_Valid_Request()
    {
        var model = new InitialPasswordChangeRequest("ADM-001", "AxiomBit@2026", "NewSecurePassword2026!", "NewSecurePassword2026!");
        var result = _initialPasswordValidator.TestValidate(model);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void InitialPasswordValidator_Rejects_Mismatched_Passwords()
    {
        var model = new InitialPasswordChangeRequest("ADM-001", "AxiomBit@2026", "NewSecurePassword2026!", "DifferentPassword123!");
        var result = _initialPasswordValidator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.ConfirmPassword);
    }

    [Fact]
    public void InitialPasswordValidator_Rejects_Identical_Current_And_New()
    {
        var model = new InitialPasswordChangeRequest("ADM-001", "SamePassword123!", "SamePassword123!", "SamePassword123!");
        var result = _initialPasswordValidator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }
}
