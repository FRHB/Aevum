using Microsoft.AspNetCore.Components.Authorization;
using PhotoAevumStudio.Web.Components;
using PhotoAevumStudio.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<ITokenStore, InMemoryTokenStore>();
builder.Services.AddScoped<AevumAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AevumAuthenticationStateProvider>());
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();

// Anonymous client for auth (login/register)
builder.Services.AddHttpClient("AevumApi.Anonymous", client =>
{
    client.BaseAddress = new Uri("http://localhost:5000/");
});

// Authenticated client with Bearer token injection
builder.Services.AddTransient<AuthHttpHandler>();
builder.Services.AddHttpClient("AevumApi.Authorized", client =>
{
    client.BaseAddress = new Uri("http://localhost:5000/");
}).AddHttpMessageHandler<AuthHttpHandler>();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IEmployeeService, MockEmployeeService>(); // Will replace in phase 3

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