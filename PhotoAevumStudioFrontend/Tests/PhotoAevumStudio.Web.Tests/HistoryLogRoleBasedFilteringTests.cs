using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PhotoAevumStudio.Api.Controllers;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Web.Components.Pages;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class HistoryLogRoleBasedFilteringTests
{
    [Fact]
    public async Task HistoryLogController_SystemAdmin_CallsServiceWithSadmId()
    {
        var mockService = new Mock<IAuditLogService>();
        mockService.Setup(s => s.GetHistoryLogsAsync("SADM-001", "SystemAdmin", 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HistoryLogDto>
            {
                new(1, "LOGIN", "Session", "SADM-001", "Login", null, null, null, "SystemAdmin", "SADM-001", DateTime.UtcNow),
                new(2, "LOGIN", "Session", "ADM-002", "Login", null, null, null, "Admin", "ADM-002", DateTime.UtcNow),
                new(3, "LOGIN", "Session", "EMP-001", "Login", null, null, "EMP-001", "Employee", "EMP-001", DateTime.UtcNow)
            });

        var controller = new HistoryLogController(mockService.Object);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("studio_id", "SADM-001"),
            new Claim(ClaimTypes.Role, "SystemAdmin")
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        var result = await controller.GetHistoryLogs(100);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var logs = Assert.IsAssignableFrom<IReadOnlyList<HistoryLogDto>>(okResult.Value);

        Assert.Equal(3, logs.Count);
        mockService.Verify(s => s.GetHistoryLogsAsync("SADM-001", "SystemAdmin", 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HistoryLogController_Admin_DispatchesWithAdminIdAndRole()
    {
        var mockService = new Mock<IAuditLogService>();
        mockService.Setup(s => s.GetHistoryLogsAsync("ADM-002", "Admin", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HistoryLogDto>
            {
                new(2, "LOGIN", "Session", "ADM-002", "Login", null, null, null, "Admin", "ADM-002", DateTime.UtcNow)
            });

        var controller = new HistoryLogController(mockService.Object);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("studio_id", "ADM-002"),
            new Claim(ClaimTypes.Role, "Admin")
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        var result = await controller.GetHistoryLogs(50);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var logs = Assert.IsAssignableFrom<IReadOnlyList<HistoryLogDto>>(okResult.Value);

        Assert.Single(logs);
        Assert.Equal("ADM-002", logs[0].ChangedBy);
        mockService.Verify(s => s.GetHistoryLogsAsync("ADM-002", "Admin", 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HistoryLogController_Employee_DispatchesWithEmployeeIdAndRole()
    {
        var mockService = new Mock<IAuditLogService>();
        mockService.Setup(s => s.GetHistoryLogsAsync("EMP-001", "Employee", 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HistoryLogDto>
            {
                new(3, "UPDATE", "EventAppointment", "EVT-1", "Updated status", "EVT-1", null, "EMP-001", "Employee", "EMP-001", DateTime.UtcNow)
            });

        var controller = new HistoryLogController(mockService.Object);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("studio_id", "EMP-001"),
            new Claim(ClaimTypes.Role, "Employee")
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        var result = await controller.GetHistoryLogs(100);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var logs = Assert.IsAssignableFrom<IReadOnlyList<HistoryLogDto>>(okResult.Value);

        Assert.Single(logs);
        Assert.Equal("EMP-001", logs[0].EmployeeId);
        mockService.Verify(s => s.GetHistoryLogsAsync("EMP-001", "Employee", 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void HistoryLog_Component_DeclaresRequiredRoleAttributes()
    {
        var attributes = typeof(HistoryLog).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true);
        Assert.NotEmpty(attributes);

        var auth = (Microsoft.AspNetCore.Authorization.AuthorizeAttribute)attributes[0];
        Assert.Contains("Admin", auth.Roles);
        Assert.Contains("SystemAdmin", auth.Roles);
        Assert.Contains("Employee", auth.Roles);
        Assert.Contains("Office Clerk", auth.Roles);
        Assert.Contains("Staff", auth.Roles);
    }

    [Fact]
    public void EmployeeRoleHelper_CorrectlyIdentifiesSystemAdmin()
    {
        var sadmUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("studio_id", "SADM-001"),
            new Claim(ClaimTypes.Role, "System Admin")
        }, "TestAuth"));

        Assert.True(EmployeeRoleHelper.IsSystemAdmin(sadmUser));
        Assert.True(EmployeeRoleHelper.IsAdmin(sadmUser));
    }

    [Fact]
    public void EmployeeRoleHelper_CorrectlyIdentifiesAdminAndEmployee()
    {
        var adminUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("studio_id", "ADM-002"),
            new Claim(ClaimTypes.Role, "Admin")
        }, "TestAuth"));

        var empUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("studio_id", "EMP-005"),
            new Claim(ClaimTypes.Role, "Employee")
        }, "TestAuth"));

        Assert.False(EmployeeRoleHelper.IsSystemAdmin(adminUser));
        Assert.True(EmployeeRoleHelper.IsAdmin(adminUser));

        Assert.False(EmployeeRoleHelper.IsSystemAdmin(empUser));
        Assert.False(EmployeeRoleHelper.IsAdmin(empUser));
        Assert.True(EmployeeRoleHelper.IsPhotographerOrCreative(empUser));
    }
}
