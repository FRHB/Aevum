using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Utils;
using PhotoAevumStudio.Application.Validation;
using PhotoAevumStudio.Infrastructure.Services;
using Xunit;

namespace PhotoAevumStudio.Infrastructure.Tests;

public class TeamLeaderAndPasswordPolicyTests
{
    [Fact]
    public void AdminCredentialGenerator_TemporaryPassword_CompliesWithUpdatedPasswordPolicy()
    {
        for (int i = 0; i < 50; i++)
        {
            var generated = AdminCredentialGenerator.GenerateTemporaryPassword(12);

            Assert.InRange(generated.Length, PasswordPolicy.MinLength, PasswordPolicy.MaxLength);
            Assert.Matches(@"[0-9]", generated);
            Assert.Matches(@"[@#$%&]", generated);
            Assert.Null(PasswordPolicy.Validate(generated));
        }
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("Abc12@", false)]
    [InlineData("123456@", false)]
    [InlineData("1234567@", true)]
    [InlineData("Pass123!", false)]
    [InlineData("Pass123^", false)]
    [InlineData("NoNumber@", false)]
    [InlineData("Pass123@", true)]
    [InlineData("Pass123#", true)]
    [InlineData("Pass123$", true)]
    [InlineData("Pass123%", true)]
    [InlineData("Pass123&", true)]
    public void PasswordPolicy_Validate_EnforcesLengthNumberAndAllowedSpecials(string password, bool expectedValid)
    {
        var error = PasswordPolicy.Validate(password);
        Assert.Equal(expectedValid, error is null);
    }

    [Fact]
    public void PasswordPolicy_Validate_EnforcesExactMaxLengthOf30()
    {
        var exactly30 = new string('a', 28) + "1@";
        var thirtyOne = new string('a', 29) + "1@";

        Assert.Equal(30, exactly30.Length);
        Assert.Equal(31, thirtyOne.Length);
        Assert.Null(PasswordPolicy.Validate(exactly30));
        Assert.NotNull(PasswordPolicy.Validate(thirtyOne));
    }

    [Theory]
    [InlineData("Short1@", false)]
    [InlineData("Pass123!", false)]
    [InlineData("TooLongPassword12345678901234@x", false)]
    [InlineData("ValidPass1#", true)]
    [InlineData("FirstLogin9$", true)]
    public void FluentValidators_And_DataAnnotations_EnforcePasswordPolicyConsistently(string password, bool expectedValid)
    {
        var initialResult = new InitialPasswordChangeRequestValidator().Validate(
            new InitialPasswordChangeRequest("EMP-001", "Temp123@", password, password));
        Assert.Equal(expectedValid, initialResult.IsValid);

        var resetResult = new ResetPasswordRequestValidator().Validate(
            new ResetPasswordRequest("user@example.com", "123456", password));
        Assert.Equal(expectedValid, resetResult.IsValid);

        var registerResult = new RegisterRequestValidator().Validate(
            new RegisterRequest("Juan", "Dela Cruz", null, "juan@example.com", password, AppRoles.Customer));
        Assert.Equal(expectedValid, registerResult.IsValid);

        var custDto = new CustomerRegisterRequest("Maria", "Santos", null, "maria@example.com", password, "123456", "09123456789");
        var validationResults = new List<ValidationResult>();
        var dataAnnotationsValid = Validator.TryValidateObject(custDto, new ValidationContext(custDto), validationResults, validateAllProperties: true);
        Assert.Equal(expectedValid, dataAnnotationsValid);
    }

    [Fact]
    public void StudioEmailTemplates_FinalPaymentRequired_RendersQrCheckoutAndBalanceDetails()
    {
        var html = StudioEmailTemplates.FinalPaymentRequired(
            customerName: "Maria Santos",
            referenceId: "EVT-1001-FINAL",
            appointmentId: "EVT-1001",
            eventName: "Wedding Highlights",
            eventDate: new DateOnly(2026, 12, 15),
            eventTime: new TimeOnly(14, 30),
            finalBalanceAmount: 8500.00m,
            checkoutConfirmUrl: "https://photoaevum.studio/confirm-payment/EVT-1001-FINAL");

        Assert.Contains("Maria Santos", html);
        Assert.Contains("EVT-1001-FINAL", html);
        Assert.Contains("8,500.00", html);
        Assert.Contains("api.qrserver.com/v1/create-qr-code", html);
        Assert.Contains("https://photoaevum.studio/confirm-payment/EVT-1001-FINAL", html);
        Assert.Contains("Pay Final Balance Now", html);
    }

    [Fact]
    public void StudioEmailTemplates_TeamLeaderQrPaymentPaidNotification_RendersVerifiedDetails()
    {
        var html = StudioEmailTemplates.TeamLeaderQrPaymentPaidNotification(
            teamLeaderName: "Carlo Reyes",
            appointmentId: "EVT-1001",
            referenceId: "EVT-1001-FINAL",
            eventName: "Wedding Highlights",
            customerName: "Maria Santos",
            eventDate: new DateOnly(2026, 12, 15),
            amountPaid: 8500.00m,
            paymentChannel: "Xendit QR (GCash/Maya)",
            staffDashboardUrl: "https://photoaevum.studio/staff");

        Assert.Contains("Carlo Reyes", html);
        Assert.Contains("Maria Santos", html);
        Assert.Contains("EVT-1001", html);
        Assert.Contains("EVT-1001-FINAL", html);
        Assert.Contains("8,500.00", html);
        Assert.Contains("Xendit QR (GCash/Maya)", html);
        Assert.Contains("https://photoaevum.studio/staff", html);
    }

    [Fact]
    public void PendingPaymentService_SchemaBatches_IncludesTeamLeaderStagesColumnsAndPaidGuard()
    {
        var batches = typeof(PendingPaymentService)
            .GetField("SchemaBatches", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(null) as string[];
        Assert.NotNull(batches);
        var allSql = string.Join("\n", batches!);

        Assert.Contains("FinalPaymentMethod", allSql);
        Assert.Contains("FinalPaymentStatus", allSql);
        Assert.Contains("FinalQrString", allSql);
        Assert.Contains("FinalPaymentReferenceId", allSql);
        Assert.Contains("FinalPaymentPaidAt", allSql);

        Assert.Contains("'Preparing'", allSql);
        Assert.Contains("'On Going'", allSql);
        Assert.Contains("'Post Event'", allSql);
        Assert.Contains("'Pending Verification'", allSql);

        Assert.Contains("ISNULL(DownpaymentStatus, 'Pending') <> 'Paid'", allSql);
    }
}
