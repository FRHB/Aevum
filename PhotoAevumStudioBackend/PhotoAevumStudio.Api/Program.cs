using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using FluentValidation.AspNetCore;
using Hangfire;
using InfisicalConfiguration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;
using PhotoAevumStudio.Infrastructure.Services;
using Serilog;

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

// Only connect to Infisical if the keys are present (prevents crashes in environments without Infisical)
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
    }
    catch (Exception ex)
    {
        if (builder.Environment.IsProduction())
        {
            throw new InvalidOperationException("Failed to load secrets from Infisical in Production.", ex);
        }
        Console.Error.WriteLine($"[Startup Warning] Infisical secret fetch failed; falling back to local configuration: {ex.Message}");
    }
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

// ── Serilog (diagnostic analytics → Seq) ─────────────────────────────────────

builder.Host.UseSerilog((ctx, services, lc) =>
{
    var seqUrl = ctx.Configuration["Seq:ServerUrl"];
    lc.MinimumLevel.Information()
      .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
      .Enrich.FromLogContext()
      .WriteTo.Console();
    if (!string.IsNullOrWhiteSpace(seqUrl))
        lc.WriteTo.Seq(seqUrl, apiKey: ctx.Configuration["Seq:ApiKey"]);
});
// ── Services ─────────────────────────────────────────────────────────────────

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<PhotoAevumStudio.Application.Validation.RegisterRequestValidator>();

// ── Database ─────────────────────────────────────────────────────────────────

var connectionString = SqlResilienceHelper.NormalizeConnectionString(
    FirstNonEmpty(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        builder.Configuration["ConnectionStrings__DefaultConnection"],
        builder.Configuration["ConnectionStrings:DefaultConnection"],
        builder.Configuration["AEVUM_DB_CONNECTION"],
        builder.Configuration["DB_CONNECTION_STRING"],
        Environment.GetEnvironmentVariable("AEVUM_DB_CONNECTION"),
        Environment.GetEnvironmentVariable("DB_CONNECTION_STRING"),
        Environment.GetEnvironmentVariable("SQLCONNSTR_DefaultConnection")));

if (string.IsNullOrWhiteSpace(connectionString))
{
    if (builder.Environment.IsEnvironment("Testing"))
    {
        connectionString = "Server=(localdb)\\mssqllocaldb;Database=PhotoAevumStudio_Test;Trusted_Connection=True;MultipleActiveResultSets=True;";
        builder.Configuration["ConnectionStrings:DefaultConnection"] = connectionString;
    }
    else
    {
        throw new InvalidOperationException(
            "ConnectionStrings:DefaultConnection is not configured. Provide it via environment variables, Infisical, or dotnet user-secrets.");
    }
}
else
{
    builder.Configuration["ConnectionStrings:DefaultConnection"] = connectionString;
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(
        connectionString,
        sql => sql.MigrationsAssembly("PhotoAevumStudio.Infrastructure")
                  .CommandTimeout(60)
                  .EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: [-2, -1, 0, 2, 19, 20, 53, 64, 121, 233, 1205, 10053, 10054, 10060]));
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});
// ── Identity ──────────────────────────────────────────────────────────────────

builder.Services.AddScoped<Microsoft.AspNetCore.Identity.IPasswordHasher<ApplicationUser>, PhotoAevumStudio.Infrastructure.Auth.BCryptPasswordHasher>();
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength         = 8;
    options.Password.RequireDigit           = true;
    options.Password.RequireLowercase       = false; // Policy requires length 8-30, digit, and special char
    options.Password.RequireUppercase       = false; // Relaxed for studio staff
    options.Password.RequireNonAlphanumeric = true;  // Special character required (@, #, $, !, %, etc.)
    options.User.RequireUniqueEmail         = true;
    options.SignIn.RequireConfirmedEmail    = false; // Email confirm done via admin
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders()
.AddErrorDescriber<PhotoAevumStudio.Infrastructure.Auth.CustomIdentityErrorDescriber>();

// ── JWT Authentication ────────────────────────────────────────────────────────

var jwtKey = FirstNonEmpty(
    builder.Configuration["Jwt:Key"],
    builder.Configuration["Jwt__Key"],
    builder.Configuration["JWT_KEY"],
    builder.Configuration["JWT_SECRET"],
    Environment.GetEnvironmentVariable("JWT_KEY"),
    Environment.GetEnvironmentVariable("JWT_SECRET"));
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
{
    if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
    {
        // Ephemeral random signing key for local dev/testing when Jwt:Key is not explicitly configured
        jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        builder.Configuration["Jwt:Key"] = jwtKey;
    }
    else
    {
        throw new InvalidOperationException("Jwt:Key must be configured with at least 32 characters.");
    }
}

var jwtIssuer = FirstNonEmpty(builder.Configuration["Jwt:Issuer"], builder.Configuration["Jwt__Issuer"])
    ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
var jwtAudience = FirstNonEmpty(builder.Configuration["Jwt:Audience"], builder.Configuration["Jwt__Audience"])
    ?? throw new InvalidOperationException("Jwt:Audience is not configured.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromSeconds(30)
    };
    // SignalR (WebSockets/SSE) cannot send an Authorization header: accept the JWT from ?access_token= on the hub route only.
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            var token = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) &&
                ctx.HttpContext.Request.Path.StartsWithSegments(PhotoAevumStudio.Api.Hubs.PaymentHub.Route))
            {
                ctx.Token = token;
            }
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization(o =>
{
    static bool MustChange(AuthorizationHandlerContext c) =>
        c.User.FindFirstValue("must_change_password") == "true";

    o.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireAssertion(c => !MustChange(c))
        .Build();

    o.AddPolicy("AllowPasswordChange", p => p.RequireAuthenticatedUser());


    o.AddPolicy("AdminOrAbove", p => p
        .RequireRole(AppRoles.Admin, AppRoles.SystemAdmin)
        .RequireAssertion(c => !MustChange(c)));

    o.AddPolicy("StaffOrAbove", p => p
        .RequireRole(AppRoles.Admin, AppRoles.SystemAdmin, AppRoles.OfficeClerk, AppRoles.Employee)
        .RequireAssertion(c => !MustChange(c)));

    o.AddPolicy("ClerkOrAdmin", p => p
        .RequireAssertion(c => !MustChange(c) && (
            c.User.IsInRole(AppRoles.Admin) ||
            c.User.IsInRole(AppRoles.SystemAdmin) ||
            c.User.IsInRole("System Admin") ||
            c.User.IsInRole(AppRoles.OfficeClerk) ||
            c.User.IsInRole("OfficeClerk") ||
            (c.User.FindFirst("studio_id")?.Value?.StartsWith("CLK-", StringComparison.OrdinalIgnoreCase) ?? false))));
});
// ── CORS — allow the Blazor frontend origin ───────────────────────────────────

builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorFrontend", policy =>
    {
        var isDevOrTest = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");

        var configuredOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? Array.Empty<string>();

        var productionOrigins = new[]
        {
            "https://photoaevumstudio.com",
            "https://www.photoaevumstudio.com",
            "https://photoaevum.tryasp.net",
            "https://site104417.siteasp.net",
            "https://api-photoaevum.tryasp.net",
            "https://site104421.siteasp.net",
            "https://site98282.siteasp.net",
            "https://site96971.siteasp.net"
        };

        var devOrigins = isDevOrTest
            ? new[]
            {
                "https://localhost:7128",
                "http://localhost:5181",
                "https://localhost:7001",
                "http://localhost:5000",
                "https://localhost:5001"
            }
            : Array.Empty<string>();

        // The configured public BaseUrl is allowed (HTTPS in production, HTTP/HTTPS in dev).
        var baseUrlOrigin = Uri.TryCreate(builder.Configuration["BaseUrl"], UriKind.Absolute, out var baseUri) &&
                            (isDevOrTest || baseUri.Scheme == Uri.UriSchemeHttps)
            ? new[] { baseUri.GetLeftPart(UriPartial.Authority) }
            : Array.Empty<string>();

        var allOrigins = configuredOrigins
            .Concat(productionOrigins)
            .Concat(devOrigins)
            .Concat(baseUrlOrigin)
            .Where(o => Uri.TryCreate(o, UriKind.Absolute, out var u) &&
                        (isDevOrTest || u.Scheme == Uri.UriSchemeHttps))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        policy.WithOrigins(allOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .SetIsOriginAllowed(origin =>
              {
                  if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                      return false;

                  if (!isDevOrTest && uri.Scheme != Uri.UriSchemeHttps)
                      return false;

                  if (allOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                      return true;

                  if (uri.Host.Equals("photoaevumstudio.com", StringComparison.OrdinalIgnoreCase) ||
                      uri.Host.EndsWith(".photoaevumstudio.com", StringComparison.OrdinalIgnoreCase))
                      return true;

                  if (isDevOrTest &&
                      (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                       || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                       || uri.Host.EndsWith(".ngrok-free.app", StringComparison.OrdinalIgnoreCase)
                       || uri.Host.EndsWith(".ngrok-free.dev", StringComparison.OrdinalIgnoreCase)
                       || uri.Host.EndsWith(".ngrok.app", StringComparison.OrdinalIgnoreCase)
                       || uri.Host.EndsWith(".ngrok.io", StringComparison.OrdinalIgnoreCase)
                       || uri.Host.StartsWith("192.168.", StringComparison.OrdinalIgnoreCase)
                       || uri.Host.StartsWith("10.", StringComparison.OrdinalIgnoreCase)
                       || uri.Host.StartsWith("172.", StringComparison.OrdinalIgnoreCase)))
                      return true;

                  return false;
              });
    });
});

// ── Problem Details, Caching & Compression ──────────────────────────────────

builder.Services.AddProblemDetails();
builder.Services.AddMemoryCache();
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("CatalogCache", policy => policy.Expire(TimeSpan.FromMinutes(10)).Tag("catalog"));
    options.AddPolicy("StaffMetaCache", policy => policy.Expire(TimeSpan.FromMinutes(15)).Tag("staff_meta"));
});
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});

// ── Application Services ─────────────────────────────────────────────────────

builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IPhotoService,   PhotoService>();
builder.Services.AddScoped<IStudioManagementService, StudioManagementService>();
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.IAuditLogService, PhotoAevumStudio.Infrastructure.Services.AuditLogService>();
builder.Services.AddScoped<IAdminManagementService, AdminManagementService>();
builder.Services.AddScoped<IStorageProvider, LocalStorageProvider>();
builder.Services.AddScoped<IEquipmentTypeService, EquipmentTypeService>();
builder.Services.Configure<PhotoAevumStudio.Application.Models.GoogleMeetSettings>(builder.Configuration.GetSection("GoogleMeetSettings"));
builder.Services.Configure<PhotoAevumStudio.Application.Models.XenditSettings>(builder.Configuration.GetSection("XenditSettings"));
builder.Services.Configure<PhotoAevumStudio.Application.StudioProfileSettings>(builder.Configuration.GetSection(PhotoAevumStudio.Application.StudioProfileSettings.SectionName));
// Public base URL of the customer portal (live domain, LAN IP or ngrok tunnel). Used for emails and QR links so they never point at localhost on a live server.
builder.Services.PostConfigure<PhotoAevumStudio.Application.Models.XenditSettings>(s =>
{
    var baseUrl = builder.Configuration["BaseUrl"]?.Trim();
    if (!string.IsNullOrWhiteSpace(baseUrl))
        s.CustomerPortalBaseUrl = baseUrl.TrimEnd('/');
});
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.IGoogleMeetAdapter, PhotoAevumStudio.Infrastructure.Services.GoogleMeetAdapter>();
builder.Services.AddHttpClient<PhotoAevumStudio.Application.Interfaces.IXenditPaymentService, PhotoAevumStudio.Infrastructure.Services.XenditPaymentService>(c =>
{
    c.BaseAddress = new Uri("https://api.xendit.co/");
});
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.IConsultationNegotiationService, PhotoAevumStudio.Infrastructure.Services.ConsultationNegotiationService>();
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.IPaymentService, PhotoAevumStudio.Infrastructure.Services.PaymentService>();
builder.Services.AddSignalR();
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.IPendingPaymentService, PhotoAevumStudio.Infrastructure.Services.PendingPaymentService>();
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.IPaymentNotifier, PhotoAevumStudio.Api.Hubs.SignalRPaymentNotifier>();
builder.Services.AddHostedService<PhotoAevumStudio.Api.BackgroundServices.PendingPaymentExpiryService>();
builder.Services.Configure<PhotoAevumStudio.Application.Models.EmailConfig>(options =>
{
    builder.Configuration.GetSection("EmailConfig").Bind(options);
    var configuredBaseUrl = builder.Configuration["BaseUrl"]?.Trim() ?? builder.Configuration["BASE_URL"]?.Trim() ?? builder.Configuration["EmailConfig:BaseUrl"]?.Trim() ?? builder.Configuration["XenditSettings:CustomerPortalBaseUrl"]?.Trim();
    if (!string.IsNullOrWhiteSpace(configuredBaseUrl)) options.BaseUrl = configuredBaseUrl.TrimEnd('/');

    // Dynamic Infisical / Environment Variable Resolution for Primary SMTP (Gmail)
    var primaryHost = builder.Configuration["EMAIL_CONFIG__PRIMARY__HOST"] ?? builder.Configuration["EMAIL_CONFIG:PRIMARY:HOST"] ?? builder.Configuration["EmailConfig:Primary:Host"] ?? builder.Configuration["GMAIL_SMTP_HOST"] ?? builder.Configuration["SMTP_HOST"];
    var primaryUser = builder.Configuration["EMAIL_CONFIG__PRIMARY__USERNAME"] ?? builder.Configuration["EMAIL_CONFIG:PRIMARY:USERNAME"] ?? builder.Configuration["EmailConfig:Primary:Username"] ?? builder.Configuration["GMAIL_SMTP_USER"] ?? builder.Configuration["SMTP_USER"] ?? builder.Configuration["SMTP_USERNAME"];
    var primaryPass = builder.Configuration["EMAIL_CONFIG__PRIMARY__PASSWORD"] ?? builder.Configuration["EMAIL_CONFIG:PRIMARY:PASSWORD"] ?? builder.Configuration["EmailConfig:Primary:Password"] ?? builder.Configuration["GMAIL_SMTP_PASSWORD"] ?? builder.Configuration["SMTP_PASSWORD"];
    var primaryFrom = builder.Configuration["EMAIL_CONFIG__PRIMARY__FROM_EMAIL"] ?? builder.Configuration["EMAIL_CONFIG_PRIMARY_FROM_EMAIL"] ?? builder.Configuration["EMAIL_CONFIG:PRIMARY:FROM_EMAIL"] ?? builder.Configuration["EmailConfig:Primary:FromEmail"] ?? builder.Configuration["GMAIL_FROM_EMAIL"] ?? builder.Configuration["SMTP_FROM_EMAIL"];
    var primaryName = builder.Configuration["EMAIL_CONFIG__PRIMARY__FROM_NAME"] ?? builder.Configuration["EMAIL_CONFIG_PRIMARY_FROM_NAME"] ?? builder.Configuration["EMAIL_CONFIG:PRIMARY:FROM_NAME"] ?? builder.Configuration["EmailConfig:Primary:FromName"] ?? builder.Configuration["GMAIL_FROM_NAME"] ?? builder.Configuration["SMTP_FROM_NAME"];
    var primaryPortStr = builder.Configuration["EMAIL_CONFIG__PRIMARY__PORT"] ?? builder.Configuration["EMAIL_CONFIG:PRIMARY:PORT"] ?? builder.Configuration["EmailConfig:Primary:Port"] ?? builder.Configuration["GMAIL_SMTP_PORT"] ?? builder.Configuration["SMTP_PORT"];

    if (!string.IsNullOrWhiteSpace(primaryHost)) options.Primary.Host = primaryHost;
    else if (string.IsNullOrWhiteSpace(options.Primary.Host)) options.Primary.Host = "smtp.gmail.com";

    if (!string.IsNullOrWhiteSpace(primaryUser)) options.Primary.Username = primaryUser;
    if (!string.IsNullOrWhiteSpace(primaryPass)) options.Primary.Password = primaryPass;
    if (!string.IsNullOrWhiteSpace(primaryFrom)) options.Primary.FromEmail = primaryFrom;
    else if (string.IsNullOrWhiteSpace(options.Primary.FromEmail) && !string.IsNullOrWhiteSpace(options.Primary.Username) && options.Primary.Username.Contains('@'))
        options.Primary.FromEmail = options.Primary.Username;
    else if (string.IsNullOrWhiteSpace(options.Primary.FromEmail))
        options.Primary.FromEmail = "photoaevumstudio@gmail.com";

    if (!string.IsNullOrWhiteSpace(primaryName)) options.Primary.FromName = primaryName;
    else if (string.IsNullOrWhiteSpace(options.Primary.FromName)) options.Primary.FromName = "Photo Aevum Studio";

    if (int.TryParse(primaryPortStr, out int pPort) && pPort > 0) options.Primary.Port = pPort;
    else if (options.Primary.Port <= 0) options.Primary.Port = 587;

    // Dynamic Infisical / Environment Variable Resolution for Fallback SMTP (Mailtrap)
    var mtHost = builder.Configuration["EMAIL_CONFIG__FALLBACK__HOST"] ?? builder.Configuration["EMAIL_CONFIG:FALLBACK:HOST"] ?? builder.Configuration["EmailConfig:Fallback:Host"] ?? builder.Configuration["MAILTRAP_SMTP_HOST"] ?? builder.Configuration["MAILTRAP_HOST"];
    var mtUser = builder.Configuration["EMAIL_CONFIG__FALLBACK__USERNAME"] ?? builder.Configuration["EMAIL_CONFIG:FALLBACK:USERNAME"] ?? builder.Configuration["EmailConfig:Fallback:Username"] ?? builder.Configuration["MAILTRAP_SMTP_USER"] ?? builder.Configuration["MAILTRAP_USERNAME"];
    var mtPass = builder.Configuration["EMAIL_CONFIG__FALLBACK__PASSWORD"] ?? builder.Configuration["EMAIL_CONFIG:FALLBACK:PASSWORD"] ?? builder.Configuration["EmailConfig:Fallback:Password"] ?? builder.Configuration["MAILTRAP_SMTP_PASSWORD"] ?? builder.Configuration["MAILTRAP_API_TOKEN"] ?? builder.Configuration["MAILTRAP_PASSWORD"];
    var mtFrom = builder.Configuration["EMAIL_CONFIG__FALLBACK__FROM_EMAIL"] ?? builder.Configuration["EMAIL_CONFIG:FALLBACK:FROM_EMAIL"] ?? builder.Configuration["EmailConfig:Fallback:FromEmail"] ?? builder.Configuration["MAILTRAP_FROM_EMAIL"];
    var mtName = builder.Configuration["EMAIL_CONFIG__FALLBACK__FROM_NAME"] ?? builder.Configuration["EMAIL_CONFIG:FALLBACK:FROM_NAME"] ?? builder.Configuration["EmailConfig:Fallback:FromName"] ?? builder.Configuration["MAILTRAP_FROM_NAME"];
    var mtPortStr = builder.Configuration["EMAIL_CONFIG__FALLBACK__PORT"] ?? builder.Configuration["EMAIL_CONFIG:FALLBACK:PORT"] ?? builder.Configuration["EmailConfig:Fallback:Port"] ?? builder.Configuration["MAILTRAP_SMTP_PORT"];

    if (!string.IsNullOrWhiteSpace(mtHost)) options.Fallback.Host = mtHost;
    else if (string.IsNullOrWhiteSpace(options.Fallback.Host)) options.Fallback.Host = "sandbox.smtp.mailtrap.io";

    if (!string.IsNullOrWhiteSpace(mtUser)) options.Fallback.Username = mtUser;
    if (!string.IsNullOrWhiteSpace(mtPass)) options.Fallback.Password = mtPass;
    if (!string.IsNullOrWhiteSpace(mtFrom)) options.Fallback.FromEmail = mtFrom;
    else if (string.IsNullOrWhiteSpace(options.Fallback.FromEmail) && !string.IsNullOrWhiteSpace(options.Fallback.Username) && options.Fallback.Username.Contains('@'))
        options.Fallback.FromEmail = options.Fallback.Username;
    else if (string.IsNullOrWhiteSpace(options.Fallback.FromEmail))
        options.Fallback.FromEmail = "photoaevumstudio@gmail.com";

    if (!string.IsNullOrWhiteSpace(mtName)) options.Fallback.FromName = mtName;
    else if (string.IsNullOrWhiteSpace(options.Fallback.FromName)) options.Fallback.FromName = "Photo Aevum Studio";

    if (int.TryParse(mtPortStr, out int mPort) && mPort > 0) options.Fallback.Port = mPort;
    else if (options.Fallback.Port <= 0) options.Fallback.Port = 587;
});
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.IOnboardingDocumentService, PhotoAevumStudio.Infrastructure.Services.OnboardingDocumentService>();
builder.Services.Configure<PhotoAevumStudio.Application.Models.OnboardingDocumentsSettings>(
    builder.Configuration.GetSection(PhotoAevumStudio.Application.Models.OnboardingDocumentsSettings.SectionName));
builder.Services.AddScoped<PhotoAevumStudio.Infrastructure.Services.EmailService>();
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.IEmailService>(sp => sp.GetRequiredService<PhotoAevumStudio.Infrastructure.Services.EmailService>());
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.IBookingNotificationService>(sp => sp.GetRequiredService<PhotoAevumStudio.Infrastructure.Services.EmailService>());
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.ISqlAuthService, PhotoAevumStudio.Infrastructure.Services.SqlAuthService>();
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.IR2StorageService, PhotoAevumStudio.Infrastructure.Services.R2StorageService>();
builder.Services.AddSingleton<PhotoAevumStudio.Application.Interfaces.IDocumentPdfService, PhotoAevumStudio.Infrastructure.Services.DocumentPdfService>();
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.IBookingDocumentService, PhotoAevumStudio.Infrastructure.Services.BookingDocumentService>();
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.IDataArchiveService, PhotoAevumStudio.Infrastructure.Services.DataArchiveService>();
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.ICustomerDropboxService, PhotoAevumStudio.Infrastructure.Services.CustomerDropboxService>();
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.IStudioAnalyticsService, PhotoAevumStudio.Infrastructure.Services.StudioAnalyticsService>();
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.ISalesReportService, PhotoAevumStudio.Infrastructure.Services.SalesReportService>();
builder.Services.AddHostedService<PhotoAevumStudio.Api.BackgroundServices.CustomerDropboxExpiryService>();

// ── Analytics: Hangfire (ETL), ML.NET (predictive), RulesEngine (prescriptive) ──
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString, new Hangfire.SqlServer.SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true,
        PrepareSchemaIfNecessary = true
    }));
builder.Services.AddHangfireServer();
builder.Services.AddScoped<PhotoAevumStudio.Api.Analytics.ETL.DailyAnalyticsSyncJob>();
builder.Services.AddScoped<PhotoAevumStudio.Api.Analytics.Predictive.BookingPredictionService>();
builder.Services.AddSingleton(new RulesEngine.RulesEngine(Array.Empty<RulesEngine.Models.Workflow>()));
builder.Services.AddScoped<PhotoAevumStudio.Api.Analytics.Prescriptive.WorkflowActionEngine>();

// Book Schedule wizard (separate context; tables created by SchedulingSchema.sql at startup)
builder.Services.AddDbContext<PhotoAevumStudio.Infrastructure.Scheduling.SchedulingDbContext>(options =>
{
    options.UseSqlServer(connectionString, sql => sql.CommandTimeout(60).EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: [-2, -1, 0, 2, 19, 20, 53, 64, 121, 233, 1205, 10053, 10054, 10060]));
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});
builder.Services.Configure<PhotoAevumStudio.Infrastructure.Scheduling.SchedulingOptions>(builder.Configuration.GetSection("Scheduling"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IScheduleBookingService, PhotoAevumStudio.Infrastructure.Scheduling.ScheduleBookingService>();
builder.Services.AddSingleton<IMeetingLinkService, PhotoAevumStudio.Infrastructure.Scheduling.MockMeetingLinkService>();

// ═════════════════════════════════════════════════════════════════════════════

// 1. Configure Forwarded Headers & Rate Limiting
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
});

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("LoginPolicy", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(ip, _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 30,
            QueueLimit = 0
        });
    });
    options.AddPolicy("PaymentPolicy", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(ip, _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 20,
            QueueLimit = 0
        });
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();
// ═════════════════════════════════════════════════════════════════════════════

// ── Global Exception Handler (returns ProblemDetails for all unhandled errors)

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features
            .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();

        if (feature?.Error is OperationCanceledException) { if (!context.Response.HasStarted) context.Response.StatusCode = 499; return; }
        if (feature?.Error is BookingRuleException bre)
        {
            context.Response.StatusCode  = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type     = "https://photoaevum.studio/errors/booking-conflict",
                title    = "Booking Conflict",
                status   = 409,
                detail   = bre.Message
            });
            return;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new
        {
            type   = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            title  = "An unexpected error occurred.",
            status = 500
        });
    });
});

// ── Pipeline ──────────────────────────────────────────────────────────────────

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

if (!app.Environment.IsEnvironment("Testing"))
    app.UseHttpsRedirection();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

    if (ctx.Request.Path.StartsWithSegments("/private-photos", StringComparison.OrdinalIgnoreCase) ||
        ctx.Request.Path.StartsWithSegments("/storage", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});

app.UseResponseCompression();
app.UseStaticFiles();
app.UseCors("BlazorFrontend");
app.UseRateLimiter();
app.UseAuthentication(); // Must come before UseAuthorization
app.UseOutputCache();
app.UseMiddleware<PhotoAevumStudio.Api.Middleware.SessionInactivityMiddleware>();
var passwordChangeExempt = new[]
{
    "/api/auth/change-password",
    "/api/auth/initial-password-change",
    "/api/auth/first-login/change-password",
    "/api/auth/login",
    "/api/auth/logout"
};
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true
        && ctx.User.FindFirstValue("must_change_password") == "true"
        && ctx.Request.Path.StartsWithSegments("/api")
        && !passwordChangeExempt.Any(p => ctx.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsJsonAsync(new { code = "PASSWORD_CHANGE_REQUIRED" });
        return;
    }
    await next();
});

app.UseAuthorization();
app.UseMiddleware<PhotoAevumStudio.Api.Middleware.AuditLoggingMiddleware>();
app.MapControllers();
// Hangfire dashboard: restricted to authenticated Admin/SystemAdmin (or local requests in Development only).
app.MapHangfireDashboard("/hangfire", new Hangfire.DashboardOptions
{
    Authorization = new Hangfire.Dashboard.IDashboardAuthorizationFilter[]
    {
        new HangfireAdminAuthorizationFilter(app.Environment.IsDevelopment())
    }
});
app.MapHub<PhotoAevumStudio.Api.Hubs.PaymentHub>(PhotoAevumStudio.Api.Hubs.PaymentHub.Route);

var informationalVersion = System.Reflection.Assembly.GetExecutingAssembly()
    .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
    .InformationalVersion;
var buildHash = informationalVersion?.Contains('+') == true 
    ? informationalVersion.Split('+')[1] 
    : (informationalVersion ?? "unknown");

app.MapGet("/", (HttpContext context) =>
{
    context.Response.Headers.Append("X-Build-Hash", buildHash);
    context.Response.Headers.Append("X-Commit-Hash", buildHash);
    return Results.Ok(new
    {
        service = "Photo Aevum Studio API",
        status = "Online",
        environment = app.Environment.EnvironmentName,
        buildHash = buildHash,
        commitHash = buildHash,
        health = "/health",
        dbHealth = "/health/db",
        timestamp = DateTime.UtcNow
    });
}).WithName("GetRoot");

app.MapGet("/version", (HttpContext context) =>
{
    context.Response.Headers.Append("X-Build-Hash", buildHash);
    context.Response.Headers.Append("X-Commit-Hash", buildHash);
    return Results.Ok(new
    {
        service = "Photo Aevum Studio API",
        version = informationalVersion,
        buildHash = buildHash,
        commitHash = buildHash,
        timestamp = DateTime.UtcNow
    });
}).WithName("GetVersion");

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }))
   .WithName("GetHealth")
   ;

app.MapGet("/health/db", async (ApplicationDbContext dbContext) => 
{
    try 
    {
        bool canConnect = await dbContext.Database.CanConnectAsync();
        return canConnect ? Results.Ok(new { Status = "Database Connected" }) : Results.StatusCode(503);
    }
    catch
    {
        return Results.StatusCode(503);
    }
}).WithName("GetDbHealth");

// ── Migrate + Seed on startup ─────────────────────────────────────────────────

await using (var scope = app.Services.CreateAsyncScope())
{
    var db           = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var schedulingDb = scope.ServiceProvider.GetRequiredService<PhotoAevumStudio.Infrastructure.Scheduling.SchedulingDbContext>();
    var roleManager  = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var logger       = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        if (await db.Database.CanConnectAsync())
        {
            logger.LogInformation("Successfully connected to the local SQL Server database.");
            await DatabaseSeeder.ReconcileMigrationHistoryAsync(db);
            await db.Database.MigrateAsync();
            await DatabaseSeeder.EnsureStoredProceduresAsync(db);
            await PhotoAevumStudio.Infrastructure.Scheduling.SchedulingSchema.EnsureAsync(schedulingDb);
            await DatabaseSeeder.SeedRolesAsync(roleManager);

            var dropboxService = scope.ServiceProvider.GetRequiredService<ICustomerDropboxService>();
            await dropboxService.EnsureSchemaAsync();
            var documentService = scope.ServiceProvider.GetRequiredService<IBookingDocumentService>();
            await documentService.EnsureSchemaAsync();
            var analyticsService = scope.ServiceProvider.GetRequiredService<IStudioAnalyticsService>();
            await analyticsService.EnsureSchemaAsync();
            RecurringJob.AddOrUpdate<ICustomerDropboxService>(
                "customer-dropbox-expiry",
                s => s.PurgeExpiredDropboxesAsync(CancellationToken.None),
                Cron.Hourly);
        }
        else
        {
            logger.LogWarning("Unable to connect to local SQL Server database on startup.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database connection or initialization failed during startup. The API server will continue running, but database operations may fail until the database is reachable.");
    }
}

app.Run();

sealed class HangfireAdminAuthorizationFilter(bool allowLocalInDev) : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context)
    {
        var http = (context as Hangfire.Dashboard.AspNetCoreDashboardContext)?.HttpContext;
        if (http == null)
        {
            return false;
        }

        if (http.User?.Identity?.IsAuthenticated == true &&
            (http.User.IsInRole(AppRoles.Admin) || http.User.IsInRole(AppRoles.SystemAdmin)))
        {
            return true;
        }

        if (allowLocalInDev && http.Connection.RemoteIpAddress != null &&
            System.Net.IPAddress.IsLoopback(http.Connection.RemoteIpAddress))
        {
            return true;
        }

        return false;
    }
}






