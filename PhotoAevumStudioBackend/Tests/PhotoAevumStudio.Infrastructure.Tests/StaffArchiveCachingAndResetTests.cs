using System.IO;
using PhotoAevumStudio.Application.Utils;
using PhotoAevumStudio.Infrastructure.Services;
using Xunit;

namespace PhotoAevumStudio.Infrastructure.Tests;

public class StaffArchiveCachingAndResetTests
{
    private static string GetRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null &&
               !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")) &&
               !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void EmployeeCredentialGenerator_GenerateInitialPassword_IsDeterministicForReset()
    {
        var first = EmployeeCredentialGenerator.GenerateInitialPassword("Dela Cruz", 1995, "EMP-0012");
        var second = EmployeeCredentialGenerator.GenerateInitialPassword("Dela Cruz", 1995, "EMP-0012");

        Assert.Equal(first, second);
        Assert.Contains("1995", first);
    }

    [Fact]
    public void StudioEmailTemplates_StaffPasswordResetNotification_RendersExpectedContent()
    {
        var html = StudioEmailTemplates.StaffPasswordResetNotification(
            employeeName: "Juan Dela Cruz",
            employeeId: "EMP-0012",
            initialPassword: "Dela1995@",
            loginUrl: "/login");

        Assert.Contains("Juan Dela Cruz", html);
        Assert.Contains("EMP-0012", html);
        Assert.Contains("Dela1995@", html);
        Assert.Contains("initial creation password", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StudioManagementService_BatchesCatalogLookupsAndEnforcesResetPasswordFlags()
    {
        var root = GetRepositoryRoot();
        var servicePath = Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Infrastructure", "Services", "StudioManagementService.cs");
        var source = File.ReadAllText(servicePath);

        // N+1 query elimination in ResolveAssociatedNamesAsync
        Assert.Contains("SELECT ServiceName FROM dbo.Services WHERE ServicesID IN ({0});", source);
        Assert.Contains("SELECT AdditionalServiceName FROM dbo.AdditionalServices WHERE AdditionalServicesID IN ({0});", source);
        Assert.Contains("SELECT ComboPackageName FROM dbo.ComboPackage WHERE ComboPackageID IN ({0});", source);

        // ResetEmployeePasswordToInitialAsync sets forced password change & writes ChangedByType to HistoryLog
        Assert.Contains("ResetEmployeePasswordToInitialAsync", source);
        Assert.Contains("isFirstLogin = 1", source);
        Assert.Contains("MustChangePassword = 1", source);
        Assert.Contains("@ActionType = 'PASSWORD_RESET'", source);
        Assert.Contains("ChangedByType", source);
    }

    [Fact]
    public void SqlSchemaFiles_IncludeCustomerDeletionColumnsAndCatalogIndexes()
    {
        var root = GetRepositoryRoot();
        var masterSql = File.ReadAllText(Path.Combine(root, "PhotoAevumStudioBackend", "database", "PhotoAevumStudio.sql"));
        var idempotentSql = File.ReadAllText(Path.Combine(root, "PhotoAevumStudioBackend", "database", "PhotoAevumStudioIdempotent.sql"));

        var expectedObjects = new[]
        {
            "DeletionRequested",
            "DeletionRequestedAt",
            "IX_ServiceCategoryJoins_Services",
            "IX_ServiceCategoryJoins_AddOn",
            "IX_ServiceCategoryJoins_Pkg",
            "IX_ComboPackageJoin_Services",
            "IX_ComboPackageJoin_AddOn",
            "IX_ServiceCategory_Name",
            "IX_Services_Name"
        };

        foreach (var obj in expectedObjects)
        {
            Assert.Contains(obj, masterSql);
            Assert.Contains(obj, idempotentSql);
        }
    }
}
