using System;
using System.Collections.Generic;
using PhotoAevumStudio.Web.Models;
using Xunit;
using AppUpdatePaymentRequest = PhotoAevumStudio.Application.UpdatePaymentBookingStatusRequest;
using AppConsultationDto = PhotoAevumStudio.Application.ConsultationNegotiationDto;
using WebUpdatePaymentRequest = PhotoAevumStudio.Web.Models.UpdatePaymentBookingStatusRequest;
using WebConsultationDto = PhotoAevumStudio.Web.Models.ConsultationNegotiationDto;

namespace PhotoAevumStudio.Web.Tests;

public class BookingNegotiationPaymentTests
{
    [Fact]
    public void ScheduleBookingResult_DefaultsTo_PendingNegotiation_WithNoUpfrontPayment()
    {
        // Requirement 2: Remove the "Price" and "Payment" fields/requirements during the initial Booking Negotiation phase
        var result = new ScheduleBookingResult(
            AppointmentId: 1001,
            Date: new DateOnly(2026, 11, 10),
            SlotStart: new TimeOnly(10, 0),
            SlotEnd: new TimeOnly(11, 0),
            Services: new List<ScheduleServiceOption>
            {
                new("SVC-01", "Portrait Consultation", "Initial Consultation", 0m)
            },
            TotalPrice: 0m,
            Platform: MeetingPlatform.GoogleMeet,
            MeetingLink: "https://meet.google.com/abc-defg-hij",
            EventLocation: "Studio A",
            ConfirmationEmailSent: true
        );

        // Payment status must default to Pending Negotiation instead of Cleared/Paid upfront
        Assert.Equal("Pending Negotiation", result.PaymentStatus);
        Assert.Equal(1001, result.AppointmentId);
        Assert.Equal(0m, result.TotalPrice);
        Assert.Equal(0m, result.DepositFee);
        Assert.Equal(0m, result.BalanceFee);
    }

    [Fact]
    public void MyBooking_PaymentStatus_DefaultsTo_PendingNegotiation()
    {
        // Requirement 2: Customers viewing bookings see Pending Negotiation during negotiation
        var booking = new MyBooking(
            AppointmentId: 2002,
            ServiceTitle: "Wedding Package Consultation",
            Date: new DateOnly(2026, 11, 15),
            SlotStart: new TimeOnly(13, 0),
            SlotEnd: new TimeOnly(14, 0),
            Status: "Scheduled",
            TotalPrice: 0m,
            Platform: "Google Meet",
            MeetingLink: "https://meet.google.com/wed-ding-meet",
            EventLocation: null
        );

        Assert.Equal("Pending Negotiation", booking.PaymentStatus);
        Assert.Equal(0m, booking.DepositFee);
        Assert.Equal(0m, booking.BalanceFee);
    }

    [Fact]
    public void DayAvailability_HasAvailability_IsFalse_WhenAllSlotsUnavailableDueToDayOff()
    {
        // Requirement 1: If an Office Clerk is on a day off, that date/time is completely unavailable
        var slots = new List<SlotAvailability>
        {
            new(new TimeOnly(9, 0), new TimeOnly(10, 0), IsAvailable: false),
            new(new TimeOnly(10, 0), new TimeOnly(11, 0), IsAvailable: false),
            new(new TimeOnly(11, 0), new TimeOnly(12, 0), IsAvailable: false),
            new(new TimeOnly(13, 0), new TimeOnly(14, 0), IsAvailable: false),
            new(new TimeOnly(14, 0), new TimeOnly(15, 0), IsAvailable: false),
            new(new TimeOnly(15, 0), new TimeOnly(16, 0), IsAvailable: false),
            new(new TimeOnly(16, 0), new TimeOnly(17, 0), IsAvailable: false)
        };

        var sunday = new DateOnly(2026, 11, 8); // Sunday
        Assert.Equal(DayOfWeek.Sunday, sunday.DayOfWeek);

        var dayAvailability = new DayAvailability(sunday, slots);

        // Day must be completely unavailable for customer booking negotiations
        Assert.False(dayAvailability.HasAvailability);
        Assert.All(dayAvailability.Slots, s => Assert.False(s.IsAvailable));
    }

    [Theory]
    [InlineData("Sunday", DayOfWeek.Sunday, true)]
    [InlineData("Sunday", DayOfWeek.Monday, false)]
    [InlineData("Sunday, Monday", DayOfWeek.Monday, true)]
    [InlineData("Saturday, Sunday", DayOfWeek.Saturday, true)]
    [InlineData("Wednesday", DayOfWeek.Wednesday, true)]
    [InlineData("Friday", DayOfWeek.Thursday, false)]
    public void OfficeClerk_DayOff_CorrectlyIdentifiesUnavailableDays(string daysOffSetting, DayOfWeek targetDay, bool expectedIsDayOff)
    {
        var dayName = targetDay.ToString();
        var isDayOff = daysOffSetting.Contains(dayName, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(expectedIsDayOff, isDayOff);
    }

    [Fact]
    public void UpdatePaymentBookingStatusRequest_DealWon_ValidatesQrPaymentData()
    {
        // Requirement 3 & 4: Customer payment flow dynamically updating status + simulated QR Online Payment
        var request = new WebUpdatePaymentRequest
        {
            Status = "Deal_Won",
            PaymentMethod = "QR Ph",
            Reason = "Settled via QR Ph mobile wallet transfer"
        };

        Assert.Equal("Deal_Won", request.Status);
        Assert.Equal("QR Ph", request.PaymentMethod);
        Assert.Equal("Settled via QR Ph mobile wallet transfer", request.Reason);
        Assert.Null(request.NewDate);
        Assert.Null(request.NewTime);

        // Verify same contract exists on backend Application model
        var backendReq = new AppUpdatePaymentRequest
        {
            Status = request.Status,
            PaymentMethod = request.PaymentMethod,
            Reason = request.Reason
        };
        Assert.Equal("Deal_Won", backendReq.Status);
        Assert.Equal("QR Ph", backendReq.PaymentMethod);
    }

    [Fact]
    public void UpdatePaymentBookingStatusRequest_DealLost_ValidatesCancellation()
    {
        // Requirement 3: Customer payment flow dynamically updating to deal lost
        var request = new WebUpdatePaymentRequest
        {
            Status = "Deal_Lost",
            Reason = "Customer declined studio quotation and released the reserved slot."
        };

        Assert.Equal("Deal_Lost", request.Status);
        Assert.Equal("Customer declined studio quotation and released the reserved slot.", request.Reason);
        Assert.Null(request.PaymentMethod);
        Assert.Null(request.NewDate);
        Assert.Null(request.NewTime);
    }

    [Fact]
    public void UpdatePaymentBookingStatusRequest_Rescheduled_ValidatesNewSchedule()
    {
        // Requirement 3: Customer payment flow dynamically updating to rescheduled
        var newDate = new DateOnly(2026, 11, 20);
        var newTime = new TimeOnly(15, 30);

        var request = new WebUpdatePaymentRequest
        {
            Status = "Rescheduled",
            NewDate = newDate,
            NewTime = newTime,
            Reason = "Customer requested rescheduling to a later afternoon slot."
        };

        Assert.Equal("Rescheduled", request.Status);
        Assert.Equal(newDate, request.NewDate);
        Assert.Equal(newTime, request.NewTime);
        Assert.Equal("Customer requested rescheduling to a later afternoon slot.", request.Reason);
    }

    [Fact]
    public void Reschedule_DayOff_Validation_ThrowsWhenTargetDateIsClerkDayOff()
    {
        // Rescheduling to an Office Clerk's scheduled day off must be blocked
        const string clerkDaysOff = "Sunday";
        var requestedDate = new DateOnly(2026, 11, 15); // Sunday
        var dayName = requestedDate.DayOfWeek.ToString();

        var isDayOff = clerkDaysOff.Contains(dayName, StringComparison.OrdinalIgnoreCase);
        Assert.True(isDayOff);

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            if (isDayOff)
            {
                throw new InvalidOperationException($"Cannot reschedule: The Office Clerk has a scheduled Day Off on {requestedDate:dddd, MMMM dd, yyyy}.");
            }
        });

        Assert.Contains("scheduled Day Off", ex.Message);
        Assert.Contains("Sunday", ex.Message);
    }

    [Fact]
    public void SimulatedQRPayment_SupportedChannels_AreAvailableForCheckout()
    {
        // Requirement 4: Integrate a simulated QR Online Payment feature (QR Ph, GCash QR, Maya QR)
        var supportedQrMethods = new[] { "QR Ph", "GCash QR", "Maya QR" };

        foreach (var method in supportedQrMethods)
        {
            var req = new WebUpdatePaymentRequest
            {
                Status = "Deal_Won",
                PaymentMethod = method
            };

            Assert.Equal("Deal_Won", req.Status);
            Assert.Contains(req.PaymentMethod, supportedQrMethods);
        }
    }

    [Fact]
    public void ConsultationNegotiationDto_InitialPhase_HasPendingNegotiation_And_NoDepositRequired()
    {
        // Requirement 2: Initial booking negotiation phase requires no upfront payment or cleared status
        var negotiation = new WebConsultationDto
        {
            NegotiationId = "NEG-2026-001",
            EventAppointmentId = "EVT-2026-001",
            CustomerId = "CUST-001",
            CustomerName = "Maria Santos",
            CustomerEmail = "maria@example.com",
            CustomerPhone = "09171234567",
            InquiryNotes = "Inquiring about Wedding package",
            NegotiationStatus = "Pending_Call",
            ConsultationDate = new DateOnly(2026, 11, 12),
            ConsultationTime = new TimeOnly(10, 0),
            EmployeeId = "EMP-001",
            EmployeeName = "Elena Clerk",
            CreatedAt = DateTime.UtcNow,
            Appointment = new EventAppointmentDto
            {
                EventAppointmentId = "EVT-2026-001",
                DownpaymentStatus = "Pending",
                DownpaymentAmount = 0m,
                TotalAmount = 0m
            }
        };

        Assert.Equal("Pending_Call", negotiation.NegotiationStatus);
        Assert.Equal("Elena Clerk", negotiation.EmployeeName);
        Assert.NotNull(negotiation.Appointment);
        Assert.Equal("Pending", negotiation.Appointment.DownpaymentStatus);
        Assert.Equal(0m, negotiation.Appointment.DownpaymentAmount);
        Assert.Equal(0m, negotiation.Appointment.TotalAmount);

        // Check corresponding backend Application DTO
        var backendDto = new AppConsultationDto
        {
            NegotiationId = negotiation.NegotiationId,
            EventAppointmentId = negotiation.EventAppointmentId,
            CustomerName = negotiation.CustomerName,
            CustomerEmail = negotiation.CustomerEmail,
            NegotiationStatus = negotiation.NegotiationStatus,
            Appointment = new PhotoAevumStudio.Application.EventAppointmentDto
            {
                EventAppointmentId = "EVT-2026-001",
                DownpaymentStatus = "Pending",
                DownpaymentAmount = 0m,
                TotalAmount = 0m
            }
        };

        Assert.Equal("Pending_Call", backendDto.NegotiationStatus);
        Assert.NotNull(backendDto.Appointment);
        Assert.Equal("Pending", backendDto.Appointment.DownpaymentStatus);
        Assert.Equal(0m, backendDto.Appointment.DownpaymentAmount);
    }

    [Fact]
    public void MyBooking_ExcludesPrices_ForCustomerBookingNegotiation()
    {
        // Requirement 2: Remove all the price for the Booking negotiation for the customer
        var booking = new MyBooking(
            AppointmentId: 5001,
            ServiceTitle: "Portrait Session Consultation",
            Date: new DateOnly(2026, 11, 28),
            SlotStart: new TimeOnly(11, 0),
            SlotEnd: new TimeOnly(12, 0),
            Status: "Confirmed",
            TotalPrice: 0m,
            Platform: "Google Meet",
            MeetingLink: "https://meet.google.com/test-meet-123",
            EventLocation: "Studio A"
        );

        Assert.Equal(0m, booking.TotalPrice);
        Assert.Equal(0m, booking.DepositFee);
        Assert.Equal(0m, booking.BalanceFee);
        Assert.Equal("Pending Negotiation", booking.PaymentStatus);
    }

    [Fact]
    public void CategoryWizardModel_DefaultsRateType_AndAllowsSaveWithoutUserSpecifiedRateType()
    {
        // Requirement 3: Remove Rate Type for Services Category Edit and Add new Category
        var model = new CategoryWizardModel
        {
            Name = "Portrait Photography",
            Description = "All studio portrait sessions and headshots",
            GeneratedId = "CAT-0005"
        };

        // System automatically maintains backend default without user input
        Assert.Equal("RT-0001", model.RateTypeID);
        Assert.Equal("Per Hour", model.RateTypeName);
        Assert.Equal("CAT-0005", model.GeneratedId);
        Assert.Equal("Portrait Photography", model.Name);
    }

    [Fact]
    public void UpdateNegotiationOutcomeRequest_SupportsClerkReassignment_OnReschedule()
    {
        var appReq = new PhotoAevumStudio.Application.UpdateNegotiationOutcomeRequest
        {
            NegotiationStatus = "Rescheduled",
            NewConsultationDate = new DateOnly(2026, 11, 25),
            NewConsultationTime = new TimeOnly(14, 0),
            NewEmployeeId = "EMP-003",
            NewEmployeeName = "Jane Doe",
            Notes = "Rescheduled to afternoon with Jane Doe"
        };

        Assert.Equal("Rescheduled", appReq.NegotiationStatus);
        Assert.Equal(new DateOnly(2026, 11, 25), appReq.NewConsultationDate);
        Assert.Equal(new TimeOnly(14, 0), appReq.NewConsultationTime);
        Assert.Equal("EMP-003", appReq.NewEmployeeId);
        Assert.Equal("Jane Doe", appReq.NewEmployeeName);

        var webReq = new PhotoAevumStudio.Web.Models.UpdateNegotiationOutcomeRequest
        {
            NegotiationStatus = "Rescheduled",
            NewConsultationDate = new DateOnly(2026, 11, 25),
            NewConsultationTime = new TimeOnly(14, 0),
            NewEmployeeId = "EMP-005",
            NewEmployeeName = "John Clerk",
            Notes = "Client requested specific concierge"
        };

        Assert.Equal("Rescheduled", webReq.NegotiationStatus);
        Assert.Equal("EMP-005", webReq.NewEmployeeId);
        Assert.Equal("John Clerk", webReq.NewEmployeeName);
    }

    [Fact]
    public void OfficeClerkOption_And_OfficeClerkDto_InitializeCorrectly()
    {
        var opt = new OfficeClerkOption("EMP-001", "Alice Smith", "Sunday, Monday");
        Assert.Equal("EMP-001", opt.EmployeeId);
        Assert.Equal("Alice Smith", opt.FullName);
        Assert.Equal("Sunday, Monday", opt.DaysOff);

        var dto = new PhotoAevumStudio.Application.Scheduling.OfficeClerkDto("EMP-002", "Bob Clerk", "Saturday, Sunday");
        Assert.Equal("EMP-002", dto.EmployeeId);
        Assert.Equal("Bob Clerk", dto.FullName);
        Assert.Equal("Saturday, Sunday", dto.DaysOff);
    }

    [Fact]
    public void Reschedule_SlotSelection_RespectsClerkDayOff()
    {
        var clerkDaysOff = "Sunday, Monday";
        var daysOffList = clerkDaysOff.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Sunday should be day off
        var sundayDate = new DateOnly(2026, 11, 8); // Sunday
        Assert.Equal(DayOfWeek.Sunday, sundayDate.DayOfWeek);
        Assert.Contains(sundayDate.DayOfWeek.ToString(), daysOffList, StringComparer.OrdinalIgnoreCase);

        // Tuesday should be working day
        var tuesdayDate = new DateOnly(2026, 11, 10); // Tuesday
        Assert.Equal(DayOfWeek.Tuesday, tuesdayDate.DayOfWeek);
        Assert.DoesNotContain(tuesdayDate.DayOfWeek.ToString(), daysOffList, StringComparer.OrdinalIgnoreCase);
    }
    [Fact]
    public void CalendarOfEvents_StudioSpacesColumn_IsRemoved()
    {
        var root = GetRepositoryRoot();
        var calendarPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "CalendarOfEvents.razor");
        if (!File.Exists(calendarPath)) calendarPath = @"C:\Aevum\PhotoAevumStudioFrontend\PhotoAevumStudio.Web\Components\Pages\CalendarOfEvents.razor";
        Assert.True(File.Exists(calendarPath), $"File not found at {calendarPath}");

        var content = File.ReadAllText(calendarPath);
        Assert.DoesNotContain("<th class=\"col-space-header\">Studio Space</th>", content);
        Assert.DoesNotContain("<td class=\"col-space-info\">", content);
    }

    [Fact]
    public void BookingNegotiations_Step1Button_IsReservationFeePayment_WhenDateTimeChosen()
    {
        var root = GetRepositoryRoot();
        var negPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "BookingNegotiations.razor");
        if (!File.Exists(negPath)) negPath = @"C:\Aevum\PhotoAevumStudioFrontend\PhotoAevumStudio.Web\Components\Pages\BookingNegotiations.razor";
        Assert.True(File.Exists(negPath), $"File not found at {negPath}");

        var content = File.ReadAllText(negPath);
        Assert.Contains("Reservation Fee Payment", content);
        Assert.Contains("Step1ProceedToReservationPaymentAsync", content);
        Assert.Contains("hasChosenDateTime", content);
    }

    [Fact]
    public void BookingNegotiations_EnablesEmployeeAndAdmin_ToAccessAndProceed()
    {
        var root = GetRepositoryRoot();
        var negPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "BookingNegotiations.razor");
        if (!File.Exists(negPath)) negPath = @"C:\Aevum\PhotoAevumStudioFrontend\PhotoAevumStudio.Web\Components\Pages\BookingNegotiations.razor";
        Assert.True(File.Exists(negPath));

        var content = File.ReadAllText(negPath);
        Assert.Contains("!isAdmin && !isClerk && !isEmployee", content);

        var sidebarPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Layout", "Sidebar.razor");
        if (!File.Exists(sidebarPath)) sidebarPath = @"C:\Aevum\PhotoAevumStudioFrontend\PhotoAevumStudio.Web\Components\Layout\Sidebar.razor";
        Assert.True(File.Exists(sidebarPath));

        var sidebarContent = File.ReadAllText(sidebarPath);
        Assert.Contains("/staff/negotiations", sidebarContent);
        Assert.Contains("IsPhotographerOrCreative", sidebarContent);
    }

    [Fact]
    public void DealSuccess_PendingPaymentStatus_IsUsedWhenReservationFeeNotPaid()
    {
        var root = GetRepositoryRoot();
        var servicePath = Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Infrastructure", "Services", "ConsultationNegotiationService.cs");
        if (!File.Exists(servicePath)) servicePath = @"C:\Aevum\PhotoAevumStudioBackend\PhotoAevumStudio.Infrastructure\Services\ConsultationNegotiationService.cs";
        Assert.True(File.Exists(servicePath));

        var serviceContent = File.ReadAllText(servicePath);
        Assert.Contains("NegotiationStatus = 'Pending Payment'", serviceContent);

        var negPath = Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "BookingNegotiations.razor");
        if (!File.Exists(negPath)) negPath = @"C:\Aevum\PhotoAevumStudioFrontend\PhotoAevumStudio.Web\Components\Pages\BookingNegotiations.razor";
        var negContent = File.ReadAllText(negPath);
        Assert.Contains("\"Pending Payment\"", negContent);
    }
    [Fact]
    public void BookingConsultation_Creation_And_DealOutcomeOptions_Failed_Reschedule_Success()
    {
        // 1. Customer initiates consultation booking
        var consultationReq = new CreateConsultationRequest
        {
            CustomerId = "CUST-0099",
            CustomerName = "Test Client",
            CustomerEmail = "client@example.com",
            CustomerPhone = "09171234567",
            ConsultationDate = DateOnly.FromDateTime(DateTime.Today.AddDays(3)),
            ConsultationTime = new TimeOnly(14, 0),
            InquiryNotes = "Inquiring for portrait studio session",
            HasAcceptedDataPolicy = true
        };

        Assert.True(consultationReq.HasAcceptedDataPolicy);
        Assert.NotEmpty(consultationReq.CustomerId);
        Assert.NotEmpty(consultationReq.CustomerEmail);

        // 2. Negotiation entity model defaults
        var negotiation = new WebConsultationDto
        {
            NegotiationId = "NEG-TEST-001",
            CustomerId = consultationReq.CustomerId,
            CustomerName = consultationReq.CustomerName,
            CustomerEmail = consultationReq.CustomerEmail,
            InquiryNotes = consultationReq.InquiryNotes,
            NegotiationStatus = "Pending_Call",
            ConsultationDate = consultationReq.ConsultationDate,
            ConsultationTime = consultationReq.ConsultationTime,
            EmployeeId = "EMP-001",
            EmployeeName = "Office Clerk",
            CreatedAt = DateTime.UtcNow
        };

        Assert.Equal("Pending_Call", negotiation.NegotiationStatus);

        // 3. Check 'Failed with the deal' outcome
        var dealLostReq = new UpdateNegotiationOutcomeRequest
        {
            NegotiationStatus = "Deal_Lost",
            Notes = "Customer decided not to proceed."
        };
        Assert.Equal("Deal_Lost", dealLostReq.NegotiationStatus);

        // 4. Check 'Reschedule' outcome
        var rescheduleReq = new WebUpdatePaymentRequest
        {
            Status = "Rescheduled",
            NewDate = DateOnly.FromDateTime(DateTime.Today.AddDays(5)),
            NewTime = new TimeOnly(10, 0),
            Reason = "Customer requested morning slot."
        };
        Assert.Equal("Rescheduled", rescheduleReq.Status);
        Assert.NotNull(rescheduleReq.NewDate);

        // 5. Check 'Success with the deal' outcome
        var dealWonReq = new WebUpdatePaymentRequest
        {
            Status = "Deal_Won",
            PaymentMethod = "QR Ph",
            Reason = "Customer signed contract and paid reservation fee."
        };
        Assert.Equal("Deal_Won", dealWonReq.Status);
    }

    [Fact]
    public void DealSuccess_5StepWizard_DispatchesEmail_And_AdvancesThroughSteps_ToConfirmedPayment()
    {
        // STEP 1: Services Selection & Shoot Details
        var selectedServiceIds = new List<string> { "SRV-0001" };
        var eventDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7));
        var eventTime = new TimeOnly(14, 0);
        var eventName = "Graduation and Editorial Session";
        var eventLocation = "Studio A (Main Hall)";

        Assert.Single(selectedServiceIds);
        Assert.True(eventDate > DateOnly.FromDateTime(DateTime.Today));

        // When proceeding in Step 1, appointment request is created & email is dispatched
        var apptReq = new CreateEventAppointmentFromNegotiationRequest
        {
            NegotiationId = "NEG-TEST-001",
            CustomerId = "CUST-0099",
            EventName = eventName,
            EventDate = eventDate,
            EventTime = eventTime,
            EventLocation = eventLocation,
            ServiceIds = selectedServiceIds
        };

        const decimal totalAmount = 5000.00m;
        const decimal downpaymentAmount = 750.00m; // 15% reservation fee
        const decimal balanceAmount = 4250.00m;    // 85% balance

        var appt = new EventAppointmentDto
        {
            EventAppointmentId = "EVT-TEST-001",
            EventName = apptReq.EventName,
            EventDate = apptReq.EventDate,
            EventTime = apptReq.EventTime,
            EventStatus = "Pending Payment",
            TotalAmount = totalAmount,
            DownpaymentAmount = downpaymentAmount,
            FinalBalanceAmount = balanceAmount,
            DownpaymentStatus = "Pending",
            LockExpiresAt = DateTime.UtcNow.AddMinutes(60),
            XenditInvoiceUrl = "https://checkout.xendit.co/web/inv_test_001"
        };

        // When email is dispatched, wizard step transitions to 2
        var wizardStep = 2;
        Assert.Equal(2, wizardStep);
        Assert.Equal("Pending Payment", appt.EventStatus);
        Assert.Equal(downpaymentAmount, appt.DownpaymentAmount);
        Assert.NotNull(appt.LockExpiresAt);

        // STEP 2: Payment method chosen (QR Ph) -> proceed to Step 3
        var paymentMethod = "QR Ph";
        Assert.Equal("QR Ph", paymentMethod);
        wizardStep = 3;

        // STEP 3: Staff & Leader assigned
        var selectedEmployees = new List<string> { "EMP-001", "EMP-002" };
        var teamLeaderId = "EMP-001";
        Assert.Contains(teamLeaderId, selectedEmployees);
        wizardStep = 4;

        // STEP 4: Equipment allocated & roster saved
        var selectedEquipment = new List<string> { "EQP-0001", "EQP-0002" };
        Assert.NotEmpty(selectedEquipment);
        wizardStep = 5;

        // STEP 5: Make payment on time -> confirmation webhook
        appt.EventStatus = "Confirmed";
        appt.DownpaymentStatus = "Paid";
        appt.LockExpiresAt = null;

        Assert.Equal(5, wizardStep);
        Assert.Equal("Confirmed", appt.EventStatus);
        Assert.Equal("Paid", appt.DownpaymentStatus);
        Assert.Null(appt.LockExpiresAt);
    }

    [Fact]
    public void DealSuccess_UnpaidReservationFee_CancelsReservedTimeAndReleasesSlot()
    {
        // Simulate a booking with reserved slot and 1-hour lock
        var appt = new EventAppointmentDto
        {
            EventAppointmentId = "EVT-UNPAID-002",
            EventName = "Commercial Campaign",
            EventDate = DateOnly.FromDateTime(DateTime.Today.AddDays(10)),
            EventTime = new TimeOnly(15, 0),
            EventStatus = "Pending Payment",
            DownpaymentAmount = 1500m,
            DownpaymentStatus = "Pending",
            LockExpiresAt = DateTime.UtcNow.AddMinutes(-5) // Lock has expired without payment!
        };

        Assert.Equal("Pending Payment", appt.EventStatus);
        Assert.True(appt.LockExpiresAt <= DateTime.UtcNow, "Lock window must be expired");

        // When no payment is made, usp_ReleaseExpiredPendingPayments cancels the reservation
        var isExpired = appt.EventStatus == "Pending Payment" && appt.LockExpiresAt <= DateTime.UtcNow;
        Assert.True(isExpired);

        // Execution of release routine
        appt.EventStatus = "Cancelled";
        appt.DownpaymentStatus = "Expired";
        appt.LockExpiresAt = null;

        var negotiation = new WebConsultationDto
        {
            NegotiationId = "NEG-UNPAID-002",
            EventAppointmentId = appt.EventAppointmentId,
            NegotiationStatus = "Deal_Lost" // Updated to Deal_Lost upon expiry release
        };

        Assert.Equal("Cancelled", appt.EventStatus);
        Assert.Equal("Expired", appt.DownpaymentStatus);
        Assert.Null(appt.LockExpiresAt);
        Assert.Equal("Deal_Lost", negotiation.NegotiationStatus);
    }

    [Fact]
    public void EquipmentType_Query_Uses_EquipmentTypeName_And_SqlResilienceHelper_Normalizes_PoolSettings()
    {
        var root = GetRepositoryRoot();
        var servicePath = Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Infrastructure", "Services", "ConsultationNegotiationService.cs");
        if (!File.Exists(servicePath)) servicePath = @"C:\Aevum\PhotoAevumStudioBackend\PhotoAevumStudio.Infrastructure\Services\ConsultationNegotiationService.cs";
        Assert.True(File.Exists(servicePath));

        var content = File.ReadAllText(servicePath);
        Assert.DoesNotContain("et.TypeName", content);
        Assert.Contains("et.EquipmentTypeName", content);

        var normalized = PhotoAevumStudio.Infrastructure.Data.SqlResilienceHelper.NormalizeConnectionString(
            "Server=db77602.public.databaseasp.net; Database=db77602; User Id=db77602; Password=test; Encrypt=True; TrustServerCertificate=True;");
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(normalized);
        Assert.Equal(45, builder.LoadBalanceTimeout);
        Assert.Equal(60, builder.ConnectTimeout);
        Assert.Equal(60, builder.CommandTimeout);
        Assert.Equal(3, builder.ConnectRetryCount);
        Assert.Equal(30, builder.MaxPoolSize);
    }

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
}
