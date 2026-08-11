using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Verifiabl.Client;
using Xunit;

namespace Verifiabl.Tests;

public class ClientRequestTests
{
    private const string Reference = "u0FE9WLIS7GYKQnpJPygBw";

    [Fact]
    public async Task RejectsADefaultIssuedAt()
    {
        var handler = new FakeHttpHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.IssuedAt = default;

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.RegisterNonPiiAsync(request));

        Assert.Contains("IssuedAt is required", exception.Message);
    }

    private static RegisterNonPiiRequest ValidRequest() => new()
    {
        Schema = "au.payslip.v1",
        IssuedAt = new DateTimeOffset(2026, 5, 31, 11, 2, 3, TimeSpan.FromHours(10)),
        PayslipNonPii = new PayslipNonPii
        {
            PeriodStart = "2026-05-01",
            PeriodEnd = "2026-05-31",
        },
        EncryptionMetadata = new EncryptionMetadata
        {
            Iv = "AAAAAAAAAAAAAAAA",
            Tag = "AAAAAAAAAAAAAAAAAAAAAA",
        },
    };

    private static VerifiablClient Client(
        FakeHttpHandler handler,
        Action<VerifiablClientOptions>? configure = null)
    {
        var options = new VerifiablClientOptions
        {
            Auth = VerifiablAuth.ApiKey("static-key"),
            HttpClient = new HttpClient(handler),
        };
        configure?.Invoke(options);
        return new VerifiablClient(options);
    }

    private static FakeHttpHandler RegistrationHandler(string reference = Reference)
    {
        return new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(
                HttpStatusCode.OK,
                $"{{\"verifiabl_reference\":\"{reference}\"}}")),
        };
    }

    [Fact]
    public async Task SendsRegistrationToTheProductionIssuerOriginWithBearerAuth()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);

        RegisterNonPiiResponse response = await client.RegisterNonPiiAsync(ValidRequest());

        Assert.Equal(Reference, response.VerifiablReference);
        CapturedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal("https://register.verifiabl.io/v1/registerNonPII", sent.Uri.ToString());
        Assert.Equal("Bearer static-key", sent.Authorization);

        using JsonDocument body = JsonDocument.Parse(sent.Body);
        Assert.Equal("au.payslip.v1", body.RootElement.GetProperty("schema").GetString());
        // The +10:00 offset input is sent as UTC, millisecond precision with a Z
        // suffix, matching the Node SDK's Date.toISOString() wire value.
        Assert.Equal(
            "2026-05-31T01:02:03.000Z",
            body.RootElement.GetProperty("issued_at").GetString());
        JsonElement nonPii = body.RootElement.GetProperty("payslip_non_pii");
        Assert.Equal("2026-05-01", nonPii.GetProperty("period_start").GetString());
        Assert.Equal("2026-05-31", nonPii.GetProperty("period_end").GetString());
        JsonElement metadata = body.RootElement.GetProperty("encryption_metadata");
        Assert.Equal("AAAAAAAAAAAAAAAA", metadata.GetProperty("iv").GetString());
        Assert.False(metadata.TryGetProperty("key_version", out _));
    }

    [Fact]
    public async Task RoutesRegistrationToTheSandboxIssuerOrigin()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(
            handler,
            options => options.Environment = VerifiablEnvironment.Sandbox);

        await client.RegisterNonPiiAsync(ValidRequest());

        Assert.StartsWith(
            "https://register.sandbox.verifiabl.io/",
            Assert.Single(handler.Requests).Uri.ToString());
    }

    [Fact]
    public async Task PassesThroughProviderSpecificPayslipFields()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.PayslipNonPii.AdditionalData = new Dictionary<string, object?>
        {
            ["total_hours"] = 152,
            ["allowances"] = new[] { "meal", "travel" },
        };

        await client.RegisterNonPiiAsync(request);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        JsonElement nonPii = body.RootElement.GetProperty("payslip_non_pii");
        Assert.Equal(152, nonPii.GetProperty("total_hours").GetInt32());
        Assert.Equal(2, nonPii.GetProperty("allowances").GetArrayLength());
    }

    [Fact]
    public async Task GeneratesAClientSideReferenceForSingleRegistration()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);

        await client.RegisterNonPiiAsync(ValidRequest());

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        string? reference = body.RootElement.GetProperty("verifiabl_reference").GetString();
        Assert.True(VerifiablReference.IsValid(reference));
    }

    [Fact]
    public async Task UsesTheCallerSuppliedReferenceVerbatim()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.VerifiablReference = "u0FE9WLIS7GYKQnpJPygBw";

        await client.RegisterNonPiiAsync(request);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal(
            "u0FE9WLIS7GYKQnpJPygBw",
            body.RootElement.GetProperty("verifiabl_reference").GetString());
    }

    [Fact]
    public async Task RejectsAMalformedCallerSuppliedReference()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.VerifiablReference = "not-a-reference";

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.RegisterNonPiiAsync(request));

        Assert.Contains("VerifiablReference", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MapsNestedNumericAndNullAdditionalDataOntoTheWireBody()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.PayslipNonPii.AdditionalData = new Dictionary<string, object?>
        {
            ["currency"] = "AUD",
            ["gross_cents"] = 1_234_500L,
            ["rate"] = 42.5m,
            ["hours"] = 7.6,
            ["final"] = true,
            ["comment"] = null,
            ["counts"] = new[] { 1, 2, 3 },
            ["employer"] = new Dictionary<string, object?>
            {
                ["abn"] = "12345678901",
                ["branches"] = new object?[] { 1, "two", null },
            },
        };

        await client.RegisterNonPiiAsync(request);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        JsonElement nonPii = body.RootElement.GetProperty("payslip_non_pii");
        Assert.Equal("AUD", nonPii.GetProperty("currency").GetString());
        Assert.Equal(1_234_500L, nonPii.GetProperty("gross_cents").GetInt64());
        Assert.Equal(42.5m, nonPii.GetProperty("rate").GetDecimal());
        Assert.Equal(7.6, nonPii.GetProperty("hours").GetDouble());
        Assert.True(nonPii.GetProperty("final").GetBoolean());
        Assert.Equal(JsonValueKind.Null, nonPii.GetProperty("comment").ValueKind);
        Assert.Equal(3, nonPii.GetProperty("counts").GetArrayLength());
        JsonElement employer = nonPii.GetProperty("employer");
        Assert.Equal("12345678901", employer.GetProperty("abn").GetString());
        Assert.Equal(JsonValueKind.Null, employer.GetProperty("branches")[2].ValueKind);
    }

    [Fact]
    public async Task MapsAReadOnlyDictionaryNestedValueAsAnObject()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.PayslipNonPii.AdditionalData = new Dictionary<string, object?>
        {
            ["employer"] = new ReadOnlyPairs(new Dictionary<string, object?>
            {
                ["abn"] = "12345678901",
            }),
        };

        await client.RegisterNonPiiAsync(request);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        JsonElement employer = body.RootElement
            .GetProperty("payslip_non_pii")
            .GetProperty("employer");
        Assert.Equal(JsonValueKind.Object, employer.ValueKind);
        Assert.Equal("12345678901", employer.GetProperty("abn").GetString());
    }

    /// <summary>
    /// Implements only the read-only dictionary surface, unlike
    /// Dictionary/ReadOnlyDictionary/ImmutableDictionary which all also carry
    /// non-generic IDictionary — the shape that regressed to an array.
    /// </summary>
    private sealed class ReadOnlyPairs(Dictionary<string, object?> inner)
        : IReadOnlyDictionary<string, object?>
    {
        public object? this[string key] => inner[key];

        public IEnumerable<string> Keys => inner.Keys;

        public IEnumerable<object?> Values => inner.Values;

        public int Count => inner.Count;

        public bool ContainsKey(string key) => inner.ContainsKey(key);

        public bool TryGetValue(string key, out object? value) =>
            inner.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            inner.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    [Fact]
    public async Task RejectsUnsupportedAdditionalDataValuesNamingTheKey()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.PayslipNonPii.AdditionalData = new Dictionary<string, object?>
        {
            ["payment_date"] = new DateTime(2026, 5, 31),
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.RegisterNonPiiAsync(request));

        Assert.Contains("payment_date", exception.Message);
        Assert.Contains("System.DateTime", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DoesNotLetPassthroughKeysOverrideTheMappedPeriodDates()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.PayslipNonPii.AdditionalData = new Dictionary<string, object?>
        {
            ["period_start"] = "1999-01-01",
            ["period_end"] = "1999-01-31",
        };

        await client.RegisterNonPiiAsync(request);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        JsonElement nonPii = body.RootElement.GetProperty("payslip_non_pii");
        Assert.Equal("2026-05-01", nonPii.GetProperty("period_start").GetString());
        Assert.Equal("2026-05-31", nonPii.GetProperty("period_end").GetString());
    }

    [Fact]
    public async Task LetsExplicitIssuerBaseUrlOverridesWinOverTheEnvironment()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler, options =>
        {
            options.Environment = VerifiablEnvironment.Sandbox;
            options.IssuerBaseUrl = new Uri("https://issuer.example.com/ignored/path");
        });

        await client.RegisterNonPiiAsync(ValidRequest());

        // Only the origin of the override is used.
        Assert.Equal(
            "https://issuer.example.com/v1/registerNonPII",
            Assert.Single(handler.Requests).Uri.ToString());
    }

    [Fact]
    public async Task MapsTheApiResponseToABarcodeImageForRegisterAndBuildBarcode()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(
                HttpStatusCode.OK,
                $"{{\"verifiabl_reference\":\"{Reference}\"," +
                "\"barcode\":{\"format\":\"png\",\"data\":\"aGVsbG8=\"}}")),
        };
        VerifiablClient client = Client(handler);

        var request = new RegisterAndBuildBarcodeRequest
        {
            Schema = "au.payslip.v1",
            IssuedAt = DateTimeOffset.UtcNow,
            PayslipNonPii = new PayslipNonPii { PeriodStart = "2026-05-01", PeriodEnd = "2026-05-31" },
            EncryptionMetadata = ValidRequest().EncryptionMetadata,
            EncryptedPii = "abc123",
        };
        RegisterAndBuildBarcodeResponse response = await client.RegisterAndBuildBarcodeAsync(request);

        Assert.Equal(Reference, response.VerifiablReference);
        Assert.Equal("png", response.Barcode.Format);
        Assert.Equal("aGVsbG8=", response.Barcode.Data);
        Assert.Equal(
            "https://register.verifiabl.io/v1/registerAndBuildBarcode",
            Assert.Single(handler.Requests).Uri.ToString());
        using JsonDocument body = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal("abc123", body.RootElement.GetProperty("encrypted_pii").GetString());
    }

    [Fact]
    public async Task ThrowsVerifiablApiExceptionWithTheStableCodeOnApiErrors()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(
                HttpStatusCode.BadRequest,
                "{\"error\":\"Validation failed\",\"code\":\"VALIDATION_FAILED\"," +
                "\"field_errors\":[{\"path\":\"payslip_non_pii.period_start\",\"message\":\"bad date\"}]}")),
        };
        VerifiablClient client = Client(handler);

        VerifiablApiException exception = await Assert.ThrowsAsync<VerifiablApiException>(
            () => client.RegisterNonPiiAsync(ValidRequest()));

        Assert.Equal(400, exception.Status);
        Assert.Equal(VerifiablErrorCodes.ValidationFailed, exception.Code);
        Assert.Equal("Validation failed", exception.Message);
        VerifiablFieldError fieldError = Assert.Single(exception.Body!.FieldErrors!);
        Assert.Equal("payslip_non_pii.period_start", fieldError.Path);
        Assert.Equal("bad date", fieldError.Message);
    }

    [Fact]
    public async Task OmitsFieldErrorsWhenTheApiSendsNone()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(
                HttpStatusCode.Forbidden,
                "{\"error\":\"Forbidden\",\"code\":\"FORBIDDEN\"}")),
        };
        VerifiablClient client = Client(handler);

        VerifiablApiException exception = await Assert.ThrowsAsync<VerifiablApiException>(
            () => client.RegisterNonPiiAsync(ValidRequest()));

        Assert.Null(exception.Body!.FieldErrors);
    }

    [Fact]
    public async Task IncludesRequestIdsOnApiErrors()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) =>
            {
                HttpResponseMessage response = FakeHttpHandler.Json(
                    HttpStatusCode.InternalServerError,
                    "{\"error\":\"boom\",\"code\":\"INTERNAL_ERROR\"}");
                response.Headers.Add("x-request-id", "req-123");
                return Task.FromResult(response);
            },
        };
        VerifiablClient client = Client(handler);

        VerifiablApiException exception = await Assert.ThrowsAsync<VerifiablApiException>(
            () => client.RegisterNonPiiAsync(ValidRequest()));

        Assert.Equal("req-123", exception.RequestId);
    }

    [Fact]
    public async Task PassesThroughUnknownErrorCodes()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(
                (HttpStatusCode)429,
                "{\"error\":\"Slow down\",\"code\":\"RATE_LIMITED\"}")),
        };
        VerifiablClient client = Client(handler);

        VerifiablApiException exception = await Assert.ThrowsAsync<VerifiablApiException>(
            () => client.RegisterNonPiiAsync(ValidRequest()));

        Assert.Equal("RATE_LIMITED", exception.Code);
    }

    [Fact]
    public async Task SurvivesNonJsonErrorBodies()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(
                FakeHttpHandler.Html(HttpStatusCode.BadGateway, "<html>gateway error</html>")),
        };
        VerifiablClient client = Client(handler);

        VerifiablApiException exception = await Assert.ThrowsAsync<VerifiablApiException>(
            () => client.RegisterNonPiiAsync(ValidRequest()));

        Assert.Equal(502, exception.Status);
        Assert.Equal(VerifiablErrorCodes.InternalError, exception.Code);
        Assert.Null(exception.Body);
    }

    [Fact]
    public async Task ToleratesAdditiveFieldsInSuccessResponses()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(
                HttpStatusCode.OK,
                $"{{\"verifiabl_reference\":\"{Reference}\",\"future_field\":42}}")),
        };
        VerifiablClient client = Client(handler);

        RegisterNonPiiResponse response = await client.RegisterNonPiiAsync(ValidRequest());

        Assert.Equal(Reference, response.VerifiablReference);
    }

    [Fact]
    public async Task EmitsRequestAndResponseHooks()
    {
        FakeHttpHandler handler = RegistrationHandler();
        var requests = new List<VerifiablRequestEvent>();
        var responses = new List<VerifiablResponseEvent>();
        VerifiablClient client = Client(handler, options =>
        {
            options.OnRequest = requests.Add;
            options.OnResponse = responses.Add;
        });

        await client.RegisterNonPiiAsync(ValidRequest());

        VerifiablRequestEvent request = Assert.Single(requests);
        Assert.Equal("/v1/registerNonPII", request.Path);
        Assert.Equal("POST", request.Method);
        VerifiablResponseEvent response = Assert.Single(responses);
        Assert.Equal(200, response.Status);
        Assert.True(response.ElapsedMs >= 0);
    }

    [Fact]
    public async Task HookFailuresDoNotChangeRequestBehaviour()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler, options =>
        {
            options.OnRequest = _ => throw new InvalidOperationException("hook boom");
            options.OnResponse = _ => throw new InvalidOperationException("hook boom");
        });

        RegisterNonPiiResponse response = await client.RegisterNonPiiAsync(ValidRequest());

        Assert.Equal(Reference, response.VerifiablReference);
    }

    [Fact]
    public async Task EmitsErrorHooksWhenTheRequestFails()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) =>
                Task.FromException<HttpResponseMessage>(new HttpRequestException("socket closed")),
        };
        var errors = new List<VerifiablErrorEvent>();
        // Retries are disabled so the hook count maps to exactly one attempt.
        VerifiablClient client = Client(handler, options =>
        {
            options.OnError = errors.Add;
            options.MaxRetries = 0;
        });

        VerifiablTransportException thrown = await Assert.ThrowsAsync<VerifiablTransportException>(
            () => client.RegisterNonPiiAsync(ValidRequest()));

        Assert.IsType<HttpRequestException>(thrown.InnerException);
        VerifiablErrorEvent error = Assert.Single(errors);
        Assert.Equal("/v1/registerNonPII", error.Path);
        Assert.IsType<HttpRequestException>(error.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"verifiabl_reference\":\"nope\"}")]
    public async Task WrapsUnusableSuccessBodiesAsTransportFailures(string body)
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, body)),
        };
        VerifiablClient client = Client(handler);

        await Assert.ThrowsAsync<VerifiablTransportException>(
            () => client.RegisterNonPiiAsync(ValidRequest()));
    }

    [Theory]
    [InlineData("payslip.v1")]
    [InlineData("AU.payslip.v1")]
    [InlineData("au.payslip.1")]
    public async Task ValidatesTheSchemaBeforeSending(string schema)
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.Schema = schema;

        await Assert.ThrowsAsync<ArgumentException>(() => client.RegisterNonPiiAsync(request));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("2026-13-01")]
    [InlineData("01-05-2026")]
    [InlineData("2026-02-30")]
    public async Task ValidatesPeriodDatesBeforeSending(string periodStart)
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.PayslipNonPii.PeriodStart = periodStart;

        await Assert.ThrowsAsync<ArgumentException>(() => client.RegisterNonPiiAsync(request));

        Assert.Empty(handler.Requests);
    }
}
