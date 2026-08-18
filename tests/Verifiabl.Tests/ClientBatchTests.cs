using System.Net;
using System.Net.Http;
using System.Text.Json;
using Verifiabl.Client;
using Xunit;

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
            Iv = "AAAAAAAAAAAAAAAA",
            Tag = "AAAAAAAAAAAAAAAAAAAAAA",
        },
    };

    private static VerifiablClient Client(FakeHttpHandler handler) => new(new VerifiablClientOptions
    {
        Auth = VerifiablAuth.ApiKey("static-key"),
        HttpClient = new HttpClient(handler),
    });

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
    public async Task RejectsAnEmptyBatchBeforeSending()
    {
        var handler = new FakeHttpHandler();
        VerifiablClient client = Client(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.RegisterNonPiiBatchAsync([]));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RejectsBatchesAboveTheApiMaximumBeforeSending()
    {
        var handler = new FakeHttpHandler();
        VerifiablClient client = Client(handler);
        IEnumerable<BatchRecord> records = Enumerable
            .Range(0, VerifiablClient.MaxBatchRecords + 1)
            .Select(_ => ValidRecord(VerifiablReference.Generate()));

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.RegisterNonPiiBatchAsync(records));

        Assert.Contains("1000", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RejectsMalformedReferencesBeforeSending()
    {
        var handler = new FakeHttpHandler();
        VerifiablClient client = Client(handler);
        BatchRecord record = ValidRecord("not-a-reference!!!");

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.RegisterNonPiiBatchAsync([record]));

        Assert.Contains("records[0]", exception.Message);
        Assert.Empty(handler.Requests);
    }
}
