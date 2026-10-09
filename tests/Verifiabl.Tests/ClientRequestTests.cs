using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Verifiabl.Client;
using Xunit;
using static Verifiabl.Tests.TestDates;

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
        Schema = PayslipSchemas.AustralianV2,
        IssuedAt = new DateTimeOffset(2026, 5, 31, 11, 2, 3, TimeSpan.FromHours(10)),
        PayslipNonPii = TestPayslips.Australian(),
        EncryptionMetadata = new EncryptionMetadata
        {
            Iv = new byte[12],
            Tag = new byte[16],
        },
    };

    /// <summary>A registration for a schema this SDK has no typed model for.</summary>
    private static RegisterNonPiiRequest FutureSchemaRequest()
    {
        RegisterNonPiiRequest request = ValidRequest();
        request.Schema = "au.payslip.v3";
        request.PayslipNonPii = TestPayslips.FreeForm();
        return request;
    }

    [Fact]
    public async Task MatchesSharedV2WireVectors()
    {
        string path = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar + "Fixtures" + Path.DirectorySeparatorChar + "v2-wire-vectors-v1.json";
        using JsonDocument vectors = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("verifiabl-v2-wire-vectors-v1", vectors.RootElement.GetProperty("format").GetString());
        JsonElement cases = vectors.RootElement.GetProperty("cases");
        Assert.Equal(3, cases.GetArrayLength());
        var seen = new HashSet<string>();
        foreach (JsonElement item in cases.EnumerateArray())
        {
            string id = item.GetProperty("id").GetString()!;
            Assert.True(seen.Add(id), $"Duplicate vector {id}");
            FakeHttpHandler handler = RegistrationHandler();
            RegisterNonPiiRequest request = ValidRequest();
            request.Schema = item.GetProperty("schema").GetString()!;
            request.PayslipNonPii = id switch
            {
                "au-codes-and-earnings" => PayslipNonPii.FromAustralianV2(new AustralianPayslipV2
                {
                    PeriodStart = PayslipDate("2026-08-01"),
                    PeriodEnd = PayslipDate("2026-08-31"),
                    PaymentDate = PayslipDate("2026-09-04"),
                    Currency = PayslipCurrencies.Aud,
                    Gross = 9000.00m,
                    Paygw = 2250.00m,
                    Net = 6750.00m,
                    PayFrequency = AustralianPayFrequencies.Monthly,
                    EmploymentBasis = AustralianEmploymentBases.FullTime,
                    Earnings = [AustralianPayslipV2EarningsItem.Ordinary(8987.50m),
                        AustralianPayslipV2EarningsItem.OtherAllowance(AustralianOtherAllowanceCategories.HomeOffice, 12.50m)],
                }),
                "au-lump-sum-and-etp" => PayslipNonPii.FromAustralianV2(new AustralianPayslipV2
                {
                    PeriodEnd = PayslipDate("2026-09-30"),
                    PaymentDate = PayslipDate("2026-09-30"),
                    Currency = PayslipCurrencies.Aud,
                    Gross = 41250.00m,
                    Paygw = 9850.00m,
                    Net = 31400.00m,
                    Earnings = [AustralianPayslipV2EarningsItem.Ordinary(3250.00m),
                        AustralianPayslipV2EarningsItem.LumpSum(AustralianLumpSumTypes.ARedundancy, 6000.00m),
                        AustralianPayslipV2EarningsItem.LumpSum(AustralianLumpSumTypes.D, 20000.00m, ytdAmount: 20000.00m),
                        AustralianPayslipV2EarningsItem.Etp(AustralianEtpTypes.RedundancySplit, AustralianEtpComponents.Taxable, 12000.00m, units: 8m, rate: 1500.00m)],
                }),
                "nz-leave-and-dates" => PayslipNonPii.FromNewZealandV2(new NewZealandPayslipV2
                {
                    PeriodEnd = PayslipDate("2026-08-31"),
                    PaymentDate = PayslipDate("2026-09-04"),
                    Currency = PayslipCurrencies.Nzd,
                    Gross = 7600.00m,
                    Paye = 1710.00m,
                    Net = 5890.00m,
                    Earnings = [NewZealandPayslipV2EarningsItem.PaidLeave(NewZealandPaidLeaveTypes.AnnualHoliday, 7000.00m),
                        NewZealandPayslipV2EarningsItem.Overtime(600.00m, units: 10.0m, rate: 60.00m)],
                    LeaveBalances = new NewZealandPayslipV2LeaveBalances
                    {
                        Annual = new NewZealandPayslipV2LeaveBalancesAnnual { Amount = 76.50m, Unit = NewZealandLeaveBalanceUnits.Hours },
                    },
                }),
                _ => throw new InvalidOperationException($"Unknown v2 wire vector {id}"),
            };
            Assert.Equal(id.StartsWith("au-", StringComparison.Ordinal) ? PayslipSchemas.AustralianV2 : PayslipSchemas.NewZealandV2, request.Schema);
            await Client(handler).RegisterNonPiiAsync(request);
            using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
            JsonNode expected = JsonNode.Parse(item.GetProperty("payslip_non_pii").GetRawText())!;
            JsonNode actual = JsonNode.Parse(body.RootElement.GetProperty("payslip_non_pii").GetRawText())!;
            Assert.True(JsonNode.DeepEquals(expected, actual), $"Wire mismatch for {id}: {actual}");
        }
        Assert.Equal(new[] { "au-codes-and-earnings", "au-lump-sum-and-etp", "nz-leave-and-dates" }, seen.OrderBy(id => id));
    }

    private static VerifiablClient Client(
        FakeHttpHandler handler,
        Action<VerifiablClientOptions>? configure = null)
    {
        handler.AutoRespondToTokenRequests = true;
        var options = new VerifiablClientOptions
        {
            Auth = VerifiablAuth.ClientCredentials("client-id", "client-secret"),
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
    public async Task PassesFutureSchemasThroughToTheApi()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = FutureSchemaRequest();

        await client.RegisterNonPiiAsync(request);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal("au.payslip.v3", body.RootElement.GetProperty("schema").GetString());
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
        Assert.Equal("Bearer test-token", sent.Authorization);

        using JsonDocument body = JsonDocument.Parse(sent.Body);
        Assert.Equal(PayslipSchemas.AustralianV2, body.RootElement.GetProperty("schema").GetString());
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
        RegisterNonPiiRequest request = FutureSchemaRequest();
        request.PayslipNonPii = TestPayslips.FreeForm(new Dictionary<string, object?>
        {
            ["total_hours"] = 152,
            ["allowances"] = new[] { "meal", "travel" },
        });

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
        RegisterNonPiiRequest request = FutureSchemaRequest();
        request.PayslipNonPii = TestPayslips.FreeForm(new Dictionary<string, object?>
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
        });

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
    public async Task MapsV2DecimalsToExactStringsWithoutLosingScale()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.Schema = PayslipSchemas.AustralianV2;
        request.PayslipNonPii = PayslipNonPii.FromAustralianV2(new AustralianPayslipV2
        {
            PeriodEnd = PayslipDate("2026-05-31"),
            PaymentDate = PayslipDate("2026-06-01"),
            Currency = PayslipCurrencies.Aud,
            Gross = 6000.00m,
            Paygw = 1500.5m,
            Net = -0.0001m,
            YtdGross = 12345678901234567890.123456789m,
            Hourly = new AustralianPayslipV2Hourly
            {
                OrdinaryRate = 47.3684m,
                Hours = -76.00m,
                Amount = 0m,
            },
            Earnings = [new AustralianPayslipV2EarningsItem
            {
                Type = "ordinary",
                Amount = 6000.00m,
            }],
        });

        await client.RegisterNonPiiAsync(request);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        JsonElement nonPii = body.RootElement.GetProperty("payslip_non_pii");
        Assert.Equal("AUD", nonPii.GetProperty("currency").GetString());
        Assert.Equal("6000.00", nonPii.GetProperty("gross").GetString());
        Assert.Equal("1500.5", nonPii.GetProperty("paygw").GetString());
        Assert.Equal("-0.0001", nonPii.GetProperty("net").GetString());
        Assert.Equal("12345678901234567890.123456789", nonPii.GetProperty("ytd_gross").GetString());
        Assert.Equal("47.3684", nonPii.GetProperty("hourly").GetProperty("ordinary_rate").GetString());
        Assert.Equal("-76.00", nonPii.GetProperty("hourly").GetProperty("hours").GetString());
        Assert.Equal("0", nonPii.GetProperty("hourly").GetProperty("amount").GetString());
        Assert.Equal("6000.00", nonPii.GetProperty("earnings")[0].GetProperty("amount").GetString());
        Assert.False(nonPii.TryGetProperty("ytd_paygw", out _));
    }

    [Theory]
    [InlineData("0.0000000000000000000000000001")]
    [InlineData("-0.00")]
    [InlineData("79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335")]
    public async Task WritesEveryDecimalInThePlainDecimalGrammar(string text)
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.Schema = PayslipSchemas.AustralianV2;
        request.PayslipNonPii = PayslipNonPii.FromAustralianV2(new AustralianPayslipV2
        {
            PeriodEnd = PayslipDate("2026-05-31"),
            PaymentDate = PayslipDate("2026-06-01"),
            Currency = PayslipCurrencies.Aud,
            Gross = decimal.Parse(text, CultureInfo.InvariantCulture),
            Paygw = 0m,
            Net = 0m,
        });

        await client.RegisterNonPiiAsync(request);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Matches(
            "\\A-?[0-9]+(?:\\.[0-9]+)?\\z",
            body.RootElement.GetProperty("payslip_non_pii").GetProperty("gross").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("aud")]
    [InlineData("XYZ")]
    [InlineData("XTS")]
    [InlineData("XXX")]
    [InlineData("XAU")]
    [InlineData("CLF")]
    public async Task RejectsAMissingOrUnsupportedV2Currency(string? currency)
    {
        var handler = new FakeHttpHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.Schema = PayslipSchemas.NewZealandV2;
        request.PayslipNonPii = PayslipNonPii.FromNewZealandV2(new NewZealandPayslipV2
        {
            PeriodEnd = PayslipDate("2026-05-31"),
            PaymentDate = PayslipDate("2026-06-01"),
            Currency = currency!,
            Gross = 100m,
            Paye = 20m,
            Net = 80m,
        });

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.RegisterNonPiiAsync(request));

        Assert.Contains("Currency must be a supported ISO 4217 currency code", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MapsAReadOnlyDictionaryNestedValueAsAnObject()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = FutureSchemaRequest();
        request.PayslipNonPii = TestPayslips.FreeForm(new Dictionary<string, object?>
        {
            ["employer"] = new ReadOnlyPairs(new Dictionary<string, object?>
            {
                ["abn"] = "12345678901",
            }),
        });

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

    // Thrown at the integrator's own call, so in batch it can never become a
    // per-record VALIDATION_FAILED result.
    [Fact]
    public void RejectsUnsupportedAdditionalDataValuesWhenCreatedNamingTheKey()
    {
        var exception = Assert.Throws<ArgumentException>(() => TestPayslips.FreeForm(
            new Dictionary<string, object?> { ["payment_date"] = new DateTime(2026, 5, 31) }));

        Assert.StartsWith("additionalData[\"payment_date\"] has unsupported type System.DateTime.", exception.Message);
        Assert.Equal("additionalData", exception.ParamName);
    }

    [Fact]
    public void RejectsUnsupportedNestedValuesAndBadKeysWhenCreated()
    {
        var nested = Assert.Throws<ArgumentException>(() => TestPayslips.FreeForm(new Dictionary<string, object?>
        {
            ["employer"] = new Dictionary<string, object?> { ["branches"] = new object?[] { "one", new object() } },
        }));
        Assert.StartsWith("additionalData[\"employer\"][\"branches\"][1] has unsupported type System.Object.", nested.Message);

        var repeated = Assert.Throws<ArgumentException>(() => TestPayslips.FreeForm(new[]
        {
            new KeyValuePair<string, object?>("hours", 1),
            new KeyValuePair<string, object?>("hours", 2),
        }));
        Assert.StartsWith("additionalData[\"hours\"] is repeated.", repeated.Message);

        var rawKey = Assert.Throws<ArgumentException>(() => TestPayslips.FreeForm(new Dictionary<string, object?>
        {
            ["codes"] = new System.Collections.Hashtable { [1] = "one" },
        }));
        Assert.StartsWith("additionalData[\"codes\"] has a non-string key", rawKey.Message);
    }

    [Fact]
    public void AcceptsAnIDictionaryWithoutACast()
    {
        IDictionary<string, object?> fields = new Dictionary<string, object?> { ["total_hours"] = 152 };

        PayslipNonPii payslip = PayslipNonPii.ForFutureSchema("2026-05-01", "2026-05-31", fields);

        Assert.Equal(152, payslip.AdditionalData!["total_hours"]);
    }

    [Fact]
    public async Task DoesNotLetPassthroughKeysOverrideTheMappedPeriodDates()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = FutureSchemaRequest();
        request.PayslipNonPii = TestPayslips.FreeForm(new Dictionary<string, object?>
        {
            ["period_start"] = "1999-01-01",
            ["period_end"] = "1999-01-31",
        });

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
            Schema = PayslipSchemas.AustralianV2,
            IssuedAt = DateTimeOffset.UtcNow,
            PayslipNonPii = TestPayslips.Australian(),
            EncryptionMetadata = ValidRequest().EncryptionMetadata,
            EncryptedPii = TestBinary.DecodeBase64Url("abc12w"),
        };
        RegisterAndBuildBarcodeResponse response = await client.RegisterAndBuildBarcodeAsync(request);

        Assert.Equal(Reference, response.VerifiablReference);
        Assert.Equal("png", response.Barcode.Format);
        Assert.Equal("aGVsbG8=", response.Barcode.Data);
        Assert.Equal(
            "https://register.verifiabl.io/v1/registerAndBuildBarcode",
            Assert.Single(handler.Requests).Uri.ToString());
        using JsonDocument body = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal("abc12w", body.RootElement.GetProperty("encrypted_pii").GetString());
    }

    [Fact]
    public async Task PassesFutureSchemasThroughForBarcodeRegistration()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(
                HttpStatusCode.OK,
                $"{{\"verifiabl_reference\":\"{Reference}\",\"barcode\":{{\"format\":\"png\",\"data\":\"aGVsbG8=\"}}}}")),
        };
        VerifiablClient client = Client(handler);
        var request = new RegisterAndBuildBarcodeRequest
        {
            Schema = "au.payslip.v3",
            IssuedAt = DateTimeOffset.UtcNow,
            PayslipNonPii = TestPayslips.FreeForm(),
            EncryptionMetadata = ValidRequest().EncryptionMetadata,
            EncryptedPii = TestBinary.DecodeBase64Url("abc12w"),
        };

        await client.RegisterAndBuildBarcodeAsync(request);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal("au.payslip.v3", body.RootElement.GetProperty("schema").GetString());
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
    [InlineData("payslip.v2")]
    [InlineData("AU.payslip.v2")]
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
        RegisterNonPiiRequest request = FutureSchemaRequest();
        request.PayslipNonPii = TestPayslips.FreeForm(periodStart: periodStart);

        await Assert.ThrowsAsync<ArgumentException>(() => client.RegisterNonPiiAsync(request));

        Assert.Empty(handler.Requests);
    }

    private static PayslipNonPii V2Payslip(string schema) => schema == PayslipSchemas.AustralianV2
        ? PayslipNonPii.FromAustralianV2(new AustralianPayslipV2
        {
            PeriodEnd = PayslipDate("2026-05-31"),
            PaymentDate = PayslipDate("2026-06-01"),
            Currency = PayslipCurrencies.Aud,
            Gross = 100m,
            Paygw = 20m,
            Net = 80m,
        })
        : PayslipNonPii.FromNewZealandV2(new NewZealandPayslipV2
        {
            PeriodEnd = PayslipDate("2026-05-31"),
            PaymentDate = PayslipDate("2026-06-01"),
            Currency = PayslipCurrencies.Nzd,
            Gross = 100m,
            Paye = 20m,
            Net = 80m,
        });

    [Fact]
    public async Task RejectsTypedV2PayloadWithMismatchedSchema()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.PayslipNonPii = V2Payslip(PayslipSchemas.AustralianV2);
        request.Schema = PayslipSchemas.NewZealandV2;

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.RegisterNonPiiAsync(request));

        Assert.StartsWith("request.PayslipNonPii was created for au.payslip.v2, not nz.payslip.v2.", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(PayslipSchemas.AustralianV2)]
    [InlineData(PayslipSchemas.NewZealandV2)]
    public async Task RejectsAFreeFormPayloadForAV2Schema(string schema)
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.Schema = schema;
        request.PayslipNonPii = TestPayslips.FreeForm();

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.RegisterNonPiiAsync(request));

        Assert.Contains("requires PayslipNonPii.FromAustralianV2 or FromNewZealandV2", exception.Message);
        Assert.Empty(handler.Requests);
    }

#if NET6_0_OR_GREATER
    [Fact]
    public async Task WritesTypedV2DatesAsIsoDatesInAnyCulture()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.Schema = PayslipSchemas.NewZealandV2;
        request.PayslipNonPii = PayslipNonPii.FromNewZealandV2(new NewZealandPayslipV2
        {
            PeriodStart = new DateOnly(2026, 2, 3),
            PeriodEnd = new DateOnly(2026, 2, 16),
            PaymentDate = new DateOnly(2026, 2, 18),
            Currency = PayslipCurrencies.Nzd,
            Gross = 100m,
            Paye = 20m,
            Net = 80m,
        });
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("th-TH");
        try
        {
            await client.RegisterNonPiiAsync(request);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        JsonElement nonPii = body.RootElement.GetProperty("payslip_non_pii");
        Assert.Equal("2026-02-03", nonPii.GetProperty("period_start").GetString());
        Assert.Equal("2026-02-16", nonPii.GetProperty("period_end").GetString());
        Assert.Equal("2026-02-18", nonPii.GetProperty("payment_date").GetString());
    }

    [Theory]
    [InlineData("PeriodStart")]
    [InlineData("PeriodEnd")]
    [InlineData("PaymentDate")]
    public async Task RejectsADefaultTypedV2DateBeforeSending(string field)
    {
        var handler = new FakeHttpHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.Schema = PayslipSchemas.AustralianV2;
        var valid = new DateOnly(2026, 5, 31);
        request.PayslipNonPii = PayslipNonPii.FromAustralianV2(new AustralianPayslipV2
        {
            PeriodStart = field == "PeriodStart" ? default(DateOnly) : valid,
            PeriodEnd = field == "PeriodEnd" ? default : valid,
            PaymentDate = field == "PaymentDate" ? default : valid,
            Currency = PayslipCurrencies.Aud,
            Gross = 100m,
            Paygw = 20m,
            Net = 80m,
        });

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.RegisterNonPiiAsync(request));

        Assert.Contains($"{field} is required", exception.Message);
        Assert.Empty(handler.Requests);
    }
#else
    [Fact]
    public async Task DefersTypedV2DateValidationToTheApi()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.BadRequest,
                "{\"error\":\"Validation failed\",\"code\":\"VALIDATION_FAILED\"}")),
        };
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.Schema = PayslipSchemas.AustralianV2;
        request.PayslipNonPii = PayslipNonPii.FromAustralianV2(new AustralianPayslipV2
        {
            PeriodEnd = "invalid",
            PaymentDate = "2026-06-01",
            Currency = PayslipCurrencies.Aud,
            Gross = 100m,
            Paygw = 20m,
            Net = 80m,
        });

        VerifiablApiException exception = await Assert.ThrowsAsync<VerifiablApiException>(
            () => client.RegisterNonPiiAsync(request));
        Assert.Equal(VerifiablErrorCodes.ValidationFailed, exception.Code);
        Assert.Equal("invalid", JsonDocument.Parse(Assert.Single(handler.Requests).Body)
            .RootElement.GetProperty("payslip_non_pii").GetProperty("period_end").GetString());
    }
#endif

    [Theory]
    [InlineData(PayslipSchemas.AustralianV2)]
    [InlineData(PayslipSchemas.NewZealandV2)]
    public async Task OmitsAnAbsentPeriodStartForV2(string schema)
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.Schema = schema;
        request.PayslipNonPii = V2Payslip(schema);

        await client.RegisterNonPiiAsync(request);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        JsonElement nonPii = body.RootElement.GetProperty("payslip_non_pii");
        Assert.False(nonPii.TryGetProperty("period_start", out _));
        Assert.Equal("2026-05-31", nonPii.GetProperty("period_end").GetString());
    }

    // Only the factories can build one, so a v2 payload cannot bypass its typed model.
    [Fact]
    public void PayslipNonPiiHasNoPublicConstructorOrSetters()
    {
        Assert.Empty(typeof(PayslipNonPii).GetConstructors());
        Assert.All(typeof(PayslipNonPii).GetProperties(), property => Assert.Null(property.GetSetMethod()));
    }

    [Fact]
    public async Task DeepCopiesFreeFormFieldsWhenTheyAreCreated()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        var allowances = new List<object?> { "meal" };
        var employer = new Dictionary<string, object?> { ["abn"] = "12345678901" };
        var fields = new Dictionary<string, object?>
        {
            ["total_hours"] = 152,
            ["allowances"] = allowances,
            ["employer"] = employer,
        };
        RegisterNonPiiRequest request = FutureSchemaRequest();
        request.PayslipNonPii = TestPayslips.FreeForm(fields);
        fields["total_hours"] = 1;
        fields["employee_name"] = "Jane Citizen";
        allowances.Add("travel");
        employer["abn"] = "98765432109";

        var copy = request.PayslipNonPii.AdditionalData!;
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)copy)["total_hours"] = 1);
        Assert.Throws<NotSupportedException>(() => ((IList<object?>)copy["allowances"]!).Add("travel"));
        Assert.Throws<NotSupportedException>(
            () => ((IDictionary<string, object?>)copy["employer"]!)["abn"] = "98765432109");

        await client.RegisterNonPiiAsync(request);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        JsonElement nonPii = body.RootElement.GetProperty("payslip_non_pii");
        Assert.Equal(152, nonPii.GetProperty("total_hours").GetInt32());
        Assert.False(nonPii.TryGetProperty("employee_name", out _));
        Assert.Equal("meal", Assert.Single(nonPii.GetProperty("allowances").EnumerateArray()).GetString());
        Assert.Equal("12345678901", nonPii.GetProperty("employer").GetProperty("abn").GetString());
    }

    [Fact]
    public async Task CopiesTypedV2PayloadsWhenTheyAreCreated()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        var earnings = new List<AustralianPayslipV2EarningsItem> { AustralianPayslipV2EarningsItem.Ordinary(100.50m) };
        var payslip = new AustralianPayslipV2
        {
            PeriodEnd = PayslipDate("2026-05-31"),
            PaymentDate = PayslipDate("2026-06-01"),
            Currency = PayslipCurrencies.Aud,
            Gross = 100m,
            Paygw = 20m,
            Net = 80m,
            Earnings = earnings,
        };
        RegisterNonPiiRequest request = ValidRequest();
        request.PayslipNonPii = PayslipNonPii.FromAustralianV2(payslip);
        earnings.Clear();

        await client.RegisterNonPiiAsync(request);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        JsonElement line = Assert.Single(body.RootElement.GetProperty("payslip_non_pii").GetProperty("earnings").EnumerateArray());
        Assert.Equal("100.50", line.GetProperty("amount").GetString());
    }

    [Fact]
    public void RequiresBothPeriodDatesForFreeFormPayloads()
    {
        Assert.Equal("periodStart", Assert.Throws<ArgumentNullException>(
            () => PayslipNonPii.ForFutureSchema(null!, "2026-05-31")).ParamName);
        Assert.Equal("periodEnd", Assert.Throws<ArgumentNullException>(
            () => PayslipNonPii.ForFutureSchema("2026-05-01", null!)).ParamName);
    }

    // v1 is no longer special: like any schema without a typed model, it goes to
    // the API, which rejects it.
    [Theory]
    [InlineData("au.payslip.v1")]
    [InlineData("nz.payslip.v1")]
    public async Task SendsV1SchemasToTheApiLikeAnyUnknownSchema(string schema)
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(
                HttpStatusCode.BadRequest,
                "{\"error\":\"Validation failed\",\"code\":\"VALIDATION_FAILED\"}")),
        };
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = FutureSchemaRequest();
        request.Schema = schema;

        VerifiablApiException exception = await Assert.ThrowsAsync<VerifiablApiException>(
            () => client.RegisterNonPiiAsync(request));

        Assert.Equal(VerifiablErrorCodes.ValidationFailed, exception.Code);
        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal(schema, body.RootElement.GetProperty("schema").GetString());
    }

    [Fact]
    public async Task RejectsATypedAustralianV2PayslipLabelledV1Locally()
    {
        FakeHttpHandler handler = RegistrationHandler();
        VerifiablClient client = Client(handler);
        RegisterNonPiiRequest request = ValidRequest();
        request.Schema = "au.payslip.v1";

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.RegisterNonPiiAsync(request));

        Assert.StartsWith("request.PayslipNonPii was created for au.payslip.v2, not au.payslip.v1.", exception.Message);
        Assert.Empty(handler.Requests);
    }
}
