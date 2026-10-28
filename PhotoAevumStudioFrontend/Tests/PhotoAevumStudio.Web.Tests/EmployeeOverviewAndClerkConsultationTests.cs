using System;
using System.Collections.Generic;
using System.Security.Claims;
using PhotoAevumStudio.Web.Models;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class EmployeeOverviewAndClerkConsultationTests
{
    [Fact]
    public void EmployeeRoleHelper_OfficeClerk_DistinguishedFromOtherSpecializations()
    {
        // 1. User with Office Clerk role claim
        var clerkUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "EMP-001"),
            new Claim("studio_id", "EMP-001"),
            new Claim(ClaimTypes.Role, AppRoles.OfficeClerk),
            new Claim("specialization", "Office Clerk")
        }, "jwt"));

        Assert.True(EmployeeRoleHelper.IsOfficeClerk(clerkUser));
        Assert.False(EmployeeRoleHelper.IsPhotographerOrCreative(clerkUser));

        // 2. User with Photographer specialization
        var photographerUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "EMP-002"),
            new Claim("studio_id", "EMP-002"),
            new Claim(ClaimTypes.Role, "Employee"),
            new Claim("specialization", "Lead Photographer")
        }, "jwt"));

        Assert.False(EmployeeRoleHelper.IsOfficeClerk(photographerUser));
        Assert.True(EmployeeRoleHelper.IsPhotographerOrCreative(photographerUser));

        // 3. User with Editor specialization
        var editorUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "EMP-003"),
            new Claim("studio_id", "EMP-003"),
            new Claim(ClaimTypes.Role, "Employee"),
            new Claim("specialization", "Senior Photo Editor")
        }, "jwt"));

        Assert.False(EmployeeRoleHelper.IsOfficeClerk(editorUser));
        Assert.True(EmployeeRoleHelper.IsPhotographerOrCreative(editorUser));
    }

    [Fact]
    public void NonOfficeClerk_MustNotHaveAccessToConsultationsAndDeals()
    {
        var photographerUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "EMP-002"),
            new Claim("studio_id", "EMP-002"),
            new Claim(ClaimTypes.Role, "Employee"),
            new Claim("specialization", "Photographer")
        }, "jwt"));

        bool isClerk = EmployeeRoleHelper.IsOfficeClerk(photographerUser);
        bool isAdmin = EmployeeRoleHelper.IsAdmin(photographerUser);

        // Consultations & Deals must only be visible if clerk or admin
        bool canAccessConsultations = isClerk || isAdmin;
        Assert.False(canAccessConsultations, "Non-office-clerk employees must not have access to consultations and deals.");
    }

    [Fact]
    public void OfficeClerk_HasAccessToConsultationsAndDeals()
    {
        var clerkUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "EMP-001"),
            new Claim("studio_id", "EMP-001"),
            new Claim(ClaimTypes.Role, "Office Clerk"),
            new Claim("specialization", "Office Clerk")
        }, "jwt"));

        bool isClerk = EmployeeRoleHelper.IsOfficeClerk(clerkUser);
        bool isAdmin = EmployeeRoleHelper.IsAdmin(clerkUser);

        bool canAccessConsultations = isClerk || isAdmin;
        Assert.True(canAccessConsultations, "Office clerk must have access to consultations and deals.");
    }

    [Fact]
    public void SlotAvailability_UnavailableSlots_AreIdentifiedCorrectly()
    {
        var morningSlots = new List<SlotAvailability>
        {
            new(new TimeOnly(9, 0), new TimeOnly(10, 0), IsAvailable: true),
            new(new TimeOnly(10, 0), new TimeOnly(11, 0), IsAvailable: false) // Booked / Unavailable
        };

        Assert.True(morningSlots[0].IsAvailable);
        Assert.False(morningSlots[1].IsAvailable);

        var day = new DayAvailability(new DateOnly(2026, 11, 1), morningSlots);
        Assert.True(day.HasAvailability);

        var allUnavailable = new DayAvailability(new DateOnly(2026, 11, 2), new List<SlotAvailability>
        {
            new(new TimeOnly(9, 0), new TimeOnly(10, 0), IsAvailable: false),
            new(new TimeOnly(10, 0), new TimeOnly(11, 0), IsAvailable: false)
        });
        Assert.False(allUnavailable.HasAvailability);
    }

    [Fact]
    public void PortfolioFolderModel_FeaturedPortfolios_ContainsHighlightAndImagesOnly()
    {
        var portfolio = new PortfolioFolderModel
        {
            PortfolioId = "PRF-0001",
            PortfolioName = "Wedding Highlights 2026",
            PortfolioDescription = "Curated wedding captures",
            CategoryID = "PCAT-0001",
            CategoryName = "Weddings",
            IsFeatured = true,
            HighlightImageUrl = "https://cdn.example.com/cover.jpg",
            Images = new List<PortfolioImageModel>
            {
                new() { Id = 1, PortfolioId = "PRF-0001", Url = "https://cdn.example.com/p1.jpg" },
                new() { Id = 2, PortfolioId = "PRF-0001", Url = "https://cdn.example.com/p2.jpg" }
            }
        };

        Assert.True(portfolio.IsFeatured);
        Assert.Equal(2, portfolio.Images.Count);
        Assert.Equal("Weddings", portfolio.CategoryName);
        Assert.False(string.IsNullOrEmpty(portfolio.HighlightImageUrl));
    }
}
