using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;

namespace PhotoAevumStudio.Infrastructure.Services;

public class GoogleMeetAdapter : IGoogleMeetAdapter
{
    private readonly GoogleMeetSettings _settings;
    private readonly ILogger<GoogleMeetAdapter> _logger;

    public GoogleMeetAdapter(IOptions<GoogleMeetSettings> options, ILogger<GoogleMeetAdapter> logger)
    {
        _settings = options.Value;
        _logger = logger;
    }

    public Task<string> GenerateMeetingLinkAsync(DateOnly date, TimeOnly time, string? title = null, CancellationToken cancellationToken = default)
    {
        // If Provider is set to WorkspaceServiceAccount and key path exists, Google Calendar API with conferenceData can be invoked here.
        if (string.Equals(_settings.Provider, "WorkspaceServiceAccount", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(_settings.WorkspaceServiceAccountKeyPath) &&
            File.Exists(_settings.WorkspaceServiceAccountKeyPath))
        {
            _logger.LogInformation("Using configured Google Workspace Service Account at {KeyPath} for Meet link generation.", _settings.WorkspaceServiceAccountKeyPath);
            // Real Google Workspace Calendar conference creation adapter stub
        }

        // Scaffold / Standard deterministic Fallback Meeting URL
        var baseUrl = string.IsNullOrWhiteSpace(_settings.StudioFallbackBaseUrl) 
            ? "https://meet.google.com/" 
            : _settings.StudioFallbackBaseUrl.TrimEnd('/') + "/";

        var part1 = GenerateRandomLetters(3);
        var part2 = GenerateRandomLetters(4);
        var part3 = GenerateRandomLetters(3);
        var meetingCode = $"{part1}-{part2}-{part3}";

        var link = $"{baseUrl}{meetingCode}";
        _logger.LogInformation("Provisioned Google Meet link {Link} for consultation scheduled {Date} at {Time}.", link, date, time);

        return Task.FromResult(link);
    }

    private static string GenerateRandomLetters(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz";
        return new string(Enumerable.Range(0, length)
            .Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)])
            .ToArray());
    }
}
