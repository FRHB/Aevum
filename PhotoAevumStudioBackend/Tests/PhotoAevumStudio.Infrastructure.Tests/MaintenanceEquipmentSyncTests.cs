using System;
using System.IO;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Infrastructure.Services;
using Xunit;

namespace PhotoAevumStudio.Infrastructure.Tests;

public class MaintenanceEquipmentSyncTests
{
    private static string GetRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    [Fact]
    public void AssignedEquipmentDto_DefaultsEquipmentStatusToAvailable()
    {
        var dto = new AssignedEquipmentDto();
        Assert.Equal("Available", dto.EquipmentStatus);
    }

    [Fact]
    public void ConsultationNegotiationService_SelectsEquipmentStatusAndBlocksMaintenanceVerification()
    {
        var root = GetRepositoryRoot();
        var servicePath = Path.Combine(
            root,
            "PhotoAevumStudioBackend",
            "PhotoAevumStudio.Infrastructure",
            "Services",
            "ConsultationNegotiationService.cs");
        var source = File.ReadAllText(servicePath);

        Assert.Contains("ISNULL(e.EquipmentStatus, 'Available') AS EquipmentStatus", source);

        var startIdx = source.IndexOf("public async Task<EventAppointmentDto> MarkEquipmentTakenAsync", StringComparison.Ordinal);
        var endIdx = source.IndexOf("public async Task<EventAppointmentDto> AddAdditionalEquipmentToEventAsync", startIdx, StringComparison.Ordinal);
        var section = source[startIdx..endIdx];

        Assert.Contains("IN ('MAINTENANCE', 'RETIRED', 'NEEDS REPLACEMENT')", section);
        Assert.Contains("Schedule a replacement equipment first.", section);
    }

    [Fact]
    public void ConsultationsController_UpdateRoster_AcceptsNotifierAndBroadcastsEventStatusChanged()
    {
        var root = GetRepositoryRoot();
        var controllerPath = Path.Combine(
            root,
            "PhotoAevumStudioBackend",
            "PhotoAevumStudio.Api",
            "Controllers",
            "ConsultationsController.cs");
        var source = File.ReadAllText(controllerPath);

        Assert.Contains("IPaymentNotifier? notifier = null", source);
        Assert.Contains("EventStatusChangedAsync(id, \"Updated\"", source);
    }
}
