using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class CustomerDashboardTests
{
    [Fact]
    public void CustomerDashboard_InjectedProperties_IncludeBookingEvents()
    {
        var injectProperties = typeof(CustomerDashboard)
            .GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(p => p.GetCustomAttribute<InjectAttribute>() != null)
            .Select(p => p.PropertyType)
            .ToList();

        Assert.Contains(typeof(BookingEvents), injectProperties);
    }

    [Fact]
    public void BookingEvents_CanBeRegisteredAndResolved()
    {
        var services = new ServiceCollection();
        services.AddScoped<BookingEvents>();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var instance = scope.ServiceProvider.GetService<BookingEvents>();
        Assert.NotNull(instance);
    }
}
