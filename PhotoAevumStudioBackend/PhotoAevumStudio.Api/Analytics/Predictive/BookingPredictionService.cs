namespace PhotoAevumStudio.Api.Analytics.Predictive;

/// <summary>Predictive analytics skeleton. A Microsoft.ML (MLContext) model will replace the dummy value.</summary>
public class BookingPredictionService
{
    private readonly ILogger<BookingPredictionService> _logger;

    public BookingPredictionService(ILogger<BookingPredictionService> logger)
    {
        _logger = logger;
    }

    public float PredictNoShowProbability(int bookingId)
    {
        const float probability = 0.15f;
        _logger.LogInformation(
            "No-show prediction requested for booking {BookingId}; returning {Probability}",
            bookingId, probability);
        return probability;
    }
}
