using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Infrastructure.Data;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Services;

builder.Services.AddScoped<IPhotoService, PhotoService>();

var builder = WebApplication.CreateBuilder(args);

// Register SQL Server Database Context
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Allow Web/Maui Frontend to connect
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseCors("AllowFrontend");
app.UseAuthorization();
app.MapControllers();

app.Run();

var app = builder.Build();

// Standardized error response for unhandled server exceptions
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(new
        {
            StatusCode = 500,
            Message = "An unexpected server error occurred. Please try again later."
        });
    });
});
