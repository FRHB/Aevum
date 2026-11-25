using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;
using PhotoAevumStudio.Application.Scheduling;
using PhotoAevumStudio.Infrastructure.Scheduling;
using PhotoAevumStudio.Infrastructure.Services;
using Xunit;

namespace PhotoAevumStudio.Infrastructure.Tests;

public class GoogleMeetAdapterTests
{
    [Fact]
    public async Task GenerateMeetingLinkAsync_WithOAuthRefreshToken_CallsGoogleCalendarAndReturnsHangoutLink()
    {
        var requests = new List<(HttpMethod Method, string Url, string Body)>();

        var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            var body = req.Content is null ? string.Empty : await req.Content.ReadAsStringAsync(ct);
            requests.Add((req.Method, req.RequestUri!.ToString(), body));

            if (req.RequestUri.Host == "oauth2.googleapis.com")
            {
                return JsonResponse(HttpStatusCode.OK, """{"access_token":"ya29.test-token","expires_in":3600}""");
            }

            if (req.RequestUri.Host == "www.googleapis.com")
            {
                Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
                Assert.Equal("ya29.test-token", req.Headers.Authorization?.Parameter);
                Assert.Contains("conferenceDataVersion=1", req.RequestUri.Query);
                Assert.Contains("hangoutsMeet", body);

                return JsonResponse(HttpStatusCode.OK, """
                {
                  "id": "evt_123",
                  "hangoutLink": "https://meet.google.com/real-meet-url"
                }
                """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var settings = new GoogleMeetSettings
        {
            Provider = "GoogleCalendar",
            ClientId = "client-id.apps.googleusercontent.com",
            ClientSecret = "client-secret",
            RefreshToken = "1//test-refresh-token",
            CalendarId = "primary",
            DefaultDurationMinutes = 45,
            TimeZone = "Asia/Manila"
        };

        var adapter = new GoogleMeetAdapter(
            new HttpClient(handler),
            Options.Create(settings),
            NullLogger<GoogleMeetAdapter>.Instance);

        var link = await adapter.GenerateMeetingLinkAsync(
            new DateOnly(2026, 12, 1),
            new TimeOnly(14, 30),
            "Wedding Consultation");

        Assert.Equal("https://meet.google.com/real-meet-url", link);
        Assert.Equal(2, requests.Count);
        Assert.Equal("https://oauth2.googleapis.com/token", requests[0].Url);
        Assert.Contains("refresh_token=1%2F%2Ftest-refresh-token", requests[0].Body);
        Assert.Contains("/calendars/primary/events?conferenceDataVersion=1", requests[1].Url);
    }

    [Fact]
    public async Task GenerateMeetingLinkAsync_WithServiceAccountKey_SignsJwtAndReturnsConferenceEntryPoint()
    {
        using var rsa = RSA.Create(2048);
        var privateKeyPem = rsa.ExportPkcs8PrivateKeyPem();
        var keyFilePath = Path.Combine(Path.GetTempPath(), $"gmeet-sa-{Guid.NewGuid():N}.json");

        try
        {
            var keyPayload = JsonSerializer.Serialize(new
            {
                client_email = "studio-bot@test-project.iam.gserviceaccount.com",
                private_key = privateKeyPem,
                token_uri = "https://oauth2.googleapis.com/token"
            });
            await File.WriteAllTextAsync(keyFilePath, keyPayload);

            var handler = new StubHttpMessageHandler(async (req, ct) =>
            {
                var body = req.Content is null ? string.Empty : await req.Content.ReadAsStringAsync(ct);

                if (req.RequestUri!.Host == "oauth2.googleapis.com")
                {
                    Assert.Contains("jwt-bearer", body);
                    return JsonResponse(HttpStatusCode.OK, """{"access_token":"ya29.sa-token"}""");
                }

                return JsonResponse(HttpStatusCode.OK, """
                {
                  "id": "evt_456",
                  "conferenceData": {
                    "entryPoints": [
                      { "entryPointType": "video", "uri": "https://meet.google.com/abc-wxyz-123" }
                    ]
                  }
                }
                """);
            });

            var settings = new GoogleMeetSettings
            {
                Provider = "WorkspaceServiceAccount",
                WorkspaceServiceAccountKeyPath = keyFilePath,
                ImpersonatedUserEmail = "clerk@photoaevum.com"
            };

            var adapter = new GoogleMeetAdapter(
                new HttpClient(handler),
                Options.Create(settings),
                NullLogger<GoogleMeetAdapter>.Instance);

            var link = await adapter.GenerateMeetingLinkAsync(
                new DateOnly(2026, 12, 5),
                new TimeOnly(10, 0));

            Assert.Equal("https://meet.google.com/abc-wxyz-123", link);
        }
        finally
        {
            if (File.Exists(keyFilePath))
                File.Delete(keyFilePath);
        }
    }

    [Fact]
    public async Task GenerateMeetingLinkAsync_WhenUnconfigured_FallsBackToScaffoldUrl()
    {
        var adapter = new GoogleMeetAdapter(
            new HttpClient(new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("Should not call HTTP when unconfigured"))),
            Options.Create(new GoogleMeetSettings { Provider = "GoogleCalendar" }),
            NullLogger<GoogleMeetAdapter>.Instance);

        var link = await adapter.GenerateMeetingLinkAsync(new DateOnly(2026, 12, 1), new TimeOnly(9, 0));

        Assert.Matches(@"^https://meet\.google\.com/[a-z]{3}-[a-z]{4}-[a-z]{3}$", link);
    }

    [Fact]
    public async Task ZoomAwareMeetingLinkService_DelegatesGoogleMeetToAdapter()
    {
        var zoomMock = new Mock<IZoomMeetingService>();
        var meetMock = new Mock<IGoogleMeetAdapter>();
        meetMock.Setup(m => m.GenerateMeetingLinkAsync(
                new DateOnly(2026, 12, 1),
                new TimeOnly(10, 0),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://meet.google.com/real-gmeet-999");

        var service = new ZoomAwareMeetingLinkService(
            zoomMock.Object,
            Options.Create(new ZoomSettings()),
            NullLogger<ZoomAwareMeetingLinkService>.Instance,
            meetMock.Object);

        var link = await service.CreateMeetingLinkAsync(
            MeetingPlatform.GoogleMeet,
            new DateOnly(2026, 12, 1),
            new TimeOnly(10, 0),
            new TimeOnly(10, 30));

        Assert.Equal("https://meet.google.com/real-gmeet-999", link);
        Assert.True(service.IsIssuedLink(MeetingPlatform.GoogleMeet, link));
        meetMock.VerifyAll();
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
