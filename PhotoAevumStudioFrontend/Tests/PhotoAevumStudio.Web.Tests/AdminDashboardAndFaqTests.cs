using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class AdminDashboardAndFaqTests
{
    private static string GetRepositoryRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")) && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }

    [Fact]
    public void AdminDashboard_Markup_ContainsCurrentUpcomingEvents_Timesheet_AndDynamicFaqs()
    {
        var root = GetRepositoryRoot();
        var adminDashboardPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "AdminDashboard.razor");
        Assert.True(File.Exists(adminDashboardPath), $"File not found: {adminDashboardPath}");

        var content = File.ReadAllText(adminDashboardPath);

        // 1. Current / Upcoming Events Widget
        Assert.Contains("Current &amp; Upcoming Events Overview", content);
        Assert.Contains("Happening Now", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Upcoming Scheduled Sessions", content);

        // 2. Staff Timesheet View
        Assert.Contains("Staff Timesheet &amp; Attendance (Today)", content);
        Assert.Contains("Open Full Timesheets Portal", content);

        // 3. Relevant Statistics (KPIs)
        Assert.Contains("Today's Schedule", content);
        Assert.Contains("On-Duty Staff", content);
        Assert.Contains("Active Consultations", content);
        Assert.Contains("Cash to Reconcile", content);
        Assert.Contains("Booked Deposits", content);

        // 4. Dynamic FAQ Management Section & Modal
        Assert.Contains("Frequently Asked Questions (FAQ) Management", content);
        Assert.Contains("OpenAddFaqModal", content);
        Assert.Contains("HandleSaveFaq", content);
        Assert.Contains("HandleToggleFaqStatus", content);
        Assert.Contains("HandleDeleteFaq", content);

        // 5. Equipment Placeholders removed
        Assert.DoesNotContain("Equipment In Use", content);
        Assert.DoesNotContain("Track Equipment", content);
    }

    [Fact]
    public void UserService_FaqManagement_SupportsCrudOperations()
    {
        var config = new ConfigurationBuilder().Build();
        var userService = new UserService(config);

        // 1. Initial FAQs list (seeded fallback)
        var initialFaqs = userService.GetFaqs(includeInactive: true);
        Assert.NotEmpty(initialFaqs);
        var originalCount = initialFaqs.Count;

        // 2. Save a new FAQ item
        var testQuestion = $"Test FAQ Question {Guid.NewGuid():N}";
        var newFaq = new FaqItem
        {
            Question = testQuestion,
            Answer = "This is a test answer for studio portrait booking.",
            Category = "Testing",
            DisplayOrder = 99,
            IsActive = true
        };

        var saveResult = userService.SaveFaq(newFaq);
        Assert.True(saveResult);

        // Verify it was added
        var updatedFaqs = userService.GetFaqs(includeInactive: true);
        var created = updatedFaqs.FirstOrDefault(f => f.Question == testQuestion);
        Assert.NotNull(created);
        Assert.True(created.Id > 0);
        Assert.Equal("Testing", created.Category);

        // 3. Update FAQ
        created.Answer = "Updated test answer content.";
        var updateResult = userService.SaveFaq(created);
        Assert.True(updateResult);

        var fetched = userService.GetFaqById(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Updated test answer content.", fetched.Answer);

        // 4. Delete FAQ
        var deleteResult = userService.DeleteFaq(created.Id);
        Assert.True(deleteResult);

        var postDeleteFaqs = userService.GetFaqs(includeInactive: true);
        Assert.DoesNotContain(postDeleteFaqs, f => f.Id == created.Id);
    }

    [Fact]
    public void BookingNegotiations_SimulateClientInquiry_AndRefreshInbox_AreRemoved()
    {
        var root = GetRepositoryRoot();
        var path = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "BookingNegotiations.razor");
        Assert.True(File.Exists(path), $"File not found: {path}");

        var content = File.ReadAllText(path);

        Assert.DoesNotContain("Simulate Client Inquiry", content);
        Assert.DoesNotContain("Refresh Inbox", content);
        Assert.DoesNotContain("_isSimulateModalOpen", content);
        Assert.DoesNotContain("OpenSimulateInquiryModal", content);
    }

    [Fact]
    public void CalendarOfEvents_LocationsFilter_IsRemoved()
    {
        var root = GetRepositoryRoot();
        var path = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "CalendarOfEvents.razor");
        Assert.True(File.Exists(path), $"File not found: {path}");

        var content = File.ReadAllText(path);

        Assert.DoesNotContain("_selectedSpaceFilter", content);
        Assert.DoesNotContain("Studio Bay", content);
    }
}
