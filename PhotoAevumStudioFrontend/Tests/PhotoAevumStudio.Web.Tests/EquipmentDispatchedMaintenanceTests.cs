using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Models;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class EquipmentDispatchedMaintenanceTests
{
    private sealed class StubNavManager : NavigationManager
    {
        public StubNavManager() => Initialize("https://localhost/", "https://localhost/");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class StubAuthStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(user));
    }

    private static ClaimsPrincipal CreateAdminPrincipal() =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "ADM-001"),
            new Claim(ClaimTypes.Name, "Studio Admin"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("studio_id", "ADM-001")
        ], "TestAuth"));

    private static async Task<string> RenderEquipmentsPageAsync(IReadOnlyList<EquipmentItemModel> equipments)
    {
        var mockEquipmentService = new Mock<IEquipmentService>();
        mockEquipmentService.Setup(s => s.GetEquipmentTypesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EquipmentTypeModel>
            {
                new() { EquipmentTypeID = "ET-CAM", EquipmentTypeName = "Camera Body" }
            });
        mockEquipmentService.Setup(s => s.GetEquipmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(equipments);
        mockEquipmentService.Setup(s => s.GetDispatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DispatchItemModel>());

        var mockUserService = new Mock<IUserService>();
        var mockHttpFactory = new Mock<IHttpClientFactory>();
        var mockTokenStore = new Mock<ITokenStore>();
        var config = new ConfigurationBuilder().Build();
        var hubClient = new PaymentHubClient(config, mockTokenStore.Object, NullLogger<PaymentHubClient>.Instance);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(mockEquipmentService.Object);
        services.AddSingleton(mockUserService.Object);
        services.AddSingleton(mockHttpFactory.Object);
        services.AddSingleton(hubClient);
        services.AddSingleton<NavigationManager, StubNavManager>();
        services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider(CreateAdminPrincipal()));

        await using var sp = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<Equipments>();
            return output.ToHtmlString();
        });
    }

    private static string ExtractRowForEquipment(string html, string equipmentId)
    {
        var idIndex = html.IndexOf(equipmentId, StringComparison.Ordinal);
        Assert.True(idIndex >= 0, $"Could not find equipment ID '{equipmentId}' in rendered HTML.");
        var rowStart = html.LastIndexOf("<tr", idIndex, StringComparison.OrdinalIgnoreCase);
        var rowEnd = html.IndexOf("</tr>", idIndex, StringComparison.OrdinalIgnoreCase);
        Assert.True(rowStart >= 0 && rowEnd > rowStart, $"Could not locate table row for '{equipmentId}'.");
        return html.Substring(rowStart, rowEnd - rowStart + 5);
    }

    [Fact]
    public async Task EquipmentsPage_HidesSendToMaintenanceForDispatchedEquipmentOnly_AndShowsAgainWhenReturned()
    {
        var dispatchedItem = new EquipmentItemModel
        {
            EquipmentID = "EQP-DISP-01",
            EquipmentName = "Sony FX3 Dispatched",
            EquipmentTypeID = "ET-CAM",
            EquipmentTypeName = "Camera Body",
            EquipmentStatus = "Available",
            CurrentEventId = "EVT-100",
            CurrentEventName = "Wedding Shoot",
            CurrentEventStatus = "Dispatched",
            IsCurrentEventTaken = true
        };

        var availableItem = new EquipmentItemModel
        {
            EquipmentID = "EQP-AVAIL-02",
            EquipmentName = "Canon R5 Available",
            EquipmentTypeID = "ET-CAM",
            EquipmentTypeName = "Camera Body",
            EquipmentStatus = "Available"
        };

        var maintenanceItem = new EquipmentItemModel
        {
            EquipmentID = "EQP-MAINT-03",
            EquipmentName = "Nikon Z8 Maintenance",
            EquipmentTypeID = "ET-CAM",
            EquipmentTypeName = "Camera Body",
            EquipmentStatus = "Maintenance"
        };

        var html = await RenderEquipmentsPageAsync([dispatchedItem, availableItem, maintenanceItem]);

        var dispatchedRow = ExtractRowForEquipment(html, "EQP-DISP-01");
        Assert.Contains("Dispatched / On Shoot", dispatchedRow);
        Assert.Contains("Edit", dispatchedRow);
        Assert.DoesNotContain("Send to Maintenance", dispatchedRow);
        Assert.DoesNotContain("Resolve Servicing", dispatchedRow);

        var availableRow = ExtractRowForEquipment(html, "EQP-AVAIL-02");
        Assert.Contains("Available", availableRow);
        Assert.Contains("Send to Maintenance", availableRow);

        var maintenanceRow = ExtractRowForEquipment(html, "EQP-MAINT-03");
        Assert.Contains("In Maintenance", maintenanceRow);
        Assert.Contains("Resolve Servicing", maintenanceRow);
        Assert.DoesNotContain("Send to Maintenance", maintenanceRow);

        // Simulate return: dispatched item is returned to studio rack
        dispatchedItem.CurrentEventId = null;
        dispatchedItem.CurrentEventName = null;
        dispatchedItem.CurrentEventStatus = null;
        dispatchedItem.IsCurrentEventTaken = false;
        dispatchedItem.EquipmentStatus = "Available";

        var returnedHtml = await RenderEquipmentsPageAsync([dispatchedItem, availableItem, maintenanceItem]);
        var returnedRow = ExtractRowForEquipment(returnedHtml, "EQP-DISP-01");
        Assert.Contains("Available", returnedRow);
        Assert.Contains("Send to Maintenance", returnedRow);
    }

    [Fact]
    public async Task EquipmentService_BlocksMaintenanceWhileDispatched_AndAllowsAfterVerifyReturn()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new EquipmentService(config);

        var (typeAdded, typeErr) = await service.AddEquipmentTypeAsync("ET-CAM", "Camera Body", "Blue");
        Assert.True(typeAdded, typeErr);

        var (eqAdded, eqErr, createdEq) = await service.CreateEquipmentAsync("Sony A7 IV", null, "ET-CAM", "SN-A74-101", null);
        Assert.True(eqAdded, eqErr);
        Assert.NotNull(createdEq);
        var eqId = createdEq!.EquipmentID;

        // Seeded in-memory events: EVT-001..etc exist, or pick first upcoming event
        var events = await service.GetUpcomingEventsAsync();
        Assert.NotEmpty(events);
        var targetEvent = events.First();

        // Force event date to today so AssignEquipmentToEventAsync marks it active Dispatched
        targetEvent.EventDate = DateTime.Today;

        var (assigned, assignErr) = await service.AssignEquipmentToEventAsync(targetEvent.EventAppointmentID, eqId, "ADM-001");
        Assert.True(assigned, assignErr);

        var eqWhileDispatched = await service.GetEquipmentByIdAsync(eqId);
        Assert.NotNull(eqWhileDispatched);
        Assert.True(eqWhileDispatched!.IsDispatched);

        // Sending dispatched equipment to maintenance must be rejected
        var (blockedMaint, blockErr) = await service.LogMaintenanceAsync(
            eqId,
            "Repair",
            "Shutter check",
            "ADM-001",
            setUnderMaintenance: true);
        Assert.False(blockedMaint);
        Assert.NotNull(blockErr);

        // Verify return
        var dispatch = (await service.GetDispatchesAsync())
            .First(d => d.EquipmentID.Equals(eqId, StringComparison.OrdinalIgnoreCase) && !d.IsReturned);
        var (returned, retErr) = await service.VerifyReturnAsync(dispatch.EventEquipmentID, "Returned in good order", "ADM-001", flagMaintenance: false);
        Assert.True(returned, retErr);

        var eqAfterReturn = await service.GetEquipmentByIdAsync(eqId);
        Assert.NotNull(eqAfterReturn);
        Assert.False(eqAfterReturn!.IsDispatched);

        // Now sending to maintenance succeeds
        var (allowedMaint, allowErr) = await service.LogMaintenanceAsync(
            eqId,
            "Repair",
            "Post-return calibration",
            "ADM-001",
            setUnderMaintenance: true);
        Assert.True(allowedMaint, allowErr);

        var eqInMaint = await service.GetEquipmentByIdAsync(eqId);
        Assert.NotNull(eqInMaint);
        Assert.True(eqInMaint!.IsMaintenance);
    }
}
