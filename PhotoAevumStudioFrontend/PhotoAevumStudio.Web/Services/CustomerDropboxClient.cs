using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public sealed class CustomerDropboxClient(
    IHttpClientFactory httpClientFactory,
    ILogger<CustomerDropboxClient> logger) : ICustomerDropboxClient
{
    private readonly HttpClient _http = httpClientFactory.CreateClient("AevumApi.Authorized");

    public async Task<ApiResult<IReadOnlyList<CustomerDropboxFolderItem>>> GetMyDropboxesAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync("api/dropbox/my", ct);
            if (!resp.IsSuccessStatusCode)
                return await ReadErrorAsync<IReadOnlyList<CustomerDropboxFolderItem>>(resp, ct);

            var data = await resp.Content.ReadFromJsonAsync<List<CustomerDropboxFolderItem>>(cancellationToken: ct)
                       ?? new List<CustomerDropboxFolderItem>();
            return ApiResult<IReadOnlyList<CustomerDropboxFolderItem>>.Ok(data);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed loading customer Dropbox folders.");
            return ApiResult<IReadOnlyList<CustomerDropboxFolderItem>>.Fail(ApiErrorKind.Network, ex.Message);
        }
    }

    public async Task<ApiResult<DropboxDownloadUrlResponse>> GetDownloadUrlAsync(string dropboxId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync($"api/dropbox/my/{Uri.EscapeDataString(dropboxId)}/download-url", ct);
            if (!resp.IsSuccessStatusCode)
                return await ReadErrorAsync<DropboxDownloadUrlResponse>(resp, ct);

            var data = await resp.Content.ReadFromJsonAsync<DropboxDownloadUrlResponse>(cancellationToken: ct);
            return data != null
                ? ApiResult<DropboxDownloadUrlResponse>.Ok(data)
                : ApiResult<DropboxDownloadUrlResponse>.Fail(ApiErrorKind.Server, "Empty download response.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed requesting Dropbox download URL for {DropboxId}.", dropboxId);
            return ApiResult<DropboxDownloadUrlResponse>.Fail(ApiErrorKind.Network, ex.Message);
        }
    }

    public async Task<ApiResult<IReadOnlyList<CustomerDropboxFolderItem>>> GetAdminEventContainersAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync("api/dropbox/admin/events", ct);
            if (!resp.IsSuccessStatusCode)
                return await ReadErrorAsync<IReadOnlyList<CustomerDropboxFolderItem>>(resp, ct);

            var data = await resp.Content.ReadFromJsonAsync<List<CustomerDropboxFolderItem>>(cancellationToken: ct)
                       ?? new List<CustomerDropboxFolderItem>();
            return ApiResult<IReadOnlyList<CustomerDropboxFolderItem>>.Ok(data);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed loading admin Dropbox event containers.");
            return ApiResult<IReadOnlyList<CustomerDropboxFolderItem>>.Fail(ApiErrorKind.Network, ex.Message);
        }
    }

    public async Task<ApiResult<CustomerDropboxFolderItem>> UploadEventZipAsync(
        string eventAppointmentId,
        Stream zipStream,
        string fileName,
        string? contentType,
        CancellationToken ct = default)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            using var fileContent = new StreamContent(zipStream);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var parsedMediaType)
                ? parsedMediaType
                : new MediaTypeHeaderValue("application/zip");
            form.Add(fileContent, "file", string.IsNullOrWhiteSpace(fileName) ? "event-photos.zip" : fileName);

            using var resp = await _http.PostAsync(
                $"api/dropbox/admin/{Uri.EscapeDataString(eventAppointmentId)}/upload",
                form,
                ct);

            if (!resp.IsSuccessStatusCode)
                return await ReadErrorAsync<CustomerDropboxFolderItem>(resp, ct);

            var data = await resp.Content.ReadFromJsonAsync<CustomerDropboxFolderItem>(cancellationToken: ct);
            return data != null
                ? ApiResult<CustomerDropboxFolderItem>.Ok(data)
                : ApiResult<CustomerDropboxFolderItem>.Fail(ApiErrorKind.Server, "Empty upload response.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed uploading ZIP archive for event {EventId}.", eventAppointmentId);
            return ApiResult<CustomerDropboxFolderItem>.Fail(ApiErrorKind.Network, ex.Message);
        }
    }

    private static async Task<ApiResult<T>> ReadErrorAsync<T>(HttpResponseMessage resp, CancellationToken ct)
    {
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement?>(cancellationToken: ct)
            is { ValueKind: System.Text.Json.JsonValueKind.Object } json
            ? json
            : (System.Text.Json.JsonElement?)null;

        var message = resp.ReasonPhrase ?? $"HTTP {(int)resp.StatusCode}";
        if (body.HasValue)
        {
            if (body.Value.TryGetProperty("detail", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.String)
                message = d.GetString() ?? message;
            else if (body.Value.TryGetProperty("message", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.String)
                message = m.GetString() ?? message;
            else if (body.Value.TryGetProperty("title", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String)
                message = t.GetString() ?? message;
        }

        var kind = resp.StatusCode switch
        {
            HttpStatusCode.BadRequest => ApiErrorKind.Validation,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ApiErrorKind.Unauthorized,
            HttpStatusCode.Conflict => ApiErrorKind.SlotTaken,
            _ => ApiErrorKind.Server
        };

        return ApiResult<T>.Fail(kind, message);
    }
}
