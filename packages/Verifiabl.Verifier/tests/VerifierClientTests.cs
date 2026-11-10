using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Verifiabl.Verifier.Tests;

public class VerifierClientTests
{
    private const string Reference = "n5waC35dPCoRtMXW5_gJqw";
    private const string Barcode = "https://v.sandbox.verifiabl.io/v/n5waC35dPCoRtMXW5_gJqw#2.JVSWY3DPO5XXE3DE";
    private const string EmployeeName = "Taylor Nguyen";

    private static readonly string VerifiedJson = $$"""
        {
          "verified": true,
          "verifiabl_reference": "{{Reference}}",
          "employee": { "employee_name": "{{EmployeeName}}", "account_name": "T Nguyen" },
          "payslip": {
            "schema": "au.payslip.v2",
            "issued_at": "2026-04-04T00:00:00.000Z",
            "payslip_non_pii": { "currency": "AUD", "gross": "3000.00", "net": "2400.00" }
          },
          "decrypted_at": "2026-06-09T09:10:11.123Z"
        }
        """;

    private static (VerifierClient Client, FakeHttpHandler Handler) CreateClient(
        Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>> responder,
        int maxRetries = 2,
        TimeSpan? timeout = null)
    {
        var handler = new FakeHttpHandler { Responder = responder };
        var client = new VerifierClient(new VerifierClientOptions
        {
            Auth = VerifierAuth.ClientCredentials("verifier-client", "verifier-secret"),
            Environment = VerifierEnvironment.Sandbox,
            HttpClient = new HttpClient(handler),
            MaxRetries = maxRetries,
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        })
        {
            DelayAsync = (_, _) => Task.CompletedTask,
        };
        return (client, handler);
    }

    private static bool IsTokenRequest(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.Contains("oauth");

    private static Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>> Respond(
        params (HttpStatusCode Status, string Body)[] apiResponses)
    {
        int call = 0;
        return (request, _, _) =>
        {
            if (IsTokenRequest(request))
            {
                return Task.FromResult(FakeHttpHandler.Token());
            }

            (HttpStatusCode status, string body) = apiResponses[Math.Min(call++, apiResponses.Length - 1)];
            return Task.FromResult(FakeHttpHandler.Json(status, body));
        };
    }

    [Fact]
    public async Task VerifyAsyncSendsTheBarcodeUnchangedWithAVerifierToken()
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(Respond((HttpStatusCode.OK, VerifiedJson)));

        await client.VerifyAsync(new VerificationRequest(Barcode));

        CapturedRequest token = Assert.Single(handler.TokenRequests);
        Assert.Equal("https://auth.sandbox.verifiabl.io/oauth/token", token.Uri.AbsoluteUri);
        using JsonDocument tokenBody = JsonDocument.Parse(token.Body);
        Assert.Equal("https://verify.sandbox.verifiabl.io", tokenBody.RootElement.GetProperty("audience").GetString());
        Assert.Equal("verifiabl:verifier", tokenBody.RootElement.GetProperty("scope").GetString());

        CapturedRequest api = Assert.Single(handler.ApiRequests);
        Assert.Equal("https://verify.sandbox.verifiabl.io/v1/verifications/payload", api.Uri.AbsoluteUri);
        Assert.Equal("Bearer token-1", api.Authorization);
        string packageVersion = typeof(VerifierClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion
            .Split('+')[0];
        Assert.StartsWith($"verifiabl-verifier-dotnet/{packageVersion} (", api.UserAgent);
        using JsonDocument apiBody = JsonDocument.Parse(api.Body);
        Assert.Equal(Barcode, apiBody.RootElement.GetProperty("barcode").GetString());
    }

    [Theory]
    [InlineData("1|n5waC35dPCoRtMXW5_gJqw|YWJj")]
    [InlineData("2|n5waC35dPCoRtMXW5_gJqw|JVSWY3DP")]
    [InlineData("https://v.verifiabl.io/v/1%7Cn5waC35dPCoRtMXW5_gJqw%7CYWJj")]
    [InlineData("  text the API must validate  ")]
    public async Task ExtractedPayloadRepresentationsAreForwardedUnchanged(string payload)
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(Respond((HttpStatusCode.OK, VerifiedJson)));

        await client.VerifyAsync(new VerificationRequest(payload));

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.ApiRequests).Body);
        Assert.Equal(payload, body.RootElement.GetProperty("barcode").GetString());
    }

    [Fact]
    public async Task FutureResponseSchemasAndFieldsArePreserved()
    {
        string json = VerifiedJson.Replace("au.payslip.v2", "future.payslip.v3");
        (VerifierClient client, _) = CreateClient(Respond((HttpStatusCode.OK, json)));

        VerificationResult result = await client.VerifyAsync(new VerificationRequest(Barcode));

        Assert.Equal("future.payslip.v3", result.Schema);
        Assert.Equal("T Nguyen", result.Employee.GetProperty("account_name").GetString());
        Assert.Equal("3000.00", result.Payslip.GetProperty("payslip_non_pii").GetProperty("gross").GetString());
    }

    [Fact]
    public async Task VerifyAsyncReturnsTheVerifiedPayslip()
    {
        (VerifierClient client, _) = CreateClient(Respond((HttpStatusCode.OK, VerifiedJson)));

        VerificationResult result = await client.VerifyAsync(new VerificationRequest(Barcode));

        Assert.Equal(Reference, result.VerifiablReference);
        Assert.Equal("au.payslip.v2", result.Schema);
        Assert.Equal(new DateTimeOffset(2026, 6, 9, 9, 10, 11, 123, TimeSpan.Zero), result.DecryptedAt);
        Assert.Equal(EmployeeName, result.Employee.GetProperty("employee_name").GetString());
        Assert.Equal("3000.00", result.Payslip.GetProperty("payslip_non_pii").GetProperty("gross").GetString());
    }

    [Fact]
    public async Task SuccessfulResultsCarryTheRequestId()
    {
        (VerifierClient client, _) = CreateClient((request, _, _) =>
        {
            if (IsTokenRequest(request))
            {
                return Task.FromResult(FakeHttpHandler.Token());
            }

            HttpResponseMessage response = FakeHttpHandler.Json(HttpStatusCode.OK, VerifiedJson);
            response.Headers.Add("X-Request-Id", "request-ok");
            return Task.FromResult(response);
        });

        VerificationResult result = await client.VerifyAsync(new VerificationRequest(Barcode));

        Assert.Equal("request-ok", result.RequestId);
    }

    [Fact]
    public async Task ResultToStringOmitsEmployeeAndPayslipData()
    {
        (VerifierClient client, _) = CreateClient(Respond((HttpStatusCode.OK, VerifiedJson)));

        VerificationResult result = await client.VerifyAsync(new VerificationRequest(Barcode));

        Assert.Contains(Reference, result.ToString());
        Assert.DoesNotContain(EmployeeName, result.ToString());
        Assert.DoesNotContain("3000.00", result.ToString());
    }

    [Fact]
    public async Task NotVerifiedIsARejectionThatIsNotRetried()
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(Respond((
            (HttpStatusCode)422,
            """{"error":"Not verified","code":"NOT_VERIFIED"}""")));

        VerifierRejectedException error = await Assert.ThrowsAsync<VerifierRejectedException>(() => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.Equal(422, error.Status);
        Assert.False(error.IsRetryable);
        Assert.Equal(VerifierErrorCodes.NotVerified, error.Code);
        Assert.Null(error.Reason);
        Assert.DoesNotContain(Reference, error.Message);
        Assert.Single(handler.ApiRequests);
    }

    // Issuer self-test routes send a granular reason with the same code.
    [Fact]
    public async Task RejectionsCarryAReasonWhenTheApiSendsOne()
    {
        (VerifierClient client, _) = CreateClient(Respond((
            (HttpStatusCode)422,
            """{"error":"Not verified","code":"NOT_VERIFIED","reason":"no_key_matched"}""")));

        VerifierRejectedException error = await Assert.ThrowsAsync<VerifierRejectedException>(() => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.Equal("no_key_matched", error.Reason);
    }

    // A malformed request means the SDK and API disagree. It is not a failed
    // verification, whatever reason or field paths it carries.
    [Theory]
    [InlineData("""{"error":"Validation failed","code":"VALIDATION_FAILED","field_errors":[{"path":"barcode","message":"Expected string"}]}""")]
    [InlineData("""{"error":"Validation failed","code":"VALIDATION_FAILED","field_errors":[{"path":"","message":"Unrecognized key: \"extra\""}]}""")]
    [InlineData("""{"error":"Validation failed","code":"VALIDATION_FAILED","reason":"invalid_barcode","field_errors":[{"path":"barcode","message":"Invalid"}]}""")]
    public async Task ValidationErrorsAreServiceErrorsWithTheirFields(string body)
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(Respond((HttpStatusCode.BadRequest, body)));

        VerifierServiceException error = await Assert.ThrowsAsync<VerifierServiceException>(() => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.Equal(VerifierErrorCodes.ValidationFailed, error.Code);
        Assert.Equal(400, error.Status);
        Assert.False(error.IsRetryable);
        Assert.Single(error.FieldErrors);
        Assert.Single(handler.ApiRequests);
    }

    [Theory]
    [InlineData(413, "PAYLOAD_TOO_LARGE")]
    [InlineData(415, "UNSUPPORTED_MEDIA_TYPE")]
    public async Task IntegrationFaultsAreServiceErrorsThatAreNotRetried(int status, string code)
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(Respond((
            (HttpStatusCode)status,
            $$"""{"error":"Request refused","code":"{{code}}"}""")));

        VerifierServiceException error = await Assert.ThrowsAsync<VerifierServiceException>(() => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.Equal(code, error.Code);
        Assert.False(error.IsRetryable);
        Assert.Single(handler.ApiRequests);
    }

    [Fact]
    public async Task ReadsOnlyTheXRequestIdHeader()
    {
        (VerifierClient client, _) = CreateClient((request, _, _) =>
        {
            if (IsTokenRequest(request))
            {
                return Task.FromResult(FakeHttpHandler.Token());
            }

            HttpResponseMessage response = FakeHttpHandler.Json(
                HttpStatusCode.Forbidden,
                """{"error":"Forbidden","code":"FORBIDDEN"}""");
            response.Headers.Add("request-id", "request-other");
            response.Headers.Add("x-verifiabl-request-id", "request-other");
            return Task.FromResult(response);
        });

        VerifierAuthException error = await Assert.ThrowsAsync<VerifierAuthException>(() => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.Null(error.RequestId);
    }

    [Fact]
    public async Task ForbiddenResponsesAreAuthErrors()
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient((request, _, _) =>
        {
            if (IsTokenRequest(request))
            {
                return Task.FromResult(FakeHttpHandler.Token());
            }

            HttpResponseMessage response = FakeHttpHandler.Json(
                HttpStatusCode.Forbidden,
                """{"error":"Forbidden","code":"FORBIDDEN"}""");
            response.Headers.Add("X-Request-Id", "request-1");
            return Task.FromResult(response);
        });

        VerifierAuthException error = await Assert.ThrowsAsync<VerifierAuthException>(() => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.Equal(403, error.Status);
        Assert.Equal(VerifierErrorCodes.Forbidden, error.Code);
        Assert.Equal("request-1", error.RequestId);
        Assert.False(error.IsRetryable);
        Assert.Single(handler.ApiRequests);
    }

    [Fact]
    public async Task UnauthorizedAfterARefreshIsAnAuthError()
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient((request, _, _) => Task.FromResult(
            IsTokenRequest(request)
                ? FakeHttpHandler.Token()
                : FakeHttpHandler.Html(HttpStatusCode.Unauthorized, "<html>unauthorized</html>")));

        VerifierAuthException error = await Assert.ThrowsAsync<VerifierAuthException>(() => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.Equal(401, error.Status);
        Assert.Equal(VerifierErrorCodes.Unauthorized, error.Code);
        Assert.False(error.IsRetryable);
        Assert.Equal(2, handler.ApiRequests.Count());
    }

    [Fact]
    public async Task ErrorsWithoutAVerifiablBodyAreNotRejections()
    {
        (VerifierClient client, _) = CreateClient((request, _, _) => Task.FromResult(IsTokenRequest(request)
            ? FakeHttpHandler.Token()
            : FakeHttpHandler.Html(HttpStatusCode.BadRequest, "<html>bad request</html>")));

        VerifierServiceException error = await Assert.ThrowsAsync<VerifierServiceException>(() => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.Equal(VerifierErrorCodes.InternalError, error.Code);
        Assert.False(error.IsRetryable);
    }

    [Theory]
    [InlineData((HttpStatusCode)429)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task TransientFailuresAreRetried(HttpStatusCode status)
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(Respond(
            (status, """{"error":"Busy","code":"SERVICE_UNAVAILABLE"}"""),
            (HttpStatusCode.OK, VerifiedJson)));

        await client.VerifyAsync(new VerificationRequest(Barcode));

        Assert.Equal(2, handler.ApiRequests.Count());
    }

    [Fact]
    public async Task ExhaustedRetriesReportTheLastResponse()
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(
            (request, _, _) => Task.FromResult(IsTokenRequest(request)
                ? FakeHttpHandler.Token()
                : FakeHttpHandler.Html(HttpStatusCode.BadGateway, "<html>bad gateway</html>")),
            maxRetries: 1);

        VerifierServiceException error = await Assert.ThrowsAsync<VerifierServiceException>(() => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.Equal(502, error.Status);
        Assert.Equal(VerifierErrorCodes.InternalError, error.Code);
        Assert.True(error.IsRetryable);
        Assert.Equal(2, handler.ApiRequests.Count());
    }

    [Fact]
    public async Task ExhaustedNetworkRetriesAreRetryableTransportFailures()
    {
        (VerifierClient client, _) = CreateClient(
            (request, _, _) => IsTokenRequest(request)
                ? Task.FromResult(FakeHttpHandler.Token())
                : throw new HttpRequestException("connection reset"),
            maxRetries: 1);

        VerifierTransportException error = await Assert.ThrowsAsync<VerifierTransportException>(() => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.True(error.IsRetryable);
    }

    [Fact]
    public async Task NetworkFaultsAreRetried()
    {
        int apiCalls = 0;
        (VerifierClient client, _) = CreateClient((request, _, _) =>
        {
            if (IsTokenRequest(request))
            {
                return Task.FromResult(FakeHttpHandler.Token());
            }

            return apiCalls++ == 0
                ? throw new HttpRequestException("connection reset")
                : Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, VerifiedJson));
        });

        await client.VerifyAsync(new VerificationRequest(Barcode));

        Assert.Equal(2, apiCalls);
    }

    [Fact]
    public async Task ARejectedTokenIsRefreshedOnce()
    {
        int tokens = 0;
        int apiCalls = 0;
        (VerifierClient client, _) = CreateClient((request, _, _) =>
        {
            if (IsTokenRequest(request))
            {
                tokens++;
                return Task.FromResult(FakeHttpHandler.Token($"token-{tokens}"));
            }

            return Task.FromResult(apiCalls++ == 0
                ? FakeHttpHandler.Json(HttpStatusCode.Unauthorized, """{"error":"Unauthorized","code":"UNAUTHORIZED"}""")
                : FakeHttpHandler.Json(HttpStatusCode.OK, VerifiedJson));
        });

        await client.VerifyAsync(new VerificationRequest(Barcode));

        Assert.Equal(2, tokens);
        Assert.Equal(2, apiCalls);
    }

    [Fact]
    public async Task TokensAreReusedAcrossCalls()
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(Respond((HttpStatusCode.OK, VerifiedJson)));

        await client.VerifyAsync(new VerificationRequest(Barcode));
        await client.VerifyAsync(new VerificationRequest(Barcode));

        Assert.Single(handler.TokenRequests);
    }

    [Fact]
    public async Task TokenFailuresThrowAuthExceptions()
    {
        (VerifierClient client, _) = CreateClient((_, _, _) =>
            Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.Forbidden, """{"error":"access_denied"}""")));

        VerifierAuthException error = await Assert.ThrowsAsync<VerifierAuthException>(() => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.Equal(403, error.Status);
        Assert.False(error.IsRetryable);
    }

    [Fact]
    public async Task UnreachableTokenEndpointsAreRetryable()
    {
        (VerifierClient client, _) = CreateClient((_, _, _) => throw new HttpRequestException("connection refused"));

        VerifierAuthException error = await Assert.ThrowsAsync<VerifierAuthException>(() => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.Null(error.Status);
        Assert.True(error.IsRetryable);
    }

    [Fact]
    public async Task UnexpectedSuccessBodiesThrowTransportExceptions()
    {
        (VerifierClient client, _) = CreateClient(Respond((HttpStatusCode.OK, """{"verified":false}""")));

        VerifierTransportException error = await Assert.ThrowsAsync<VerifierTransportException>(() => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.False(error.IsRetryable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{\"verified\":false}")]
    public async Task MalformedResponsesPreserveDiagnostics(string body)
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient((request, _, _) =>
        {
            if (IsTokenRequest(request))
            {
                return Task.FromResult(FakeHttpHandler.Token());
            }

            HttpResponseMessage response = FakeHttpHandler.Json(HttpStatusCode.OK, body);
            response.Headers.Add("X-Request-Id", "malformed-1");
            return Task.FromResult(response);
        });

        VerifierTransportException error = await Assert.ThrowsAsync<VerifierTransportException>(
            () => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.Equal(200, error.Status);
        Assert.Equal("malformed-1", error.RequestId);
        Assert.False(error.IsRetryable);
        Assert.Null(error.RetryAfter);
        Assert.Single(handler.ApiRequests);
        Assert.DoesNotContain(Barcode, error.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task FinalServiceResponsePreservesRetryAfter(int maxRetries)
    {
        int apiCalls = 0;
        (VerifierClient client, FakeHttpHandler handler) = CreateClient((request, _, _) =>
        {
            if (IsTokenRequest(request))
            {
                return Task.FromResult(FakeHttpHandler.Token());
            }

            apiCalls++;
            HttpResponseMessage response = FakeHttpHandler.Json(
                (HttpStatusCode)429,
                """{"error":"Busy","code":"RATE_LIMITED"}""");
            response.Headers.Add("Retry-After", apiCalls.ToString());
            response.Headers.Add("X-Request-Id", $"request-{apiCalls}");
            return Task.FromResult(response);
        }, maxRetries: maxRetries);
        var delays = new List<TimeSpan>();
        client.DelayAsync = (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        };

        VerifierServiceException error = await Assert.ThrowsAsync<VerifierServiceException>(
            () => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.Equal(TimeSpan.FromSeconds(1 + maxRetries), error.RetryAfter);
        Assert.Equal($"request-{1 + maxRetries}", error.RequestId);
        Assert.Equal(1 + maxRetries, handler.ApiRequests.Count());
        Assert.Equal(maxRetries, delays.Count);
        if (maxRetries > 0)
        {
            Assert.Equal(TimeSpan.FromSeconds(1), delays[0]);
        }
    }

    [Fact]
    public async Task RetryAfterBeyondTheDeadlineReportsTheResponse()
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(
            (request, _, _) =>
            {
                if (IsTokenRequest(request))
                {
                    return Task.FromResult(FakeHttpHandler.Token());
                }

                HttpResponseMessage response = FakeHttpHandler.Json(
                    (HttpStatusCode)429,
                    """{"error":"Busy","code":"RATE_LIMITED"}""");
                response.Headers.Add("Retry-After", "60");
                return Task.FromResult(response);
            },
            timeout: TimeSpan.FromSeconds(30));
        var delays = new List<TimeSpan>();
        client.DelayAsync = (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        };

        VerifierServiceException error = await Assert.ThrowsAsync<VerifierServiceException>(
            () => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.Equal(VerifierErrorCodes.RateLimited, error.Code);
        Assert.Equal(TimeSpan.FromSeconds(60), error.RetryAfter);
        Assert.True(error.IsRetryable);
        Assert.Single(handler.ApiRequests);
        Assert.Empty(delays);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("Wed, 01 Jan 2020 00:00:00 GMT")]
    public async Task MissingOrNonPositiveRetryAfterIsNotExposed(string? header)
    {
        (VerifierClient client, _) = CreateClient((request, _, _) =>
        {
            if (IsTokenRequest(request))
            {
                return Task.FromResult(FakeHttpHandler.Token());
            }

            HttpResponseMessage response = FakeHttpHandler.Json(HttpStatusCode.ServiceUnavailable, "{}");
            if (header is not null)
            {
                response.Headers.TryAddWithoutValidation("Retry-After", header);
            }

            return Task.FromResult(response);
        }, maxRetries: 0);

        VerifierServiceException error = await Assert.ThrowsAsync<VerifierServiceException>(
            () => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.True(error.IsRetryable);
        Assert.Null(error.RetryAfter);
    }

    [Fact]
    public async Task RetryAfterDoesNotMakeForbiddenResponsesRetryable()
    {
        (VerifierClient client, _) = CreateClient((request, _, _) =>
        {
            if (IsTokenRequest(request))
            {
                return Task.FromResult(FakeHttpHandler.Token());
            }

            HttpResponseMessage response = FakeHttpHandler.Json(HttpStatusCode.Forbidden, "{}");
            response.Headers.Add("Retry-After", "60");
            return Task.FromResult(response);
        });

        VerifierAuthException error = await Assert.ThrowsAsync<VerifierAuthException>(
            () => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.False(error.IsRetryable);
        Assert.Equal(TimeSpan.FromSeconds(60), error.RetryAfter);
    }

    [Fact]
    public async Task RetryAfterDatesAreExposedAsRemainingDelay()
    {
        DateTimeOffset retryAt = DateTimeOffset.UtcNow.AddMinutes(2);
        (VerifierClient client, _) = CreateClient((_, _, _) =>
        {
            HttpResponseMessage response = FakeHttpHandler.Json(HttpStatusCode.ServiceUnavailable, "{}");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAt);
            response.Headers.Add("X-Request-Id", "token-1");
            return Task.FromResult(response);
        });

        VerifierAuthException error = await Assert.ThrowsAsync<VerifierAuthException>(
            () => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.True(error.IsRetryable);
        Assert.Equal(503, error.Status);
        Assert.Null(error.RequestId);
        Assert.InRange(error.RetryAfter!.Value.TotalSeconds, 110, 120);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    public async Task MalformedTokenResponsesCarryNoRequestId(string body)
    {
        (VerifierClient client, _) = CreateClient((_, _, _) =>
        {
            HttpResponseMessage response = FakeHttpHandler.Json(HttpStatusCode.OK, body);
            response.Headers.Add("X-Request-Id", "token-malformed-1");
            return Task.FromResult(response);
        });

        VerifierAuthException error = await Assert.ThrowsAsync<VerifierAuthException>(
            () => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.True(error.IsRetryable);
        Assert.Null(error.RequestId);
        Assert.Null(error.RetryAfter);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    public async Task MalformedTokenResponsesPreserveRetryAfter(string body)
    {
        (VerifierClient client, _) = CreateClient((_, _, _) =>
        {
            HttpResponseMessage response = FakeHttpHandler.Json(HttpStatusCode.OK, body);
            response.Headers.Add("Retry-After", "60");
            return Task.FromResult(response);
        });

        VerifierAuthException error = await Assert.ThrowsAsync<VerifierAuthException>(
            () => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.True(error.IsRetryable);
        Assert.Equal(TimeSpan.FromSeconds(60), error.RetryAfter);
    }

    [Fact]
    public async Task EmptyResponseReferencesRemainTransportFailures()
    {
        string json = VerifiedJson.Replace(Reference, "");
        (VerifierClient client, _) = CreateClient(Respond((HttpStatusCode.OK, json)));

        await Assert.ThrowsAsync<VerifierTransportException>(() => client.VerifyAsync(new VerificationRequest(Barcode)));
    }

    [Fact]
    public async Task SlowCallsTimeOut()
    {
        (VerifierClient client, _) = CreateClient(
            async (request, _, cancellationToken) =>
            {
                if (IsTokenRequest(request))
                {
                    return FakeHttpHandler.Token();
                }

                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("unreachable");
            },
            timeout: TimeSpan.FromMilliseconds(50));

        VerifierTimeoutException error = await Assert.ThrowsAsync<VerifierTimeoutException>(() => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.True(error.IsRetryable);
    }

    [Fact]
    public async Task CallerCancellationIsNotATimeout()
    {
        (VerifierClient client, _) = CreateClient(Respond((HttpStatusCode.OK, VerifiedJson)));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.VerifyAsync(new VerificationRequest(Barcode), cancelled.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyAndOversizedBarcodesAreRejectedLocally(bool oversized)
    {
        int length = oversized ? VerifierClient.MaxBarcodeLength + 1 : 0;
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(Respond((HttpStatusCode.OK, VerifiedJson)));

        // The rejection is in the task, not thrown synchronously.
        Task<VerificationResult> verification = client.VerifyAsync(new VerificationRequest(new string('A', length)));
        VerifierRejectedException error = await Assert.ThrowsAsync<VerifierRejectedException>(() => verification);

        Assert.Equal(VerifierErrorCodes.NotVerified, error.Code);
        Assert.Null(error.Status);
        Assert.Null(error.Reason);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NullRequestsAndBarcodesAreProgrammerErrors()
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(Respond((HttpStatusCode.OK, VerifiedJson)));

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.VerifyAsync((VerificationRequest)null!));
        Assert.Throws<ArgumentNullException>(() => new VerificationRequest(null!));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RequestsAreSentAsGivenAndOmitTheBarcodeFromToString()
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(Respond((HttpStatusCode.OK, VerifiedJson)));
        var request = new VerificationRequest(Barcode);

        await client.VerifyAsync(request);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.ApiRequests).Body);
        Assert.Equal(Barcode, body.RootElement.GetProperty("barcode").GetString());
        Assert.DoesNotContain(Barcode, request.ToString());
    }

    [Fact]
    public void OptionsAreValidated()
    {
        VerifierAuth auth = VerifierAuth.ClientCredentials("id", "secret");

        Assert.Throws<ArgumentNullException>(() => new VerifierClient(null!));
        Assert.Throws<ArgumentException>(() => new VerifierClient(new VerifierClientOptions()));
        Assert.Throws<ArgumentException>(() => new VerifierClient(new VerifierClientOptions
        {
            Auth = auth,
            VerifierBaseUrl = new Uri("http://verify.example.com"),
        }));
        Assert.Throws<ArgumentException>(() => new VerifierClient(new VerifierClientOptions
        {
            Auth = VerifierAuth.ClientCredentials("id", "secret", new Uri("https://auth.example.com/oauth/token")),
        }));
        Assert.Throws<ArgumentException>(() => new VerifierClient(new VerifierClientOptions { Auth = auth, MaxRetries = -1 }));
        Assert.Throws<ArgumentException>(() => new VerifierClient(new VerifierClientOptions { Auth = auth, Timeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VerifierClient(new VerifierClientOptions
        {
            Auth = auth,
            Environment = (VerifierEnvironment)7,
        }));
    }

    [Fact]
    public async Task LocalhostOverridesAreAllowedForDevelopment()
    {
        var handler = new FakeHttpHandler { Responder = Respond((HttpStatusCode.OK, VerifiedJson)) };
        var client = new VerifierClient(new VerifierClientOptions
        {
            Auth = VerifierAuth.ClientCredentials("id", "secret", new Uri("http://localhost:4010/oauth/token")),
            VerifierBaseUrl = new Uri("http://localhost:4020/"),
            HttpClient = new HttpClient(handler),
        });

        await client.VerifyAsync(new VerificationRequest(Barcode));

        Assert.Equal("http://localhost:4010/oauth/token", Assert.Single(handler.TokenRequests).Uri.AbsoluteUri);
        Assert.Equal("http://localhost:4020/v1/verifications/payload", Assert.Single(handler.ApiRequests).Uri.AbsoluteUri);
    }

    [Fact]
    public async Task EveryAttemptSendsTheRequestsIdempotencyKey()
    {
        int apiCalls = 0;
        (VerifierClient client, FakeHttpHandler handler) = CreateClient((request, _, _) =>
        {
            if (IsTokenRequest(request))
            {
                return Task.FromResult(FakeHttpHandler.Token());
            }

            return apiCalls++ switch
            {
                0 => throw new HttpRequestException("connection reset"),
                1 => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.Unauthorized, """{"error":"Expired","code":"UNAUTHORIZED"}""")),
                2 => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.ServiceUnavailable, """{"error":"Busy","code":"SERVICE_UNAVAILABLE"}""")),
                _ => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, VerifiedJson)),
            };
        });
        var request = new VerificationRequest(Barcode);

        VerificationResult result = await client.VerifyAsync(request);

        string expected = request.IdempotencyKey.ToString("D");
        Assert.Equal(4, handler.ApiRequests.Count());
        Assert.All(handler.ApiRequests, sent => Assert.Equal(expected, sent.IdempotencyKey));
        Assert.Equal(request.IdempotencyKey, result.IdempotencyKey);
    }

    [Fact]
    public async Task EachRequestHasItsOwnIdempotencyKeyAndAReplayReusesIt()
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(Respond((HttpStatusCode.OK, VerifiedJson)));
        var replayed = new VerificationRequest(Barcode);

        await client.VerifyAsync(new VerificationRequest(Barcode));
        await client.VerifyAsync(new VerificationRequest(Barcode));
        await client.VerifyAsync(replayed);
        await client.VerifyAsync(replayed);

        string?[] keys = handler.ApiRequests.Select(sent => sent.IdempotencyKey).ToArray();
        Assert.Equal(3, keys.Distinct().Count());
        Assert.Equal(replayed.IdempotencyKey.ToString("D"), keys[2]);
        Assert.Equal(keys[2], keys[3]);
    }

    [Fact]
    public async Task ASuppliedIdempotencyKeyIsSent()
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(Respond((HttpStatusCode.OK, VerifiedJson)));
        var key = Guid.Parse("5f0c2a7e-8d4b-4c19-9e3a-7b6d1f2c4e80");

        VerificationResult result = await client.VerifyAsync(new VerificationRequest(Barcode) { IdempotencyKey = key });

        Assert.Equal("5f0c2a7e-8d4b-4c19-9e3a-7b6d1f2c4e80", Assert.Single(handler.ApiRequests).IdempotencyKey);
        Assert.Equal(key, result.IdempotencyKey);
    }

    [Fact]
    public async Task FailuresCarryTheIdempotencyKey()
    {
        (VerifierClient service, _) = CreateClient(Respond((HttpStatusCode.ServiceUnavailable, """{"error":"Busy","code":"SERVICE_UNAVAILABLE"}""")));
        (VerifierClient transport, _) = CreateClient((request, _, _) => IsTokenRequest(request)
            ? Task.FromResult(FakeHttpHandler.Token())
            : throw new HttpRequestException("connection reset"));
        (VerifierClient rejecting, _) = CreateClient(Respond(((HttpStatusCode)422, """{"error":"Not verified","code":"NOT_VERIFIED"}""")));
        var request = new VerificationRequest(Barcode);

        VerifierException serviceError = await Assert.ThrowsAsync<VerifierServiceException>(() => service.VerifyAsync(request));
        VerifierException transportError = await Assert.ThrowsAsync<VerifierTransportException>(() => transport.VerifyAsync(request));
        VerifierException rejection = await Assert.ThrowsAsync<VerifierRejectedException>(() => rejecting.VerifyAsync(request));

        Assert.Equal(request.IdempotencyKey, serviceError.IdempotencyKey);
        Assert.Equal(request.IdempotencyKey, transportError.IdempotencyKey);
        Assert.Equal(request.IdempotencyKey, rejection.IdempotencyKey);
    }

    [Fact]
    public async Task TimeoutsCarryTheIdempotencyKey()
    {
        (VerifierClient client, _) = CreateClient(
            async (request, _, cancellationToken) =>
            {
                if (IsTokenRequest(request))
                {
                    return FakeHttpHandler.Token();
                }

                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("unreachable");
            },
            timeout: TimeSpan.FromMilliseconds(50));
        var request = new VerificationRequest(Barcode);

        VerifierTimeoutException error = await Assert.ThrowsAsync<VerifierTimeoutException>(() => client.VerifyAsync(request));

        Assert.Equal(request.IdempotencyKey, error.IdempotencyKey);
    }

    [Fact]
    public async Task TokenFailuresCarryTheIdempotencyKey()
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient((_, _, _) =>
            Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.ServiceUnavailable, """{"error":"unavailable"}""")));
        var request = new VerificationRequest(Barcode);

        VerifierAuthException error = await Assert.ThrowsAsync<VerifierAuthException>(() => client.VerifyAsync(request));

        Assert.Empty(handler.ApiRequests);
        Assert.Equal(request.IdempotencyKey, error.IdempotencyKey);
    }

    [Fact]
    public async Task LocalRejectionsHaveNoIdempotencyKey()
    {
        (VerifierClient client, _) = CreateClient(Respond((HttpStatusCode.OK, VerifiedJson)));

        VerifierRejectedException error = await Assert.ThrowsAsync<VerifierRejectedException>(() => client.VerifyAsync(new VerificationRequest(string.Empty)));

        Assert.Null(error.IdempotencyKey);
    }

    [Fact]
    public async Task AReusedIdempotencyKeyIsAServiceErrorThatIsNotRetried()
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(Respond((
            HttpStatusCode.Conflict,
            """{"error":"Idempotency key reused","code":"IDEMPOTENCY_KEY_REUSED"}""")));

        VerifierServiceException error = await Assert.ThrowsAsync<VerifierServiceException>(() => client.VerifyAsync(new VerificationRequest(Barcode)));

        Assert.Equal(VerifierErrorCodes.IdempotencyKeyReused, error.Code);
        Assert.Equal(409, error.Status);
        Assert.False(error.IsRetryable);
        Assert.Single(handler.ApiRequests);
    }
}
