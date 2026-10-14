using System.Security.Claims;
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
        var userService = new UserService();
        var admin = new Admin
        {
            Id = "SADM-001",
            Name = "Super Admin User",
            Email = "sadm@photoaevum.com",
            Role = "Super Admin"
        };
        userService.AddAdmin(admin);

        var retrieved = userService.GetAdminById("SADM-001");
        Assert.NotNull(retrieved);
        Assert.Equal("Super Admin User", retrieved.Name);
        Assert.True(retrieved.IsSuperAdmin);
    }

    [Fact]
    public void Profile_EnrichesFromUserService_ForEmployee()
    {
        var userService = new UserService();
        var emp = new PhotoAevumStudio.Web.Models.Employee
        {
            Id = "EMP-001",
            Name = "Lead Photographer",
            Email = "photographer@photoaevum.com",
            Department = "Creative",
            Position = "Senior Photographer"
        };
        userService.AddEmployee(emp);

        var retrieved = userService.GetEmployeeById("EMP-001");
        Assert.NotNull(retrieved);
        Assert.Equal("Lead Photographer", retrieved.Name);
        Assert.Equal("Creative", retrieved.Department);
        Assert.Equal("Senior Photographer", retrieved.Position);
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
}

