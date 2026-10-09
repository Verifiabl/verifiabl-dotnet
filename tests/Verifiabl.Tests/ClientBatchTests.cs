using System.Net;
using System.Net.Http;
using System.Text.Json;
using Verifiabl.Client;
using Xunit;
using static Verifiabl.Tests.TestDates;

namespace Verifiabl.Tests;

public class ClientBatchTests
{
    private const string ReferenceA = "u0FE9WLIS7GYKQnpJPygBw";
    private const string ReferenceB = "Xk2mP9qRsT4uVwYzAbCdEf";

    private static BatchRecord ValidRecord(string reference) => new()
    {
        VerifiablReference = reference,
        Schema = PayslipSchemas.AustralianV2,
        IssuedAt = new DateTimeOffset(2026, 5, 31, 1, 2, 3, TimeSpan.Zero),
        PayslipNonPii = TestPayslips.Australian(),
        EncryptionMetadata = new EncryptionMetadata
        {
            Iv = new byte[12],
            Tag = new byte[16],
        },
    };

    private static VerifiablClient Client(FakeHttpHandler handler)
    {
        handler.AutoRespondToTokenRequests = true;
        return new VerifiablClient(new VerifiablClientOptions
        {
            Auth = VerifiablAuth.ClientCredentials("client-id", "client-secret"),
            HttpClient = new HttpClient(handler),
        });
    }

    [Fact]
    public async Task PostsTheBatchWireBodyAndMapsTheResponse()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(
                HttpStatusCode.OK,
                "{\"results\":[" +
                $"{{\"status\":\"created\",\"verifiabl_reference\":\"{ReferenceA}\"}}," +
                $"{{\"status\":\"error\",\"verifiabl_reference\":\"{ReferenceB}\"," +
                "\"code\":\"VALIDATION_FAILED\",\"detail\":\"bad record\"}]}")),
        };
        VerifiablClient client = Client(handler);

        RegisterNonPiiBatchResponse response = await client.RegisterNonPiiBatchAsync(
            [ValidRecord(ReferenceA), ValidRecord(ReferenceB)]);

        CapturedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal("https://register.verifiabl.io/v1/registerNonPIIBatch", sent.Uri.ToString());
        using JsonDocument body = JsonDocument.Parse(sent.Body);
        JsonElement records = body.RootElement.GetProperty("records");
        Assert.Equal(2, records.GetArrayLength());
        Assert.Equal(
            ReferenceA,
            records[0].GetProperty("verifiabl_reference").GetString());
        Assert.Equal(PayslipSchemas.AustralianV2, records[0].GetProperty("schema").GetString());
        Assert.Equal("9000.00", records[0].GetProperty("payslip_non_pii").GetProperty("gross").GetString());

        Assert.Equal(2, response.Results.Count);
        Assert.Equal(BatchRecordStatuses.Created, response.Results[0].Status);
        Assert.Null(response.Results[0].Code);
        Assert.Equal(BatchRecordStatuses.Error, response.Results[1].Status);
        Assert.Equal("VALIDATION_FAILED", response.Results[1].Code);
        Assert.Equal("bad record", response.Results[1].Detail);
    }

    [Fact]
    public async Task PostsNewZealandV2BatchRecords()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(
                HttpStatusCode.OK,
                $"{{\"results\":[{{\"status\":\"created\",\"verifiabl_reference\":\"{ReferenceA}\"}}]}}")),
        };
        VerifiablClient client = Client(handler);
        BatchRecord record = ValidRecord(ReferenceA);
        record.Schema = PayslipSchemas.NewZealandV2;
        record.PayslipNonPii = PayslipNonPii.FromNewZealandV2(new NewZealandPayslipV2
        {
            PeriodEnd = PayslipDate("2026-05-31"),
            PaymentDate = PayslipDate("2026-06-01"),
            Currency = PayslipCurrencies.Nzd,
            Gross = 100m,
            Paye = 20m,
            Net = 80m,
        });

        RegisterNonPiiBatchResponse response = await client.RegisterNonPiiBatchAsync([record]);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        JsonElement sent = Assert.Single(body.RootElement.GetProperty("records").EnumerateArray());
        Assert.Equal(PayslipSchemas.NewZealandV2, sent.GetProperty("schema").GetString());
        Assert.Equal("20", sent.GetProperty("payslip_non_pii").GetProperty("paye").GetString());
        Assert.Equal(BatchRecordStatuses.Created, Assert.Single(response.Results).Status);
    }

    // v1 is no longer special: like any schema without a typed model, it goes to
    // the API, which rejects it per record.
    [Theory]
    [InlineData("au.payslip.v1")]
    [InlineData("nz.payslip.v1")]
    public async Task SendsV1BatchRecordsToTheApiLikeAnyUnknownSchema(string schema)
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK,
                $"{{\"results\":[{{\"status\":\"error\",\"code\":\"VALIDATION_FAILED\",\"detail\":\"unsupported schema\",\"verifiabl_reference\":\"{ReferenceA}\"}}]}}")),
        };
        VerifiablClient client = Client(handler);
        BatchRecord record = ValidRecord(ReferenceA);
        record.Schema = schema;
        record.PayslipNonPii = TestPayslips.FreeForm();

        RegisterNonPiiBatchResponse response = await client.RegisterNonPiiBatchAsync([record]);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        JsonElement sent = Assert.Single(body.RootElement.GetProperty("records").EnumerateArray());
        Assert.Equal(schema, sent.GetProperty("schema").GetString());
        Assert.Equal("VALIDATION_FAILED", Assert.Single(response.Results).Code);
    }

    [Theory]
    [InlineData("au.payslip.v1")]
    [InlineData(PayslipSchemas.NewZealandV2)]
    public async Task ThrowsForATypedAustralianV2PayslipUnderAnotherSchema(string schema)
    {
        var handler = new FakeHttpHandler();
        VerifiablClient client = Client(handler);
        BatchRecord record = ValidRecord(ReferenceB);
        record.Schema = schema;

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.RegisterNonPiiBatchAsync([ValidRecord(ReferenceA), record]));

        Assert.StartsWith($"records[1].PayslipNonPii was created for au.payslip.v2, not {schema}.", exception.Message);
        Assert.Equal("records", exception.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PassesFutureBatchSchemasThroughToTheApi()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(
                HttpStatusCode.OK,
                $"{{\"results\":[{{\"status\":\"created\",\"verifiabl_reference\":\"{ReferenceA}\"}}]}}")),
        };
        VerifiablClient client = Client(handler);
        BatchRecord record = ValidRecord(ReferenceA);
        record.Schema = "au.payslip.v3";
        record.PayslipNonPii = TestPayslips.FreeForm();

        RegisterNonPiiBatchResponse response = await client.RegisterNonPiiBatchAsync([record]);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        JsonElement sent = Assert.Single(body.RootElement.GetProperty("records").EnumerateArray());
        Assert.Equal("au.payslip.v3", sent.GetProperty("schema").GetString());
        Assert.Equal(BatchRecordStatuses.Created, Assert.Single(response.Results).Status);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"status\":\"created\",\"verifiabl_reference\":\"u0FE9WLIS7GYKQnpJPygBw\"},{\"status\":\"created\",\"verifiabl_reference\":\"Xk2mP9qRsT4uVwYzAbCdEf\"}]")]
    public async Task RejectsBatchResponsesWithTheWrongResultCount(string results)
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(
                HttpStatusCode.OK, $"{{\"results\":{results}}}")),
        };
        VerifiablClient client = Client(handler);

        VerifiablTransportException exception = await Assert.ThrowsAsync<VerifiablTransportException>(
            () => client.RegisterNonPiiBatchAsync([ValidRecord(ReferenceA)]));

        Assert.Contains("results for 1 sent records", exception.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SendsExternalIdOnTheWireAndMapsItBack()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(
                HttpStatusCode.OK,
                "{\"results\":[" +
                $"{{\"status\":\"created\",\"verifiabl_reference\":\"{ReferenceA}\",\"external_id\":\"payslip-1\"}}," +
                $"{{\"status\":\"created\",\"verifiabl_reference\":\"{ReferenceB}\"}}]}}")),
        };
        VerifiablClient client = Client(handler);

        BatchRecord recordA = ValidRecord(ReferenceA);
        recordA.ExternalId = "payslip-1";
        RegisterNonPiiBatchResponse response = await client.RegisterNonPiiBatchAsync(
            [recordA, ValidRecord(ReferenceB)]);

        CapturedRequest sent = Assert.Single(handler.Requests);
        using JsonDocument body = JsonDocument.Parse(sent.Body);
        JsonElement records = body.RootElement.GetProperty("records");
        // Sent only when supplied.
        Assert.Equal("payslip-1", records[0].GetProperty("external_id").GetString());
        Assert.False(records[1].TryGetProperty("external_id", out _));

        // Echoed back on the matching result; null when the API omits it.
        Assert.Equal("payslip-1", response.Results[0].ExternalId);
        Assert.Null(response.Results[1].ExternalId);
    }

    [Fact]
    public async Task SurfacesDuplicatesAndPassesThroughUnknownStatuses()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(
                HttpStatusCode.OK,
                "{\"results\":[" +
                $"{{\"status\":\"duplicate\",\"verifiabl_reference\":\"{ReferenceA}\"}}," +
                $"{{\"status\":\"quarantined\",\"verifiabl_reference\":\"{ReferenceB}\"," +
                "\"future_field\":true}]}")),
        };
        VerifiablClient client = Client(handler);

        RegisterNonPiiBatchResponse response = await client.RegisterNonPiiBatchAsync(
            [ValidRecord(ReferenceA), ValidRecord(ReferenceB)]);

        Assert.Equal(BatchRecordStatuses.Duplicate, response.Results[0].Status);
        // Unknown statuses and additive fields must flow through untouched.
        Assert.Equal("quarantined", response.Results[1].Status);
    }

    // A free-form payload under a v2 schema is a coding error, not payslip data the
    // API would reject, so the whole call throws like RegisterNonPiiAsync.
    [Theory]
    [InlineData(PayslipSchemas.AustralianV2)]
    [InlineData(PayslipSchemas.NewZealandV2)]
    public async Task ThrowsForAFreeFormPayslipUnderAV2SchemaAndSendsNothing(string schema)
    {
        var handler = new FakeHttpHandler();
        VerifiablClient client = Client(handler);
        BatchRecord invalid = ValidRecord(ReferenceB);
        invalid.Schema = schema;
        invalid.PayslipNonPii = TestPayslips.FreeForm();

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.RegisterNonPiiBatchAsync([ValidRecord(ReferenceA), invalid]));

        Assert.StartsWith(
            "records[1].PayslipNonPii requires PayslipNonPii.FromAustralianV2 or FromNewZealandV2.",
            exception.Message);
        Assert.Equal("records", exception.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ThrowsForANullPayslipAndSendsNothing()
    {
        var handler = new FakeHttpHandler();
        VerifiablClient client = Client(handler);
        BatchRecord invalid = ValidRecord(ReferenceB);
        invalid.PayslipNonPii = null!;

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.RegisterNonPiiBatchAsync([ValidRecord(ReferenceA), invalid]));

        Assert.StartsWith("records[1].PayslipNonPii is required.", exception.Message);
        Assert.Equal("records", exception.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ReportsFreeFormPayslipDataErrorsPerRecord()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(
                HttpStatusCode.OK,
                $"{{\"results\":[{{\"status\":\"created\",\"verifiabl_reference\":\"{ReferenceB}\"}}]}}")),
        };
        VerifiablClient client = Client(handler);
        BatchRecord invalid = ValidRecord(ReferenceA);
        invalid.Schema = "au.payslip.v3";
        invalid.ExternalId = "bad-1";
        invalid.PayslipNonPii = TestPayslips.FreeForm(periodStart: "2026-02-30");

        RegisterNonPiiBatchResponse response = await client.RegisterNonPiiBatchAsync(
            [invalid, ValidRecord(ReferenceB)]);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal(ReferenceB, Assert.Single(body.RootElement.GetProperty("records").EnumerateArray())
            .GetProperty("verifiabl_reference").GetString());
        Assert.Equal(BatchRecordStatuses.Error, response.Results[0].Status);
        Assert.Equal("VALIDATION_FAILED", response.Results[0].Code);
        Assert.StartsWith("records[0].PayslipNonPii.PeriodStart must be a YYYY-MM-DD date.", response.Results[0].Detail);
        Assert.Equal("bad-1", response.Results[0].ExternalId);
        Assert.Equal(ReferenceA, response.Results[0].VerifiablReference);
        Assert.Equal(BatchRecordStatuses.Created, response.Results[1].Status);
    }

    [Fact]
    public async Task TypedV2BatchPayslipUsesTheApiPerRecordValidation()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK,
                $"{{\"results\":[{{\"status\":\"error\",\"code\":\"VALIDATION_FAILED\",\"detail\":\"invalid date\",\"verifiabl_reference\":\"{ReferenceA}\"}}]}}")),
        };
        VerifiablClient client = Client(handler);
        BatchRecord record = ValidRecord(ReferenceA);
        record.Schema = PayslipSchemas.AustralianV2;
        record.PayslipNonPii = PayslipNonPii.FromAustralianV2(new AustralianPayslipV2
        {
            PeriodStart = PayslipDate("2026-06-01"),
            PeriodEnd = PayslipDate("2026-05-31"),
            PaymentDate = PayslipDate("2026-06-01"),
            Currency = PayslipCurrencies.Aud,
            Gross = 100m,
            Paygw = 20m,
            Net = 80m,
        });

        RegisterNonPiiBatchResponse response = await client.RegisterNonPiiBatchAsync([record]);

        Assert.Single(handler.Requests);
        Assert.Equal("VALIDATION_FAILED", Assert.Single(response.Results).Code);
    }

    [Fact]
    public async Task ReportsAnUnknownV2CurrencyLocallyWithoutSendingTheRecord()
    {
        var handler = new FakeHttpHandler();
        VerifiablClient client = Client(handler);
        BatchRecord record = ValidRecord(ReferenceA);
        record.Schema = PayslipSchemas.AustralianV2;
        record.PayslipNonPii = PayslipNonPii.FromAustralianV2(new AustralianPayslipV2
        {
            PeriodEnd = PayslipDate("2026-05-31"),
            PaymentDate = PayslipDate("2026-06-01"),
            Currency = "XYZ",
            Gross = 100m,
            Paygw = 20m,
            Net = 80m,
        });

        RegisterNonPiiBatchResponse response = await client.RegisterNonPiiBatchAsync([record]);

        Assert.Empty(handler.Requests);
        BatchRecordResult result = Assert.Single(response.Results);
        Assert.Equal("VALIDATION_FAILED", result.Code);
        Assert.Contains("Currency", result.Detail);
    }

    [Fact]
    public void RejectsNullBatchSynchronously()
    {
        var handler = new FakeHttpHandler();
        VerifiablClient client = Client(handler);

        Assert.Throws<ArgumentNullException>(() => { _ = client.RegisterNonPiiBatchAsync(null!); });

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void RejectsNullRecordWithItsIndexBeforeSending()
    {
        var handler = new FakeHttpHandler();
        VerifiablClient client = Client(handler);

        var exception = Assert.Throws<ArgumentException>(() =>
        {
            _ = client.RegisterNonPiiBatchAsync([ValidRecord(ReferenceA), null!]);
        });

        Assert.Equal("records", exception.ParamName);
        Assert.Contains("records[1] must not be null", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void RejectsAnEmptyBatchBeforeSending()
    {
        var handler = new FakeHttpHandler();
        VerifiablClient client = Client(handler);

        Assert.Throws<ArgumentException>(() => { _ = client.RegisterNonPiiBatchAsync([]); });

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void RejectsBatchesAboveTheApiMaximumBeforeSending()
    {
        var handler = new FakeHttpHandler();
        VerifiablClient client = Client(handler);
        IEnumerable<BatchRecord> records = Enumerable
            .Range(0, VerifiablClient.MaxBatchRecords + 1)
            .Select(_ => ValidRecord(VerifiablReference.Generate()));

        var exception = Assert.Throws<ArgumentException>(
            () => { _ = client.RegisterNonPiiBatchAsync(records); });

        Assert.Contains("1000", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void RejectsMalformedReferencesBeforeSending()
    {
        var handler = new FakeHttpHandler();
        VerifiablClient client = Client(handler);
        BatchRecord record = ValidRecord("not-a-reference!!!");

        var exception = Assert.Throws<ArgumentException>(
            () => { _ = client.RegisterNonPiiBatchAsync([record]); });

        Assert.Contains("records[0]", exception.Message);
        Assert.Empty(handler.Requests);
    }
}
