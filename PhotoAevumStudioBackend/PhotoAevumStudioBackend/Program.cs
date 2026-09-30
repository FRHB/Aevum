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
        return db.Database.CanConnect() ? "✅ Database is Connected!" : "❌ Connection Failed.";
    }
    catch (Exception ex)
    {
        return $"❌ Error: {ex.Message}";
    }
});

app.Run();