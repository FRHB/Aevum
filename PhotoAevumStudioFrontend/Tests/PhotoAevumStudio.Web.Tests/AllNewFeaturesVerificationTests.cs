using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Utils;
using PhotoAevumStudio.Web.Models;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class AllNewFeaturesVerificationTests
{
    [Fact]
    public void DropboxActiveUploadRegistry_TracksOtherUserLock_AndReleasesOnComplete()
    {
        var registry = new DropboxActiveUploadRegistry();
        int changeNotifications = 0;
        registry.OnChanged += () => changeNotifications++;

        var started = registry.TryStartOrUpdate("EVT-001", "sess-1", "EMP-001", "Alice Studio", "wedding.zip", 15, "Uploading");
        Assert.True(started);
        Assert.True(registry.IsUploadingByOther("EVT-001", "EMP-002", out var activeForOther));
        Assert.NotNull(activeForOther);
        Assert.Equal("Alice Studio", activeForOther!.UploadedByName);
        Assert.Equal(15, activeForOther.Percent);

        // Another staff member cannot hijack the active upload lock
        var conflict = registry.TryStartOrUpdate("EVT-001", "sess-2", "EMP-002", "Bob Clerk", "other.zip", 0, "Uploading");
        Assert.False(conflict);

        // Same user can update progress and then release
        registry.UpdateProgress("EVT-001", "sess-1", 80, "Uploading");
        Assert.Equal(80, registry.GetActiveUpload("EVT-001")!.Percent);

        registry.CompleteOrRemove("EVT-001", "sess-1");
        Assert.False(registry.IsUploadingByOther("EVT-001", "EMP-002", out _));
        Assert.True(changeNotifications >= 3);
    }

    [Fact]
    public async Task CatalogRefreshNotifier_BroadcastsImmediatelyToSubscribers()
    {
        var notifier = new CatalogRefreshNotifier();
        string? receivedCategory = null;
        notifier.OnCatalogUpdatedAsync += cat =>
        {
            receivedCategory = cat;
            return Task.CompletedTask;
        };

        await notifier.NotifyCatalogUpdatedAsync("Portfolio");
        Assert.Equal("Portfolio", receivedCategory);
    }

    [Theory]
    [InlineData("Baclig", 2006, "EMP-001", "Baclig2006E001")]
    [InlineData("baclig", 2006, "ADM-001", "Baclig2006A001")]
    [InlineData("Baclig", 2006, "SADM-101", "Baclig2006S101")]
    [InlineData("Dela Cruz", 1999, "CLK-007", "DelaCruz1999C007")]
    public void CredentialGenerators_FollowLastNameBirthYearCompactIdFormat(
        string lastName, int birthYear, string id, string expected)
    {
        Assert.Equal(expected, PhotoAevumStudio.Web.Services.EmployeeCredentialGenerator.GenerateInitialPassword(lastName, birthYear, id));
        Assert.Equal(expected, PhotoAevumStudio.Application.Utils.EmployeeCredentialGenerator.GenerateInitialPassword(lastName, birthYear, id));
        Assert.Equal(expected, AdminCredentialGenerator.GenerateInitialPassword(lastName, birthYear, id));
    }

    [Fact]
    public void TeamLeaderAndCatalogSnapshotInterfaces_ExposeRequiredApprovalAndArchiveContracts()
    {
        Assert.NotNull(typeof(IConsultationNegotiationService).GetMethod(nameof(IConsultationNegotiationService.SubmitEquipmentRequestAsync)));
        Assert.NotNull(typeof(IConsultationNegotiationService).GetMethod(nameof(IConsultationNegotiationService.ReviewEquipmentRequestAsync)));
        Assert.NotNull(typeof(IConsultationNegotiationService).GetMethod(nameof(IConsultationNegotiationService.RejectCrewReplacementAsync)));
        Assert.NotNull(typeof(IStudioManagementService).GetMethod(nameof(IStudioManagementService.SetCatalogItemArchivedAsync)));
        Assert.NotNull(typeof(IStudioManagementService).GetMethod(nameof(IStudioManagementService.DeleteCatalogItemWithSnapshotAsync)));
        Assert.NotNull(typeof(IEmployeeService).GetMethod(nameof(IEmployeeService.SubmitPasswordResetRequestAsync)));
        Assert.NotNull(typeof(IEmployeeService).GetMethod(nameof(IEmployeeService.ApprovePasswordResetRequestAsync)));
    }
}
