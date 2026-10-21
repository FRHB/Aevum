namespace AevumStudio.Dashboard.Components.Pages;

public partial class EmployeeProfile
{
    // Employee Data Model
    public class Employee
    {
        public string EmployeeID { get; set; } = "ADM-001";
        public string EmployeeFirstName { get; set; } = "Axiom";
        public string EmployeeLastName { get; set; } = "Bit";
        public string EmployeeMiddleName { get; set; } = "M.";
        public string EmployeeEmail { get; set; } = "adm-001@photoaevum.studio";
        public string EmployeePassword { get; set; } = "";
        public string EmployeeStatus { get; set; } = "Active";
        public string EmployeeRole { get; set; } = "Administrator";
        public string EmployeeDepartment { get; set; } = "Administration & Studio Management";
        public string EmployeePosition { get; set; } = "Studio Administrator";
        public bool isFirstLogin { get; set; } = true;
    }

    private Employee employeeProfile = new();

    // Navigation & Dropdown State
    private readonly HashSet<string> openDropdowns = new(StringComparer.OrdinalIgnoreCase);

    private bool IsMenuExpanded(string menuKey) => openDropdowns.Contains(menuKey);

    private void ToggleDropdown(string menuKey)
    {
        if (!openDropdowns.Add(menuKey))
        {
            openDropdowns.Remove(menuKey);
        }
    }

    // Interactive Toast Notification
    private string toastMessage = "";
    private bool showToast;

    private async Task ShowToast(string message)
    {
        toastMessage = message;
        showToast = true;
        StateHasChanged();

        await Task.Delay(1800);

        showToast = false;
        await InvokeAsync(StateHasChanged);
    }

    // Card Collapsible State
    private bool isAccountDetailsExpanded = true;
    private bool isPermissionsExpanded = false;
    private bool isSecurityExpanded = true;

    private void ToggleAccountDetails() => isAccountDetailsExpanded = !isAccountDetailsExpanded;
    private void TogglePermissions() => isPermissionsExpanded = !isPermissionsExpanded;
    private void ToggleSecurity() => isSecurityExpanded = !isSecurityExpanded;

    // Profile Edit Mode
    private bool isEditingProfile = false;

    private void ToggleEditMode()
    {
        isEditingProfile = !isEditingProfile;
    }

    private void SaveProfileDetails()
    {
        isEditingProfile = false;
        _ = ShowToast("Profile details updated successfully");
    }

    // Password & Security Fields
    private string currentPassword = "";
    private string newPassword = "";
    private string confirmPassword = "";

    private bool showCurrentPassword = false;
    private bool showNewPassword = false;
    private bool showConfirmPassword = false;

    private string? passwordError;
    private string? passwordSuccess;

    private void ToggleCurrentPasswordVisibility() => showCurrentPassword = !showCurrentPassword;
    private void ToggleNewPasswordVisibility() => showNewPassword = !showNewPassword;
    private void ToggleConfirmPasswordVisibility() => showConfirmPassword = !showConfirmPassword;

    private void SaveProfile()
    {
        passwordError = null;
        passwordSuccess = null;

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            passwordError = "Please enter a new password.";
            return;
        }

        if (newPassword.Length < 8 || !newPassword.Any(char.IsLetter) || !newPassword.Any(char.IsDigit))
        {
            passwordError = "Password must be at least 8 characters long and contain both letters and digits.";
            return;
        }

        if (newPassword != confirmPassword)
        {
            passwordError = "New password and confirmation password do not match.";
            return;
        }

        employeeProfile.EmployeePassword = newPassword;
        employeeProfile.isFirstLogin = false;

        currentPassword = "";
        newPassword = "";
        confirmPassword = "";

        passwordSuccess = "Your account password has been updated successfully.";
        _ = ShowToast("Password updated successfully");
    }

    private string GetInitials()
    {
        var first = employeeProfile.EmployeeFirstName?.Trim().FirstOrDefault();
        var last = employeeProfile.EmployeeLastName?.Trim().FirstOrDefault();

        if (first.HasValue && last.HasValue)
        {
            return $"{char.ToUpper(first.Value)}{char.ToUpper(last.Value)}";
        }
        if (first.HasValue)
        {
            return $"{char.ToUpper(first.Value)}";
        }
        return "AB";
    }

    // Permissions & Module Access Matrix
    private readonly List<ModulePermission> modulePermissions = new()
    {
        new ModulePermission { Name = "Dashboard & Overview", Category = "Management", AccessLevel = "Full Access", BadgeClass = "badge-green" },
        new ModulePermission { Name = "Schedule & Shift Booking", Category = "Management", AccessLevel = "Full Access", BadgeClass = "badge-green" },
        new ModulePermission { Name = "Assigned Shoot Bookings", Category = "Operations", AccessLevel = "Manage & Execute", BadgeClass = "badge-purple" },
        new ModulePermission { Name = "Studio Equipment & Inventory", Category = "Logistics", AccessLevel = "Checkout & Audit", BadgeClass = "badge-blue" },
        new ModulePermission { Name = "Client Directory & Contacts", Category = "CRM", AccessLevel = "View & Contact", BadgeClass = "badge-blue" },
        new ModulePermission { Name = "Studio Administration & Security", Category = "System", AccessLevel = "Administrator", BadgeClass = "badge-purple" }
    };

    private sealed class ModulePermission
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string AccessLevel { get; set; } = "";
        public string BadgeClass { get; set; } = "badge-green";
    }
}
