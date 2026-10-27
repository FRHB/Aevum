using System.Net;
using System.Net.Http.Json;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public sealed class ConsultationClient(
    IHttpClientFactory httpClientFactory,
    ITokenStore tokenStore,
    ILogger<ConsultationClient> logger) : IConsultationClient
{
    private readonly HttpClient _http = httpClientFactory.CreateClient("AevumApi.Authorized");

    public Task<ApiResult<ConsultationNegotiationDto>> ScheduleConsultationAsync(CreateConsultationRequest request, CancellationToken ct = default) =>
        SendAsync<ConsultationNegotiationDto>(() => _http.PostAsJsonAsync("api/consultations/schedule", request, ct), ct);

    public async Task<ApiResult<IReadOnlyList<ConsultationNegotiationDto>>> GetNegotiationsAsync(string? clerkEmployeeId = null, string? status = null, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (!string.IsNullOrWhiteSpace(clerkEmployeeId)) q.Add($"clerkEmployeeId={Uri.EscapeDataString(clerkEmployeeId)}");
        if (!string.IsNullOrWhiteSpace(status)) q.Add($"status={Uri.EscapeDataString(status)}");
        var url = "api/consultations" + (q.Count > 0 ? "?" + string.Join("&", q) : "");

        var result = await SendAsync<List<ConsultationNegotiationDto>>(() => _http.GetAsync(url, ct), ct);
        return result.IsSuccess ? ApiResult<IReadOnlyList<ConsultationNegotiationDto>>.Ok(result.Value!) : new(null, result.Error);
    }

    public Task<ApiResult<ConsultationNegotiationDto>> GetNegotiationByIdAsync(string id, CancellationToken ct = default) =>
        SendAsync<ConsultationNegotiationDto>(() => _http.GetAsync($"api/consultations/{id}", ct), ct);

    public async Task<ApiResult<bool>> UpdateNegotiationOutcomeAsync(string id, UpdateNegotiationOutcomeRequest request, CancellationToken ct = default)
    {
        var result = await SendAsync<object>(() => _http.PostAsJsonAsync($"api/consultations/{id}/outcome", request, ct), ct);
        return result.IsSuccess ? ApiResult<bool>.Ok(true) : new(false, result.Error);
    }

    public Task<ApiResult<EventAppointmentDto>> CreateEventAppointmentAsync(string negotiationId, CreateEventAppointmentFromNegotiationRequest request, CancellationToken ct = default) =>
        SendAsync<EventAppointmentDto>(() => _http.PostAsJsonAsync($"api/consultations/{negotiationId}/create-event", request, ct), ct);

    public Task<ApiResult<EventAppointmentDto>> GetAppointmentByIdAsync(string id, CancellationToken ct = default) =>
        SendAsync<EventAppointmentDto>(() => _http.GetAsync($"api/payments/appointment/{id}", ct), ct);

    public async Task<ApiResult<IReadOnlyList<EventAppointmentDto>>> GetCustomerAppointmentsAsync(string? status = null, CancellationToken ct = default)
    {
        var url = "api/payments/customer-appointments" + (!string.IsNullOrWhiteSpace(status) ? $"?status={Uri.EscapeDataString(status)}" : "");
        var result = await SendAsync<List<EventAppointmentDto>>(() => _http.GetAsync(url, ct), ct);
        return result.IsSuccess ? ApiResult<IReadOnlyList<EventAppointmentDto>>.Ok(result.Value!) : new(null, result.Error);
    }

    public Task<ApiResult<EventAppointmentDto>> VerifyDownpaymentAsync(string appointmentId, string? invoiceId = null, CancellationToken ct = default)
    {
        var url = $"api/payments/verify-downpayment/{appointmentId}" + (!string.IsNullOrWhiteSpace(invoiceId) ? $"?invoiceId={Uri.EscapeDataString(invoiceId)}" : "");
        return SendAsync<EventAppointmentDto>(() => _http.PostAsync(url, null, ct), ct);
    }

    public async Task<ApiResult<bool>> RequestExtensionAsync(string appointmentId, CancellationToken ct = default)
    {
        var result = await SendAsync<object>(() => _http.PostAsync($"api/payments/request-extension/{appointmentId}", null, ct), ct);
        return result.IsSuccess ? ApiResult<bool>.Ok(true) : new(false, result.Error);
    }

    public Task<ApiResult<EventAppointmentDto>> CompleteEventAsync(string appointmentId, CompleteEventRequest request, CancellationToken ct = default) =>
        SendAsync<EventAppointmentDto>(() => _http.PostAsJsonAsync($"api/payments/complete-event/{appointmentId}", request, ct), ct);

    private async Task<ApiResult<T>> SendAsync<T>(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        try
        {
            await tokenStore.GetTokenAsync(ct);
            using var response = await send();

            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(ct);
                return value is null
                    ? ApiResult<T>.Fail(ApiErrorKind.Server, "The server returned an empty response.")
                    : ApiResult<T>.Ok(value);
            }

            var detail = await ReadProblemDetailAsync(response, ct);
            return response.StatusCode switch
            {
                HttpStatusCode.Conflict => ApiResult<T>.Fail(ApiErrorKind.SlotTaken, detail ?? "Resource conflict."),
                HttpStatusCode.BadRequest => ApiResult<T>.Fail(ApiErrorKind.Validation, detail ?? "Validation failed."),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ApiResult<T>.Fail(ApiErrorKind.Unauthorized, "Session expired."),
                _ => ApiResult<T>.Fail(ApiErrorKind.Server, detail ?? "Server request failed.")
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Consultation API request failed.");
            return ApiResult<T>.Fail(ApiErrorKind.Network, "Connection failed.");
        }
    }

    private static async Task<string?> ReadProblemDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>(ct);
            return string.IsNullOrWhiteSpace(problem?.Detail) ? problem?.Title : problem.Detail;
        }
        catch
        {
            return null;
        }
    }

    private sealed record ProblemPayload(string? Title, string? Detail);
}
