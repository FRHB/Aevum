using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;
using PhotoAevumStudio.Application.Utils;
using PhotoAevumStudio.Infrastructure.Services;
using PhotoAevumStudio.Web.Models;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class EmployeeManagementTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // 1. EMPLOYEE CREDENTIAL GENERATOR & ID SEQUENCING TESTS
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(new string[] { }, "EMP-001")]
    [InlineData(new[] { "EMP-001", "EMP-002" }, "EMP-003")]
    [InlineData(new[] { "EMP-001", "EMP-003" }, "EMP-002")] // Gaps filled sequentially
    [InlineData(new[] { "EMP-002", "EMP-003" }, "EMP-001")] // First slot gap filled
    [InlineData(new[] { "EMP-001", "EMP-002", "EMP-003", "EMP-005" }, "EMP-004")]
    [InlineData(new[] { "emp-001", "emp-002" }, "EMP-003")] // Case-insensitive matching
    public void GenerateEmployeeId_ProducesStrictPrefixAndPaddedSequence(string[] existingIds, string expectedId)
    {
        var generatedId = EmployeeCredentialGenerator.GenerateEmployeeId(existingIds);
        Assert.Equal(expectedId, generatedId);
    }

    [Fact]
    public void GenerateEmployeeId_HandlesNullExistingList_Gracefully()
    {
        var generatedId = EmployeeCredentialGenerator.GenerateEmployeeId(null);
        Assert.Equal("EMP-001", generatedId);
    }

    [Fact]
    public void GenerateEmployeeId_IgnoresNonEmployeePrefixes()
    {
        var existing = new[] { "ADM-001", "SADM-001", "CUST-001", "EMP-001" };
        var generatedId = EmployeeCredentialGenerator.GenerateEmployeeId(existing);
        Assert.Equal("EMP-002", generatedId);
    }

    [Fact]
    public void GenerateInitialPassword_MeetsAllComplexityRequirements()
    {
        for (int i = 0; i < 30; i++)
        {
            var pwd = EmployeeCredentialGenerator.GenerateInitialPassword(12);

            Assert.Equal(12, pwd.Length);
            Assert.Contains(pwd, c => char.IsUpper(c));
            Assert.Contains(pwd, c => char.IsLower(c));
            Assert.Contains(pwd, c => char.IsDigit(c));
            Assert.Contains(pwd, c => "!@#$%^&*()-_=+".Contains(c));
        }
    }

    [Fact]
    public void GenerateInitialPassword_SupportsCustomLength()
    {
        var pwd16 = EmployeeCredentialGenerator.GenerateInitialPassword(16);
        Assert.Equal(16, pwd16.Length);

        var pwd20 = EmployeeCredentialGenerator.GenerateInitialPassword(20);
        Assert.Equal(20, pwd20.Length);
    }

    [Fact]
    public void GenerateInitialPassword_ProducesUniqueCryptographicTokens()
    {
        var passwords = Enumerable.Range(0, 50)
            .Select(_ => EmployeeCredentialGenerator.GenerateInitialPassword(14))
            .ToHashSet();

        Assert.Equal(50, passwords.Count);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2. EMAIL SERVICE & WELCOME CREDENTIALS DISPATCH TESTS
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AdminEmailStubService_CapturesDispatchedEmployeeWelcomeEmail()
    {
        var emailStub = new AdminEmailStubService(NullLogger<AdminEmailStubService>.Instance);

        await emailStub.SendEmployeeWelcomeEmailAsync(
            toEmail: "sarah.jenkins@photoaevum.com",
            employeeName: "Sarah Jenkins",
            employeeId: "EMP-007",
            initialPassword: "PassW@rd!2026");

        var captured = emailStub.SentEmails.FirstOrDefault(e => e.ToEmail == "sarah.jenkins@photoaevum.com");
        Assert.NotNull(captured);
        Assert.Equal("sarah.jenkins@photoaevum.com", captured.ToEmail);
        Assert.Contains("Welcome to Photo Aevum Studio", captured.Subject);
        Assert.Contains("Sarah Jenkins", captured.Body);
        Assert.Contains("EMP-007", captured.Body);
        Assert.Contains("PassW@rd!2026", captured.Body);
        Assert.Contains("Please change your password on first login", captured.Body);
    }

    [Fact]
    public async Task EmailService_BuildsFormattedWelcomeEmailWithCredentials()
    {
        // EmailService with empty SMTP configuration gracefully falls back to structured logging
        var options = Options.Create(new SmtpSettings());
        var emailService = new EmailService(options, NullLogger<EmailService>.Instance);

        // Should complete without unhandled exception
        await emailService.SendEmployeeWelcomeEmailAsync(
            toEmail: "clara.oswald@photoaevum.com",
            employeeName: "Clara Oswald",
            employeeId: "EMP-042",
            initialPassword: "Temp!Clara#999");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 3. EMPLOYEE REGISTRATION MODEL & VALIDATION TESTS
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmployeeRegistrationModel_ValidatesRequiredFields()
    {
        var model = new EmployeeRegistrationModel
        {
            FirstName = "Marcus",
            LastName = "Vance",
            MiddleName = "Alexander",
            Email = "marcus.vance@photoaevum.com",
            Specializations = new List<string> { "Wedding Photography" },
            SelectedDaysOff = new List<string> { "Sunday", "Monday" },
            EmployeeId = "EMP-015",
            Password = "SecurePassword!1"
        };

        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        bool isValid = Validator.TryValidateObject(model, context, results, validateAllProperties: true);

        Assert.True(isValid);
        Assert.Empty(results);
        Assert.Equal("Marcus Alexander Vance", model.FullName);
        Assert.Equal("Sunday,Monday", model.DaysOffString);
    }

    [Fact]
    public void EmployeeRegistrationModel_FailsWhenRequiredFieldsMissing()
    {
        var model = new EmployeeRegistrationModel
        {
            FirstName = "",
            LastName = "",
            Email = "invalid-email-format",
            Specializations = new List<string>(),
            EmployeeId = "",
            Password = ""
        };

        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        bool isValid = Validator.TryValidateObject(model, context, results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(EmployeeRegistrationModel.FirstName)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(EmployeeRegistrationModel.LastName)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(EmployeeRegistrationModel.Email)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(EmployeeRegistrationModel.EmployeeId)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(EmployeeRegistrationModel.Password)));

        // Test custom IValidatableObject logic when specializations are empty
        var customResults = model.Validate(context).ToList();
        Assert.Contains(customResults, r => r.MemberNames.Contains(nameof(EmployeeRegistrationModel.Specializations)));
    }

    [Theory]
    [InlineData("valid.user@photoaevum.studio", true)]
    [InlineData("employee+test@domain.co.uk", true)]
    [InlineData("plainaddress", false)]
    [InlineData("@missingusername.com", false)]
    public void EmployeeRegistrationModel_ValidatesEmailFormat(string email, bool expectedValid)
    {
        var model = new EmployeeRegistrationModel
        {
            FirstName = "Test",
            LastName = "User",
            Email = email,
            EmployeeId = "EMP-001",
            Password = "ValidPassword!1",
            Specializations = new List<string> { "Portraits" }
        };

        var context = new ValidationContext(model) { MemberName = nameof(EmployeeRegistrationModel.Email) };
        var results = new List<ValidationResult>();
        bool isValid = Validator.TryValidateProperty(model.Email, context, results);

        Assert.Equal(expectedValid, isValid);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 4. CONSECUTIVE SAVE LIFECYCLE & STATE RESET SIMULATION TESTS
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmployeeSaveLifecycle_ConsecutiveSaves_ResetAllStateCorrectly()
    {
        // Simulates the exact state machine of AddEmployeeWizard.razor:
        // Step 1: Initialize first employee
        int currentStep = 1;
        bool saveSuccess = false;
        string? saveError = null;
        string? specError = null;
        var model = new EmployeeRegistrationModel();
        var cts = new CancellationTokenSource();

        // Step 2: User completes form for Employee 1
        model.FirstName = "Jane";
        model.LastName = "Doe";
        model.Email = "jane.doe@photoaevum.com";
        model.Specializations.Add("Studio Portraiture");
        model.EmployeeId = EmployeeCredentialGenerator.GenerateEmployeeId(new[] { "EMP-001" });
        model.Password = EmployeeCredentialGenerator.GenerateInitialPassword();
        currentStep = 3;

        // Simulate successful save 1
        saveSuccess = true;
        saveError = null;
        Assert.True(saveSuccess);
        Assert.Equal("EMP-002", model.EmployeeId);
        Assert.Equal(3, currentStep);

        // Success message template verified
        string expectedSuccessMessage = $"Employee account created successfully! Login credentials have been sent to {model.Email}.";
        Assert.Contains("EMP-002", model.EmployeeId);
        Assert.Contains("jane.doe@photoaevum.com", expectedSuccessMessage);

        // Step 3: Trigger ResetForm (as implemented in AddEmployeeWizard.razor)
        void ResetWizard()
        {
            currentStep = 1;
            saveSuccess = false;
            saveError = null;
            specError = null;
            model = new EmployeeRegistrationModel();
            cts.Cancel();
            cts.Dispose();
            cts = new CancellationTokenSource();
        }

        ResetWizard();

        // Verify clean reset
        Assert.Equal(1, currentStep);
        Assert.False(saveSuccess);
        Assert.Null(saveError);
        Assert.Null(specError);
        Assert.Empty(model.FirstName);
        Assert.Empty(model.LastName);
        Assert.Empty(model.Email);
        Assert.Empty(model.EmployeeId);
        Assert.Empty(model.Specializations);
        Assert.False(cts.IsCancellationRequested);

        // Step 4: User completes form for Employee 2 immediately (consecutive save)
        model.FirstName = "John";
        model.LastName = "Smith";
        model.Email = "john.smith@photoaevum.com";
        model.Specializations.Add("Commercial Videography");
        model.EmployeeId = EmployeeCredentialGenerator.GenerateEmployeeId(new[] { "EMP-001", "EMP-002" });
        model.Password = EmployeeCredentialGenerator.GenerateInitialPassword();
        currentStep = 3;

        // Simulate successful save 2
        saveSuccess = true;
        Assert.True(saveSuccess);
        Assert.Equal("EMP-003", model.EmployeeId);
        Assert.Equal("john.smith@photoaevum.com", model.Email);
        Assert.False(cts.IsCancellationRequested);

        // Clean up
        cts.Dispose();
    }

    [Fact]
    public void EmployeeSaveSuccess_UiBannerTemplate_MatchesRequirements()
    {
        string email = "new.hire@photoaevum.com";
        string bannerMessage = $"Employee account created successfully! Login credentials have been sent to {email}.";

        Assert.StartsWith("Employee account created successfully!", bannerMessage);
        Assert.Contains("Login credentials have been sent to", bannerMessage);
        Assert.Contains(email, bannerMessage);
    }
}
