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
    options.UseSqlServer(
        connectionString,
        sql => sql.MigrationsAssembly("PhotoAevumStudio.Infrastructure")
                  .EnableRetryOnFailure()));

// ── Identity ──────────────────────────────────────────────────────────────────

builder.Services.AddScoped<Microsoft.AspNetCore.Identity.IPasswordHasher<ApplicationUser>, PhotoAevumStudio.Infrastructure.Auth.PlaintextPasswordHasher>();
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
        .RequireRole(AppRoles.Admin)
        .RequireAssertion(c => !MustChange(c)));

    o.AddPolicy("StaffOrAbove", p => p
        .RequireRole(AppRoles.Admin, AppRoles.Employee)
        .RequireAssertion(c => !MustChange(c)));
});
// ── CORS — allow the Blazor frontend origin ───────────────────────────────────

builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorFrontend", policy =>
    {
        var origins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? ["https://localhost:7001"];

        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ── Problem Details (RFC 7807) ───────────────────────────────────────────────

builder.Services.AddProblemDetails();
builder.Services.AddMemoryCache();

// ── Application Services ─────────────────────────────────────────────────────

builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IPhotoService,   PhotoService>();
builder.Services.AddScoped<IStudioManagementService, StudioManagementService>();
builder.Services.AddScoped<IStorageProvider, LocalStorageProvider>();
builder.Services.Configure<PhotoAevumStudio.Application.Models.SmtpSettings>(builder.Configuration.GetSection("SmtpSettings"));
builder.Services.AddScoped<PhotoAevumStudio.Application.Interfaces.IEmailService, PhotoAevumStudio.Infrastructure.Services.EmailService>();

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

app.UseHttpsRedirection();
app.UseCors("BlazorFrontend");
app.UseRateLimiter();
app.UseAuthentication(); // Must come before UseAuthorization
var passwordChangeExempt = new[] { "/api/auth/change-password", "/api/auth/login", "/api/auth/logout" };
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
    var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    // Apply any pending EF migrations automatically in Development
    if (app.Environment.IsDevelopment())
    {
        try
        {
            await DatabaseSeeder.ReconcileMigrationHistoryAsync(db);
            await db.Database.MigrateAsync();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 2714)
        {
            // Object already exists collision — re-run reconciliation and complete migrations
            await DatabaseSeeder.ReconcileMigrationHistoryAsync(db);
            await db.Database.MigrateAsync();
        }
    }

    // Ensure required database stored procedures (e.g. usp_CreateEventAppointment) exist
    await DatabaseSeeder.EnsureStoredProceduresAsync(db);

    var adminEmail    = builder.Configuration["Seed:AdminEmail"]    ?? "admin@photoaevum.studio";
    
    var adminPassword = builder.Configuration["Seed:AdminPassword"];
    if (!app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(adminPassword))
    {
        throw new InvalidOperationException("CRITICAL: Seed:AdminPassword is required outside of Development to prevent fallback passwords.");
    }
    adminPassword ??= "Admin@12345!";


    await DatabaseSeeder.SeedAsync(roleManager, userManager, db, adminEmail, adminPassword);
}

app.Run();



