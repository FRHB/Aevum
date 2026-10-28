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
}
