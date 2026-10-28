using System.Net;
using System.Net.Http;
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
            Auth = VerifierAuth.ClientCredentials("lender-client", "lender-secret"),
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

        await client.VerifyAsync(Barcode);

        CapturedRequest token = Assert.Single(handler.TokenRequests);
        Assert.Equal("https://auth.sandbox.verifiabl.io/oauth/token", token.Uri.AbsoluteUri);
        using JsonDocument tokenBody = JsonDocument.Parse(token.Body);
        Assert.Equal("https://verify.sandbox.verifiabl.io", tokenBody.RootElement.GetProperty("audience").GetString());
        Assert.Equal("verifiabl:verifier", tokenBody.RootElement.GetProperty("scope").GetString());

        CapturedRequest api = Assert.Single(handler.ApiRequests);
        Assert.Equal("https://verify.sandbox.verifiabl.io/v1/verifications/payload", api.Uri.AbsoluteUri);
        Assert.Equal("Bearer token-1", api.Authorization);
        Assert.StartsWith("verifiabl-verifier-dotnet/", api.UserAgent);
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

        await client.VerifyAsync(payload);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.ApiRequests).Body);
        Assert.Equal(payload, body.RootElement.GetProperty("barcode").GetString());
    }

    [Fact]
    public async Task FutureResponseSchemasAndFieldsArePreserved()
    {
        string json = VerifiedJson.Replace("au.payslip.v2", "future.payslip.v3");
        (VerifierClient client, _) = CreateClient(Respond((HttpStatusCode.OK, json)));

        VerificationResult result = await client.VerifyAsync(Barcode);

        Assert.Equal("future.payslip.v3", result.Schema);
        Assert.Equal("T Nguyen", result.Employee.GetProperty("account_name").GetString());
        Assert.Equal("3000.00", result.Payslip.GetProperty("payslip_non_pii").GetProperty("gross").GetString());
    }

    [Fact]
    public async Task VerifyAsyncReturnsTheVerifiedPayslip()
    {
        (VerifierClient client, _) = CreateClient(Respond((HttpStatusCode.OK, VerifiedJson)));

        VerificationResult result = await client.VerifyAsync(Barcode);

        Assert.Equal(Reference, result.VerifiablReference);
        Assert.Equal("au.payslip.v2", result.Schema);
        Assert.Equal(new DateTimeOffset(2026, 6, 9, 9, 10, 11, 123, TimeSpan.Zero), result.DecryptedAt);
        Assert.Equal(EmployeeName, result.Employee.GetProperty("employee_name").GetString());
        Assert.Equal("3000.00", result.Payslip.GetProperty("payslip_non_pii").GetProperty("gross").GetString());
    }

    [Fact]
    public async Task ResultToStringOmitsEmployeeAndPayslipData()
    {
        (VerifierClient client, _) = CreateClient(Respond((HttpStatusCode.OK, VerifiedJson)));

        VerificationResult result = await client.VerifyAsync(Barcode);

        Assert.Contains(Reference, result.ToString());
        Assert.DoesNotContain(EmployeeName, result.ToString());
        Assert.DoesNotContain("3000.00", result.ToString());
    }

    [Fact]
    public async Task RejectionsCarryTheCodeAndReasonButNotTheBarcode()
    {
        (VerifierClient client, _) = CreateClient(Respond((
            HttpStatusCode.BadRequest,
            """{"error":"Decryption failed","code":"DECRYPTION_FAILED","reason":"no_key_matched"}""")));

        VerifierRejectedException error = await Assert.ThrowsAsync<VerifierRejectedException>(() => client.VerifyAsync(Barcode));

        Assert.Equal(400, error.Status);
        Assert.False(error.IsRetryable);
        Assert.Equal(VerifierErrorCodes.DecryptionFailed, error.Code);
        Assert.Equal("no_key_matched", error.Reason);
        Assert.DoesNotContain(Reference, error.Message);
    }

    [Fact]
    public async Task ValidationErrorsListTheFields()
    {
        (VerifierClient client, _) = CreateClient(Respond((
            HttpStatusCode.BadRequest,
            """{"error":"Validation failed","code":"VALIDATION_FAILED","field_errors":[{"path":"barcode","message":"Unrecognised barcode format"}]}""")));

        VerifierRejectedException error = await Assert.ThrowsAsync<VerifierRejectedException>(() => client.VerifyAsync("not a barcode"));

        VerifierFieldError field = Assert.Single(error.FieldErrors);
        Assert.Equal("barcode", field.Path);
        Assert.Equal("Unrecognised barcode format", field.Message);
    }

    [Fact]
    public async Task UnknownReferencesAreRejectedWithoutRetrying()
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(Respond((
            HttpStatusCode.NotFound,
            """{"error":"Not found","code":"VERIFIABL_REFERENCE_NOT_FOUND"}""")));

        VerifierRejectedException error = await Assert.ThrowsAsync<VerifierRejectedException>(() => client.VerifyAsync(Barcode));

        Assert.Equal(VerifierErrorCodes.VerifiablReferenceNotFound, error.Code);
        Assert.Single(handler.ApiRequests);
    }

    [Fact]
    public async Task UnavailableKeysAreServiceErrorsThatAreNotRetryable()
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(Respond((
            (HttpStatusCode)422,
            """{"error":"Unavailable","code":"KEY_VERSION_UNAVAILABLE","detail":"Contact support."}""")));

        VerifierServiceException error = await Assert.ThrowsAsync<VerifierServiceException>(() => client.VerifyAsync(Barcode));

        Assert.Equal(VerifierErrorCodes.KeyVersionUnavailable, error.Code);
        Assert.False(error.IsRetryable);
        Assert.Single(handler.ApiRequests);
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
            response.Headers.Add("x-request-id", "request-1");
            return Task.FromResult(response);
        });

        VerifierAuthException error = await Assert.ThrowsAsync<VerifierAuthException>(() => client.VerifyAsync(Barcode));

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

        VerifierAuthException error = await Assert.ThrowsAsync<VerifierAuthException>(() => client.VerifyAsync(Barcode));

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

        VerifierServiceException error = await Assert.ThrowsAsync<VerifierServiceException>(() => client.VerifyAsync(Barcode));

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

        await client.VerifyAsync(Barcode);

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

        VerifierServiceException error = await Assert.ThrowsAsync<VerifierServiceException>(() => client.VerifyAsync(Barcode));

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

        VerifierTransportException error = await Assert.ThrowsAsync<VerifierTransportException>(() => client.VerifyAsync(Barcode));

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

        await client.VerifyAsync(Barcode);

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

        await client.VerifyAsync(Barcode);

        Assert.Equal(2, tokens);
        Assert.Equal(2, apiCalls);
    }

    [Fact]
    public async Task TokensAreReusedAcrossCalls()
    {
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(Respond((HttpStatusCode.OK, VerifiedJson)));

        await client.VerifyAsync(Barcode);
        await client.VerifyAsync(Barcode);

        Assert.Single(handler.TokenRequests);
    }

    [Fact]
    public async Task TokenFailuresThrowAuthExceptions()
    {
        (VerifierClient client, _) = CreateClient((_, _, _) =>
            Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.Forbidden, """{"error":"access_denied"}""")));

        VerifierAuthException error = await Assert.ThrowsAsync<VerifierAuthException>(() => client.VerifyAsync(Barcode));

        Assert.Equal(403, error.Status);
        Assert.False(error.IsRetryable);
    }

    [Fact]
    public async Task UnreachableTokenEndpointsAreRetryable()
    {
        (VerifierClient client, _) = CreateClient((_, _, _) => throw new HttpRequestException("connection refused"));

        VerifierAuthException error = await Assert.ThrowsAsync<VerifierAuthException>(() => client.VerifyAsync(Barcode));

        Assert.Null(error.Status);
        Assert.True(error.IsRetryable);
    }

    [Fact]
    public async Task UnexpectedSuccessBodiesThrowTransportExceptions()
    {
        (VerifierClient client, _) = CreateClient(Respond((HttpStatusCode.OK, """{"verified":false}""")));

        VerifierTransportException error = await Assert.ThrowsAsync<VerifierTransportException>(() => client.VerifyAsync(Barcode));

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
            response.Headers.Add("x-request-id", "malformed-1");
            return Task.FromResult(response);
        });

        VerifierTransportException error = await Assert.ThrowsAsync<VerifierTransportException>(
            () => client.VerifyAsync(Barcode));

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
            response.Headers.Add("Retry-After", (60 + apiCalls).ToString());
            response.Headers.Add("x-request-id", $"request-{apiCalls}");
            return Task.FromResult(response);
        }, maxRetries: maxRetries);
        var delays = new List<TimeSpan>();
        client.DelayAsync = (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        };

        VerifierServiceException error = await Assert.ThrowsAsync<VerifierServiceException>(
            () => client.VerifyAsync(Barcode));

        Assert.Equal(TimeSpan.FromSeconds(61 + maxRetries), error.RetryAfter);
        Assert.Equal($"request-{1 + maxRetries}", error.RequestId);
        Assert.Equal(1 + maxRetries, handler.ApiRequests.Count());
        Assert.Equal(maxRetries, delays.Count);
        if (maxRetries > 0)
        {
            Assert.Equal(TimeSpan.FromSeconds(61), delays[0]);
        }
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
            () => client.VerifyAsync(Barcode));

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
            () => client.VerifyAsync(Barcode));

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
            response.Headers.Add("request-id", "token-1");
            return Task.FromResult(response);
        });

        VerifierAuthException error = await Assert.ThrowsAsync<VerifierAuthException>(
            () => client.VerifyAsync(Barcode));

        Assert.True(error.IsRetryable);
        Assert.Equal(503, error.Status);
        Assert.Equal("token-1", error.RequestId);
        Assert.InRange(error.RetryAfter!.Value.TotalSeconds, 110, 120);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    public async Task MalformedTokenResponsesPreserveRequestIds(string body)
    {
        (VerifierClient client, _) = CreateClient((_, _, _) =>
        {
            HttpResponseMessage response = FakeHttpHandler.Json(HttpStatusCode.OK, body);
            response.Headers.Add("x-verifiabl-request-id", "token-malformed-1");
            return Task.FromResult(response);
        });

        VerifierAuthException error = await Assert.ThrowsAsync<VerifierAuthException>(
            () => client.VerifyAsync(Barcode));

        Assert.True(error.IsRetryable);
        Assert.Equal("token-malformed-1", error.RequestId);
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
            () => client.VerifyAsync(Barcode));

        Assert.True(error.IsRetryable);
        Assert.Equal(TimeSpan.FromSeconds(60), error.RetryAfter);
    }

    [Fact]
    public async Task EmptyResponseReferencesRemainTransportFailures()
    {
        string json = VerifiedJson.Replace(Reference, "");
        (VerifierClient client, _) = CreateClient(Respond((HttpStatusCode.OK, json)));

        await Assert.ThrowsAsync<VerifierTransportException>(() => client.VerifyAsync(Barcode));
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

        VerifierTimeoutException error = await Assert.ThrowsAsync<VerifierTimeoutException>(() => client.VerifyAsync(Barcode));

        Assert.True(error.IsRetryable);
    }

    [Fact]
    public async Task CallerCancellationIsNotATimeout()
    {
        (VerifierClient client, _) = CreateClient(Respond((HttpStatusCode.OK, VerifiedJson)));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.VerifyAsync(Barcode, cancelled.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyAndOversizedBarcodesAreRejectedLocally(bool oversized)
    {
        int length = oversized ? VerifierClient.MaxBarcodeLength + 1 : 0;
        (VerifierClient client, FakeHttpHandler handler) = CreateClient(Respond((HttpStatusCode.OK, VerifiedJson)));

        // The rejection is in the task, not thrown synchronously.
        Task<VerificationResult> verification = client.VerifyAsync(new string('A', length));
        VerifierRejectedException error = await Assert.ThrowsAsync<VerifierRejectedException>(() => verification);

        Assert.Equal(VerifierErrorCodes.ValidationFailed, error.Code);
        Assert.Null(error.Status);
        Assert.Equal("barcode", Assert.Single(error.FieldErrors).Path);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NullBarcodesAreProgrammerErrors()
    {
        (VerifierClient client, _) = CreateClient(Respond((HttpStatusCode.OK, VerifiedJson)));

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.VerifyAsync(null!));
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

        await client.VerifyAsync(Barcode);

        Assert.Equal("http://localhost:4010/oauth/token", Assert.Single(handler.TokenRequests).Uri.AbsoluteUri);
        Assert.Equal("http://localhost:4020/v1/verifications/payload", Assert.Single(handler.ApiRequests).Uri.AbsoluteUri);
    }
}
