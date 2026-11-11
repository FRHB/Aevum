using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
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
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public XenditPaymentService(
        IOptions<XenditSettings> options,
        HttpClient httpClient,
        ILogger<XenditPaymentService> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _settings = options.Value;
        _httpClient = httpClient;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    private string ResolveBaseUrl()
    {
        var configured = _settings.CustomerPortalBaseUrl?.Trim();
        var ctx = _httpContextAccessor?.HttpContext;

        if (!string.IsNullOrWhiteSpace(configured) &&
            !configured.Contains("localhost", StringComparison.OrdinalIgnoreCase) &&
            !configured.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return configured.TrimEnd('/');
        }

        if (ctx?.Request.Headers.TryGetValue("Origin", out var origin) == true &&
            Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var originUri) &&
            !originUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
            !originUri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return originUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }

        if (ctx?.Request.Host.HasValue == true &&
            !ctx.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
            !ctx.Request.Host.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
            !ctx.Request.Host.Host.StartsWith("api-", StringComparison.OrdinalIgnoreCase))
        {
            return $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}".TrimEnd('/');
        }

        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');

        return "http://photoaevum.tryasp.net";
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
        // Restrict automated invoice generation strictly to QR Ph and Philippine E-Wallets (card processing removed)
        var allowedPaymentMethods = new[]
        {
            "QRPH",
            "GCASH",
            "PAYMAYA",
            "GRABPAY",
            "SHOPEEPAY"
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
            PaymentMethod = "QRPH",
            PaymentChannel = "QRPH"
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
            PaymentMethod = "QRPH",
            PaymentChannel = "QRPH"
        };
    }

    public async Task<XenditQrResult> CreateQrCodeAsync(
        string referenceId,
        decimal amount,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        var isMock = string.IsNullOrWhiteSpace(_settings.SecretKey) ||
                     _settings.SecretKey.Contains("mock", StringComparison.OrdinalIgnoreCase);

        if (isMock)
        {
            // Development only: no live key configured. Encodes the full confirmation URL so mobile / webcam scanners open ConfirmPayment.razor.
            var confirmUrl = $"{ResolveBaseUrl()}/checkout/confirm/{Uri.EscapeDataString(referenceId)}";
            _logger.LogWarning("Xendit SecretKey is not configured; generated MOCK QR URL {ConfirmUrl} for {ReferenceId}.", confirmUrl, referenceId);
            return new XenditQrResult
            {
                QrId = $"qr_mock_{Guid.NewGuid():N}",
                ReferenceId = referenceId,
                QrString = confirmUrl,
                Status = "ACTIVE",
                Amount = amount
            };
        }

        var body = new
        {
            reference_id = referenceId,
            type = "DYNAMIC",
            currency = "PHP",
            amount,
            expires_at = expiresAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.xendit.co/qr_codes")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.SecretKey}:")));
        request.Headers.Add("api-version", "2022-07-31");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Xendit /qr_codes returned {Status}: {Body}", (int)response.StatusCode, json);
            throw new InvalidOperationException("Unable to generate the payment QR code. Please try again.");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var qrString = root.TryGetProperty("qr_string", out var q) ? q.GetString() : null;
        if (string.IsNullOrWhiteSpace(qrString))
            throw new InvalidOperationException("Xendit did not return a qr_string.");

        _logger.LogInformation("Xendit QR created for {ReferenceId} (PHP {Amount:N2}).", referenceId, amount);
        return new XenditQrResult
        {
            QrId = root.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            ReferenceId = referenceId,
            QrString = qrString,
            Status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "ACTIVE" : "ACTIVE",
            Amount = amount
        };
    }
}