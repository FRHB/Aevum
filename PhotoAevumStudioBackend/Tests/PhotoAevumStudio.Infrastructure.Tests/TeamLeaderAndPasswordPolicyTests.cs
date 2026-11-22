using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Utils;
using PhotoAevumStudio.Application.Validation;
using PhotoAevumStudio.Infrastructure.Services;
using Xunit;

namespace PhotoAevumStudio.Infrastructure.Tests;

public class TeamLeaderAndPasswordPolicyTests
{
    [Fact]
    public void AdminCredentialGenerator_TemporaryPassword_CompliesWithUpdatedPasswordPolicy()
    {
        for (int i = 0; i < 50; i++)
        {
            var generated = AdminCredentialGenerator.GenerateTemporaryPassword(12);

            Assert.InRange(generated.Length, PasswordPolicy.MinLength, PasswordPolicy.MaxLength);
            Assert.Matches(@"[0-9]", generated);
            Assert.Matches(@"[@#$%&]", generated);
            Assert.Null(PasswordPolicy.Validate(generated));
        }
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("Abc12@", false)]
    [InlineData("123456@", false)]
    [InlineData("1234567@", true)]
    [InlineData("Pass123!", false)]
    [InlineData("Pass123^", false)]
    [InlineData("NoNumber@", false)]
    [InlineData("Pass123@", true)]
    [InlineData("Pass123#", true)]
    [InlineData("Pass123$", true)]
    [InlineData("Pass123%", true)]
    [InlineData("Pass123&", true)]
    public void PasswordPolicy_Validate_EnforcesLengthNumberAndAllowedSpecials(string password, bool expectedValid)
    {
        var error = PasswordPolicy.Validate(password);
        Assert.Equal(expectedValid, error is null);
    }

    [Fact]
    public void PasswordPolicy_Validate_EnforcesExactMaxLengthOf30()
    {
        var exactly30 = new string('a', 28) + "1@";
        var thirtyOne = new string('a', 29) + "1@";

        Assert.Equal(30, exactly30.Length);
        Assert.Equal(31, thirtyOne.Length);
        Assert.Null(PasswordPolicy.Validate(exactly30));
        Assert.NotNull(PasswordPolicy.Validate(thirtyOne));
    }

    [Theory]
    [InlineData("Short1@", false)]
    [InlineData("Pass123!", false)]
    [InlineData("TooLongPassword12345678901234@x", false)]
    [InlineData("ValidPass1#", true)]
    [InlineData("FirstLogin9$", true)]
    public void FluentValidators_And_DataAnnotations_EnforcePasswordPolicyConsistently(string password, bool expectedValid)
    {
        var initialResult = new InitialPasswordChangeRequestValidator().Validate(
            new InitialPasswordChangeRequest("EMP-001", "Temp123@", password, password));
        Assert.Equal(expectedValid, initialResult.IsValid);

        var resetResult = new ResetPasswordRequestValidator().Validate(
            new ResetPasswordRequest("user@example.com", "123456", password));
        Assert.Equal(expectedValid, resetResult.IsValid);

        var registerResult = new RegisterRequestValidator().Validate(
            new RegisterRequest("Juan", "Dela Cruz", null, "juan@example.com", password, AppRoles.Customer));
        Assert.Equal(expectedValid, registerResult.IsValid);

        var custDto = new CustomerRegisterRequest("Maria", "Santos", null, "maria@example.com", password, "123456", "09123456789");
        var validationResults = new List<ValidationResult>();
        var dataAnnotationsValid = Validator.TryValidateObject(custDto, new ValidationContext(custDto), validationResults, validateAllProperties: true);
        Assert.Equal(expectedValid, dataAnnotationsValid);
    }

    [Fact]
    public void StudioEmailTemplates_FinalPaymentRequired_RendersQrCheckoutAndBalanceDetails()
    {
        var html = StudioEmailTemplates.FinalPaymentRequired(
            customerName: "Maria Santos",
            referenceId: "EVT-1001-FINAL",
            appointmentId: "EVT-1001",
            eventName: "Wedding Highlights",
            eventDate: new DateOnly(2026, 12, 15),
            eventTime: new TimeOnly(14, 30),
            finalBalanceAmount: 8500.00m,
            checkoutConfirmUrl: "https://photoaevum.studio/confirm-payment/EVT-1001-FINAL");

        Assert.Contains("Maria Santos", html);
        Assert.Contains("EVT-1001-FINAL", html);
        Assert.Contains("8,500.00", html);
        Assert.Contains("api.qrserver.com/v1/create-qr-code", html);
        Assert.Contains("https://photoaevum.studio/confirm-payment/EVT-1001-FINAL", html);
        Assert.Contains("Pay Final Balance Now", html);
    }

    [Fact]
    public void StudioEmailTemplates_TeamLeaderQrPaymentPaidNotification_RendersVerifiedDetails()
    {
        var html = StudioEmailTemplates.TeamLeaderQrPaymentPaidNotification(
            teamLeaderName: "Carlo Reyes",
            appointmentId: "EVT-1001",
            referenceId: "EVT-1001-FINAL",
            eventName: "Wedding Highlights",
            customerName: "Maria Santos",
            eventDate: new DateOnly(2026, 12, 15),
            amountPaid: 8500.00m,
            paymentChannel: "Xendit QR (GCash/Maya)",
            staffDashboardUrl: "https://photoaevum.studio/staff");

        Assert.Contains("Carlo Reyes", html);
        Assert.Contains("Maria Santos", html);
        Assert.Contains("EVT-1001", html);
        Assert.Contains("EVT-1001-FINAL", html);
        Assert.Contains("8,500.00", html);
        Assert.Contains("Xendit QR (GCash/Maya)", html);
        Assert.Contains("Payment Verification (QR:Paid)", html);
        Assert.Contains("Completed", html);
        Assert.Contains("https://photoaevum.studio/staff", html);
    }

    [Fact]
    public void StudioEmailTemplates_CustomerFinalQrPaymentConfirmation_RendersVerifiedDetails()
    {
        var html = StudioEmailTemplates.CustomerFinalQrPaymentConfirmation(
            customerName: "Maria Santos",
            appointmentId: "EVT-1001",
            referenceId: "EVT-1001-FINAL",
            eventName: "Wedding Highlights",
            eventDate: new DateOnly(2026, 12, 15),
            amountPaid: 8500.00m,
            paymentChannel: "QRPH",
            myBookingsUrl: "https://photoaevum.studio/my/bookings");

        Assert.Contains("Maria Santos", html);
        Assert.Contains("EVT-1001", html);
        Assert.Contains("EVT-1001-FINAL", html);
        Assert.Contains("8,500.00", html);
        Assert.Contains("QRPH", html);
        Assert.Contains("Payment Verification (QR:Paid)", html);
        Assert.Contains("Completed", html);
        Assert.Contains("https://photoaevum.studio/my/bookings", html);
    }

    [Theory]
    [InlineData("Payment Verification (Cash)", null, "Payment Verification (Cash)", null, "Payment Verification (Cash)")]
    [InlineData("Pending Payment", "Xendit_QR", "Payment Verification (QR:Pending Payment)", "EVT-1-FINAL", "Payment Verification (QR:Pending Payment)")]
    [InlineData("Completed", "Xendit_QR", "Payment Verification (QR:Paid)", "EVT-1-FINAL", "Payment Verification (QR:Paid)")]
    [InlineData("Completed", "Xendit_QR", "Paid", "EVT-1-FINAL", "Payment Verification (QR:Paid)")]
    [InlineData("Ongoing", null, null, null, "")]
    public void ConsultationNegotiationService_MapPaymentVerificationStatus_StandardizesLabels(
        string eventStatus,
        string? finalMethod,
        string? finalStatus,
        string? finalRefId,
        string expected)
    {
        var actual = ConsultationNegotiationService.MapPaymentVerificationStatus(eventStatus, finalMethod, finalStatus, finalRefId);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Preparing", null, null, null, "Preparing")]
    [InlineData("Ongoing", null, null, null, "Ongoing")]
    [InlineData("Event Done", null, null, null, "Event Done")]
    [InlineData("Post Event", null, null, null, "Event Done")]
    [InlineData("Payment Verification (Cash)", "Cash_OnHand", "Payment Verification (Cash)", null, "Payment Verification (Cash)")]
    [InlineData("Pending Payment", "Xendit_QR", "Payment Verification (QR:Pending Payment)", "EVT-1-FINAL", "Payment Verification (QR:Pending Payment)")]
    [InlineData("Completed", "Xendit_QR", "Payment Verification (QR:Paid)", "EVT-1-FINAL", "Completed")]
    public void ConsultationNegotiationService_MapDisplayStage_StandardizesExecutionAndPaymentStages(
        string eventStatus,
        string? finalMethod,
        string? finalStatus,
        string? finalRefId,
        string expected)
    {
        var actual = ConsultationNegotiationService.MapDisplayStage(eventStatus, finalMethod, finalStatus, finalRefId);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PendingPaymentService_SchemaBatches_IncludesTeamLeaderStagesColumnsAndPaidGuard()
    {
        var batches = typeof(PendingPaymentService)
            .GetField("SchemaBatches", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(null) as string[];
        Assert.NotNull(batches);
        var allSql = string.Join("\n", batches!);

        Assert.Contains("FinalPaymentMethod", allSql);
        Assert.Contains("FinalPaymentStatus", allSql);
        Assert.Contains("FinalPaymentStatus nvarchar(60)", allSql);
        Assert.Contains("FinalQrString", allSql);
        Assert.Contains("FinalPaymentReferenceId", allSql);
        Assert.Contains("FinalPaymentPaidAt", allSql);

        Assert.Contains("'Preparing'", allSql);
        Assert.Contains("'Preparation'", allSql);
        Assert.Contains("'Dispatch'", allSql);
        Assert.Contains("'Ongoing'", allSql);
        Assert.Contains("'On Going'", allSql);
        Assert.Contains("'Event Done'", allSql);
        Assert.Contains("'Post Event'", allSql);
        Assert.Contains("'Pending Verification'", allSql);
        Assert.Contains("'Payment Verification (Cash)'", allSql);
        Assert.Contains("'Complete'", allSql);
        Assert.Contains("'Completed'", allSql);
        Assert.Contains("IsTaken", allSql);
        Assert.Contains("IsAdditionalRequest", allSql);

        Assert.Contains("ISNULL(DownpaymentStatus, 'Pending') <> 'Paid'", allSql);
    }

    [Fact]
    public void TeamLeaderMonitorSelector_SelectUpcomingAndOngoing_FiltersToOnlyPaidUpcomingAndOngoingEvents()
    {
        var now = new DateTime(2026, 11, 20, 10, 0, 0);
        var today = DateOnly.FromDateTime(now.Date);

        var events = new[]
        {
            new PhotoAevumStudio.Application.TeamLeaderEventDto
            {
                EventAppointmentId = "EVT-UNPAID",
                EventStatus = "Pending Payment",
                DownpaymentStatus = "Pending",
                EventDate = today.AddDays(1),
                EventTime = new TimeOnly(9, 0)
            },
            new PhotoAevumStudio.Application.TeamLeaderEventDto
            {
                EventAppointmentId = "EVT-DONE",
                EventStatus = "Completed",
                DownpaymentStatus = "Paid",
                EventDate = today,
                EventTime = new TimeOnly(8, 0)
            },
            new PhotoAevumStudio.Application.TeamLeaderEventDto
            {
                EventAppointmentId = "EVT-CANCELLED",
                EventStatus = "Cancelled",
                DownpaymentStatus = "Paid",
                EventDate = today.AddDays(1),
                EventTime = new TimeOnly(10, 0)
            },
            new PhotoAevumStudio.Application.TeamLeaderEventDto
            {
                EventAppointmentId = "EVT-UPCOMING",
                EventStatus = "Confirmed",
                DownpaymentStatus = "Paid",
                EventDate = today.AddDays(2),
                EventTime = new TimeOnly(14, 0)
            },
            new PhotoAevumStudio.Application.TeamLeaderEventDto
            {
                EventAppointmentId = "EVT-ONGOING",
                EventStatus = "Ongoing",
                DownpaymentStatus = "Paid",
                EventDate = today,
                EventTime = new TimeOnly(9, 30)
            },
            new PhotoAevumStudio.Application.TeamLeaderEventDto
            {
                EventAppointmentId = "EVT-PREP",
                EventStatus = "Preparing",
                DownpaymentStatus = "Paid",
                EventDate = today,
                EventTime = new TimeOnly(13, 0)
            }
        };

        var selected = PhotoAevumStudio.Application.TeamLeaderMonitorSelector.SelectUpcomingAndOngoing(events, now);
        Assert.Equal(3, selected.Count);
        Assert.Equal("EVT-ONGOING", selected[0].EventAppointmentId);
        Assert.Equal("EVT-PREP", selected[1].EventAppointmentId);
        Assert.Equal("EVT-UPCOMING", selected[2].EventAppointmentId);
    }

    [Fact]
    public void TeamLeaderMonitorSelector_GetProcessSteps_ReflectsCurrentStageProgress()
    {
        var ongoingSteps = PhotoAevumStudio.Application.TeamLeaderMonitorSelector.GetProcessSteps(
            new PhotoAevumStudio.Application.TeamLeaderEventDto
            {
                EventStatus = "Ongoing",
                DownpaymentStatus = "Paid"
            });

        Assert.Equal(5, ongoingSteps.Count);
        Assert.Equal("done", ongoingSteps[0].State);   // Booked
        Assert.Equal("done", ongoingSteps[1].State);   // Preparing
        Assert.Equal("active", ongoingSteps[2].State); // Ongoing
        Assert.Equal("pending", ongoingSteps[3].State); // Event Done
        Assert.Equal("pending", ongoingSteps[4].State); // Settlement

        var cashPendingSteps = PhotoAevumStudio.Application.TeamLeaderMonitorSelector.GetProcessSteps(
            new PhotoAevumStudio.Application.TeamLeaderEventDto
            {
                EventStatus = "Payment Verification (Cash)",
                FinalPaymentStatus = "Payment Verification (Cash)",
                DownpaymentStatus = "Paid"
            });

        Assert.Equal("done", cashPendingSteps[3].State);
        Assert.Equal("active-amber", cashPendingSteps[4].State);
        Assert.Equal("Payment Verification (Cash)", cashPendingSteps[4].Label);
    }

    [Fact]
    public void PaymentNotifierInterface_ExposesEventStatusChangedAsync_ForCrossPlatformBroadcast()
    {
        var method = typeof(PhotoAevumStudio.Application.Interfaces.IPaymentNotifier)
            .GetMethod(nameof(PhotoAevumStudio.Application.Interfaces.IPaymentNotifier.EventStatusChangedAsync));

        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(5, parameters.Length);
        Assert.Equal("appointmentId", parameters[0].Name);
        Assert.Equal("eventStatus", parameters[1].Name);
        Assert.Equal("finalPaymentStatus", parameters[2].Name);
        Assert.Equal("customerId", parameters[3].Name);
    }

    [Fact]
    public void ConsultationNegotiationService_TeamLeaderQueries_AvoidDistinctOrderByErrorAndShareFallback()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")))
        {
            dir = dir.Parent;
        }
        var root = dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        var servicePath = Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Infrastructure", "Services", "ConsultationNegotiationService.cs");
        var source = File.ReadAllText(servicePath);

        var getIdx = source.IndexOf("public async Task<IReadOnlyList<TeamLeaderEventDto>> GetTeamLeaderEventsAsync", StringComparison.Ordinal);
        var nextIdx = source.IndexOf("public async Task<EventAppointmentDto> UpdateTeamLeaderEventStatusAsync", getIdx, StringComparison.Ordinal);
        var getSection = source[getIdx..nextIdx];

        Assert.DoesNotContain("SELECT DISTINCT", getSection, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXISTS (", getSection);
        Assert.Contains("ORDER BY eeFirst.EmployeeID ASC", getSection);
        Assert.Contains("IsTeamLeaderForEmp", getSection);
        Assert.Contains("eeAny.EmployeeID", getSection);

        var isTlIdx = source.IndexOf("public async Task<bool> IsTeamLeaderForEventAsync", StringComparison.Ordinal);
        var autoIdx = source.IndexOf("public async Task<int> AutoTriggerPostEventsAsync", isTlIdx, StringComparison.Ordinal);
        var isTlSection = source[isTlIdx..autoIdx];

        Assert.Contains("FROM dbo.EventAppointment ea", isTlSection);
        Assert.Contains("ORDER BY eeFirst.EmployeeID ASC", isTlSection);
    }

    [Fact]
    public void ConsultationNegotiationService_MarkEquipmentTakenAsync_ChecksExistenceOutsideTransactionAndLogsEquipmentStatus()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")))
        {
            dir = dir.Parent;
        }
        var root = dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        var servicePath = Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Infrastructure", "Services", "ConsultationNegotiationService.cs");
        var source = File.ReadAllText(servicePath);

        var startIdx = source.IndexOf("public async Task<EventAppointmentDto> MarkEquipmentTakenAsync", StringComparison.Ordinal);
        var endIdx = source.IndexOf("public async Task<EventAppointmentDto> AddAdditionalEquipmentToEventAsync", startIdx, StringComparison.Ordinal);
        var section = source[startIdx..endIdx];

        var beginTxIdx = section.IndexOf("BeginTransactionAsync", StringComparison.Ordinal);
        var commitTxIdx = section.IndexOf("CommitAsync", StringComparison.Ordinal);
        Assert.True(beginTxIdx > 0 && commitTxIdx > beginTxIdx);

        var insideTx = section[beginTxIdx..commitTxIdx];
        Assert.DoesNotContain("GetAppointmentByIdOnConnectionAsync", insideTx);
        Assert.Contains("GetAppointmentByIdOnConnectionAsync", section[..beginTxIdx]);
        Assert.Contains("SET EventStatus = 'Dispatch'", insideTx);
        Assert.Contains("EntityName, EntityID", insideTx);
        Assert.Contains("'EventEquipment'", insideTx);
    }

    [Fact]
    public void CrewVerification_ReplacementAndSuddenAbsence_ContractsAndTemplates_AreConfigured()
    {
        // 1. SchemaBatches includes EventEmployee verification columns & EventCrewReplacementRequest table
        var batches = typeof(PendingPaymentService)
            .GetField("SchemaBatches", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(null) as string[];
        Assert.NotNull(batches);
        var allSql = string.Join("\n", batches!);
        Assert.Contains("EventEmployee", allSql);
        Assert.Contains("IsVerified", allSql);
        Assert.Contains("DutyStatus", allSql);
        Assert.Contains("EventCrewReplacementRequest", allSql);

        // 2. Emergency / Sudden Sick leave type is registered
        Assert.Contains(PhotoAevumStudio.Application.Entities.LeaveTypes.EmergencySick,
            PhotoAevumStudio.Application.Entities.LeaveTypes.All);
        Assert.NotNull(PhotoAevumStudio.Application.Entities.LeaveTypes.Resolve("EMERG"));

        // 3. Interface methods for Crew Duty Verification & Replacement Requests exist
        var iface = typeof(PhotoAevumStudio.Application.Interfaces.IConsultationNegotiationService);
        Assert.NotNull(iface.GetMethod(nameof(PhotoAevumStudio.Application.Interfaces.IConsultationNegotiationService.VerifyCrewDutyAsync)));
        Assert.NotNull(iface.GetMethod(nameof(PhotoAevumStudio.Application.Interfaces.IConsultationNegotiationService.RequestCrewReplacementAsync)));
        Assert.NotNull(iface.GetMethod(nameof(PhotoAevumStudio.Application.Interfaces.IConsultationNegotiationService.FulfillCrewReplacementAsync)));

        // 4. IPaymentNotifier exposes RosterUpdatedAsync for real-time staff portal updates
        var notifierMethod = typeof(PhotoAevumStudio.Application.Interfaces.IPaymentNotifier)
            .GetMethod(nameof(PhotoAevumStudio.Application.Interfaces.IPaymentNotifier.RosterUpdatedAsync));
        Assert.NotNull(notifierMethod);

        // 5. Email templates render assignment & removal details
        var assignedHtml = StudioEmailTemplates.StaffEventRosterAssignmentNotification(
            "Elena Rostova", "EVT-300", "Wedding Shoot", "Maria Santos",
            new DateOnly(2026, 12, 1), new TimeOnly(10, 0), "Studio A",
            isTeamLeader: true, isReplacement: true, staffPortalUrl: "https://photoaevum.studio/staff/team-lead-monitor");
        Assert.Contains("Elena Rostova", assignedHtml);
        Assert.Contains("Team Leader", assignedHtml);
        Assert.Contains("EVT-300", assignedHtml);
        Assert.Contains("https://photoaevum.studio/staff/team-lead-monitor", assignedHtml);

        var removedHtml = StudioEmailTemplates.StaffRemovedFromEventNotification(
            "Marco Diaz", "EVT-300", "Wedding Shoot", new DateOnly(2026, 12, 1),
            "Sudden sick absence", "https://photoaevum.studio/staff");
        Assert.Contains("Marco Diaz", removedHtml);
        Assert.Contains("Sudden sick absence", removedHtml);
        Assert.Contains("EVT-300", removedHtml);
    }

    [Fact]
    public void CashVerificationSqlAndAuth_AvoidComputedChangedBy_SupportBothAdminAndOfficeClerk()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")))
        {
            dir = dir.Parent;
        }
        var root = dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");

        var consultationService = File.ReadAllText(Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Infrastructure", "Services", "ConsultationNegotiationService.cs"));
        var verifyStart = consultationService.IndexOf("public async Task<EventAppointmentDto> VerifyCashSettlementAsync", StringComparison.Ordinal);
        var verifyEnd = consultationService.IndexOf("public async Task<CustomerAppointmentActionResultDto> CustomerCancelAppointmentAsync", verifyStart, StringComparison.Ordinal);
        var verifySection = consultationService[verifyStart..verifyEnd];

        Assert.DoesNotContain("'APPROVAL'", verifySection);
        Assert.DoesNotContain("ChangedBy,", verifySection);
        Assert.Contains("'STATUS_CHANGE'", verifySection);
        Assert.Contains("ChangedByEmployeeID, ChangedByAdminID", verifySection);

        var paymentService = File.ReadAllText(Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Infrastructure", "Services", "PaymentService.cs"));
        Assert.DoesNotContain("'APPROVAL'", paymentService);
        Assert.DoesNotContain("ChangedBy,", paymentService);
        Assert.Contains("ChangedByEmployeeID, ChangedByAdminID", paymentService);

        var storedProcs = File.ReadAllText(Path.Combine(root, "PhotoAevumStudioBackend", "database", "PhotoAevumStudioStoredProcedures.sql"));
        var procStart = storedProcs.IndexOf("CREATE OR ALTER PROCEDURE dbo.usp_VerifyCashPayment", StringComparison.Ordinal);
        var procSection = storedProcs[procStart..];
        Assert.DoesNotContain("'APPROVAL'", procSection);
        Assert.DoesNotContain("ChangedBy,", procSection);
        Assert.Contains("ChangedByEmployeeID, ChangedByAdminID", procSection);

        var authService = File.ReadAllText(Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Infrastructure", "Services", "SqlAuthService.cs"));
        Assert.Contains("CLK-", authService);
        Assert.Contains("LIKE '%CLERK%'", authService);
    }
}
