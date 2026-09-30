using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Register the Database Context
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Quick Database Connection Test Endpoint
app.MapGet("/test-db", (ApplicationDbContext db) =>
{
    try
    {
        if (db.Database.CanConnect())
        {
            return "✅ Database is Connected successfully!";
        }
        else
        {
            return "❌ CanConnect returned false (Database exists or server is up, but connection refused).";
        }
    }
    catch (Exception ex)
    {
        // 👉 This will show us the exact MonsterASP error (e.g., firewall, password, or IP block)
        return $"❌ Detailed Error: {ex.Message}";
    }
});

app.Run();