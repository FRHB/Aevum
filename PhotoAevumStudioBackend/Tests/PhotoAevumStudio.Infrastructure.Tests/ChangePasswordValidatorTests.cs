using FluentValidation.TestHelper;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Validation;
using PhotoAevumStudio.Infrastructure.Auth;
using Xunit;

namespace PhotoAevumStudio.Infrastructure.Tests;

public class ChangePasswordValidatorTests
{
    private readonly ChangePasswordRequestValidator _changePasswordValidator = new();
    private readonly InitialPasswordChangeRequestValidator _initialPasswordValidator = new();
    private readonly CustomIdentityErrorDescriber _describer = new();

    // ── ChangePasswordRequestValidator Tests ─────────────────────────────────

    [Theory]
    [InlineData("Valid1Pass!")]
    [InlineData("P@ssw0rd2026")]
    [InlineData("Photo#Aevum99")]
    [InlineData("Secret$888")]
    public void ChangePassword_Accepts_CompliantPassword(string password)
    {
        var request = new ChangePasswordRequest("CurrentPass123!", password, password);
        var result = _changePasswordValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("Short1!")]               // Less than 8 characters
    [InlineData("12345678!")]             // No letters
    [InlineData("OnlyLetters!")]          // No numbers
    [InlineData("OnlyLetters123")]        // No special characters
    public void ChangePassword_Rejects_NonCompliantPassword(string password)
    {
        var request = new ChangePasswordRequest("CurrentPass123!", password, password);
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
    [InlineData("ADM-001", "Adm!n2026Secure")]
    [InlineData("EMP-005", "Emp#Loyee777")]
    public void InitialPasswordChange_Accepts_CompliantPassword(string userId, string password)
    {
        var request = new InitialPasswordChangeRequest(userId, "TempInitialPassword123!", password, password);
        var result = _initialPasswordValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("ADM-001", "P@ss1")]              // Less than 8 chars
    [InlineData("EMP-001", "98765432!#")]          // No letters
    [InlineData("ADM-002", "LettersOnly!")]        // No numbers
    [InlineData("EMP-002", "LettersAndDigits123")] // No special characters
    public void InitialPasswordChange_Rejects_NonCompliantPassword(string userId, string password)
    {
        var request = new InitialPasswordChangeRequest(userId, "TempInitialPassword123!", password, password);
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
}
