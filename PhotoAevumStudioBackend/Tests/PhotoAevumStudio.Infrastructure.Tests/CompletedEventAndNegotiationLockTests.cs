using PhotoAevumStudio.Application.Models;
using PhotoAevumStudio.Infrastructure.Services;
using Xunit;

namespace PhotoAevumStudio.Infrastructure.Tests;

public class CompletedEventAndNegotiationLockTests
{
    [Theory]
    [InlineData("Completed", "")]
    [InlineData("Complete", "")]
    [InlineData("Delivered", "")]
    [InlineData("Shot", "")]
    [InlineData("Cancelled", "")]
    [InlineData("Expired", "")]
    [InlineData("Deal_Lost", "")]
    [InlineData("Post Event", "Paid")]
    [InlineData("Confirmed", "Payment Verification (QR:Paid)")]
    public void EnsureEventModifiable_Throws_WhenEventIsCompletedOrInactive(string eventStatus, string finalPaymentStatus)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConsultationNegotiationService.EnsureEventModifiable(eventStatus, finalPaymentStatus, "EVT-1001", "roster"));

        Assert.Contains("EVT-1001", ex.Message);
    }

    [Theory]
    [InlineData("Confirmed", "Pending")]
    [InlineData("Pending Payment", "")]
    [InlineData("Preparation", "Pending")]
    [InlineData("Ready", "")]
    public void EnsureEventModifiable_Allows_ActiveNonCompletedEvents(string eventStatus, string finalPaymentStatus)
    {
        var exception = Record.Exception(() =>
            ConsultationNegotiationService.EnsureEventModifiable(eventStatus, finalPaymentStatus, "EVT-1002", "roster"));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("Deal_Won", null)]
    [InlineData("Appointment Booked", null)]
    [InlineData("Pending Payment", null)]
    [InlineData("Pending_Payment", null)]
    [InlineData("Completed", null)]
    [InlineData("Pending_Call", "EVT-2001")]
    [InlineData("Rescheduled", "EVT-2002")]
    [InlineData("Deal_Lost", null)]
    [InlineData("Cancelled", null)]
    public void EnsureConsultationOutcomeModifiable_Throws_WhenNegotiationSucceededOrEnded(string status, string? linkedEventId)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConsultationNegotiationService.EnsureConsultationOutcomeModifiable(status, linkedEventId, "NEG-0001"));

        Assert.Contains("NEG-0001", ex.Message);
    }

    [Theory]
    [InlineData("Pending_Call", null)]
    [InlineData("Rescheduled", "")]
    public void EnsureConsultationOutcomeModifiable_Allows_PendingOrRescheduledWithoutLinkedEvent(string status, string? linkedEventId)
    {
        var exception = Record.Exception(() =>
            ConsultationNegotiationService.EnsureConsultationOutcomeModifiable(status, linkedEventId, "NEG-0002"));

        Assert.Null(exception);
    }
}
