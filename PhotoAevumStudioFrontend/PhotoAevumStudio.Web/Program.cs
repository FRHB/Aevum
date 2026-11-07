using System.Reflection;
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
builder.Services.AddScoped<PaymentHubClient>();

var apiBaseUrl = builder.Configuration["Api:BaseUrl"]
    ?? builder.Configuration["ApiBaseUrl"]
    ?? "http://api-photoaevum.tryasp.net/";

if (!apiBaseUrl.EndsWith("/"))
{
    apiBaseUrl += "/";
}

HttpMessageHandler CreateBypassSslHandler() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
};

builder.Services.AddHttpClient("AevumApi", client =>
    client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<AuthHttpHandler>()
    .ConfigurePrimaryHttpMessageHandler(CreateBypassSslHandler);

builder.Services.AddHttpClient("AevumApi.Authorized", client =>
    client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<AuthHttpHandler>()
    .ConfigurePrimaryHttpMessageHandler(CreateBypassSslHandler);

builder.Services.AddHttpClient("AevumApi.Anonymous", client =>
    client.BaseAddress = new Uri(apiBaseUrl))
    .ConfigurePrimaryHttpMessageHandler(CreateBypassSslHandler);

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
var informationalVersion = System.Reflection.Assembly.GetExecutingAssembly()
    .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
    .InformationalVersion;
var buildHash = informationalVersion?.Contains('+') == true 
    ? informationalVersion.Split('+')[1] 
    : (informationalVersion ?? "unknown");

app.MapGet("/version", (HttpContext context) =>
{
    context.Response.Headers.Append("X-Build-Hash", buildHash);
    context.Response.Headers.Append("X-Commit-Hash", buildHash);
    return Results.Ok(new
    {
        service = "Photo Aevum Studio Web",
        version = informationalVersion,
        buildHash = buildHash,
        commitHash = buildHash,
        timestamp = DateTime.UtcNow
    });
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
