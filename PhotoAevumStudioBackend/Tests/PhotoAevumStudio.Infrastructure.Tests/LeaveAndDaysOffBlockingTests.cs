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

    [Fact]
    public void PhilippineHolidays_CalculateCountableWorkdays_ExcludesEmployeeDaysOffAndHolidays()
    {
        // Nov 16 (Mon) to Nov 22 (Sun), 2026:
        // EMP-003 (Fernest, Sunday off) => 6.0 countable workdays out of 7 calendar days
        var emp003 = PhilippineHolidays.CalculateCountableWorkdays(
            new DateOnly(2026, 11, 16),
            new DateOnly(2026, 11, 22),
            isHalfDay: false,
            employeeDaysOff: "Sunday");
        Assert.Equal(7, emp003.CalendarDays);
        Assert.Equal(1, emp003.ExcludedDaysOffCount);
        Assert.Equal(6.0m, emp003.CountableDays);

        // EMP-004 (Francis, Sunday & Monday off) => 5.0 countable workdays out of 7 calendar days
        var emp004 = PhilippineHolidays.CalculateCountableWorkdays(
            new DateOnly(2026, 11, 16),
            new DateOnly(2026, 11, 22),
            isHalfDay: false,
            employeeDaysOff: "Sunday,Monday");
        Assert.Equal(7, emp004.CalendarDays);
        Assert.Equal(2, emp004.ExcludedDaysOffCount);
        Assert.Equal(5.0m, emp004.CountableDays);

        // Nov 30, 2026 is Bonifacio Day (Monday) -> excluded for EMP-003
        var withHoliday = PhilippineHolidays.CalculateCountableWorkdays(
            new DateOnly(2026, 11, 30),
            new DateOnly(2026, 12, 1),
            isHalfDay: false,
            employeeDaysOff: "Sunday");
        Assert.Equal(2, withHoliday.CalendarDays);
        Assert.Equal(1, withHoliday.ExcludedHolidaysCount);
        Assert.Equal(1.0m, withHoliday.CountableDays);
    }

    [Fact]
    public void EmployeeTimesheet_ApplyDoleShiftMinutes_EnforcesArt88NoOffsetting()
    {
        // Office Clerk (isTitleIExempt = false): arrives 30m late at 09:30 AM, works 9.0 hours until 06:30 PM (540m total)
        // RegularMinutes capped at 480m, UndertimeMinutes = 30m (late arrival), PendingOvertimeMinutes = 60m (never reduced by undertime)
        var clerkShift = new EmployeeTimesheet
        {
            EmployeeStudioId = "EMP-003",
            WorkDate = new DateOnly(2026, 11, 18),
            ClockIn = new DateTime(2026, 11, 18, 9, 30, 0),
            ClockOut = new DateTime(2026, 11, 18, 18, 30, 0)
        };
        clerkShift.ApplyDoleShiftMinutes(isTitleIExempt: false);

        Assert.Equal(480, clerkShift.RegularMinutes);
        Assert.Equal(30, clerkShift.UndertimeMinutes);
        Assert.Equal(60, clerkShift.PendingOvertimeMinutes);

        // Shoot Crew (isTitleIExempt = true): no hourly OT or undertime tracking
        var crewShift = new EmployeeTimesheet
        {
            EmployeeStudioId = "EMP-002",
            WorkDate = new DateOnly(2026, 11, 18),
            ClockIn = new DateTime(2026, 11, 18, 9, 30, 0),
            ClockOut = new DateTime(2026, 11, 18, 19, 30, 0)
        };
        crewShift.ApplyDoleShiftMinutes(isTitleIExempt: true);
        Assert.Equal(600, crewShift.RegularMinutes);
        Assert.Equal(0, crewShift.UndertimeMinutes);
        Assert.Equal(0, crewShift.PendingOvertimeMinutes);
    }
}
