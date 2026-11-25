using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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

public class ZoomMeetingServiceTests
{
    private static ZoomSettings CreateConfiguredSettings() => new()
    {
        AccountId = "acct-123",
        ClientId = "client-abc",
        ClientSecret = "super-secret-xyz",
        WebhookSecretToken = "webhook-secret"
    };

    [Fact]
    public async Task GetAccessTokenAsync_RequestsBasicAuthAndCachesUntilPreExpiryBuffer()
    {
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://zoom.us/oauth/token", req.RequestUri?.ToString());
            Assert.Equal("Basic", req.Headers.Authorization?.Scheme);
            var expectedBasic = Convert.ToBase64String(Encoding.UTF8.GetBytes("client-abc:super-secret-xyz"));
            Assert.Equal(expectedBasic, req.Headers.Authorization?.Parameter);

            var form = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Contains("grant_type=account_credentials", form);
            Assert.Contains("account_id=acct-123", form);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"tok-1","token_type":"bearer","expires_in":3600}""", Encoding.UTF8, "application/json")
            };
        });

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://zoom.us/") };
        using var service = new ZoomTokenService(http, Options.Create(CreateConfiguredSettings()), NullLogger<ZoomTokenService>.Instance, time);

        var t1 = await service.GetAccessTokenAsync();
        time.Advance(TimeSpan.FromMinutes(30));
        var t2 = await service.GetAccessTokenAsync();

        Assert.Equal("tok-1", t1);
        Assert.Equal("tok-1", t2);
        Assert.Equal(1, handler.CallCount);

        // Advance into the 60s pre-expiry refresh window (3540s -> 3541s)
        time.Advance(TimeSpan.FromMinutes(29).Add(TimeSpan.FromSeconds(1)));
        var t3 = await service.GetAccessTokenAsync();

        Assert.Equal("tok-1", t3);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenMissingCredentials_ThrowsClearErrorWithoutSecrets()
    {
        var settings = new ZoomSettings { AccountId = "acct-123" };
        using var http = new HttpClient(new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)))
        {
            BaseAddress = new Uri("https://zoom.us/")
        };
        using var service = new ZoomTokenService(http, Options.Create(settings), NullLogger<ZoomTokenService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAccessTokenAsync());
        Assert.Contains("ZOOM_CLIENT_ID", ex.Message);
        Assert.Contains("ZOOM_CLIENT_SECRET", ex.Message);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenNon2xx_LogsWithoutExposingSecretAndThrowsZoomApiException()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"reason":"Invalid client_id or client_secret","error":"invalid_client"}""", Encoding.UTF8, "application/json")
        });

        var logger = new ListLogger<ZoomTokenService>();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://zoom.us/") };
        using var service = new ZoomTokenService(http, Options.Create(CreateConfiguredSettings()), logger);

        var ex = await Assert.ThrowsAsync<ZoomApiException>(() => service.GetAccessTokenAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.DoesNotContain("super-secret-xyz", ex.Message);
        Assert.All(logger.Messages, msg => Assert.DoesNotContain("super-secret-xyz", msg));
    }

    [Fact]
    public async Task CreateMeetingAsync_PostsScheduledMeetingAndReturnsIdsAndUrls()
    {
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://api.zoom.us/v2/users/me/meetings", req.RequestUri?.ToString());
            Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
            Assert.Equal("cached-access-token", req.Headers.Authorization?.Parameter);
            Assert.Equal("application/json", req.Content?.Headers.ContentType?.MediaType);

            capturedBody = await req.Content!.ReadAsStringAsync(ct);

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""
                {
                  "id": 98765432101,
                  "topic": "Studio Consultation",
                  "start_time": "2026-12-01T10:00:00Z",
                  "duration": 30,
                  "timezone": "UTC",
                  "join_url": "https://us05web.zoom.us/j/98765432101?pwd=abc",
                  "start_url": "https://us05web.zoom.us/s/98765432101?zak=xyz"
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        var tokenMock = new Mock<IZoomTokenService>();
        tokenMock.Setup(t => t.GetAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("cached-access-token");

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.zoom.us/v2/") };
        var service = new ZoomMeetingService(http, tokenMock.Object, Options.Create(CreateConfiguredSettings()), NullLogger<ZoomMeetingService>.Instance);

        var result = await service.CreateMeetingAsync(new CreateZoomMeetingRequest(
            Topic: "Studio Consultation",
            StartTime: "2026-12-01T10:00:00Z"));

        Assert.NotNull(capturedBody);
        using var sentDoc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("Studio Consultation", sentDoc.RootElement.GetProperty("topic").GetString());
        Assert.Equal(2, sentDoc.RootElement.GetProperty("type").GetInt32());
        Assert.Equal("2026-12-01T10:00:00Z", sentDoc.RootElement.GetProperty("start_time").GetString());
        Assert.Equal(30, sentDoc.RootElement.GetProperty("duration").GetInt32());
        Assert.Equal("UTC", sentDoc.RootElement.GetProperty("timezone").GetString());

        Assert.Equal(98765432101L, result.Id);
        Assert.Equal("https://us05web.zoom.us/j/98765432101?pwd=abc", result.JoinUrl);
        Assert.Equal("https://us05web.zoom.us/s/98765432101?zak=xyz", result.StartUrl);
    }

    [Fact]
    public async Task CreateMeetingAsync_WhenForbidden_IncludesScopeHintAndDoesNotLogBearerToken()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"code":4711,"message":"Invalid access token, does not contain scopes:[meeting:write:meeting]."}""", Encoding.UTF8, "application/json")
        });

        var tokenMock = new Mock<IZoomTokenService>();
        tokenMock.Setup(t => t.GetAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("secret-bearer-token-999");

        var logger = new ListLogger<ZoomMeetingService>();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.zoom.us/v2/") };
        var service = new ZoomMeetingService(http, tokenMock.Object, Options.Create(CreateConfiguredSettings()), logger);

        var ex = await Assert.ThrowsAsync<ZoomApiException>(() =>
            service.CreateMeetingAsync(new CreateZoomMeetingRequest("Consultation", "2026-12-01T10:00:00Z")));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Equal(4711, ex.ZoomErrorCode);
        Assert.Contains("meeting:write:meeting", ex.Message);
        Assert.DoesNotContain("secret-bearer-token-999", ex.Message);
        Assert.All(logger.Messages, msg => Assert.DoesNotContain("secret-bearer-token-999", msg));
    }

    [Fact]
    public async Task ZoomAwareMeetingLinkService_UsesZoomWhenConfiguredAndFallsBackWhenUnconfigured()
    {
        var zoomMock = new Mock<IZoomMeetingService>();
        zoomMock.Setup(z => z.CreateMeetingAsync(It.IsAny<CreateZoomMeetingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ZoomMeetingDetails(
                12345678901,
                "https://us05web.zoom.us/j/12345678901?pwd=ok",
                "https://us05web.zoom.us/s/12345678901",
                "Photo Aevum Studio Consultation",
                "2026-12-01T10:00:00Z",
                30,
                "UTC"));

        var configured = new ZoomAwareMeetingLinkService(
            zoomMock.Object,
            Options.Create(CreateConfiguredSettings()),
            NullLogger<ZoomAwareMeetingLinkService>.Instance);

        var realLink = await configured.CreateMeetingLinkAsync(
            MeetingPlatform.Zoom,
            new DateOnly(2026, 12, 1),
            new TimeOnly(10, 0),
            new TimeOnly(10, 30));

        Assert.Equal("https://us05web.zoom.us/j/12345678901?pwd=ok", realLink);
        Assert.True(configured.IsIssuedLink(MeetingPlatform.Zoom, realLink));

        var unconfigured = new ZoomAwareMeetingLinkService(
            zoomMock.Object,
            Options.Create(new ZoomSettings()),
            NullLogger<ZoomAwareMeetingLinkService>.Instance);

        var mockLink = await unconfigured.CreateMeetingLinkAsync(
            MeetingPlatform.Zoom,
            new DateOnly(2026, 12, 1),
            new TimeOnly(10, 0),
            new TimeOnly(10, 30));

        Assert.True(unconfigured.IsIssuedLink(MeetingPlatform.Zoom, mockLink));
        zoomMock.Verify(z => z.CreateMeetingAsync(It.IsAny<CreateZoomMeetingRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> syncResponder)
            : this((req, ct) => Task.FromResult(syncResponder(req, ct)))
        {
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return responder(request, cancellationToken);
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset startUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = startUtc;
        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
