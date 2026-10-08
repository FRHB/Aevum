using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;
using PhotoAevumStudio.Infrastructure.Services;

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
        sql => sql.EnableRetryOnFailure()));

// ── Identity ──────────────────────────────────────────────────────────────────

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

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer           = true,
        ValidateAudience         = true,
        ValidateLifetime         = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer              = builder.Configuration["Jwt:Issuer"],
        ValidAudience            = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey         = new SymmetricSecurityKey(
                                       Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew                = TimeSpan.FromSeconds(30)
    };
});

builder.Services.AddAuthorization();

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

// ── Application Services ─────────────────────────────────────────────────────

builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IPhotoService,   PhotoService>();
builder.Services.AddScoped<IStudioManagementService, StudioManagementService>();
builder.Services.AddScoped<IStorageProvider, LocalStorageProvider>();

// ═════════════════════════════════════════════════════════════════════════════

// 1. Configure Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("LoginPolicy", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 5; // 5 attempts per minute
        opt.QueueLimit = 0;
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
        await db.Database.MigrateAsync();

    var adminEmail    = builder.Configuration["Seed:AdminEmail"]    ?? "admin@photoaevum.studio";
    
    var adminPassword = builder.Configuration["Seed:AdminPassword"];
    if (!app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(adminPassword))
    {
        throw new InvalidOperationException("CRITICAL: Seed:AdminPassword is required outside of Development to prevent fallback passwords.");
    }
    adminPassword ??= "Admin@12345!";


    await DatabaseSeeder.SeedAsync(roleManager, userManager, adminEmail, adminPassword);
}

app.Run();