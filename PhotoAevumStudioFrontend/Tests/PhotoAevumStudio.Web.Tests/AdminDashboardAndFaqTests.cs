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
     

        // 4. FAQ Management moved to dedicated page — verify quick-action link exists
        Assert.Contains("/admin/faqs", content);
        Assert.DoesNotContain("_isFaqModalOpen", content);
        Assert.DoesNotContain("OpenAddFaqModal", content);

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

    [Fact]
    public void FaqManagement_DedicatedPage_ContainsFullFaqCrud()
    {
        var root = GetRepositoryRoot();
        var path = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "FaqManagement.razor");
        Assert.True(File.Exists(path), $"File not found: {path}");
        var content = File.ReadAllText(path);

        Assert.Contains("Frequently Asked Questions (FAQ) Management", content);
        Assert.Contains("OpenAddFaqModal", content);
        Assert.Contains("HandleSaveFaq", content);
        Assert.Contains("HandleToggleFaqStatus", content);
        Assert.Contains("HandleDeleteFaq", content);
        Assert.Contains("/admin/faqs", content);
        Assert.Contains("/admin/faq", content);
    }

    [Fact]
    public void AdminDashboard_RefactoredLayoutHierarchy_Contains12ColGrid_RecentActivity_AndRelocatedSections()
    {
        var root = GetRepositoryRoot();
        var path = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "AdminDashboard.razor");
        Assert.True(File.Exists(path), $"File not found: {path}");
        var content = File.ReadAllText(path);

        // 1. Personalized greeting header & date pill
        Assert.Contains("@GetGreeting(), @_adminName", content);
        Assert.Contains("Here's what's happening with your team today.", content);
        Assert.Contains("@DateTime.Today.ToString(\"dddd, MMMM d, yyyy\")", content);

        // 2. 12-column responsive 8/4 layout grid
        Assert.Contains("col-span-12 lg:col-span-8", content);
        Assert.Contains("col-span-12 lg:col-span-4", content);

        // 3. Left column order: KPI cards -> LiveEventsOverviewPanel -> Current & Upcoming Events
        var kpiIdx = content.IndexOf("Today's Schedule", StringComparison.Ordinal);
        var liveIdx = content.IndexOf("<LiveEventsOverviewPanel", StringComparison.Ordinal);
        var eventsIdx = content.IndexOf("Current &amp; Upcoming Events Overview", StringComparison.Ordinal);
        Assert.True(kpiIdx > 0 && liveIdx > kpiIdx && eventsIdx > liveIdx);

        // 4. Right sidebar order: Recent Activity -> Recent Bookings -> Quick Actions
        var activityIdx = content.IndexOf("Recent Activity", StringComparison.Ordinal);
        var bookingsIdx = content.IndexOf("Recent Bookings", StringComparison.Ordinal);
        var quickIdx = content.IndexOf("Quick Actions", StringComparison.Ordinal);
        Assert.True(activityIdx > 0 && bookingsIdx > activityIdx && quickIdx > bookingsIdx);
        Assert.Contains("/admin/history-log", content);
        Assert.Contains("GetHistoryLogs", content);

        // 5. Bottom full-width sections order: Staff Timesheet & Attendance -> Staff Leave Requests
        var attendanceIdx = content.IndexOf("Staff Timesheet &amp; Attendance (Today)", StringComparison.Ordinal);
        var leavesIdx = content.IndexOf("Staff Leave Requests", StringComparison.Ordinal);
        Assert.True(attendanceIdx > quickIdx && leavesIdx > attendanceIdx);
    }
}
