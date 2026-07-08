using System.Net;
using System.Net.Http;
using System.Text;

namespace Verifiabl.Tests;

internal sealed class CapturedRequest
{
    internal CapturedRequest(Uri uri, string body, string? authorization, string? userAgent)
    {
        Uri = uri;
        Body = body;
        Authorization = authorization;
        UserAgent = userAgent;
    }

    internal Uri Uri { get; }

    internal string Body { get; }

    internal string? Authorization { get; }

    internal string? UserAgent { get; }
}

internal sealed class FakeHttpHandler : HttpMessageHandler
{
    internal Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>>? Responder { get; set; }

    internal List<CapturedRequest> Requests { get; } = [];

    internal IEnumerable<CapturedRequest> TokenRequests =>
        Requests.Where(r => r.Uri.AbsolutePath.Contains("oauth"));

    internal IEnumerable<CapturedRequest> ApiRequests =>
        Requests.Where(r => !r.Uri.AbsolutePath.Contains("oauth"));

    internal static HttpResponseMessage Json(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    internal static HttpResponseMessage Token(string accessToken = "token-1", double expiresIn = 3600)
    {
        return Json(
            HttpStatusCode.OK,
            $"{{\"access_token\":\"{accessToken}\",\"token_type\":\"Bearer\",\"expires_in\":{expiresIn}}}");
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
        string? userAgent = request.Headers.TryGetValues("User-Agent", out IEnumerable<string>? ua)
            ? string.Join(" ", ua)
            : null;
        Requests.Add(new CapturedRequest(
            request.RequestUri!,
            body,
            request.Headers.Authorization?.ToString(),
            userAgent));
        return await Responder!(request, body, cancellationToken).ConfigureAwait(false);
    }
}
