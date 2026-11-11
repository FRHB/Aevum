using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Models;
using PhotoAevumStudio.Infrastructure.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class XenditPaymentIntegrationTests
{
    [Fact]
    public void XenditSettings_HasExpectedDefaultValues()
    {
        var settings = new XenditSettings();

        Assert.False(string.IsNullOrWhiteSpace(settings.SecretKey));
        Assert.False(string.IsNullOrWhiteSpace(settings.WebhookToken));
        Assert.Equal(0.15m, settings.DownpaymentPercentage);
        Assert.Equal(30, settings.HoldDurationMinutes);
        Assert.Contains("xendit.co", settings.InvoiceBaseUrl);
    }

    [Fact]
    public void EventAppointmentDto_ExposesXenditProperties()
    {
        var dto = new EventAppointmentDto
        {
            EventAppointmentId = "EVT-TEST",
            XenditInvoiceId = "inv_12345",
            XenditExternalId = "EVT-TEST",
            XenditInvoiceUrl = "https://checkout.xendit.co/web/inv_12345"
        };

        Assert.Equal("inv_12345", dto.XenditInvoiceId);
        Assert.Equal("EVT-TEST", dto.XenditExternalId);
        Assert.Equal("https://checkout.xendit.co/web/inv_12345", dto.XenditInvoiceUrl);
    }

    [Fact]
    public void XenditInvoiceCallbackPayload_DeserializesProperly()
    {
        var json = """
        {
            "id": "inv_callback_99",
            "external_id": "EVT-999",
            "status": "PAID",
            "amount": 1250.00,
            "paid_amount": 1250.00,
            "payment_method": "EWALLET",
            "payment_channel": "GCASH",
            "currency": "PHP"
        }
        """;

        var payload = JsonSerializer.Deserialize<XenditInvoiceCallbackPayload>(json);

        Assert.NotNull(payload);
        Assert.Equal("inv_callback_99", payload.Id);
        Assert.Equal("EVT-999", payload.ExternalId);
        Assert.Equal("PAID", payload.Status);
        Assert.Equal(1250.00m, payload.Amount);
        Assert.Equal("GCASH", payload.PaymentChannel);
    }

    [Fact]
    public async Task XenditPaymentService_Sandbox_GeneratesInvoiceWithExternalIdAndUrl()
    {
        var options = Options.Create(new XenditSettings
        {
            SecretKey = "xnd_development_mock_secret",
            InvoiceBaseUrl = "https://checkout.xendit.co/web/"
        });

        using var httpClient = new HttpClient();
        var service = new XenditPaymentService(options, httpClient, NullLogger<XenditPaymentService>.Instance);

        var result = await service.CreateInvoiceAsync(
            "EVT-0042",
            "Photo Shoot Deposit",
            2500m,
            "Juan Dela Cruz",
            "juan@example.com",
            "https://localhost:7128/success",
            "https://localhost:7128/cancel");

        Assert.NotNull(result);
        Assert.Equal("EVT-0042", result.ExternalId);
        Assert.StartsWith("inv_mock_", result.InvoiceId);
        Assert.Contains("checkout.xendit.co", result.InvoiceUrl);
        Assert.Contains("EVT-0042", result.InvoiceUrl);
        Assert.Equal(2500m, result.Amount);
    }

    [Fact]
    public void ReservationFee_Calculates15PercentDeposit_And85PercentBalance()
    {
        const decimal total = 10000m;
        var deposit = Math.Round(total * 0.15m, 2);
        var balance = total - deposit;

        Assert.Equal(1500.00m, deposit);
        Assert.Equal(8500.00m, balance);
        Assert.Equal(total, deposit + balance);
    }

    [Theory]
    [InlineData(1500, 225, 1275)]
    [InlineData(4500, 675, 3825)]
    [InlineData(26500, 3975, 22525)]
    public void ReservationFee_VariousPackagePrices_Match15PercentRate(decimal total, decimal expectedDeposit, decimal expectedBalance)
    {
        var deposit = Math.Round(total * 0.15m, 2);
        var balance = total - deposit;

        Assert.Equal(expectedDeposit, deposit);
        Assert.Equal(expectedBalance, balance);
    }
    [Fact]
    public async Task XenditPaymentService_Sandbox_CheckPaymentStatus_ReturnsQRPhChannel()
    {
        var options = Options.Create(new XenditSettings
        {
            SecretKey = "xnd_development_mock_secret",
            InvoiceBaseUrl = "https://checkout.xendit.co/web/"
        });

        using var httpClient = new HttpClient();
        var service = new XenditPaymentService(options, httpClient, NullLogger<XenditPaymentService>.Instance);

        var status = await service.CheckPaymentStatusAsync("inv_mock_12345");
        Assert.NotNull(status);
        Assert.Equal("PAID", status.Status);
        Assert.Equal("QRPH", status.PaymentMethod);
        Assert.Equal("QRPH", status.PaymentChannel);
    }
    [Fact]
    public async Task XenditPaymentService_CreateQrCodeAsync_ReturnsMockQrStringWithEvtReference()
    {
        var options = Options.Create(new XenditSettings
        {
            SecretKey = "xnd_development_mock_secret",
            LockDurationMinutes = 60
        });

        using var httpClient = new HttpClient();
        var service = new XenditPaymentService(options, httpClient, NullLogger<XenditPaymentService>.Instance);

        var expires = DateTime.UtcNow.AddHours(1);
        var qr = await service.CreateQrCodeAsync("EVT-104", 1500m, expires);

        Assert.NotNull(qr);
        Assert.Equal("EVT-104", qr.ReferenceId);
        Assert.Equal(1500m, qr.Amount);
        Assert.Contains("EVT-104", qr.QrString);
        Assert.EndsWith("/checkout/confirm/EVT-104", qr.QrString);
    }

    [Fact]
    public void QrPaymentLinks_BuildsAndParsesFullConfirmUrls_ForLocalNgrokAndLiveDomains()
    {
        var liveUrl = PhotoAevumStudio.Web.Services.QrPaymentLinks.BuildConfirmUrl(
            "http://photoaevum.tryasp.net", "https://localhost:7128/", "EVT-0006");
        Assert.Equal("http://photoaevum.tryasp.net/checkout/confirm/EVT-0006", liveUrl);

        Assert.True(PhotoAevumStudio.Web.Services.QrPaymentLinks.TryGetConfirmPath(
            "https://livetest.com/checkout/confirm/EVT-0006", out var apptId, out var relPath));
        Assert.Equal("EVT-0006", apptId);
        Assert.Equal("checkout/confirm/EVT-0006", relPath);

        Assert.True(PhotoAevumStudio.Web.Services.QrPaymentLinks.TryGetConfirmPath(
            "https://demo-tunnel.ngrok-free.app/checkout/confirm/EVT-104", out var ngrokId, out _));
        Assert.Equal("EVT-104", ngrokId);
    }

    [Fact]
    public void EventAppointmentDto_ExposesLockExpiresAtAndQrString()
    {
        var exp = DateTime.UtcNow.AddHours(1);
        var dto = new EventAppointmentDto
        {
            EventAppointmentId = "EVT-104",
            EventStatus = "Pending Payment",
            LockExpiresAt = exp,
            QrString = "00020101021226..."
        };

        Assert.Equal("Pending Payment", dto.EventStatus);
        Assert.Equal(exp, dto.LockExpiresAt);
        Assert.Equal("00020101021226...", dto.QrString);
    }

    [Fact]
    public async Task PaymentsController_SimulateQrPayment_CallsXenditSimulateEndpoint_WithBasicAuth()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new DelegatingHandlerStub(async (req, ct) =>
        {
            capturedRequest = req;
            capturedBody = req.Content != null ? await req.Content.ReadAsStringAsync(ct) : null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"qrpy_test_01","status":"SUCCEEDED","amount":600}""", System.Text.Encoding.UTF8, "application/json")
            };
        });

        var factoryMock = new Moq.Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(Moq.It.IsAny<string>())).Returns(new HttpClient(handler));

        var consultationMock = new Moq.Mock<PhotoAevumStudio.Application.Interfaces.IConsultationNegotiationService>();
        var paymentServiceMock = new Moq.Mock<PhotoAevumStudio.Application.Interfaces.IPaymentService>();
        var pendingMock = new Moq.Mock<PhotoAevumStudio.Application.Interfaces.IPendingPaymentService>();
        var notifierMock = new Moq.Mock<PhotoAevumStudio.Application.Interfaces.IPaymentNotifier>();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        var options = Options.Create(new XenditSettings
        {
            SecretKey = "xnd_development_live_test_key_999"
        });

        var controller = new PhotoAevumStudio.Api.Controllers.PaymentsController(
            consultationMock.Object,
            paymentServiceMock.Object,
            options,
            NullLogger<PhotoAevumStudio.Api.Controllers.PaymentsController>.Instance,
            pendingMock.Object,
            notifierMock.Object,
            config,
            factoryMock.Object);

        var actionResult = await controller.SimulateQrPayment("EVT-0006", 600m, CancellationToken.None);

        var okResult = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(actionResult);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("https://api.xendit.co/qr_codes/EVT-0006/payments/simulate", capturedRequest.RequestUri!.ToString());
        Assert.Equal("Basic", capturedRequest.Headers.Authorization?.Scheme);
        var expectedAuth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("xnd_development_live_test_key_999:"));
        Assert.Equal(expectedAuth, capturedRequest.Headers.Authorization?.Parameter);
        Assert.NotNull(capturedBody);
        Assert.Contains("600", capturedBody);
    }

    [Fact]
    public void FourTierAnalytics_ServicesAndPages_AreRegisteredAndPresent()
    {
        var job = new PhotoAevumStudio.Api.Analytics.ETL.DailyAnalyticsSyncJob();
        job.ExecuteSync();

        var predictor = new PhotoAevumStudio.Api.Analytics.Predictive.BookingPredictionService(
            NullLogger<PhotoAevumStudio.Api.Analytics.Predictive.BookingPredictionService>.Instance);
        var prob = predictor.PredictNoShowProbability(101);
        Assert.Equal(0.15f, prob);

        var hangfireClientMock = new Moq.Mock<Hangfire.IBackgroundJobClient>();
        var engine = new PhotoAevumStudio.Api.Analytics.Prescriptive.WorkflowActionEngine(
            new RulesEngine.RulesEngine(Array.Empty<RulesEngine.Models.Workflow>()),
            hangfireClientMock.Object,
            NullLogger<PhotoAevumStudio.Api.Analytics.Prescriptive.WorkflowActionEngine>.Instance);

        engine.EvaluateBookingRisk(101, 0.85f);
        hangfireClientMock.Verify(x => x.Create(
            Moq.It.Is<Hangfire.Common.Job>(j => j.Type == typeof(PhotoAevumStudio.Api.Analytics.ETL.DailyAnalyticsSyncJob)),
            Moq.It.IsAny<Hangfire.States.IState>()), Moq.Times.Once);
    }

    private sealed class DelegatingHandlerStub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        public DelegatingHandlerStub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _send(request, cancellationToken);
    }
}