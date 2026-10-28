using System.Net;
using System.Net.Http.Json;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public interface IBookScheduleClient
{
    Task<ApiResult<IReadOnlyList<ScheduleServiceOption>>> GetServicesAsync(CancellationToken ct = default);
    Task<ApiResult<MonthAvailability>> GetAvailabilityAsync(int year, int month, CancellationToken ct = default);
    Task<ApiResult<DayAvailability>> GetDayAvailabilityAsync(DateOnly date, CancellationToken ct = default);
    Task<ApiResult<MeetingLinkPreview>> PreviewMeetingLinkAsync(MeetingPlatform platform, DateOnly date, TimeOnly slotStart, CancellationToken ct = default);
    Task<ApiResult<ScheduleBookingResult>> BookAsync(ScheduleBookingRequest request, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<MyBooking>>> GetMyBookingsAsync(CancellationToken ct = default);
}

/// <summary>Calls api/schedule/*. Never throws; every failure becomes an <see cref="ApiError"/>.</summary>
public sealed class BookScheduleClient(IHttpClientFactory httpClientFactory, ITokenStore tokenStore, ILogger<BookScheduleClient> logger) : IBookScheduleClient
{
    private readonly HttpClient _http = httpClientFactory.CreateClient("AevumApi.Authorized");

    public async Task<ApiResult<IReadOnlyList<ScheduleServiceOption>>> GetServicesAsync(CancellationToken ct = default)
    {
        var result = await SendAsync<List<ScheduleServiceOption>>(() => _http.GetAsync("api/schedule/services", ct), ct);
        return result.IsSuccess ? ApiResult<IReadOnlyList<ScheduleServiceOption>>.Ok(result.Value!) : new(null, result.Error);
    }

    public Task<ApiResult<MonthAvailability>> GetAvailabilityAsync(int year, int month, CancellationToken ct = default) =>
        SendAsync<MonthAvailability>(() => _http.GetAsync($"api/schedule/availability?year={year}&month={month}", ct), ct);

    public Task<ApiResult<DayAvailability>> GetDayAvailabilityAsync(DateOnly date, CancellationToken ct = default) =>
        SendAsync<DayAvailability>(() => _http.GetAsync($"api/schedule/availability/day?date={date:yyyy-MM-dd}", ct), ct);

    public Task<ApiResult<MeetingLinkPreview>> PreviewMeetingLinkAsync(MeetingPlatform platform, DateOnly date, TimeOnly slotStart, CancellationToken ct = default) =>
        SendAsync<MeetingLinkPreview>(() => _http.PostAsJsonAsync("api/schedule/meeting-link/preview", new { platform, date, slotStart }, ct), ct);

    public Task<ApiResult<ScheduleBookingResult>> BookAsync(ScheduleBookingRequest request, CancellationToken ct = default) =>
        SendAsync<ScheduleBookingResult>(() => _http.PostAsJsonAsync("api/schedule/bookings", request, ct), ct);

    public async Task<ApiResult<IReadOnlyList<MyBooking>>> GetMyBookingsAsync(CancellationToken ct = default)
    {
        var result = await SendAsync<List<MyBooking>>(() => _http.GetAsync("api/schedule/my-bookings", ct), ct);
        return result.IsSuccess ? ApiResult<IReadOnlyList<MyBooking>>.Ok(result.Value!) : new(null, result.Error);
    }

    private async Task<ApiResult<T>> SendAsync<T>(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        try
        {
            await tokenStore.GetTokenAsync(ct); // primes the ambient token for AuthHttpHandler
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
                HttpStatusCode.Conflict => ApiResult<T>.Fail(ApiErrorKind.SlotTaken,
                    detail ?? "That time slot was just booked by another client."),
                HttpStatusCode.BadRequest => ApiResult<T>.Fail(ApiErrorKind.Validation,
                    detail ?? "Some of the booking details are invalid."),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ApiResult<T>.Fail(ApiErrorKind.Unauthorized,
                    "Your session has expired. Please sign in again."),
                _ => ApiResult<T>.Fail(ApiErrorKind.Server, detail ?? "Something went wrong on our side. Please try again.")
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller cancelled; let it bubble
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Book Schedule API call failed.");
            return ApiResult<T>.Fail(ApiErrorKind.Network, "We couldn't reach the booking service. Check your connection and try again.");
        }
    }

    private static async Task<string?> ReadProblemDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>(ct);
            return string.IsNullOrWhiteSpace(problem?.Detail) ? null : problem.Detail;
        }
        catch
        {
            return null; // non-JSON error body
        }
    }

    private sealed record ProblemPayload(string? Title, string? Detail);
}
