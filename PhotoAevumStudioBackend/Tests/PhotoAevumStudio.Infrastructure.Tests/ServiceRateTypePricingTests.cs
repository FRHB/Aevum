using PhotoAevumStudio.Infrastructure.Services;
using Xunit;

namespace PhotoAevumStudio.Infrastructure.Tests;

public class ServiceRateTypePricingTests
{
    [Theory]
    [InlineData(500.00, "per hour", 1, 3, 3, 1500.00, 3)]
    [InlineData(500.00, "Per Hour", 4, null, 4, 2000.00, 4)]
    [InlineData(120.00, "per picture", 5, null, 5, 600.00, null)]
    [InlineData(250.00, "per single shot", 3, null, 3, 750.00, null)]
    [InlineData(1800.00, "multi-shot", 2, null, 2, 3600.00, null)]
    public void CalculateServiceLinePricing_RespectsRateTypeAcrossServices(
        decimal unitPrice,
        string rateType,
        int quantity,
        int? durationHours,
        int expectedQty,
        decimal expectedLineTotal,
        int? expectedDurationHours)
    {
        var (effectiveQty, lineTotal, effectiveDuration) =
            ConsultationNegotiationService.CalculateServiceLinePricing(unitPrice, rateType, quantity, durationHours);

        Assert.Equal(expectedQty, effectiveQty);
        Assert.Equal(expectedLineTotal, lineTotal);
        Assert.Equal(expectedDurationHours, effectiveDuration);
    }
}
