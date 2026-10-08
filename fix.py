import sys
content = open(r'C:\Aevum\PhotoAevumStudioBackend\PhotoAevumStudio.Api\Program.cs', 'r', encoding='utf-8').read()

# 1. Strip the duplicate builder.CreateBuilder at the end
idx = content.find('var builder = WebApplication.CreateBuilder(args);', 100)
if idx != -1:
    content = content[:idx].strip() + "\n\napp.Run();\n"

# 2. Remove .WithOpenApi()
content = content.replace('.WithOpenApi();', ';')

# 3. Add RateLimiting to builder.Services
rate_limit_code = """
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
"""
if 'AddRateLimiter' not in content:
    content = content.replace('var app = builder.Build();', rate_limit_code + '\nvar app = builder.Build();')

# 4. Add UseRateLimiter
use_rl = 'app.UseRateLimiter();\napp.UseAuthentication();'
if 'app.UseRateLimiter()' not in content:
    content = content.replace('app.UseAuthentication();', use_rl)

# 5. Add strict password check
strict_check = """
    var adminPassword = builder.Configuration["Seed:AdminPassword"];
    if (!app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(adminPassword))
    {
        throw new InvalidOperationException("CRITICAL: Seed:AdminPassword is required outside of Development to prevent fallback passwords.");
    }
    adminPassword ??= "Admin@12345!";
"""
if 'CRITICAL: Seed:AdminPassword' not in content:
    content = content.replace('var adminPassword = builder.Configuration["Seed:AdminPassword"] ?? "Admin@12345!";', strict_check)

open(r'C:\Aevum\PhotoAevumStudioBackend\PhotoAevumStudio.Api\Program.cs', 'w', encoding='utf-8').write(content)
