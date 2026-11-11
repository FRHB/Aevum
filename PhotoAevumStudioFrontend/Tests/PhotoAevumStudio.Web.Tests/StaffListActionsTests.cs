using System.IO;
using Xunit;
using PhotoAevumStudio.Web.Services;

namespace PhotoAevumStudio.Web.Tests;

public class StaffListActionsTests
{
    [Fact]
    public void EmployeeService_Interface_ExposesLifecycleActionMethods()
    {
        var archiveMethod = typeof(IEmployeeService).GetMethod(nameof(IEmployeeService.ArchiveEmployeeAsync));
        var restoreMethod = typeof(IEmployeeService).GetMethod(nameof(IEmployeeService.RestoreEmployeeAsync));
        var deleteMethod = typeof(IEmployeeService).GetMethod(nameof(IEmployeeService.DeleteEmployeeAsync));

        Assert.NotNull(archiveMethod);
        Assert.NotNull(restoreMethod);
        Assert.NotNull(deleteMethod);
    }

    [Fact]
    public void UserService_Interface_ExposesArchiveAndRestoreMethods()
    {
        var archiveMethod = typeof(IUserService).GetMethod(nameof(IUserService.ArchiveEmployee));
        var restoreMethod = typeof(IUserService).GetMethod(nameof(IUserService.RestoreEmployee));
        var deleteMethod = typeof(IUserService).GetMethod(nameof(IUserService.DeleteEmployee));

        Assert.NotNull(archiveMethod);
        Assert.NotNull(restoreMethod);
        Assert.NotNull(deleteMethod);
    }

    [Fact]
    public void StaffListRazor_ContainsRequestedActionsAndIcons()
    {
        var solutionRoot = FindSolutionRoot();
        var staffListPath = Path.Combine(solutionRoot, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "StaffList.razor");
        Assert.True(File.Exists(staffListPath), $"StaffList.razor not found at {staffListPath}");

        var markup = File.ReadAllText(staffListPath);

        // 1. In active list, word "Edit" is removed from the action button, leaving clean pencil icon
        Assert.DoesNotContain(">Edit<", markup);
        Assert.DoesNotContain("> Edit<", markup);
        Assert.Contains("M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z", markup); // Pencil SVG

        // 2. Archive icon right beside pencil icon
        Assert.Contains("PromptArchive", markup);
        Assert.Contains("title=\"Archive employee\"", markup);

        // 3. In archived list: restore and delete icons right beside each other
        Assert.Contains("PromptRestore", markup);
        Assert.Contains("PromptDelete", markup);
        Assert.Contains("title=\"Restore employee to active directory\"", markup);
        Assert.Contains("title=\"Permanently delete employee\"", markup);
        Assert.Contains("M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16", markup); // Trash SVG
    }

    [Fact]
    public void EmployeeRazor_AlsoContainsRequestedActionsAndIcons()
    {
        var solutionRoot = FindSolutionRoot();
        var employeeRazorPath = Path.Combine(solutionRoot, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "Employee.razor");
        Assert.True(File.Exists(employeeRazorPath), $"Employee.razor not found at {employeeRazorPath}");

        var markup = File.ReadAllText(employeeRazorPath);

        // Active Actions: no text Edit, pencil icon and archive icon
        Assert.DoesNotContain(">Edit<", markup);
        Assert.DoesNotContain("> Edit<", markup);
        Assert.Contains("PromptArchive", markup);

        // Archived Actions: restore and delete icons
        Assert.Contains("PromptRestore", markup);
        Assert.Contains("PromptDelete", markup);
    }

    private static string FindSolutionRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "PhotoAevumStudio.slnx")) ||
                File.Exists(Path.Combine(dir, "PhotoAevumStudio.sln")))
            {
                return dir;
            }
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        return @"C:\Aevum\Aevum";
    }
}
