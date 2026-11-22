using InfisicalConfiguration;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using PhotoAevumStudio.Web.Components;
using PhotoAevumStudio.Web.Services;

var builder = WebApplication.CreateBuilder(args);

static string? FirstNonEmpty(params string?[] candidates)
{
    foreach (var candidate in candidates)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
            return candidate.Trim();
    }
    return null;
}

// Fetch Infisical auth keys from appsettings, user secrets, or environment variables
var infisicalClientId = FirstNonEmpty(
    builder.Configuration["Infisical:ClientId"],
    builder.Configuration["Infisical__ClientId"],
    builder.Configuration["INFISICAL_CLIENT_ID"],
    Environment.GetEnvironmentVariable("INFISICAL_CLIENT_ID"));
var infisicalClientSecret = FirstNonEmpty(
    builder.Configuration["Infisical:ClientSecret"],
    builder.Configuration["Infisical__ClientSecret"],
    builder.Configuration["INFISICAL_CLIENT_SECRET"],
    Environment.GetEnvironmentVariable("INFISICAL_CLIENT_SECRET"));
var infisicalProjectId = FirstNonEmpty(
    builder.Configuration["Infisical:ProjectId"],
    builder.Configuration["Infisical__ProjectId"],
    builder.Configuration["INFISICAL_PROJECT_ID"],
    Environment.GetEnvironmentVariable("INFISICAL_PROJECT_ID")) ?? string.Empty;
var infisicalEnvironment = FirstNonEmpty(
    builder.Configuration["Infisical:Environment"],
    builder.Configuration["Infisical__Environment"],
    builder.Configuration["INFISICAL_ENVIRONMENT"],
    Environment.GetEnvironmentVariable("INFISICAL_ENVIRONMENT")) ?? "dev";

// Only connect to Infisical if the keys are present
if (!string.IsNullOrEmpty(infisicalClientId) && !string.IsNullOrEmpty(infisicalClientSecret) && !string.IsNullOrEmpty(infisicalProjectId))
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
        if (builder.Environment.IsProduction())
        {
            throw new InvalidOperationException("Failed to load secrets from Infisical in Production.", ex);
        }
        Console.Error.WriteLine($"[Startup Warning] Infisical secret fetch failed in Frontend; falling back to local configuration: {ex.Message}");
    }
}

var resolvedWebConnectionString = FirstNonEmpty(
    builder.Configuration.GetConnectionString("DefaultConnection"),
    builder.Configuration["ConnectionStrings__DefaultConnection"],
    builder.Configuration["ConnectionStrings:DefaultConnection"],
    builder.Configuration["AEVUM_DB_CONNECTION"],
    builder.Configuration["DB_CONNECTION_STRING"],
    Environment.GetEnvironmentVariable("AEVUM_DB_CONNECTION"),
    Environment.GetEnvironmentVariable("DB_CONNECTION_STRING"),
    Environment.GetEnvironmentVariable("SQLCONNSTR_DefaultConnection"));
if (!string.IsNullOrWhiteSpace(resolvedWebConnectionString))
{
    builder.Configuration["ConnectionStrings:DefaultConnection"] = SqlResilienceHelper.NormalizeConnectionString(resolvedWebConnectionString);
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
builder.Services.AddScoped<IAnalyticsClient, AnalyticsClient>();
builder.Services.AddScoped<ISalesReportsClient, SalesReportsClient>();
builder.Services.AddScoped<BookingEvents>();
builder.Services.AddScoped<PaymentHubClient>();

var biConfig = builder.Configuration.GetSection(BiConfig.SectionName).Get<BiConfig>() ?? new BiConfig();
builder.Services.AddSingleton(biConfig);

var apiBaseUrl = builder.Configuration["Api:BaseUrl"]
    ?? builder.Configuration["ApiBaseUrl"]
    ?? "http://api-photoaevum.tryasp.net/";

if (!apiBaseUrl.EndsWith("/"))
{
    apiBaseUrl += "/";
}

HttpMessageHandler CreateApiHttpHandler() => builder.Environment.IsDevelopment()
    ? new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    }
    : new HttpClientHandler();

builder.Services.AddHttpClient("AevumApi", client =>
    client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<AuthHttpHandler>()
    .ConfigurePrimaryHttpMessageHandler(CreateApiHttpHandler);

builder.Services.AddHttpClient("AevumApi.Authorized", client =>
    client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<AuthHttpHandler>()
    .ConfigurePrimaryHttpMessageHandler(CreateApiHttpHandler);

builder.Services.AddHttpClient("AevumApi.Anonymous", client =>
    client.BaseAddress = new Uri(apiBaseUrl))
    .ConfigurePrimaryHttpMessageHandler(CreateApiHttpHandler);

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
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
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
