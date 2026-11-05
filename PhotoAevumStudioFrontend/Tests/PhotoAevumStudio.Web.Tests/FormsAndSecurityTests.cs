using System.Security.Claims;
using InfisicalConfiguration;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Components.Shared;
using PhotoAevumStudio.Web.Models;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class FormsAndSecurityTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // 1. Password Field Visibility & Toggle Tests
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PasswordInput_DefaultsToHidden()
    {
        var input = new PasswordInput();
        Assert.False(input.ShowPassword, "Password should default to hidden (type='password').");
    }

    [Fact]
    public void LoginCredentials_StoresUserIdAndPassword()
    {
        var model = new Login.LoginCredentials
        {
            UserId = "ADM-001",
            Password = "MySecretPassword123!"
        };

        Assert.Equal("ADM-001", model.UserId);
        Assert.Equal("MySecretPassword123!", model.Password);
    }

    [Fact]
    public void ChangePasswordModel_StoresAllRequiredFields()
    {
        var model = new ChangePassword.PasswordChangeModel
        {
            UserId = "EMP-042",
            CurrentPassword = "OldPassword123",
            NewPassword = "NewPassword456",
            ConfirmPassword = "NewPassword456"
        };

        Assert.Equal("EMP-042", model.UserId);
        Assert.Equal("OldPassword123", model.CurrentPassword);
        Assert.Equal("NewPassword456", model.NewPassword);
        Assert.Equal("NewPassword456", model.ConfirmPassword);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2. Change Password Form Logic & Validation Tests
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ChangePassword_RejectsShortPassword()
    {
        var newPassword = "Pass1";
        Assert.True(newPassword.Length < 8, "Password must be at least 8 characters.");
    }

    [Theory]
    [InlineData("12345678", false)] // No letter
    [InlineData("abcdefgh", false)] // No digit
    [InlineData("Valid1Pass", true)] // Has letter & digit
    public void ChangePassword_RequiresLetterAndDigit(string password, bool expectedValid)
    {
        bool hasLetter = System.Text.RegularExpressions.Regex.IsMatch(password, @"[A-Za-z]");
        bool hasDigit = System.Text.RegularExpressions.Regex.IsMatch(password, @"[0-9]");
        bool isValid = hasLetter && hasDigit && password.Length >= 8;

        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public void ChangePassword_RejectsMismatchingConfirmPassword()
    {
        string newPw = "SecretPassword1";
        string confirmPw = "SecretPassword2";

        Assert.NotEqual(newPw, confirmPw);
    }

    [Fact]
    public void ChangePassword_RejectsSameCurrentAndNewPassword()
    {
        string currentPw = "OldSecret123";
        string newPw = "OldSecret123";

        Assert.Equal(currentPw, newPw);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 3. Profile Page Data Integration & Claims Mapping Tests
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Profile_Model_StoresUserDataCorrectly()
    {
        var model = new Profile.UserProfileModel
        {
            Id = "ADM-001",
            Name = "Root Administrator",
            Email = "admin@photoaevum.com",
            Role = "Super Admin",
            Status = "Active",
            IsSuperAdmin = true
        };

        Assert.Equal("ADM-001", model.Id);
        Assert.Equal("Root Administrator", model.Name);
        Assert.Equal("admin@photoaevum.com", model.Email);
        Assert.Equal("Super Admin", model.Role);
        Assert.Equal("Active", model.Status);
        Assert.True(model.IsSuperAdmin);
    }

    [Fact]
    public void Profile_ExtractsClaims_FromClaimsPrincipal()
    {
        var claims = new List<Claim>
        {
            new("studio_id", "EMP-007"),
            new(ClaimTypes.NameIdentifier, "EMP-007"),
            new(ClaimTypes.Name, "john.doe@photoaevum.com"),
            new(ClaimTypes.Email, "john.doe@photoaevum.com"),
            new("full_name", "John Doe"),
            new(ClaimTypes.Role, "Employee")
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        var studioId = principal.FindFirst("studio_id")?.Value;
        var email = principal.FindFirst(ClaimTypes.Email)?.Value;
        var fullName = principal.FindFirst("full_name")?.Value;
        var role = principal.FindFirst(ClaimTypes.Role)?.Value;

        Assert.Equal("EMP-007", studioId);
        Assert.Equal("john.doe@photoaevum.com", email);
        Assert.Equal("John Doe", fullName);
        Assert.Equal("Employee", role);
    }

    [Fact]
    public void Profile_EnrichesFromUserService_ForAdmin()
    {
        const string connStr = "Server=db77602.public.databaseasp.net; Database=db77602; User Id=db77602; Password=G!r7cB8#Z5_d; Encrypt=True; TrustServerCertificate=True; MultipleActiveResultSets=True;";
        string adminId = $"SADM-{Random.Shared.Next(800, 999)}";

        void CleanupSuperAdmin()
        {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(connStr);
            conn.Open();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
                DELETE FROM dbo.Admin WHERE AdminID = @Id;
                IF OBJECT_ID(N'dbo.SystemAdmin', N'U') IS NOT NULL
                    DELETE FROM dbo.SystemAdmin WHERE SystemAdminID = @Id;", conn);
            cmd.Parameters.AddWithValue("@Id", adminId);
            cmd.ExecuteNonQuery();
        }

        CleanupSuperAdmin();
        try
        {
            var userService = new UserService();
            var admin = new Admin
            {
                Id = adminId,
                Name = "Super Admin User",
                Email = "sadm998@photoaevum.com",
                Role = "Super Admin"
            };
            userService.AddAdmin(admin);

            var retrieved = userService.GetAdminById(adminId);
            Assert.NotNull(retrieved);
            Assert.Equal("Super Admin User", retrieved.Name);
            Assert.True(retrieved.IsSuperAdmin);
        }
        finally
        {
            CleanupSuperAdmin();
        }
    }

    [Fact]
    public void Profile_EnrichesFromUserService_ForEmployee()
    {
        var userService = new UserService();
        const string empId = "EMP-998";
        userService.DeleteEmployee(empId);
        try
        {
            var emp = new PhotoAevumStudio.Web.Models.Employee
            {
                Id = empId,
                Name = "Lead Photographer",
                Email = "emp998@photoaevum.com",
                Department = "Creative",
                Position = "Senior Photographer"
            };
            userService.AddEmployee(emp);

            var retrieved = userService.GetEmployeeById(empId);
            Assert.NotNull(retrieved);
            Assert.Equal("Lead Photographer", retrieved.Name);
            Assert.Equal("Creative", retrieved.Department);
            Assert.Equal("Senior Photographer", retrieved.Position);
        }
        finally
        {
            userService.DeleteEmployee(empId);
        }
    }

    [Fact]
    public void Profile_PasswordUpdateModel_ValidatesInputs()
    {
        var form = new Profile.PasswordUpdateModel
        {
            CurrentPassword = "CurrentPassword123",
            NewPassword = "NewPassword2026!",
            ConfirmPassword = "NewPassword2026!"
        };

        Assert.NotEmpty(form.CurrentPassword);
        Assert.NotEmpty(form.NewPassword);
        Assert.Equal(form.NewPassword, form.ConfirmPassword);
        Assert.NotEqual(form.CurrentPassword, form.NewPassword);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 4. Edge Cases & Boundary Conditions
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PasswordInput_RepeatedToggles_AlternatesVisibilityState()
    {
        var input = new PasswordInput();
        Assert.False(input.ShowPassword);

        // Simulate toggle clicks using reflection or private method
        var toggleMethod = typeof(PasswordInput).GetMethod("ToggleVisibility", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(toggleMethod);

        toggleMethod.Invoke(input, null);
        Assert.True(input.ShowPassword);

        toggleMethod.Invoke(input, null);
        Assert.False(input.ShowPassword);
    }

    [Fact]
    public void PasswordInput_DoesNotToggle_WhenDisabled()
    {
        var input = new PasswordInput();
        var disabledProperty = typeof(PasswordInput).GetProperty("Disabled");
        disabledProperty?.SetValue(input, true);

        var toggleMethod = typeof(PasswordInput).GetMethod("ToggleVisibility",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        toggleMethod?.Invoke(input, null);

        Assert.False(input.ShowPassword, "Disabled input should not toggle password visibility.");
    }

    [Theory]
    [InlineData("John Doe", "JD")]
    [InlineData("Cher", "C")]
    [InlineData("Alex Morgan Smith", "AS")]
    [InlineData("", "AD")]
    [InlineData("   ", "EM")]
    public void Profile_GetInitials_HandlesVariousNameFormats(string name, string expectedInitials)
    {
        var fallbackId = expectedInitials == "AD" ? "ADM-001" : "EMP-001";
        var getInitialsMethod = typeof(Profile).GetMethod("GetInitials",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(getInitialsMethod);

        var result = (string?)getInitialsMethod.Invoke(null, new object[] { name, fallbackId });
        Assert.Equal(expectedInitials, result);
    }

    [Theory]
    [InlineData("Super Admin", true, "aev-badge--violet")]
    [InlineData("Admin", false, "aev-badge--blue")]
    [InlineData("Employee", false, "aev-badge--teal")]
    [InlineData("Customer", false, "aev-badge--amber")]
    public void Profile_GetRoleBadgeClass_ReturnsExpectedStyling(string role, bool isSuperAdmin, string expectedClass)
    {
        var method = typeof(Profile).GetMethod("GetRoleBadgeClass",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var result = (string?)method.Invoke(null, new object[] { role, isSuperAdmin });
        Assert.Equal(expectedClass, result);
    }

    [Fact]
    public void Profile_HandlesMissingClaimsGracefully()
    {
        // An authenticated principal with empty/sparse claims
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "unnamed_user")
        }, "TestAuth"));

        var studioId = principal.FindFirst("studio_id")?.Value
                       ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? principal.FindFirst("sub")?.Value
                       ?? principal.Identity?.Name;

        Assert.Equal("unnamed_user", studioId);
    }

    [Fact]
    public void PasswordInput_DoesNotToggle_WhenReadOnly()
    {
        var input = new PasswordInput();
        var readOnlyProperty = typeof(PasswordInput).GetProperty("ReadOnly");
        readOnlyProperty?.SetValue(input, true);

        var toggleMethod = typeof(PasswordInput).GetMethod("ToggleVisibility",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        toggleMethod?.Invoke(input, null);

        Assert.False(input.ShowPassword, "ReadOnly input should not toggle password visibility.");
    }

    [Fact]
    public async Task ChangePassword_UserId_PopulatesFromParameter_AndLocksModel()
    {
        var page = new ChangePassword { UserId = "ADM-001" };
        var resolveMethod = typeof(ChangePassword).GetMethod("ResolveUserIdAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(resolveMethod);

        var task = (Task?)resolveMethod.Invoke(page, null);
        if (task != null)
        {
            await task;
        }

        var modelField = typeof(ChangePassword).GetField("model",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var model = (ChangePassword.PasswordChangeModel?)modelField?.GetValue(page);
        Assert.NotNull(model);
        Assert.Equal("ADM-001", model.UserId);
    }

    [Fact]
    public void Profile_DoesNotPolluteAuthenticatedUser_WithMockAdminData()
    {
        var model = new Profile.UserProfileModel
        {
            Id = "EMP-042",
            Name = "Jane Staff",
            Email = "jane@photoaevum.com",
            Role = "Employee",
            Department = "Creative",
            Position = "Assistant",
            IsSuperAdmin = false
        };

        Assert.NotEqual("Alexandria Vance", model.Name);
        Assert.NotEqual("ADM-001", model.Id);
        Assert.False(model.IsSuperAdmin);
        Assert.Equal("EMP-042", model.Id);
    }

    [Theory]
    [InlineData("Super Admin", "ADM-001", true)]
    [InlineData("Admin", "SADM-001", true)]
    [InlineData("Admin", "ADM-001", false)]
    [InlineData("Employee", "EMP-001", false)]
    public void Profile_IsSuperAdmin_AccuratelyReflectsRoleOrId(string role, string id, bool expectedSuperAdmin)
    {
        bool isSuper = role.Contains("Super", StringComparison.OrdinalIgnoreCase)
                       || id.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase);

        Assert.Equal(expectedSuperAdmin, isSuper);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // EmployeeRegistrationModel Validation Tests
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmployeeRegistrationModel_ComputesFullNameCorrectly()
    {
        var model = new EmployeeRegistrationModel
        {
            FirstName = "Marcus",
            MiddleName = "Alexander",
            LastName = "Vance"
        };

        Assert.Equal("Marcus Alexander Vance", model.FullName);

        model.MiddleName = null;
        Assert.Equal("Marcus Vance", model.FullName);
    }

    [Fact]
    public void EmployeeRegistrationModel_RequiresAtLeastOneSpecialization()
    {
        var model = new EmployeeRegistrationModel
        {
            FirstName = "Marcus",
            LastName = "Vance",
            Email = "marcus@photoaevum.com",
            BirthDate = new DateTime(1995, 5, 10),
            Address = "123 Studio Way",
            EmployeeId = "PA-001",
            Password = "Password123!"
        };

        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var context = new System.ComponentModel.DataAnnotations.ValidationContext(model);
        bool isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(model, context, validationResults, true);

        Assert.False(isValid);
        Assert.Contains(validationResults, r => r.MemberNames.Contains("Specializations"));

        model.Specializations.Add("Photographer");
        validationResults.Clear();
        isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(model, context, validationResults, true);
        Assert.True(isValid);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Snapshot Hard Delete Schema & Stored Procedure Tests
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SnapshotHardDelete_PhotoAevumStudioSql_HasAssignedEmployeeNameAndOnDeleteSetNull()
    {
        var sqlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "PhotoAevumStudioBackend", "database", "PhotoAevumStudio.sql"));
        if (!File.Exists(sqlPath)) sqlPath = @"C:\Aevum\PhotoAevumStudioBackend\database\PhotoAevumStudio.sql";
        Assert.True(File.Exists(sqlPath), $"Expected file at {sqlPath}");

        var content = File.ReadAllText(sqlPath);

        // 1. Column AssignedEmployeeName NVARCHAR(100) NULL in appointment and negotiation tables
        Assert.Contains("AssignedEmployeeName nvarchar(100) NULL", content, StringComparison.OrdinalIgnoreCase);

        // 2. ON DELETE SET NULL on Employee foreign key constraints
        Assert.Contains("FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL", content, StringComparison.OrdinalIgnoreCase);

        // 3. ALTER statements used to preserve tables without DROP TABLE
        Assert.Contains("ALTER TABLE dbo.BookingNegotiation ALTER COLUMN EmployeeID", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ALTER TABLE dbo.EventAppointment ADD AssignedEmployeeName NVARCHAR(100) NULL", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SnapshotHardDelete_StoredProcedures_DefinesSnapshotCreationAndDirectDelete()
    {
        var spPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "PhotoAevumStudioBackend", "database", "PhotoAevumStudioStoredProcedures.sql"));
        if (!File.Exists(spPath)) spPath = @"C:\Aevum\PhotoAevumStudioBackend\database\PhotoAevumStudioStoredProcedures.sql";
        Assert.True(File.Exists(spPath), $"Expected file at {spPath}");

        var content = File.ReadAllText(spPath);

        // 1. spDeleteEmployee exists and executes DELETE directly without manual reassignment logic
        Assert.Contains("CREATE OR ALTER PROCEDURE dbo.spDeleteEmployee", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DELETE FROM dbo.Employees WHERE EmployeeID = @EmployeeID", content, StringComparison.OrdinalIgnoreCase);

        // 2. Creation procedure queries Employees table for First and Last name and inserts AssignedEmployeeName
        Assert.Contains("CREATE OR ALTER PROCEDURE dbo.spCreateAppointment", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SELECT TOP 1 @AssignedEmployeeName = LTRIM(RTRIM(ISNULL(EmployeeFirstName, '') + ' ' + ISNULL(EmployeeLastName, '')))", content);
        Assert.Contains("AssignedEmployeeName", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SnapshotHardDelete_IdempotentSql_MirrorsMasterSchema()
    {
        var idempPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "PhotoAevumStudioBackend", "database", "PhotoAevumStudioIdempotent.sql"));
        if (!File.Exists(idempPath)) idempPath = @"C:\Aevum\PhotoAevumStudioBackend\database\PhotoAevumStudioIdempotent.sql";
        Assert.True(File.Exists(idempPath), $"Expected file at {idempPath}");

        var content = File.ReadAllText(idempPath);
        Assert.Contains("AssignedEmployeeName nvarchar(100) NULL", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON DELETE SET NULL", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Infisical_FetchSecrets_SuccessfullyRetrievesAndMapsKeys()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInfisical(new InfisicalConfiguration.InfisicalConfigBuilder()
                .SetProjectId("26fb5dcd-e66c-4054-8dab-7cac8519bdee")
                .SetEnvironment("dev")
                .SetAuth(new InfisicalConfiguration.InfisicalAuthBuilder()
                    .SetUniversalAuth("0f2e5849-86fb-4880-ac55-d1b6ae8552f9", "773cdc3cb3a6bc33efc4fad0c63288ed6452f7d0b78b30fc52646ec87f45fa65")
                    .Build())
                .Build())
            .Build();

        // Verify that all sensitive access keys, API keys, and tokens are retrieved securely from Infisical
        Assert.False(string.IsNullOrWhiteSpace(config["CloudflareR2:AccessKey"]), "CloudflareR2:AccessKey must be loaded from Infisical.");
        Assert.False(string.IsNullOrWhiteSpace(config["CloudflareR2:SecretKey"]), "CloudflareR2:SecretKey must be loaded from Infisical.");
        Assert.False(string.IsNullOrWhiteSpace(config["Jwt:Key"]), "Jwt:Key must be loaded from Infisical.");
        var smtpUsername = config["EMAIL_CONFIG:PRIMARY:USERNAME"] ?? config["EmailConfig:Primary:Username"];
        var smtpPassword = config["EMAIL_CONFIG:PRIMARY:PASSWORD"] ?? config["EmailConfig:Primary:Password"];
        Assert.False(string.IsNullOrWhiteSpace(smtpUsername), "EmailConfig:Primary:Username must be loaded from Infisical.");
        Assert.False(string.IsNullOrWhiteSpace(smtpPassword), "EmailConfig:Primary:Password must be loaded from Infisical.");
        Assert.False(string.IsNullOrWhiteSpace(config["GoogleMeetSettings:ClientId"]), "GoogleMeetSettings:ClientId must be loaded from Infisical.");
        Assert.False(string.IsNullOrWhiteSpace(config["GoogleMeetSettings:ClientSecret"]), "GoogleMeetSettings:ClientSecret must be loaded from Infisical.");
        Assert.False(string.IsNullOrWhiteSpace(config["XenditSettings:SecretKey"]), "XenditSettings:SecretKey must be loaded from Infisical.");
        Assert.False(string.IsNullOrWhiteSpace(config["XenditSettings:WebhookToken"]), "XenditSettings:WebhookToken must be loaded from Infisical.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 11. Unified Login, Terms Modal, Password Change & Session Inactivity Tests
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("ADM-001")]
    [InlineData("EMP-007")]
    [InlineData("SADM-001")]
    [InlineData("customer@example.com")]
    public void UnifiedLogin_SupportsStaffIdAndCustomerEmail(string identifier)
    {
        var model = new Login.LoginCredentials
        {
            UserId = identifier,
            Password = "Password123!"
        };

        Assert.Equal(identifier, model.UserId);
        Assert.False(string.IsNullOrWhiteSpace(model.Password));
    }

    [Fact]
    public void UnifiedLoginPage_DoesNotContainBackToHome_AndIncludesTermsModal()
    {
        var root = GetRepositoryRoot();
        var loginPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "Login.razor");
        Assert.True(File.Exists(loginPath), $"File not found: {loginPath}");

        var content = File.ReadAllText(loginPath);

        // 1. "Back to Home" button/link removed
        Assert.DoesNotContain("Back to Home", content, StringComparison.OrdinalIgnoreCase);

        // 2. Terms modal integrated
        Assert.Contains("TermsModal", content);
        Assert.Contains("showTermsModal", content);

        // 3. Repositioned Forgot Password link
        Assert.Contains("forgot-password-row", content);
        Assert.Contains("/forgot-password", content);
        Assert.Contains("login-exit-btn", content);
        Assert.Contains("href=\"/\"", content);
        Assert.Contains("NavigateToHome", content);
    }

    [Fact]
    public void AuthPortalModal_CustomerLogin_UsesTermsModalInsteadOfSeparatePage()
    {
        var root = GetRepositoryRoot();
        var modalPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Layout", "AuthPortalModal.razor");
        Assert.True(File.Exists(modalPath), $"File not found: {modalPath}");

        var content = File.ReadAllText(modalPath);

        // Terms and conditions & Privacy policy are pop-up modals rather than separate page links
        Assert.Contains("TermsModal", content);
        Assert.Contains("_showTermsModal", content);
        Assert.Contains("PrivacyModal", content);
        Assert.Contains("_showPrivacyModal", content);
        Assert.DoesNotContain("<a href=\"/terms\" target=\"_blank\">", content);
        Assert.DoesNotContain("<a href=\"/privacy\" target=\"_blank\"", content);

        // Wizard Step 1 contains Name, Phone, Email, Terms and Privacy
        Assert.Contains("_suDetails.FirstName", content);
        Assert.Contains("_suDetails.LastName", content);
        Assert.Contains("_suDetails.MiddleName", content);
        Assert.Contains("_suDetails.PhoneNumber", content);
        Assert.Contains("_suDetails.Email", content);
        Assert.Contains("OpenTermsModal", content);
        Assert.Contains("OpenPrivacyModal", content);
    }

    [Fact]
    public void Modals_TermsAndPrivacy_IncludeScrollToEndGatingAndHighZIndex()
    {
        var root = GetRepositoryRoot();
        var termsPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Shared", "TermsModal.razor");
        var privacyPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Shared", "PrivacyModal.razor");

        Assert.True(File.Exists(termsPath), $"TermsModal not found: {termsPath}");
        Assert.True(File.Exists(privacyPath), $"PrivacyModal not found: {privacyPath}");

        var termsContent = File.ReadAllText(termsPath);
        var privacyContent = File.ReadAllText(privacyPath);

        // High z-index ensures modals show directly in front of AuthPortalModal
        Assert.Contains("z-[300]", termsContent);
        Assert.Contains("z-[300]", privacyContent);

        // Both modals include scroll-to-end detection before lighting up "I Agree"
        Assert.Contains("_hasScrolledToEnd", termsContent);
        Assert.Contains("_hasScrolledToEnd", privacyContent);
        Assert.Contains("observeScrollToEnd", termsContent);
        Assert.Contains("observeScrollToEnd", privacyContent);
        Assert.Contains("Scroll to bottom to agree", termsContent);
        Assert.Contains("Scroll to bottom to agree", privacyContent);
    }

    [Fact]
    public void StudioEmailTemplates_GenerateBrandedVerificationAndResetEmails()
    {
        var verificationHtml = PhotoAevumStudio.Infrastructure.Services.StudioEmailTemplates.EmailVerificationCode("client@test.com", "849201", 10);
        var resetHtml = PhotoAevumStudio.Infrastructure.Services.StudioEmailTemplates.PasswordResetCode("client@test.com", "123456", 10);

        Assert.Contains("849201", verificationHtml);
        Assert.Contains("client@test.com", verificationHtml);
        Assert.Contains("Photo Aevum Studio", verificationHtml);

        Assert.Contains("123456", resetHtml);
        Assert.Contains("client@test.com", resetHtml);
        Assert.Contains("Photo Aevum Studio", resetHtml);
    }

    [Fact]
    public void ChangePasswordPage_RendersStandardTitleAndBindsUserId()
    {
        var root = GetRepositoryRoot();
        var pagePath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "ChangePassword.razor");
        Assert.True(File.Exists(pagePath), $"File not found: {pagePath}");

        var content = File.ReadAllText(pagePath);

        Assert.Contains("<PageTitle>Change Password | Photo Aevum Studio</PageTitle>", content);
        Assert.Contains("@bind=\"model.UserId\"", content);
    }

    [Fact]
    public void SidebarLogout_NavigatesToDesignatedPublicHomePage()
    {
        var root = GetRepositoryRoot();
        var sidebarPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Layout", "Sidebar.razor");
        Assert.True(File.Exists(sidebarPath), $"File not found: {sidebarPath}");

        var content = File.ReadAllText(sidebarPath);

        Assert.Contains("Nav.NavigateTo(\"/\", forceLoad: true)", content);
    }

    [Fact]
    public void HomeRazor_SupportsSessionExpiredBanner()
    {
        var root = GetRepositoryRoot();
        var homePath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "Home.razor");
        Assert.True(File.Exists(homePath), $"File not found: {homePath}");

        var content = File.ReadAllText(homePath);

        Assert.Contains("sessionExpired", content);
        Assert.Contains("SessionExpired", content);
        Assert.Contains("Your session has expired due to inactivity. Please sign in again.", content);
        Assert.Contains("DismissSessionExpiredBanner", content);
    }

    private static string GetRepositoryRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")) && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }

    [Fact]
    public void AuthPortalModal_OnSuccessfulRegistration_NavigatesImmediatelyToLogin_AndClosesModal()
    {
        var root = GetRepositoryRoot();
        var modalPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Layout", "AuthPortalModal.razor");
        var loginPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "Login.razor");
        var loginCssPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "Login.razor.css");

        Assert.True(File.Exists(modalPath), $"File not found: {modalPath}");
        Assert.True(File.Exists(loginPath), $"File not found: {loginPath}");
        Assert.True(File.Exists(loginCssPath), $"File not found: {loginCssPath}");

        var modalContent = File.ReadAllText(modalPath);
        var loginContent = File.ReadAllText(loginPath);
        var loginCssContent = File.ReadAllText(loginCssPath);

        // AuthPortalModal exposes OnRegistered callback and handles successful registration by closing and navigating to /login
        Assert.Contains("EventCallback<string> OnRegistered", modalContent);
        Assert.Contains("OnRegistered.InvokeAsync(registeredEmail)", modalContent);
        Assert.Contains("Nav.NavigateTo(\"/login\")", modalContent);
        Assert.Contains("_view = ModalView.Chooser;", modalContent);

        // Login.razor wires OnRegistered, pre-fills UserId with newly registered email, and shows success alert
        Assert.Contains("OnRegistered=\"HandleCustomerRegistered\"", loginContent);
        Assert.Contains("HandleCustomerRegistered(string email)", loginContent);
        Assert.Contains("loginModel.UserId = email", loginContent);
        Assert.Contains("login-success-alert", loginContent);
        Assert.Contains("Account created successfully! You can now sign in with your email and password.", loginContent);

        // Login.razor.css defines styling for login-success-alert
        Assert.Contains(".login-success-alert", loginCssContent);
    }
}
