using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ── Services ─────────────────────────────────────────────────────────────────

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<PhotoAevumStudio.Application.Validation.RegisterRequestValidator>();

// ── Database ─────────────────────────────────────────────────────────────────

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string 'DefaultConnection' not found or empty. Set it in user-secrets or environment variables.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(
        connectionString,
        sql => sql.MigrationsAssembly("PhotoAevumStudio.Infrastructure")
                  .CommandTimeout(60)
                  .EnableRetryOnFailure());
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});
// ── Identity ──────────────────────────────────────────────────────────────────

builder.Services.AddScoped<Microsoft.AspNetCore.Identity.IPasswordHasher<ApplicationUser>, PhotoAevumStudio.Infrastructure.Auth.BCryptPasswordHasher>();
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength         = 8;
    options.Password.RequireDigit           = true;
    options.Password.RequireUppercase       = false; // Relaxed for studio staff
    options.Password.RequireNonAlphanumeric = false;
    options.User.RequireUniqueEmail         = true;
    options.SignIn.RequireConfirmedEmail    = false; // Email confirm done via admin
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// ── JWT Authentication ────────────────────────────────────────────────────────

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "Jwt:Key is not configured. Set it in User Secrets or environment variables.");

var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("Jwt:Audience is not configured.");

if (jwtKey.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 32 characters.");

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
        .RequireRole(AppRoles.Admin, AppRoles.SystemAdmin, AppRoles.OfficeClerk)
        .RequireAssertion(c => !MustChange(c)));
});
// ── CORS — allow the Blazor frontend origin ───────────────────────────────────

builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorFrontend", policy =>
    {
        var origins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? [
                "https://site98282.siteasp.net",
                "http://site98282.siteasp.net",
                "https://site96971.siteasp.net",
                "http://site96971.siteasp.net",
                "https://localhost:7128",
                "http://localhost:5181",
                "https://localhost:7001",
                "http://localhost:5000",
                "https://localhost:5001"
            ];

        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .SetIsOriginAllowed(origin =>
              {
                  if (origins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                      return true;

                  if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                  {
                      return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                          || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
                  }
                  return false;
              });
    });
});

// ── Problem Details (RFC 7807) ───────────────────────────────────────────────

builder.Services.AddProblemDetails();
builder.Services.AddMemoryCache();

// ── Application Services ─────────────────────────────────────────────────────

builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IPhotoService,   PhotoService>();
builder.Services.AddScoped<IStudioManagementService, StudioManagementService>();
builder.Services.AddScoped<IAdminManagementService, AdminManagementService>();
builder.Services.AddScoped<IStorageProvider, LocalStorageProvider>();
builder.Services.AddScoped<IEquipmentTypeService, EquipmentTypeService>();
builder.Services.Configure<PhotoAevumStudio.Application.Models.GoogleMeetSettings>(builder.Configuration.GetSection("GoogleMeetSettings"));
builder.Services.Configure<PhotoAevumStudio.Application.Models.XenditSettings>(builder.Configuration.GetSection("XenditSettings"));
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.IGoogleMeetAdapter, PhotoAevumStudio.Infrastructure.Services.GoogleMeetAdapter>();
builder.Services.AddHttpClient<PhotoAevumStudio.Application.Interfaces.IXenditPaymentService, PhotoAevumStudio.Infrastructure.Services.XenditPaymentService>(c =>
{
    c.BaseAddress = new Uri("https://api.xendit.co/");
});
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.IConsultationNegotiationService, PhotoAevumStudio.Infrastructure.Services.ConsultationNegotiationService>();
builder.Services.Configure<PhotoAevumStudio.Application.Models.SmtpSettings>(builder.Configuration.GetSection("SmtpSettings"));
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.IEmailService, PhotoAevumStudio.Infrastructure.Services.EmailService>();
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.ISqlAuthService, PhotoAevumStudio.Infrastructure.Services.SqlAuthService>();
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.IR2StorageService, PhotoAevumStudio.Infrastructure.Services.R2StorageService>();

// Book Schedule wizard (separate context; tables created by SchedulingSchema.sql at startup)
builder.Services.AddDbContext<PhotoAevumStudio.Infrastructure.Scheduling.SchedulingDbContext>(options =>
{
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});
builder.Services.Configure<PhotoAevumStudio.Infrastructure.Scheduling.SchedulingOptions>(builder.Configuration.GetSection("Scheduling"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IScheduleBookingService, PhotoAevumStudio.Infrastructure.Scheduling.ScheduleBookingService>();
builder.Services.AddSingleton<IMeetingLinkService, PhotoAevumStudio.Infrastructure.Scheduling.MockMeetingLinkService>();
builder.Services.AddScoped<IBookingNotificationService, PhotoAevumStudio.Infrastructure.Scheduling.BookingNotificationService>();

// ═════════════════════════════════════════════════════════════════════════════

// 1. Configure Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("LoginPolicy", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(ip, _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 5,
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

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

if (!app.Environment.IsEnvironment("Testing"))
    app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors("BlazorFrontend");
app.UseRateLimiter();
app.UseAuthentication(); // Must come before UseAuthorization
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






