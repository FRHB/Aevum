using Microsoft.AspNetCore.Components.Authorization;
using PhotoAevumStudio.Web.Components;
using PhotoAevumStudio.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<ITokenStore, ProtectedSessionTokenStore>();
builder.Services.AddScoped<AevumAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AevumAuthenticationStateProvider>());
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();

// Removed duplicate localhost:5000 clients
builder.Services.AddTransient<AuthHttpHandler>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IUserService, UserService>();   // Mock user data service (Task 3.1)
builder.Services.AddScoped<IScheduleService, ScheduleService>();
builder.Services.AddSingleton<IEquipmentService, EquipmentService>();

var apiBaseUrl = builder.Configuration["Api:BaseUrl"]
    ?? throw new InvalidOperationException("Api:BaseUrl is missing from configuration.");

builder.Services.AddHttpClient("AevumApi", client =>
    client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<AuthHttpHandler>();

builder.Services.AddHttpClient("AevumApi.Authorized", client =>
    client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<AuthHttpHandler>();

builder.Services.AddHttpClient("AevumApi.Anonymous", client =>
    client.BaseAddress = new Uri(apiBaseUrl));
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
