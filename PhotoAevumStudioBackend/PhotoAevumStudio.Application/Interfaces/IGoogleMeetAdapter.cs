namespace PhotoAevumStudio.Application.Interfaces;

public interface IGoogleMeetAdapter
{
    Task<string> GenerateMeetingLinkAsync(DateOnly date, TimeOnly time, string? title = null, CancellationToken cancellationToken = default);
}
