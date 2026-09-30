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
        Schema = "au.payslip.v1",
        IssuedAt = new DateTimeOffset(2026, 5, 31, 1, 2, 3, TimeSpan.Zero),
        PayslipNonPii = new PayslipNonPii
        {
            PeriodStart = "2026-05-01",
            PeriodEnd = "2026-05-31",
        },
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
        Assert.Equal("au.payslip.v1", records[0].GetProperty("schema").GetString());

        Assert.Equal(2, response.Results.Count);
        Assert.Equal(BatchRecordStatuses.Created, response.Results[0].Status);
        Assert.Null(response.Results[0].Code);
        Assert.Equal(BatchRecordStatuses.Error, response.Results[1].Status);
        Assert.Equal("VALIDATION_FAILED", response.Results[1].Code);
        Assert.Equal("bad record", response.Results[1].Detail);
    }

    [Fact]
    public async Task PostsLegacyNewZealandV1BatchRecords()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(
                HttpStatusCode.OK,
                $"{{\"results\":[{{\"status\":\"created\",\"verifiabl_reference\":\"{ReferenceA}\"}}]}}")),
        };
        VerifiablClient client = Client(handler);
        BatchRecord record = ValidRecord(ReferenceA);
        record.Schema = PayslipSchemas.NewZealandV1;

        RegisterNonPiiBatchResponse response = await client.RegisterNonPiiBatchAsync([record]);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        JsonElement sent = Assert.Single(body.RootElement.GetProperty("records").EnumerateArray());
        Assert.Equal(PayslipSchemas.NewZealandV1, sent.GetProperty("schema").GetString());
        Assert.Equal(BatchRecordStatuses.Created, Assert.Single(response.Results).Status);
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

    [Fact]
    public async Task UntypedV2PayslipIsNotSentAndDoesNotDropOtherRecords()
    {
        var handler = new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(
                HttpStatusCode.OK,
                $"{{\"results\":[{{\"status\":\"created\",\"verifiabl_reference\":\"{ReferenceB}\"}}]}}")),
        };
        VerifiablClient client = Client(handler);
        BatchRecord invalid = ValidRecord(ReferenceA);
        invalid.Schema = PayslipSchemas.AustralianV2;
        invalid.ExternalId = "bad-1";
        invalid.PayslipNonPii.AdditionalData = new Dictionary<string, object?>
        {
            ["employee_name"] = "Jane",
        };

        RegisterNonPiiBatchResponse response = await client.RegisterNonPiiBatchAsync(
            [invalid, ValidRecord(ReferenceB)]);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal(ReferenceB, Assert.Single(body.RootElement.GetProperty("records").EnumerateArray())
            .GetProperty("verifiabl_reference").GetString());
        Assert.Equal(BatchRecordStatuses.Error, response.Results[0].Status);
        Assert.Equal("VALIDATION_FAILED", response.Results[0].Code);
        Assert.Equal("bad-1", response.Results[0].ExternalId);
        Assert.Equal(ReferenceA, response.Results[0].VerifiablReference);
        Assert.Equal(BatchRecordStatuses.Created, response.Results[1].Status);
    }

    [Fact]
    public async Task AllUntypedV2PayslipsReturnLocalResultsWithoutNetwork()
    {
        var handler = new FakeHttpHandler();
        VerifiablClient client = Client(handler);
        BatchRecord invalid = ValidRecord(ReferenceA);
        invalid.Schema = PayslipSchemas.AustralianV2;
        invalid.PayslipNonPii.AdditionalData = new Dictionary<string, object?> { ["employee_name"] = "Jane" };
        RegisterNonPiiBatchResponse response = await client.RegisterNonPiiBatchAsync([invalid]);

        Assert.Empty(handler.Requests);
        Assert.Equal("VALIDATION_FAILED", Assert.Single(response.Results).Code);
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
