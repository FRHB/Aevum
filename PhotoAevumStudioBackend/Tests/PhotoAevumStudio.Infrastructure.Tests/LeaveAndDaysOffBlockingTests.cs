using System.Reflection;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Services;
using Xunit;

namespace PhotoAevumStudio.Infrastructure.Tests;

public class LeaveAndDaysOffBlockingTests
{
    [Fact]
    public void LeaveTypes_ContainsAllExpectedOptions()
    {
        Assert.Contains(LeaveTypes.SickLeave, LeaveTypes.All);
        Assert.Contains(LeaveTypes.VacationLeave, LeaveTypes.All);
        Assert.Contains(LeaveTypes.MaternityLeave, LeaveTypes.All);
        Assert.Contains(LeaveTypes.PaternityLeave, LeaveTypes.All);
    }

    [Fact]
    public void StudioManagementService_DefinesUpdateEmployeeDaysOffAsync()
    {
        var ifaceMethod = typeof(IStudioManagementService).GetMethod(nameof(IStudioManagementService.UpdateEmployeeDaysOffAsync));
        var implMethod = typeof(StudioManagementService).GetMethod(nameof(StudioManagementService.UpdateEmployeeDaysOffAsync));

        Assert.NotNull(ifaceMethod);
        Assert.NotNull(implMethod);

        var dto = new UpdateEmployeeDaysOffDto("Sunday, Monday");
        Assert.Equal("Sunday, Monday", dto.DaysOff);
    }

    [Fact]
    public void ConsultationNegotiationService_DefinesValidateEmployeesAvailableAsync()
    {
        var validateMethod = typeof(ConsultationNegotiationService).GetMethod(
            "ValidateEmployeesAvailableAsync",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(validateMethod);
    }
}
