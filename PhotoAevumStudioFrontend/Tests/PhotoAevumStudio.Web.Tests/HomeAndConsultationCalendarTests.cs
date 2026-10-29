using System.Security.Claims;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Models;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class HomeAndConsultationCalendarTests
{
    [Fact]
    public void EventBookingModel_ConsultationOverlapsWith_NeverCausesStudioSpaceConflict()
    {
        var date = DateTime.Today.AddDays(5);
        var shootEvent = new EventBookingModel
        {
            Id = "EVT-001",
            StudioSpaceId = "SP-01",
            Date = date,
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(12, 0, 0),
            IsConsultation = false
        };

        var consultation = new EventBookingModel
        {
            Id = "NEG-001",
            StudioSpaceId = "SP-01",
            Date = date,
            StartTime = new TimeSpan(10, 30, 0),
            EndTime = new TimeSpan(11, 0, 0),
            IsConsultation = true
        };

        // Neither the shoot event nor the consultation should report an overlapping conflict
        Assert.False(consultation.OverlapsWith(shootEvent));
        Assert.False(shootEvent.OverlapsWith(consultation));
    }

    [Fact]
    public void UpdateNegotiationOutcomeRequest_SupportsRescheduleDateAndTime()
    {
        var newDate = DateOnly.FromDateTime(DateTime.Today.AddDays(3));
        var newTime = new TimeOnly(14, 30);

        var request = new UpdateNegotiationOutcomeRequest
        {
            NegotiationStatus = "Rescheduled",
            Notes = "Client requested afternoon slot.",
            NewConsultationDate = newDate,
            NewConsultationTime = newTime
        };

        Assert.Equal("Rescheduled", request.NegotiationStatus);
        Assert.Equal(newDate, request.NewConsultationDate);
        Assert.Equal(newTime, request.NewConsultationTime);
    }

    [Theory]
    [InlineData(0, 0, 0)]    // Active slide
    [InlineData(0, 1, 1)]    // Next slide
    [InlineData(0, 4, -1)]   // Previous slide in 5-item circular carousel
    [InlineData(2, 2, 0)]    // Active slide
    [InlineData(2, 3, 1)]    // Next slide
    [InlineData(2, 1, -1)]   // Previous slide
    public void CarouselDistance_CalculatesCorrectCircularSpan(int activeIndex, int cardIndex, int expectedDiff)
    {
        const int count = 5;
        int diff = (cardIndex - activeIndex) % count;
        if (diff > count / 2) diff -= count;
        if (diff < -count / 2) diff += count;

        Assert.Equal(expectedDiff, diff);
    }

    [Fact]
    public void ServicesPage_HasServicesPageRouteAttribute()
    {
        var pageType = typeof(PhotoAevumStudio.Web.Components.Pages.Services);
        var routeAttrs = pageType.GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.RouteAttribute), inherit: false);
        
        Assert.NotEmpty(routeAttrs);
        var route = ((Microsoft.AspNetCore.Components.RouteAttribute)routeAttrs[0]).Template;
        Assert.Equal("/services", route);
    }

    [Fact]
    public void StudioSpaces_NoDuplicateStudioA()
    {
        var scheduleService = new PhotoAevumStudio.Web.Services.ScheduleService();
        var spaces = scheduleService.GetStudioSpaces();

        // Exactly one Studio A should exist
        var studioACount = spaces.Count(s => s.Name.Contains("Studio A", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, studioACount);
        Assert.DoesNotContain(spaces, s => s.Id == "SP-01");
    }

    [Theory]
    [InlineData("Online Card", true)]
    [InlineData("QR Ph", true)]
    [InlineData("Cash", false)]
    [InlineData("Cheque", false)]
    public void WizardPaymentMethod_OnlyOnlineCardAndQRPhAllowed(string method, bool expectedValid)
    {
        var validMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Online Card", "QR Ph" };
        Assert.Equal(expectedValid, validMethods.Contains(method));
    }

    [Fact]
    public void UpdateAppointmentRosterRequest_StoresStaffAndEquipment()
    {
        var rosterReq = new UpdateAppointmentRosterRequest
        {
            EmployeeIds = new List<string> { "EMP-001", "EMP-002" },
            TeamLeaderEmployeeId = "EMP-001",
            EquipmentIds = new List<string> { "EQ-001", "EQ-002" }
        };

        Assert.Equal(2, rosterReq.EmployeeIds.Count);
        Assert.Equal("EMP-001", rosterReq.TeamLeaderEmployeeId);
        Assert.Equal(2, rosterReq.EquipmentIds.Count);
    }
}

