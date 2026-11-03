using InfisicalConfiguration;
using Microsoft.AspNetCore.Components.Authorization;
using PhotoAevumStudio.Web.Components;
using PhotoAevumStudio.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Fetch Infisical auth keys from appsettings or environment variables
var infisicalClientId = builder.Configuration["Infisical:ClientId"]?.Trim();
var infisicalClientSecret = builder.Configuration["Infisical:ClientSecret"]?.Trim();
var infisicalProjectId = builder.Configuration["Infisical:ProjectId"]?.Trim() ?? "26fb5dcd-e66c-4054-8dab-7cac8519bdee";
var infisicalEnvironment = builder.Configuration["Infisical:Environment"]?.Trim() ?? "dev";

// Only connect to Infisical if the keys are present
if (!string.IsNullOrEmpty(infisicalClientId) && !string.IsNullOrEmpty(infisicalClientSecret))
{
    try
    {
        builder.Configuration.AddInfisical(new InfisicalConfigBuilder()
            .SetProjectId(infisicalProjectId)
            .SetEnvironment(infisicalEnvironment)
            .SetAuth(new InfisicalAuthBuilder()
                .SetUniversalAuth(infisicalClientId, infisicalClientSecret)
                .Build())
            .Build());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Startup Warning] Infisical secret fetch failed in Frontend; falling back to local configuration: {ex.Message}");
    }
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<ITokenStore, ProtectedSessionTokenStore>();
builder.Services.AddScoped<AevumAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AevumAuthenticationStateProvider>());
builder.Services.AddAuthentication();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();

builder.Services.AddTransient<AuthHttpHandler>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IScheduleService, ScheduleService>();
builder.Services.AddScoped<IPortfolioService, PortfolioService>();
builder.Services.AddSingleton<IEquipmentService, EquipmentService>();
builder.Services.AddScoped<IServicesApiClient, ServicesApiClient>();
builder.Services.AddScoped<PhotoAevumStudio.Web.Models.BookScheduleState>();
builder.Services.AddScoped<IBookScheduleClient, BookScheduleClient>();
builder.Services.AddScoped<IConsultationClient, ConsultationClient>();
builder.Services.AddScoped<BookingEvents>();

var apiBaseUrl = builder.Configuration["Api:BaseUrl"]
    ?? builder.Configuration["ApiBaseUrl"]
    ?? (builder.Environment.IsDevelopment() ? "https://localhost:5001/" : "https://api-photoaevum.tryasp.net/");

if (!apiBaseUrl.EndsWith("/"))
{
    apiBaseUrl += "/";
}

builder.Services.AddHttpClient("AevumApi", client =>
    client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<AuthHttpHandler>();

builder.Services.AddHttpClient("AevumApi.Authorized", client =>
    client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<AuthHttpHandler>();

builder.Services.AddHttpClient("AevumApi.Anonymous", client =>
    client.BaseAddress = new Uri(apiBaseUrl));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
