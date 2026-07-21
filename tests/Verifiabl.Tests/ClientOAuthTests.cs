using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace Verifiabl.Tests;

public class ClientOAuthTests
{
    private const string Reference = "u0FE9WLIS7GYKQnpJPygBw";

    private static RegisterNonPiiRequest ValidRequest() => new()
    {
        Schema = "au.payslip.v1",
        IssuedAt = new DateTimeOffset(2026, 5, 31, 1, 2, 3, TimeSpan.Zero),
        PayslipNonPii = new PayslipNonPii { PeriodStart = "2026-05-01", PeriodEnd = "2026-05-31" },
        EncryptionMetadata = new EncryptionMetadata
        {
            Iv = "AAAAAAAAAAAAAAAA",
            Tag = "AAAAAAAAAAAAAAAAAAAAAA",
            KeyVersion = "0f8fad5b-d9cb-469f-a165-70867728950e.1",
        },
    };

    private static string RegistrationJson => $"{{\"verifiabl_reference\":\"{Reference}\"}}";

    private static VerifiablClient Client(
        FakeHttpHandler handler,
        Action<VerifiablClientOptions>? configure = null)
    {
        var options = new VerifiablClientOptions
        {
            Auth = VerifiablAuth.ClientCredentials("client-id", "client-secret"),
            HttpClient = new HttpClient(handler),
        };
        configure?.Invoke(options);
        return new VerifiablClient(options);
    }

    private static bool IsTokenRequest(HttpRequestMessage request) =>
        request.RequestUri!.Host.StartsWith("auth.", StringComparison.Ordinal);

    [Fact]
    public async Task FetchesATokenWithTheIssuerAudienceAndScope()
    {
        var handler = new FakeHttpHandler();
        handler.Responder = (request, _, _) => Task.FromResult(IsTokenRequest(request)
            ? FakeHttpHandler.Token()
            : FakeHttpHandler.Json(HttpStatusCode.OK, RegistrationJson));
        VerifiablClient client = Client(handler);

        await client.RegisterNonPiiAsync(ValidRequest());

        CapturedRequest tokenRequest = Assert.Single(handler.TokenRequests);
        Assert.Equal("https://auth.verifiabl.io/oauth/token", tokenRequest.Uri.ToString());
        using JsonDocument body = JsonDocument.Parse(tokenRequest.Body);
        Assert.Equal("client_credentials", body.RootElement.GetProperty("grant_type").GetString());
        Assert.Equal("client-id", body.RootElement.GetProperty("client_id").GetString());
        Assert.Equal("client-secret", body.RootElement.GetProperty("client_secret").GetString());
        Assert.Equal(
            "https://register.verifiabl.io",
            body.RootElement.GetProperty("audience").GetString());
        Assert.Equal("verifiabl:issuer", body.RootElement.GetProperty("scope").GetString());

        Assert.Equal("Bearer token-1", Assert.Single(handler.ApiRequests).Authorization);
    }

    [Fact]
    public async Task UsesTheSandboxTokenEndpointForSandbox()
    {
        var handler = new FakeHttpHandler();
        handler.Responder = (request, _, _) => Task.FromResult(IsTokenRequest(request)
            ? FakeHttpHandler.Token()
            : FakeHttpHandler.Json(HttpStatusCode.OK, RegistrationJson));
        VerifiablClient client = Client(
            handler,
            options => options.Environment = VerifiablEnvironment.Sandbox);

        await client.RegisterNonPiiAsync(ValidRequest());

        Assert.Equal(
            "https://auth.sandbox.verifiabl.io/oauth/token",
            Assert.Single(handler.TokenRequests).Uri.ToString());
    }

    [Fact]
    public async Task CachesTheTokenAcrossIssuerCalls()
    {
        var handler = new FakeHttpHandler();
        handler.Responder = (request, _, _) => Task.FromResult(IsTokenRequest(request)
            ? FakeHttpHandler.Token()
            : FakeHttpHandler.Json(HttpStatusCode.OK, RegistrationJson));
        VerifiablClient client = Client(handler);

        await client.RegisterNonPiiAsync(ValidRequest());
        await client.RegisterNonPiiAsync(ValidRequest());

        Assert.Single(handler.TokenRequests);
        Assert.Equal(2, handler.ApiRequests.Count());
    }

    [Fact]
    public async Task DeduplicatesConcurrentTokenRequests()
    {
        var handler = new FakeHttpHandler();
        handler.Responder = async (request, _, cancellationToken) =>
        {
            if (IsTokenRequest(request))
            {
                await Task.Delay(100, cancellationToken);
                return FakeHttpHandler.Token();
            }

            return FakeHttpHandler.Json(HttpStatusCode.OK, RegistrationJson);
        };
        VerifiablClient client = Client(handler);

        await Task.WhenAll(
            client.RegisterNonPiiAsync(ValidRequest()),
            client.RegisterNonPiiAsync(ValidRequest()));

        Assert.Single(handler.TokenRequests);
    }

    [Fact]
    public async Task RefreshesTheTokenAndRetriesOnceOnA401()
    {
        int apiCalls = 0;
        int tokenCalls = 0;
        var handler = new FakeHttpHandler();
        handler.Responder = (request, _, _) =>
        {
            if (IsTokenRequest(request))
            {
                tokenCalls++;
                return Task.FromResult(FakeHttpHandler.Token($"token-{tokenCalls}"));
            }

            apiCalls++;
            return Task.FromResult(apiCalls == 1
                ? FakeHttpHandler.Json(
                    HttpStatusCode.Unauthorized,
                    "{\"error\":\"expired\",\"code\":\"UNAUTHORIZED\"}")
                : FakeHttpHandler.Json(HttpStatusCode.OK, RegistrationJson));
        };
        VerifiablClient client = Client(handler);

        RegisterNonPiiResponse response = await client.RegisterNonPiiAsync(ValidRequest());

        Assert.Equal(Reference, response.VerifiablReference);
        Assert.Equal(2, tokenCalls);
        Assert.Equal(2, apiCalls);
        Assert.Equal(
            ["Bearer token-1", "Bearer token-2"],
            handler.ApiRequests.Select(r => r.Authorization));
    }

    [Fact]
    public async Task SurfacesAPersistent401AfterOneRetry()
    {
        int apiCalls = 0;
        var handler = new FakeHttpHandler();
        handler.Responder = (request, _, _) =>
        {
            if (IsTokenRequest(request))
            {
                return Task.FromResult(FakeHttpHandler.Token());
            }

            apiCalls++;
            return Task.FromResult(FakeHttpHandler.Json(
                HttpStatusCode.Unauthorized,
                "{\"error\":\"nope\",\"code\":\"UNAUTHORIZED\"}"));
        };
        VerifiablClient client = Client(handler);

        VerifiablApiException exception = await Assert.ThrowsAsync<VerifiablApiException>(
            () => client.RegisterNonPiiAsync(ValidRequest()));

        Assert.Equal(401, exception.Status);
        Assert.Equal(VerifiablErrorCodes.Unauthorized, exception.Code);
        Assert.Equal(2, apiCalls);
    }

    [Fact]
    public async Task ThrowsVerifiablAuthExceptionWhenTheTokenEndpointFails()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(
                FakeHttpHandler.Json(HttpStatusCode.InternalServerError, "{}")),
        };
        VerifiablClient client = Client(handler);

        VerifiablAuthException exception = await Assert.ThrowsAsync<VerifiablAuthException>(
            () => client.RegisterNonPiiAsync(ValidRequest()));

        Assert.Equal(500, exception.Status);
    }

    [Fact]
    public async Task ThrowsVerifiablAuthExceptionWhenTheTokenEndpointIsUnreachable()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) =>
                Task.FromException<HttpResponseMessage>(new HttpRequestException("dns failure")),
        };
        VerifiablClient client = Client(handler);

        VerifiablAuthException exception = await Assert.ThrowsAsync<VerifiablAuthException>(
            () => client.RegisterNonPiiAsync(ValidRequest()));

        Assert.Null(exception.Status);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData("{\"access_token\":\"\",\"token_type\":\"Bearer\",\"expires_in\":3600}")]
    [InlineData("{\"access_token\":\"t\",\"token_type\":\"basic\",\"expires_in\":3600}")]
    [InlineData("{\"access_token\":\"t\",\"token_type\":\"Bearer\",\"expires_in\":0}")]
    [InlineData("{\"access_token\":\"t\",\"token_type\":\"Bearer\"}")]
    [InlineData("not json")]
    public async Task ThrowsVerifiablAuthExceptionOnMalformedTokenResponses(string tokenBody)
    {
        var handler = new FakeHttpHandler();
        handler.Responder = (request, _, _) => Task.FromResult(IsTokenRequest(request)
            ? FakeHttpHandler.Json(HttpStatusCode.OK, tokenBody)
            : FakeHttpHandler.Json(HttpStatusCode.OK, RegistrationJson));
        VerifiablClient client = Client(handler);

        await Assert.ThrowsAsync<VerifiablAuthException>(
            () => client.RegisterNonPiiAsync(ValidRequest()));
    }

    [Fact]
    public async Task AcceptsALowercaseBearerTokenType()
    {
        var handler = new FakeHttpHandler();
        handler.Responder = (request, _, _) => Task.FromResult(IsTokenRequest(request)
            ? FakeHttpHandler.Json(
                HttpStatusCode.OK,
                "{\"access_token\":\"t\",\"token_type\":\"bearer\",\"expires_in\":3600}")
            : FakeHttpHandler.Json(HttpStatusCode.OK, RegistrationJson));
        VerifiablClient client = Client(handler);

        // RFC 6749 §7.1: token_type is case-insensitive.
        await client.RegisterNonPiiAsync(ValidRequest());

        Assert.Equal("Bearer t", handler.ApiRequests.Single().Authorization);
    }

    [Fact]
    public async Task RequestsAFreshTokenOnceTheCachedOneNearsExpiry()
    {
        var handler = new FakeHttpHandler();
        handler.Responder = (request, _, _) => Task.FromResult(IsTokenRequest(request)
            ? FakeHttpHandler.Token(expiresIn: 2)
            : FakeHttpHandler.Json(HttpStatusCode.OK, RegistrationJson));
        VerifiablClient client = Client(handler);

        await client.RegisterNonPiiAsync(ValidRequest());
        // A 2-second token has a 1-second refresh buffer; after 1.5s it is
        // comfortably stale even on a slow CI runner.
        await Task.Delay(1500);
        await client.RegisterNonPiiAsync(ValidRequest());

        Assert.Equal(2, handler.TokenRequests.Count());
    }

    [Fact]
    public async Task TimesOutWhenTheApiCallExceedsTheConfiguredTimeout()
    {
        var handler = new FakeHttpHandler();
        handler.Responder = async (request, _, cancellationToken) =>
        {
            if (IsTokenRequest(request))
            {
                return FakeHttpHandler.Token();
            }

            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return FakeHttpHandler.Json(HttpStatusCode.OK, RegistrationJson);
        };
        VerifiablClient client = Client(
            handler,
            options => options.Timeout = TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<TimeoutException>(() => client.RegisterNonPiiAsync(ValidRequest()));
    }

    [Fact]
    public async Task TheTimeoutCoversTheTokenFetchToo()
    {
        var handler = new FakeHttpHandler();
        handler.Responder = async (request, _, cancellationToken) =>
        {
            if (IsTokenRequest(request))
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                return FakeHttpHandler.Token();
            }

            return FakeHttpHandler.Json(HttpStatusCode.OK, RegistrationJson);
        };
        VerifiablClient client = Client(
            handler,
            options => options.Timeout = TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<TimeoutException>(() => client.RegisterNonPiiAsync(ValidRequest()));
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsATimeout()
    {
        var handler = new FakeHttpHandler();
        handler.Responder = async (request, _, cancellationToken) =>
        {
            if (IsTokenRequest(request))
            {
                return FakeHttpHandler.Token();
            }

            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return FakeHttpHandler.Json(HttpStatusCode.OK, RegistrationJson);
        };
        VerifiablClient client = Client(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.RegisterNonPiiAsync(ValidRequest(), cts.Token));
    }
}
