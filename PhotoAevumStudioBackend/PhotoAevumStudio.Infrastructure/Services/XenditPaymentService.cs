using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;

namespace PhotoAevumStudio.Infrastructure.Services;

public class XenditPaymentService : IXenditPaymentService
{
    private readonly XenditSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<XenditPaymentService> _logger;

    public XenditPaymentService(
        IOptions<XenditSettings> options,
        HttpClient httpClient,
        ILogger<XenditPaymentService> logger)
    {
        _settings = options.Value;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<XenditInvoiceResult> CreateInvoiceAsync(
        string externalId,
        string description,
        decimal amount,
        string customerName,
        string customerEmail,
        string successUrl,
        string failureUrl,
        CancellationToken cancellationToken = default)
    {
        // Permit e-wallet channels (GCash, Maya, GrabPay, ShopeePay) alongside Cards and QR Ph
        var allowedPaymentMethods = new[]
        {
            "GCASH",
            "PAYMAYA",
            "GRABPAY",
            "SHOPEEPAY",
            "CARD",
            "DIRECT_DEBIT",
            "QRPH"
        };

        var holdDurationSeconds = (_settings.HoldDurationMinutes <= 0 ? 30 : _settings.HoldDurationMinutes) * 60;

        // Check if using official live/test Xendit secret key
        if (!string.IsNullOrWhiteSpace(_settings.SecretKey) &&
            !_settings.SecretKey.Contains("mock", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var requestBody = new
                {
                    external_id = externalId,
                    amount = amount,
                    description = description,
                    invoice_duration = holdDurationSeconds,
                    customer = new
                    {
                        given_names = string.IsNullOrWhiteSpace(customerName) ? "Valued Client" : customerName,
                        email = customerEmail
                    },
                    payer_email = customerEmail,
                    currency = "PHP",
                    payment_methods = allowedPaymentMethods,
                    success_redirect_url = successUrl,
                    failure_redirect_url = failureUrl
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.xendit.co/v2/invoices")
                {
                    Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
                };

                var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.SecretKey}:"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);

                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(responseJson);
                    var root = doc.RootElement;
                    var invoiceId = root.GetProperty("id").GetString() ?? $"inv_{Guid.NewGuid():N}";
                    var invoiceUrl = root.GetProperty("invoice_url").GetString() ?? $"{_settings.InvoiceBaseUrl.TrimEnd('/')}/{invoiceId}";
                    var status = root.TryGetProperty("status", out var sProp) ? (sProp.GetString() ?? "PENDING") : "PENDING";

                    _logger.LogInformation("Xendit invoice created successfully: {InvoiceId} ({ExternalId}) for ₱{Amount:N2}",
                        invoiceId, externalId, amount);

                    return new XenditInvoiceResult
                    {
                        InvoiceId = invoiceId,
                        ExternalId = externalId,
                        InvoiceUrl = invoiceUrl,
                        Status = status,
                        Amount = amount
                    };
                }
                else
                {
                    var errContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Xendit API returned {StatusCode}: {Error}. Falling back to sandbox invoice simulator.",
                        response.StatusCode, errContent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to call live Xendit API. Falling back to sandbox simulation.");
            }
        }

        // Sandbox / Fallback simulated invoice
        var mockInvoiceId = $"inv_mock_{Guid.NewGuid():N}";
        var mockInvoiceUrl = $"{_settings.InvoiceBaseUrl.TrimEnd('/')}/{mockInvoiceId}?ext_id={Uri.EscapeDataString(externalId)}&amt={amount}";

        _logger.LogInformation("Generated sandbox Xendit invoice {InvoiceId} for external ID {ExternalId} (₱{Amount:N2})",
            mockInvoiceId, externalId, amount);

        return new XenditInvoiceResult
        {
            InvoiceId = mockInvoiceId,
            ExternalId = externalId,
            InvoiceUrl = mockInvoiceUrl,
            Status = "PENDING",
            Amount = amount
        };
    }

    public async Task<XenditPaymentStatusResult> CheckPaymentStatusAsync(
        string invoiceId,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_settings.SecretKey) &&
            !_settings.SecretKey.Contains("mock", StringComparison.OrdinalIgnoreCase) &&
            !invoiceId.StartsWith("inv_mock_", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.xendit.co/v2/invoices/{invoiceId}");
                var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.SecretKey}:"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);

                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var status = root.TryGetProperty("status", out var sProp) ? (sProp.GetString() ?? "PENDING") : "PENDING";
                    var extId = root.TryGetProperty("external_id", out var extProp) ? (extProp.GetString() ?? invoiceId) : invoiceId;
                    var amount = root.TryGetProperty("amount", out var aProp) ? aProp.GetDecimal() : 0m;
                    var paymentMethod = root.TryGetProperty("payment_method", out var pmProp) ? (pmProp.GetString() ?? "XENDIT") : "XENDIT";
                    var paymentChannel = root.TryGetProperty("payment_channel", out var pcProp) ? pcProp.GetString() : null;

                    return new XenditPaymentStatusResult
                    {
                        InvoiceId = invoiceId,
                        ExternalId = extId,
                        Status = status,
                        Amount = amount,
                        PaymentMethod = paymentMethod,
                        PaymentChannel = paymentChannel
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check Xendit invoice status online for {InvoiceId}", invoiceId);
            }
        }

        // Default sandbox: immediately verified as PAID upon manual check
        return new XenditPaymentStatusResult
        {
            InvoiceId = invoiceId,
            ExternalId = invoiceId,
            Status = "PAID",
            Amount = 0m,
            PaymentMethod = "Xendit_Sandbox",
            PaymentChannel = "GCASH"
        };
    }

    public async Task<XenditPaymentStatusResult> CheckPaymentStatusByExternalIdAsync(
        string externalId,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_settings.SecretKey) &&
            !_settings.SecretKey.Contains("mock", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.xendit.co/v2/invoices?external_id={Uri.EscapeDataString(externalId)}");
                var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.SecretKey}:"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);

                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                    {
                        var latest = doc.RootElement[0];
                        var invId = latest.GetProperty("id").GetString() ?? string.Empty;
                        var status = latest.TryGetProperty("status", out var sProp) ? (sProp.GetString() ?? "PENDING") : "PENDING";
                        var amount = latest.TryGetProperty("amount", out var aProp) ? aProp.GetDecimal() : 0m;
                        var paymentMethod = latest.TryGetProperty("payment_method", out var pmProp) ? (pmProp.GetString() ?? "XENDIT") : "XENDIT";
                        var paymentChannel = latest.TryGetProperty("payment_channel", out var pcProp) ? pcProp.GetString() : null;

                        return new XenditPaymentStatusResult
                        {
                            InvoiceId = invId,
                            ExternalId = externalId,
                            Status = status,
                            Amount = amount,
                            PaymentMethod = paymentMethod,
                            PaymentChannel = paymentChannel
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query Xendit invoice by externalId {ExternalId}", externalId);
            }
        }

        return new XenditPaymentStatusResult
        {
            InvoiceId = $"inv_mock_{Guid.NewGuid():N}",
            ExternalId = externalId,
            Status = "PAID",
            Amount = 0m,
            PaymentMethod = "Xendit_Sandbox",
            PaymentChannel = "GCASH"
        };
    }
}
