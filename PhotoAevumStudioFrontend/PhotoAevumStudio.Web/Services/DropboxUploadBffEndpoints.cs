using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace PhotoAevumStudio.Web.Services;

public static class DropboxUploadBffEndpoints
{
    public static IEndpointRouteBuilder MapDropboxUploadBffEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/bff/dropbox/upload/{ticketId}");

        group.MapPost("/init", async (
            string ticketId,
            DropboxUploadTicketStore ticketStore,
            IHttpClientFactory httpClientFactory,
            CancellationToken ct) =>
        {
            if (!ticketStore.TryGetValidTicket(ticketId, out var ticket) || ticket == null)
                return Results.Unauthorized();

            var client = httpClientFactory.CreateClient("AevumApi.Upload");
            using var req = new HttpRequestMessage(
                HttpMethod.Post,
                $"api/dropbox/admin/{Uri.EscapeDataString(ticket.EventAppointmentId)}/upload/init");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket.JwtToken);
            req.Content = JsonContent.Create(new
            {
                fileName = ticket.FileName,
                fileSizeBytes = ticket.FileSizeBytes,
                contentType = ticket.ContentType
            });

            using var resp = await client.SendAsync(req, ct);
            return await ForwardJsonResponseAsync(resp, ct);
        }).DisableAntiforgery();

        group.MapPost("/chunk", async (
            string ticketId,
            HttpContext httpContext,
            DropboxUploadTicketStore ticketStore,
            IHttpClientFactory httpClientFactory,
            CancellationToken ct) =>
        {
            if (!ticketStore.TryGetValidTicket(ticketId, out var ticket) || ticket == null)
                return Results.Unauthorized();

            var bodyLimitFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodyLimitFeature is { IsReadOnly: false })
                bodyLimitFeature.MaxRequestBodySize = 16_777_216;

            var sessionId = httpContext.Request.Query["sessionId"].ToString();
            var chunkIndexStr = httpContext.Request.Query["chunkIndex"].ToString();
            if (string.IsNullOrWhiteSpace(sessionId) || !int.TryParse(chunkIndexStr, out var chunkIndex))
                return Results.BadRequest(new { detail = "sessionId and chunkIndex are required." });

            if (!httpContext.Request.HasFormContentType)
                return Results.BadRequest(new { detail = "Multipart form payload required." });

            var form = await httpContext.Request.ReadFormAsync(ct);
            var chunkFile = form.Files.GetFile("chunk") ?? form.Files.FirstOrDefault();
            if (chunkFile == null || chunkFile.Length <= 0)
                return Results.BadRequest(new { detail = "Empty chunk." });

            var client = httpClientFactory.CreateClient("AevumApi.Upload");
            using var outboundForm = new MultipartFormDataContent();
            await using var chunkStream = chunkFile.OpenReadStream();
            using var streamContent = new StreamContent(chunkStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            outboundForm.Add(streamContent, "chunk", "chunk.bin");

            var url = $"api/dropbox/admin/{Uri.EscapeDataString(ticket.EventAppointmentId)}/upload/chunk" +
                      $"?sessionId={Uri.EscapeDataString(sessionId)}&chunkIndex={chunkIndex}";

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket.JwtToken);
            req.Content = outboundForm;

            using var resp = await client.SendAsync(req, ct);
            return await ForwardJsonResponseAsync(resp, ct);
        }).DisableAntiforgery();

        group.MapPost("/complete", async (
            string ticketId,
            HttpContext httpContext,
            DropboxUploadTicketStore ticketStore,
            IHttpClientFactory httpClientFactory,
            CancellationToken ct) =>
        {
            if (!ticketStore.TryGetValidTicket(ticketId, out var ticket) || ticket == null)
                return Results.Unauthorized();

            var sessionId = httpContext.Request.Query["sessionId"].ToString();
            if (string.IsNullOrWhiteSpace(sessionId))
                return Results.BadRequest(new { detail = "sessionId is required." });

            var client = httpClientFactory.CreateClient("AevumApi.Upload");
            var url = $"api/dropbox/admin/{Uri.EscapeDataString(ticket.EventAppointmentId)}/upload/complete" +
                      $"?sessionId={Uri.EscapeDataString(sessionId)}";

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket.JwtToken);

            using var resp = await client.SendAsync(req, ct);
            return await ForwardJsonResponseAsync(resp, ct);
        }).DisableAntiforgery();

        group.MapGet("/status", async (
            string ticketId,
            HttpContext httpContext,
            DropboxUploadTicketStore ticketStore,
            IHttpClientFactory httpClientFactory,
            CancellationToken ct) =>
        {
            if (!ticketStore.TryGetValidTicket(ticketId, out var ticket) || ticket == null)
                return Results.Unauthorized();

            var sessionId = httpContext.Request.Query["sessionId"].ToString();
            if (string.IsNullOrWhiteSpace(sessionId))
                return Results.BadRequest(new { detail = "sessionId is required." });

            var client = httpClientFactory.CreateClient("AevumApi.Upload");
            var url = $"api/dropbox/admin/upload/status/{Uri.EscapeDataString(sessionId)}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket.JwtToken);

            using var resp = await client.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (resp.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("status", out var st))
                    {
                        var statusVal = st.GetString();
                        if (string.Equals(statusVal, "Completed", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(statusVal, "Failed", StringComparison.OrdinalIgnoreCase))
                        {
                            ticketStore.RemoveTicket(ticketId);
                        }
                    }
                }
                catch
                {
                }
            }

            return Results.Content(body, "application/json", statusCode: (int)resp.StatusCode);
        }).DisableAntiforgery();

        group.MapDelete("", async (
            string ticketId,
            HttpContext httpContext,
            DropboxUploadTicketStore ticketStore,
            IHttpClientFactory httpClientFactory,
            CancellationToken ct) =>
        {
            if (!ticketStore.TryGetValidTicket(ticketId, out var ticket) || ticket == null)
                return Results.NoContent();

            var sessionId = httpContext.Request.Query["sessionId"].ToString();
            ticketStore.RemoveTicket(ticketId);

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                try
                {
                    var client = httpClientFactory.CreateClient("AevumApi.Upload");
                    using var req = new HttpRequestMessage(
                        HttpMethod.Delete,
                        $"api/dropbox/admin/upload/{Uri.EscapeDataString(sessionId)}");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket.JwtToken);
                    using var _ = await client.SendAsync(req, ct);
                }
                catch
                {
                }
            }

            return Results.NoContent();
        }).DisableAntiforgery();

        return app;
    }

    private static async Task<IResult> ForwardJsonResponseAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var content = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(content))
            return Results.StatusCode((int)resp.StatusCode);

        return Results.Content(content, "application/json", statusCode: (int)resp.StatusCode);
    }
}
