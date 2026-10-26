using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public class AdminService : IAdminService
{
    private readonly HttpClient _http;
    private readonly ITokenStore _tokenStore;
    private readonly IUserService _userService;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly ILogger<AdminService> _logger;

    private static readonly Regex SadmRegex = new(@"^SADM-(\d{3})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AdmRegex  = new(@"^ADM-(\d{3})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public AdminService(
        IHttpClientFactory httpClientFactory,
        ITokenStore tokenStore,
        IUserService userService,
        AuthenticationStateProvider authStateProvider,
        ILogger<AdminService> logger)
    {
        _http = httpClientFactory.CreateClient("AevumApi.Authorized");
        _tokenStore = tokenStore;
        _userService = userService;
        _authStateProvider = authStateProvider;
        _logger = logger;
    }

    private async Task<(string UserId, bool IsSystemAdmin)> GetCurrentUserInfoAsync()
    {
        try
        {
            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;
            var userId = user.FindFirstValue("studio_id")
                         ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? user.FindFirstValue(ClaimTypes.Name)
                         ?? string.Empty;

            bool isSystemAdmin = user.IsInRole("SystemAdmin")
                                 || user.IsInRole("System Admin")
                                 || user.IsInRole("Super Admin")
                                 || userId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase);

            return (userId, isSystemAdmin);
        }
        catch
        {
            return (string.Empty, false);
        }
    }

    public async Task<IReadOnlyList<AdminViewModel>> GetAdminsAsync(CancellationToken cancellationToken = default)
    {
        await _tokenStore.GetTokenAsync(cancellationToken);

        try
        {
            var response = await _http.GetAsync("api/management/admins", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var apiAdmins = await response.Content.ReadFromJsonAsync<List<AdminViewModel>>(cancellationToken: cancellationToken);
                if (apiAdmins is not null)
                    return apiAdmins;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "API call to get admins failed. Falling back to direct database service.");
        }

        // Direct database service fallback with strict role visibility logic
        var (_, isSystemAdmin) = await GetCurrentUserInfoAsync();
        var dbAdmins = _userService.GetAdmins();

        var result = new List<AdminViewModel>();
        foreach (var admin in dbAdmins)
        {
            var isSuper = admin.IsSuperAdmin
                          || admin.Role.Contains("System", StringComparison.OrdinalIgnoreCase)
                          || admin.Id.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase);

            // Strict Visibility: Admins cannot see System Admins
            if (!isSystemAdmin && isSuper)
            {
                continue;
            }

            var (first, middle, last) = SplitFullName(admin.Name);

            result.Add(new AdminViewModel
            {
                Id = admin.Id,
                FirstName = first,
                LastName = last,
                MiddleName = middle,
                Email = admin.Email,
                Role = isSuper ? "System Admin" : "Admin",
                IsSuperAdmin = isSuper,
                Status = "Active"
            });
        }

        return result;
    }

    public async Task<string> GetNextAdminIdAsync(string role, CancellationToken cancellationToken = default)
    {
        await _tokenStore.GetTokenAsync(cancellationToken);

        try
        {
            var res = await _http.GetFromJsonAsync<NextAdminIdApiResult>(
                $"api/management/admins/next-id?role={Uri.EscapeDataString(role)}", cancellationToken);
            if (!string.IsNullOrWhiteSpace(res?.NextId))
                return res.NextId;
        }
        catch
        {
            // Fall back to local calculation
        }

        bool isSysAdmin = role.Equals("SystemAdmin", StringComparison.OrdinalIgnoreCase)
                       || role.Equals("System Admin", StringComparison.OrdinalIgnoreCase)
                       || role.Equals("Super Admin", StringComparison.OrdinalIgnoreCase);

        string prefix = isSysAdmin ? "SADM" : "ADM";
        var regex = isSysAdmin ? SadmRegex : AdmRegex;

        var existingAdmins = _userService.GetAdmins();
        var numbers = new HashSet<int>();
        foreach (var a in existingAdmins)
        {
            var m = regex.Match(a.Id.Trim());
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n))
            {
                numbers.Add(n);
            }
        }

        int candidate = 1;
        while (numbers.Contains(candidate)) candidate++;
        return $"{prefix}-{candidate:D3}";
    }

    public async Task<SaveAdminResult> CreateAdminAsync(AddAdminWizardModel model, CancellationToken cancellationToken = default)
    {
        await _tokenStore.GetTokenAsync(cancellationToken);

        var payload = new
        {
            model.FirstName,
            model.LastName,
            model.MiddleName,
            model.Email,
            Role = model.IsSystemAdmin ? "SystemAdmin" : "Admin",
            CustomId = model.GeneratedId,
            TemporaryPassword = model.TemporaryPassword
        };

        try
        {
            var response = await _http.PostAsJsonAsync("api/management/admins", payload, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var created = await response.Content.ReadFromJsonAsync<AdminViewModel>(cancellationToken: cancellationToken);
                return new SaveAdminResult(true, null, created);
            }

            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: cancellationToken);
            return new SaveAdminResult(false, problem?.Detail ?? $"Failed to create administrator (HTTP {response.StatusCode}).");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "API call to create admin failed. Falling back to direct database service.");
            try
            {
                var (currentUserId, isSystemAdmin) = await GetCurrentUserInfoAsync();
                if (!isSystemAdmin)
                {
                    return new SaveAdminResult(false, "Permission Denied: Only a SystemAdmin (SADM-###) is authorized to create new Admin accounts.");
                }

                var admin = new Admin
                {
                    Id = model.GeneratedId,
                    Name = model.FullName,
                    Email = model.Email,
                    Role = model.IsSystemAdmin ? "System Admin" : "Admin"
                };
                _userService.AddAdmin(admin, currentUserId);

                var createdVm = new AdminViewModel
                {
                    Id = admin.Id,
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    MiddleName = model.MiddleName,
                    Email = admin.Email,
                    Role = admin.Role,
                    IsSuperAdmin = model.IsSystemAdmin,
                    Status = "Active"
                };
                return new SaveAdminResult(true, null, createdVm);
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "Database fallback to create admin failed.");
                return new SaveAdminResult(false, ex.Message);
            }
        }
    }

    public async Task<SaveAdminResult> UpdateAdminAsync(string id, EditAdminModel model, CancellationToken cancellationToken = default)
    {
        await _tokenStore.GetTokenAsync(cancellationToken);

        var (_, isCallerSystemAdmin) = await GetCurrentUserInfoAsync();
        if (!isCallerSystemAdmin)
        {
            return new SaveAdminResult(false, "Permission Denied: Admins have read-only access and cannot edit administrator records.");
        }

        var payload = new
        {
            model.FirstName,
            model.LastName,
            model.MiddleName,
            model.Email
        };

        try
        {
            var response = await _http.PutAsJsonAsync($"api/management/admins/{id}", payload, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var updated = await response.Content.ReadFromJsonAsync<AdminViewModel>(cancellationToken: cancellationToken);
                return new SaveAdminResult(true, null, updated);
            }

            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: cancellationToken);
            return new SaveAdminResult(false, problem?.Detail ?? $"Update failed (HTTP {response.StatusCode}).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API call to update admin failed.");
            return new SaveAdminResult(false, ex.Message);
        }
    }

    public async Task<bool> DeleteAdminAsync(string id, CancellationToken cancellationToken = default)
    {
        await _tokenStore.GetTokenAsync(cancellationToken);

        try
        {
            var response = await _http.DeleteAsync($"api/management/admins/{id}", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API call to delete admin failed.");
            return false;
        }
    }

    private static (string First, string? Middle, string Last) SplitFullName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return ("Admin", null, "User");

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return (parts[0], null, string.Empty);
        if (parts.Length == 2) return (parts[0], null, parts[1]);
        return (parts[0], string.Join(" ", parts.Skip(1).Take(parts.Length - 2)), parts[^1]);
    }

    private sealed record NextAdminIdApiResult(string NextId, string Role);
}
