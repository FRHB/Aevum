using FluentValidation.TestHelper;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Validation;
using PhotoAevumStudio.Infrastructure.Auth;
using Xunit;

namespace PhotoAevumStudio.Infrastructure.Tests;

public class ChangePasswordValidatorTests
{
    private readonly ChangePasswordRequestValidator _changePasswordValidator = new();
    private readonly InitialPasswordChangeRequestValidator _initialPasswordValidator = new();
    private readonly ResetPasswordRequestValidator _resetPasswordValidator = new();
    private readonly CustomIdentityErrorDescriber _describer = new();

    // ── ChangePasswordRequestValidator Tests ─────────────────────────────────

    [Theory]
    [InlineData("Valid1Pass@")]
    [InlineData("P@ssw0rd2026")]
    [InlineData("Photo#Aevum99")]
    [InlineData("Secret$888")]
    [InlineData("Percent%99")]
    [InlineData("Ampersand&7")]
    [InlineData("12345678@")]
    public void ChangePassword_Accepts_CompliantPassword(string password)
    {
        var request = new ChangePasswordRequest("CurrentPass123@", password, password);
        var result = _changePasswordValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ChangePassword_Accepts_Exactly30Characters()
    {
        var maxBoundary = new string('a', 28) + "1@";
        Assert.Equal(30, maxBoundary.Length);
        var request = new ChangePasswordRequest("CurrentPass123@", maxBoundary, maxBoundary);
        var result = _changePasswordValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("Short1@")]               // Less than 8 characters
    [InlineData("OnlyLetters@")]          // No numbers
    [InlineData("OnlyLetters123")]        // No special characters
    [InlineData("InvalidBang1!")]         // Special char not in @, #, $, %, &
    public void ChangePassword_Rejects_NonCompliantPassword(string password)
    {
        var request = new ChangePasswordRequest("CurrentPass123@", password, password);
        var result = _changePasswordValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public void ChangePassword_Rejects_Over30Characters()
    {
        var tooLong = new string('a', 29) + "1@";
        Assert.Equal(31, tooLong.Length);
        var request = new ChangePasswordRequest("CurrentPass123@", tooLong, tooLong);
        var result = _changePasswordValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public void ChangePassword_Rejects_WhenSameAsCurrent()
    {
        var request = new ChangePasswordRequest("CurrentP@ss1", "CurrentP@ss1", "CurrentP@ss1");
        var result = _changePasswordValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.NewPassword)
              .WithErrorMessage("New password must be different from the current password.");
    }

    [Fact]
    public void ChangePassword_Rejects_MismatchedConfirmation()
    {
        var request = new ChangePasswordRequest("CurrentPass123!", "NewP@ssword1", "DifferentP@ssword2");
        var result = _changePasswordValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.ConfirmPassword)
              .WithErrorMessage("Passwords do not match.");
    }

    // ── InitialPasswordChangeRequestValidator Tests ──────────────────────────

    [Theory]
    [InlineData("ADM-001", "Adm@n2026Secure")]
    [InlineData("EMP-005", "Emp#Loyee777")]
    [InlineData("ADM-002", "Admin$2026%")]
    public void InitialPasswordChange_Accepts_CompliantPassword(string userId, string password)
    {
        var request = new InitialPasswordChangeRequest(userId, "TempInitialPassword123@", password, password);
        var result = _initialPasswordValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("ADM-001", "P@ss1")]              // Less than 8 chars
    [InlineData("EMP-001", "98765432!")]          // Special char not in @#$%&
    [InlineData("ADM-002", "LettersOnly#")]       // No numbers
    [InlineData("EMP-002", "LettersAndDigits123")] // No special characters
    public void InitialPasswordChange_Rejects_NonCompliantPassword(string userId, string password)
    {
        var request = new InitialPasswordChangeRequest(userId, "TempInitialPassword123@", password, password);
        var result = _initialPasswordValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public void InitialPasswordChange_Rejects_Over30Characters()
    {
        var tooLong = new string('x', 29) + "1#";
        var request = new InitialPasswordChangeRequest("EMP-003", "TempInitialPassword123@", tooLong, tooLong);
        var result = _initialPasswordValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public void InitialPasswordChange_Rejects_EmptyUserId()
    {
        var request = new InitialPasswordChangeRequest("", "OldPass123!", "NewP@ssword1", "NewP@ssword1");
        var result = _initialPasswordValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    // ── CustomIdentityErrorDescriber Tests ────────────────────────────────────

    [Fact]
    public void IdentityErrorDescriber_ReturnsClearComplexityMessages()
    {
        var digitError = _describer.PasswordRequiresDigit();
        Assert.Contains("number (0–9)", digitError.Description);

        var specialError = _describer.PasswordRequiresNonAlphanumeric();
        Assert.Contains("special character", specialError.Description);

        var shortError = _describer.PasswordTooShort(8);
        Assert.Equal("Password must be at least 8 characters long.", shortError.Description);
    }

    // ── ResetPasswordRequestValidator Tests ──────────────────────────────────

    [Fact]
    public void ResetPassword_Accepts_ValidCustomerEmailCodeAndPassword()
    {
        var request = new ResetPasswordRequest("customer@example.com", "123456", "Cust#Pass2026");
        var result = _resetPasswordValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("", "123456", "Cust#Pass2026", nameof(ResetPasswordRequest.Email))]
    [InlineData("not-an-email", "123456", "Cust#Pass2026", nameof(ResetPasswordRequest.Email))]
    [InlineData("customer@example.com", "12345", "Cust#Pass2026", nameof(ResetPasswordRequest.VerificationCode))]
    [InlineData("customer@example.com", "abcdef", "Cust#Pass2026", nameof(ResetPasswordRequest.VerificationCode))]
    [InlineData("customer@example.com", "123456", "Short1@", nameof(ResetPasswordRequest.NewPassword))]
    [InlineData("customer@example.com", "123456", "NoNumber#", nameof(ResetPasswordRequest.NewPassword))]
    [InlineData("customer@example.com", "123456", "NoSpecial123", nameof(ResetPasswordRequest.NewPassword))]
    [InlineData("customer@example.com", "123456", "WrongSpec!1", nameof(ResetPasswordRequest.NewPassword))]
    public void ResetPassword_Rejects_InvalidInputs(string email, string code, string password, string expectedProperty)
    {
        var request = new ResetPasswordRequest(email, code, password);
        var result = _resetPasswordValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(expectedProperty);
    }
}
