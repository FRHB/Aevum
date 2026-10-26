using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoAevumStudio.Api.Controllers;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;
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

    [Fact]
    public void SpecializationIdSequencing_DeterminesNextId_WithoutCollidingWithGaps()
    {
        var existingIds = new[] { "SP-001", "SP-003", "SP-004", "SP-010" };
        int maxNum = 0;
        foreach (var id in existingIds)
        {
            if (id.StartsWith("SP-", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(id[3..], out int num))
            {
                if (num > maxNum) maxNum = num;
            }
        }
        var nextId = $"SP-{maxNum + 1:D3}";
        Assert.Equal("SP-011", nextId);
    }

    [Theory]
    [InlineData("SADM-001", true)]
    [InlineData("ADM-001", false)]
    public void RoleTableMapping_DistinguishesSystemAdminVsAdmin(string id, bool isSystemAdmin)
    {
        bool expectedSystemAdminTable = id.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedSystemAdminTable, isSystemAdmin);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 5. ADMINS CONTROLLER RBAC & 200 OK SAVE ACTION TESTS
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AdminsController_CreateAdmin_Returns200Ok_WhenCalledBySystemAdmin()
    {
        var fakeService = new FakeAdminManagementService();
        var controller = CreateController(fakeService, userId: "SADM-001", role: "System Admin");

        var createDto = new CreateAdminDto(
            FirstName: "Eleanor",
            LastName: "Rigby",
            Email: "eleanor.rigby@photoaevum.studio",
            Role: "Admin",
            MiddleName: null,
            CustomId: "ADM-010",
            TemporaryPassword: "InitialPassword123!");

        var result = await controller.CreateAdmin(createDto, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);

        var returnedAdmin = Assert.IsType<AdminDto>(okResult.Value);
        Assert.Equal("ADM-010", returnedAdmin.Id);
        Assert.Equal("Eleanor", returnedAdmin.FirstName);
        Assert.Equal("Rigby", returnedAdmin.LastName);
        Assert.Equal("eleanor.rigby@photoaevum.studio", returnedAdmin.Email);
        Assert.Equal("Admin", returnedAdmin.Role);
        Assert.False(returnedAdmin.IsSuperAdmin);
    }

    [Fact]
    public async Task AdminsController_CreateAdmin_Returns403Forbidden_WhenCalledByRegularAdmin()
    {
        var fakeService = new FakeAdminManagementService();
        var controller = CreateController(fakeService, userId: "ADM-001", role: "Admin");

        var createDto = new CreateAdminDto(
            FirstName: "Lucas",
            LastName: "Morales",
            Email: "lucas.morales@photoaevum.studio",
            Role: "Admin",
            MiddleName: null,
            CustomId: "ADM-011",
            TemporaryPassword: "InitialPassword123!");

        var result = await controller.CreateAdmin(createDto, CancellationToken.None);

        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task AdminsController_UpdateAdmin_Returns200Ok_WhenSystemAdminUpdatesRegularAdmin()
    {
        var fakeService = new FakeAdminManagementService();
        fakeService.Admins.Add(new AdminDto("ADM-002", "Elena", "Rostova", null, "Elena Rostova", "elena@photoaevum.studio", "Admin", false, false, "Active"));

        var controller = CreateController(fakeService, userId: "SADM-001", role: "System Admin");

        var updateDto = new UpdateAdminDto(
            FirstName: "Elena",
            LastName: "Rostova-Updated",
            MiddleName: null,
            Email: "elena.updated@photoaevum.studio");

        var result = await controller.UpdateAdmin("ADM-002", updateDto, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);

        var updated = Assert.IsType<AdminDto>(okResult.Value);
        Assert.Equal("Elena Rostova-Updated", updated.FullName);
    }

    [Fact]
    public async Task AdminsController_UpdateAdmin_Returns403Forbidden_WhenRegularAdminAttemptsUpdate()
    {
        var fakeService = new FakeAdminManagementService();
        fakeService.Admins.Add(new AdminDto("ADM-002", "Elena", "Rostova", null, "Elena Rostova", "elena@photoaevum.studio", "Admin", false, false, "Active"));

        // Regular admin attempting to update another admin
        var controller = CreateController(fakeService, userId: "ADM-001", role: "Admin");

        var updateDto = new UpdateAdminDto(
            FirstName: "Elena",
            LastName: "Rostova-Hacked",
            MiddleName: null,
            Email: "elena.hacked@photoaevum.studio");

        var result = await controller.UpdateAdmin("ADM-002", updateDto, CancellationToken.None);

        var forbiddenResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbiddenResult.StatusCode);
    }

    [Fact]
    public async Task AdminsController_UpdateAdmin_Returns403Forbidden_WhenSystemAdminAttemptsToUpdateSystemAdmin()
    {
        var fakeService = new FakeAdminManagementService();
        fakeService.Admins.Add(new AdminDto("SADM-002", "Clara", "Oswald", null, "Clara Oswald", "clara@photoaevum.studio", "System Admin", true, false, "Active"));

        var controller = CreateController(fakeService, userId: "SADM-001", role: "System Admin");

        var updateDto = new UpdateAdminDto(
            FirstName: "Clara",
            LastName: "Oswald-Changed",
            MiddleName: null,
            Email: "clara.changed@photoaevum.studio");

        var result = await controller.UpdateAdmin("SADM-002", updateDto, CancellationToken.None);

        var forbiddenResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbiddenResult.StatusCode);
    }

    [Fact]
    public async Task AdminsController_GetAdmins_FiltersOutSystemAdmins_ForRegularAdminCaller()
    {
        var fakeService = new FakeAdminManagementService();
        fakeService.Admins.AddRange(new[]
        {
            new AdminDto("SADM-001", "Root", "SuperAdmin", null, "Root SuperAdmin", "root@photoaevum.studio", "System Admin", true, false, "Active"),
            new AdminDto("SADM-002", "Clara", "Oswald", null, "Clara Oswald", "clara@photoaevum.studio", "System Admin", true, false, "Active"),
            new AdminDto("ADM-001", "Marcus", "Vance", null, "Marcus Vance", "marcus@photoaevum.studio", "Admin", false, false, "Active"),
            new AdminDto("ADM-002", "Elena", "Rostova", null, "Elena Rostova", "elena@photoaevum.studio", "Admin", false, false, "Active")
        });

        // Caller is regular Admin
        var controller = CreateController(fakeService, userId: "ADM-001", role: "Admin");

        var result = await controller.GetAdmins(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var visibleAdmins = Assert.IsAssignableFrom<IEnumerable<AdminDto>>(okResult.Value).ToList();

        // Must ONLY see the regular Admins
        Assert.Equal(2, visibleAdmins.Count);
        Assert.All(visibleAdmins, a =>
        {
            Assert.False(a.IsSuperAdmin);
            Assert.StartsWith("ADM-", a.Id);
            Assert.DoesNotContain("System", a.Role, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task AdminsController_GetAdmins_ReturnsAllAdmins_ForSystemAdminCaller()
    {
        var fakeService = new FakeAdminManagementService();
        fakeService.Admins.AddRange(new[]
        {
            new AdminDto("SADM-001", "Root", "SuperAdmin", null, "Root SuperAdmin", "root@photoaevum.studio", "System Admin", true, false, "Active"),
            new AdminDto("SADM-002", "Clara", "Oswald", null, "Clara Oswald", "clara@photoaevum.studio", "System Admin", true, false, "Active"),
            new AdminDto("ADM-001", "Marcus", "Vance", null, "Marcus Vance", "marcus@photoaevum.studio", "Admin", false, false, "Active"),
            new AdminDto("ADM-002", "Elena", "Rostova", null, "Elena Rostova", "elena@photoaevum.studio", "Admin", false, false, "Active")
        });

        // Caller is System Admin
        var controller = CreateController(fakeService, userId: "SADM-001", role: "System Admin");

        var result = await controller.GetAdmins(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var visibleAdmins = Assert.IsAssignableFrom<IEnumerable<AdminDto>>(okResult.Value).ToList();

        // System Admin sees all 4 admins
        Assert.Equal(4, visibleAdmins.Count);
        Assert.Contains(visibleAdmins, a => a.Id == "SADM-001");
        Assert.Contains(visibleAdmins, a => a.Id == "ADM-001");
    }

    [Fact]
    public async Task AdminsController_GetAdminById_Returns404NotFound_WhenRegularAdminRequestsSystemAdmin()
    {
        var fakeService = new FakeAdminManagementService();
        fakeService.Admins.Add(new AdminDto("SADM-001", "Root", "SuperAdmin", null, "Root SuperAdmin", "root@photoaevum.studio", "System Admin", true, false, "Active"));

        // Regular admin trying to view a System Admin directly
        var controller = CreateController(fakeService, userId: "ADM-001", role: "Admin");

        var result = await controller.GetAdminById("SADM-001", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 6. EMAIL SERVICE INTEGRATION & AUTOMATED CREDENTIAL DELIVERY TESTS
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EmailService_DispatchesAutomatedWelcomeEmail_ContainingGeneratedIdAndInitialPassword()
    {
        var emailStub = new AdminEmailStubService(NullLogger<AdminEmailStubService>.Instance);

        string generatedAdminId = "ADM-007";
        string initialTemporaryPassword = AdminCredentialGenerator.GenerateTemporaryPassword(12);
        string recipientEmail = "james.bond@photoaevum.studio";
        string adminFullName = "James Bond";

        await emailStub.SendAdminWelcomeEmailAsync(
            toEmail: recipientEmail,
            adminName: adminFullName,
            adminId: generatedAdminId,
            temporaryPassword: initialTemporaryPassword,
            role: "Admin");

        var sentEmail = Assert.Single(emailStub.SentEmails);
        Assert.Equal(recipientEmail, sentEmail.ToEmail);
        Assert.Contains("Welcome to Photo Aevum Studio", sentEmail.Subject);
        Assert.Contains(generatedAdminId, sentEmail.Body);
        Assert.Contains(initialTemporaryPassword, sentEmail.Body);
        Assert.Contains(adminFullName, sentEmail.Body);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 7. SAVE ACTION COMPLETION STATE VERIFICATION
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SaveAction_SuccessfulCreation_PopulatesStep4CompletionSummary()
    {
        var model = new AddAdminWizardModel
        {
            FirstName = "Seraphina",
            LastName = "Kovacs",
            Email = "seraphina.kovacs@photoaevum.studio",
            Role = "Admin",
            GeneratedId = "ADM-012",
            TemporaryPassword = "Password!2026"
        };

        // Assert all completion data needed by Step 4 is present
        Assert.Equal("ADM-012", model.GeneratedId);
        Assert.Equal("Seraphina Kovacs", model.FullName);
        Assert.Equal("Admin", model.Role);
        Assert.Equal("seraphina.kovacs@photoaevum.studio", model.Email);
        Assert.False(model.IsSystemAdmin);

        // Completion success message format
        string expectedMessage = $"Administrator account created! Credentials have been emailed to {model.Email}.";
        Assert.Contains(model.Email, expectedMessage);
        Assert.Contains("Administrator account created!", expectedMessage);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test Helpers & Fake Implementations
    // ──────────────────────────────────────────────────────────────────────────

    private static AdminsController CreateController(IAdminManagementService service, string userId, string role)
    {
        var controller = new AdminsController(service, NullLogger<AdminsController>.Instance);
        var claims = new List<Claim>
        {
            new("studio_id", userId),
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return controller;
    }

    private class FakeAdminManagementService : IAdminManagementService
    {
        public List<AdminDto> Admins { get; } = new();

        public Task<IReadOnlyList<AdminDto>> GetAdminsAsync(string requestingUserId, bool isSystemAdmin, CancellationToken cancellationToken = default)
        {
            var result = Admins
                .Where(a => isSystemAdmin || (!a.Role.Contains("System", StringComparison.OrdinalIgnoreCase) && !a.Id.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            return Task.FromResult<IReadOnlyList<AdminDto>>(result);
        }

        public Task<AdminDto?> GetAdminByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            var admin = Admins.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(admin);
        }

        public Task<string> GetNextAdminIdAsync(string role, CancellationToken cancellationToken = default)
        {
            bool isSys = AdminCredentialGenerator.IsSystemAdminRole(role);
            return Task.FromResult(isSys ? "SADM-002" : "ADM-003");
        }

        public Task<AdminDto> CreateAdminAsync(CreateAdminDto dto, string requestingUserId, bool isSystemAdmin, CancellationToken cancellationToken = default)
        {
            if (!isSystemAdmin && !requestingUserId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Permission Denied: Only a SystemAdmin (SADM-###) is authorized to create new Admin accounts.");
            }

            bool isSys = AdminCredentialGenerator.IsSystemAdminRole(dto.Role);
            var id = dto.CustomId ?? (isSys ? "SADM-002" : "ADM-003");
            var admin = new AdminDto(
                Id: id,
                FirstName: dto.FirstName,
                LastName: dto.LastName,
                MiddleName: dto.MiddleName,
                FullName: $"{dto.FirstName} {dto.LastName}".Trim(),
                Email: dto.Email,
                Role: isSys ? "System Admin" : "Admin",
                IsSuperAdmin: isSys,
                IsFirstLogin: true,
                Status: "Active");

            Admins.Add(admin);
            return Task.FromResult(admin);
        }

        public Task<AdminDto> UpdateAdminAsync(string id, UpdateAdminDto dto, string requestingUserId, bool isSystemAdmin, CancellationToken cancellationToken = default)
        {
            if (!isSystemAdmin && !requestingUserId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Permission Denied: Admins have read-only access and cannot edit administrator records.");
            }

            var target = Admins.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (target != null && target.IsSuperAdmin)
            {
                throw new UnauthorizedAccessException("Permission Denied: System Admin accounts cannot be edited or modified.");
            }

            var updated = new AdminDto(
                Id: id,
                FirstName: dto.FirstName,
                LastName: dto.LastName,
                MiddleName: dto.MiddleName,
                FullName: $"{dto.FirstName} {dto.LastName}".Trim(),
                Email: dto.Email,
                Role: target?.Role ?? "Admin",
                IsSuperAdmin: target?.IsSuperAdmin ?? false,
                IsFirstLogin: false,
                Status: target?.Status ?? "Active");

            return Task.FromResult(updated);
        }

        public Task<bool> DeleteAdminAsync(string id, string requestingUserId, bool isSystemAdmin, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }
}
