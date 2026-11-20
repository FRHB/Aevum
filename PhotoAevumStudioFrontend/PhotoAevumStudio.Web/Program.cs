using InfisicalConfiguration;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
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

        var normalizedSecrets = builder.Configuration.AsEnumerable()
            .Where(kvp => kvp.Key.Contains("__", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(kvp.Value))
            .ToDictionary(
                kvp => kvp.Key.Replace("__", ":", StringComparison.Ordinal),
                kvp => kvp.Value,
                StringComparer.OrdinalIgnoreCase);

        if (normalizedSecrets.Count > 0)
        {
            builder.Configuration.AddInMemoryCollection(normalizedSecrets);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Startup Warning] Infisical secret fetch failed in Frontend; falling back to local configuration: {ex.Message}");
    }
}

var bootInfo = new AppBootInfo();
builder.Services.AddSingleton(bootInfo);

var dataProtectionKeysDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
var dataProtectionBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("PhotoAevumStudio.Web");
try
{
    Directory.CreateDirectory(dataProtectionKeysDir);
    dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDir));
}
catch
{
    // Fall back to default key ring if content root is read-only.
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<ITokenStore, ProtectedSessionTokenStore>();
builder.Services.AddScoped<CircuitHandler, TokenCircuitHandler>();
builder.Services.AddScoped<AevumAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AevumAuthenticationStateProvider>());
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/login";
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();

builder.Services.AddTransient<AuthHttpHandler>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<ITimesheetService, TimesheetService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IScheduleService, ScheduleService>();
builder.Services.AddScoped<IPortfolioService, PortfolioService>();
builder.Services.AddSingleton<IEquipmentService, EquipmentService>();
builder.Services.AddScoped<IServicesApiClient, ServicesApiClient>();
builder.Services.AddScoped<PhotoAevumStudio.Web.Models.BookScheduleState>();
builder.Services.AddScoped<IBookScheduleClient, BookScheduleClient>();
builder.Services.AddScoped<IConsultationClient, ConsultationClient>();
builder.Services.AddScoped<ICustomerDropboxClient, CustomerDropboxClient>();
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
app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
    {
        context.Response.OnStarting(() =>
        {
            var contentType = context.Response.ContentType;
            if (!string.IsNullOrEmpty(contentType) &&
                contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                context.Response.Headers.Pragma = "no-cache";
                context.Response.Headers.Expires = "0";
                context.Response.Headers["X-Boot-Id"] = bootInfo.BootId;
            }
            return Task.CompletedTask;
        });
    }

    await next();
});
app.MapStaticAssets();

app.MapGet("/version", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
    context.Response.Headers.Pragma = "no-cache";
    context.Response.Headers.Expires = "0";
    context.Response.Headers.Append("X-Build-Hash", bootInfo.BuildHash);
    context.Response.Headers.Append("X-Commit-Hash", bootInfo.BuildHash);
    context.Response.Headers.Append("X-Boot-Id", bootInfo.BootId);
    return Results.Ok(new
    {
        service = "Photo Aevum Studio Web",
        version = bootInfo.InformationalVersion,
        buildHash = bootInfo.BuildHash,
        commitHash = bootInfo.BuildHash,
        bootId = bootInfo.BootId,
        timestamp = DateTime.UtcNow
    });
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AllowAnonymous();

app.Run();
