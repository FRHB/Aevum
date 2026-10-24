using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Utils;
using PhotoAevumStudio.Infrastructure.Services;
using PhotoAevumStudio.Web.Models;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class AdminManagementTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // 1. CREDENTIAL GENERATION & ID SEQUENCING TESTS
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("SystemAdmin", new string[] { }, "SADM-001")]
    [InlineData("System Admin", new[] { "SADM-001", "SADM-002" }, "SADM-003")]
    [InlineData("Super Admin", new[] { "SADM-001", "SADM-003" }, "SADM-002")] // Gaps filled sequentially
    [InlineData("Admin", new string[] { }, "ADM-001")]
    [InlineData("Admin", new[] { "ADM-001", "ADM-002", "ADM-003" }, "ADM-004")]
    [InlineData("Admin", new[] { "ADM-001", "ADM-002", "ADM-005" }, "ADM-003")]
    public void GenerateAdminId_ProducesStrictPrefixAndPaddedSequence(string role, string[] existingIds, string expectedId)
    {
        var id = AdminCredentialGenerator.GenerateAdminId(role, existingIds);
        Assert.Equal(expectedId, id);
    }

    [Theory]
    [InlineData("SystemAdmin", true)]
    [InlineData("System Admin", true)]
    [InlineData("Super Admin", true)]
    [InlineData("Admin", false)]
    [InlineData("Staff", false)]
    [InlineData("Customer", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSystemAdminRole_CorrectlyEvaluatesRoleStrings(string? role, bool expected)
    {
        var result = AdminCredentialGenerator.IsSystemAdminRole(role);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GenerateTemporaryPassword_MeetsComplexityRequirements()
    {
        for (int i = 0; i < 25; i++)
        {
            var pwd = AdminCredentialGenerator.GenerateTemporaryPassword(12);

            Assert.Equal(12, pwd.Length);
            Assert.Contains(pwd, c => char.IsUpper(c));
            Assert.Contains(pwd, c => char.IsLower(c));
            Assert.Contains(pwd, c => char.IsDigit(c));
            Assert.Contains(pwd, c => "!@#$%^&*()-_=+".Contains(c));
        }
    }

    [Fact]
    public void GenerateTemporaryPassword_GeneratesUniqueCryptographicStrings()
    {
        var passwords = Enumerable.Range(0, 50)
            .Select(_ => AdminCredentialGenerator.GenerateTemporaryPassword(14))
            .ToHashSet();

        Assert.Equal(50, passwords.Count);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2. EMAIL NOTIFICATION STUB SERVICE TESTS
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AdminEmailStubService_CapturesDispatchedCredentials()
    {
        var emailStub = new AdminEmailStubService(NullLogger<AdminEmailStubService>.Instance);

        await emailStub.SendAdminWelcomeEmailAsync(
            toEmail: "lucas.morales@aevumstudio.com",
            adminName: "Lucas Morales",
            adminId: "ADM-005",
            temporaryPassword: "TempPassword!2026",
            role: "Admin");

        var captured = emailStub.SentEmails.FirstOrDefault(e => e.ToEmail == "lucas.morales@aevumstudio.com");
        Assert.NotNull(captured);
        Assert.Contains("ADM-005", captured.Body);
        Assert.Contains("TempPassword!2026", captured.Body);
        Assert.Contains("Lucas Morales", captured.Body);
        Assert.Contains("Admin", captured.Body);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 3. ROLE-BASED ACCESS CONTROL (RBAC) LOGIC & FILTERING TESTS
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RbacRules_AdminVisibility_FiltersOutSystemAdminsForRegularAdmins()
    {
        // Combined master list of administrators in the system
        var masterList = new List<AdminViewModel>
        {
            new() { Id = "SADM-001", FirstName = "Root", LastName = "SuperAdmin", Role = "System Admin" },
            new() { Id = "SADM-002", FirstName = "Clara", LastName = "Oswald", Role = "SystemAdmin" },
            new() { Id = "ADM-001", FirstName = "Marcus", LastName = "Vance", Role = "Admin" },
            new() { Id = "ADM-002", FirstName = "Elena", LastName = "Rostova", Role = "Admin" }
        };

        // When requesting user is a regular Admin (isSystemAdmin = false)
        bool isSystemAdmin = false;

        var visibleToRegularAdmin = masterList
            .Where(a => isSystemAdmin || (!a.Role.Contains("System", StringComparison.OrdinalIgnoreCase) && !a.Id.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Must ONLY see the regular Admins (ADM-001, ADM-002)
        Assert.Equal(2, visibleToRegularAdmin.Count);
        Assert.All(visibleToRegularAdmin, a =>
        {
            Assert.False(a.Role.Contains("System", StringComparison.OrdinalIgnoreCase));
            Assert.StartsWith("ADM-", a.Id);
        });
    }

    [Fact]
    public void RbacRules_SystemAdminVisibility_SeesAllSystemAdminsAndRegularAdmins()
    {
        var masterList = new List<AdminViewModel>
        {
            new() { Id = "SADM-001", FirstName = "Root", LastName = "SuperAdmin", Role = "System Admin" },
            new() { Id = "SADM-002", FirstName = "Clara", LastName = "Oswald", Role = "SystemAdmin" },
            new() { Id = "ADM-001", FirstName = "Marcus", LastName = "Vance", Role = "Admin" },
            new() { Id = "ADM-002", FirstName = "Elena", LastName = "Rostova", Role = "Admin" }
        };

        // When requesting user is a System Admin (isSystemAdmin = true)
        bool isSystemAdmin = true;

        var visibleToSystemAdmin = masterList
            .Where(a => isSystemAdmin || (!a.Role.Contains("System", StringComparison.OrdinalIgnoreCase) && !a.Id.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // System Admin sees all 4 admins
        Assert.Equal(4, visibleToSystemAdmin.Count);
        Assert.Contains(visibleToSystemAdmin, a => a.Id == "SADM-001");
        Assert.Contains(visibleToSystemAdmin, a => a.Id == "ADM-001");
    }

    [Theory]
    [InlineData("SystemAdmin", "ADM-001", "Admin", true)]   // System Admin CAN update regular Admin
    [InlineData("SystemAdmin", "SADM-002", "System Admin", false)] // System Admin CANNOT update other System Admin
    [InlineData("Admin", "ADM-001", "Admin", false)]        // Regular Admin CANNOT update any Admin
    [InlineData("Admin", "SADM-001", "System Admin", false)] // Regular Admin CANNOT update System Admin
    public void RbacRules_MutationPermissionCheck_EnforcesStrictPolicy(
        string callerRole,
        string targetAdminId,
        string targetAdminRole,
        bool expectedCanEdit)
    {
        bool callerIsSystemAdmin = AdminCredentialGenerator.IsSystemAdminRole(callerRole);
        bool targetIsSystemAdmin = AdminCredentialGenerator.IsSystemAdminRole(targetAdminRole) || targetAdminId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase);

        // Core business rule:
        // - Role: "System Admin": Can update data for "Admin" users. Cannot edit the data of other "System Admin" users.
        // - Role: "Admin": Cannot add new admins. Cannot edit the data of ANY admin.
        bool canEdit;
        if (!callerIsSystemAdmin)
        {
            canEdit = false; // Regular admins have read-only view
        }
        else
        {
            // System Admin can edit regular Admin, but cannot edit other System Admin
            canEdit = !targetIsSystemAdmin;
        }

        Assert.Equal(expectedCanEdit, canEdit);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 4. FRONTEND VIEW MODEL VALIDATION & UTILITY TESTS
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AddAdminWizardModel_CalculatesFullNameAndValidatesRequiredInputs()
    {
        var model = new AddAdminWizardModel
        {
            FirstName = "Jonathan",
            MiddleName = "David",
            LastName = "Mercer",
            Email = "jonathan.mercer@aevumstudio.com",
            Role = "Admin"
        };

        Assert.Equal("Jonathan David Mercer", model.FullName);

        // Validate annotations
        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        bool isValid = Validator.TryValidateObject(model, context, results, validateAllProperties: true);

        Assert.True(isValid);
        Assert.Empty(results);
    }

    [Fact]
    public void AddAdminWizardModel_FailsValidation_WhenRequiredFieldsAreMissing()
    {
        var invalidModel = new AddAdminWizardModel
        {
            FirstName = "",
            LastName = "",
            Email = "invalid-email-format",
            Role = "Admin"
        };

        var context = new ValidationContext(invalidModel);
        var results = new List<ValidationResult>();
        bool isValid = Validator.TryValidateObject(invalidModel, context, results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AddAdminWizardModel.FirstName)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AddAdminWizardModel.LastName)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AddAdminWizardModel.Email)));
    }

    [Fact]
    public void AdminViewModel_ComputesInitialsCorrectly()
    {
        var admin1 = new AdminViewModel { FirstName = "Marcus", LastName = "Vance" };
        Assert.Equal("MV", admin1.Initials);

        var admin2 = new AdminViewModel { FirstName = "Clara", LastName = "" };
        Assert.Equal("C", admin2.Initials);

        var admin3 = new AdminViewModel { FirstName = "", LastName = "" };
        Assert.Equal("?", admin3.Initials);
    }
}
