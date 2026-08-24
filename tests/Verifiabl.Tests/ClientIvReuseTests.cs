using System.Net;
using System.Net.Http;
using Verifiabl.Client;
using Xunit;

namespace Verifiabl.Tests;

/// <summary>
/// The IV reuse rejection the API added in VER-422. The detail strings below are
/// copied from the API's two variants so the SDK is held to the real wire shape.
/// </summary>
public class ClientIvReuseTests
{
    private const string ReferenceA = "u0FE9WLIS7GYKQnpJPygBw";
    private const string ReferenceB = "Xk2mP9qRsT4uVwYzAbCdEf";

    private const string StoredCollisionDetail =
        "encryption_metadata.iv has already been used by this issuer; " +
        "re-encrypt the record with a fresh iv";

    private const string WithinBatchDetail =
        "duplicate encryption_metadata.iv within batch; re-encrypt the record with a fresh iv";

    private static readonly string IvReuseConflictBody =
        "{\"error\":\"Conflict\",\"code\":\"IV_REUSED\",\"detail\":\"" + StoredCollisionDetail + "\"}";

    private static EncryptionMetadata Metadata() => new()
    {
        Iv = "AAAAAAAAAAAAAAAA",
        Tag = "AAAAAAAAAAAAAAAAAAAAAA",
    };

    private static RegisterNonPiiRequest SingleRequest() => new()
    {
        Schema = "au.payslip.v1",
        IssuedAt = new DateTimeOffset(2026, 5, 31, 1, 2, 3, TimeSpan.Zero),
        PayslipNonPii = new PayslipNonPii { PeriodStart = "2026-05-01", PeriodEnd = "2026-05-31" },
        EncryptionMetadata = Metadata(),
    };

    private static RegisterAndBuildBarcodeRequest BarcodeRequest() => new()
    {
        Schema = "au.payslip.v1",
        IssuedAt = new DateTimeOffset(2026, 5, 31, 1, 2, 3, TimeSpan.Zero),
        PayslipNonPii = new PayslipNonPii { PeriodStart = "2026-05-01", PeriodEnd = "2026-05-31" },
        EncryptionMetadata = Metadata(),
        EncryptedPii = "abc123DEF456-_",
    };

    private static BatchRecord BatchRecordItem(string reference) => new()
    {
        VerifiablReference = reference,
        Schema = "au.payslip.v1",
        IssuedAt = new DateTimeOffset(2026, 5, 31, 1, 2, 3, TimeSpan.Zero),
        PayslipNonPii = new PayslipNonPii { PeriodStart = "2026-05-01", PeriodEnd = "2026-05-31" },
        EncryptionMetadata = Metadata(),
    };

    private static VerifiablClient Client(FakeHttpHandler handler) => new(new VerifiablClientOptions
    {
        Auth = VerifiablAuth.ApiKey("static-key"),
        HttpClient = new HttpClient(handler),
    });

    private static FakeHttpHandler Responds(HttpStatusCode status, string json)
    {
        return new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(FakeHttpHandler.Json(status, json)),
        };
    }

    [Fact]
    public async Task SingleRegistrationSurfacesIvReuseAsTheTypedException()
    {
        FakeHttpHandler handler = Responds(HttpStatusCode.Conflict, IvReuseConflictBody);
        VerifiablClient client = Client(handler);

        VerifiablIvReuseException error = await Assert.ThrowsAsync<VerifiablIvReuseException>(
            () => client.RegisterNonPiiAsync(SingleRequest()));

        Assert.Equal(409, error.Status);
        Assert.Equal(VerifiablErrorCodes.IvReused, error.Code);
        Assert.Equal(StoredCollisionDetail, error.Body?.Detail);
    }

    [Fact]
    public async Task BuildBarcodeSurfacesIvReuseAsTheTypedException()
    {
        FakeHttpHandler handler = Responds(HttpStatusCode.Conflict, IvReuseConflictBody);
        VerifiablClient client = Client(handler);

        VerifiablIvReuseException error = await Assert.ThrowsAsync<VerifiablIvReuseException>(
            () => client.RegisterAndBuildBarcodeAsync(BarcodeRequest()));

        Assert.Equal(VerifiablErrorCodes.IvReused, error.Code);
    }

    [Fact]
    public async Task IvReuseMessageCarriesTheRemedyRatherThanTheServerSummary()
    {
        FakeHttpHandler handler = Responds(HttpStatusCode.Conflict, IvReuseConflictBody);
        VerifiablClient client = Client(handler);

        VerifiablIvReuseException error = await Assert.ThrowsAsync<VerifiablIvReuseException>(
            () => client.RegisterNonPiiAsync(SingleRequest()));

        // The server summary is the bare word "Conflict", which tells the caller
        // nothing about what to do next.
        Assert.NotEqual("Conflict", error.Message);
        Assert.Contains("encrypt the payslip again", error.Message, StringComparison.Ordinal);
        Assert.Contains("fresh iv", error.Message, StringComparison.Ordinal);
        Assert.Contains("resend", error.Message, StringComparison.Ordinal);
        Assert.Contains("barcode", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IvReuseIsCaughtByExistingApiExceptionHandling()
    {
        FakeHttpHandler handler = Responds(HttpStatusCode.Conflict, IvReuseConflictBody);
        VerifiablClient client = Client(handler);

        VerifiablApiException error = await Assert.ThrowsAnyAsync<VerifiablApiException>(
            () => client.RegisterNonPiiAsync(SingleRequest()));

        Assert.IsType<VerifiablIvReuseException>(error);
    }

    [Fact]
    public async Task IvReuseIsNotRetried()
    {
        FakeHttpHandler handler = Responds(HttpStatusCode.Conflict, IvReuseConflictBody);
        VerifiablClient client = Client(handler);

        await Assert.ThrowsAsync<VerifiablIvReuseException>(
            () => client.RegisterNonPiiAsync(SingleRequest()));

        // Re-sending identical encryption metadata can only be rejected again, and
        // the SDK must not silently re-encrypt on the caller's behalf.
        Assert.Single(handler.ApiRequests);
    }

    [Fact]
    public async Task ReferenceConflictStaysAPlainApiException()
    {
        FakeHttpHandler handler = Responds(
            HttpStatusCode.Conflict,
            "{\"error\":\"Conflict\",\"code\":\"CONFLICT\"," +
            "\"detail\":\"verifiabl_reference already registered with different data\"}");
        VerifiablClient client = Client(handler);

        VerifiablApiException error = await Assert.ThrowsAsync<VerifiablApiException>(
            () => client.RegisterNonPiiAsync(SingleRequest()));

        Assert.Equal(409, error.Status);
        Assert.Equal(VerifiablErrorCodes.Conflict, error.Code);
        Assert.Equal("Conflict", error.Message);
    }

    [Fact]
    public async Task BatchDetectsIvReuseForBothDetailVariants()
    {
        FakeHttpHandler handler = Responds(
            HttpStatusCode.OK,
            "{\"results\":[" +
            $"{{\"status\":\"error\",\"verifiabl_reference\":\"{ReferenceA}\"," +
            $"\"code\":\"IV_REUSED\",\"detail\":\"{StoredCollisionDetail}\"}}," +
            $"{{\"status\":\"error\",\"verifiabl_reference\":\"{ReferenceB}\"," +
            $"\"code\":\"IV_REUSED\",\"detail\":\"{WithinBatchDetail}\"}}]}}");
        VerifiablClient client = Client(handler);

        RegisterNonPiiBatchResponse response = await client.RegisterNonPiiBatchAsync(
            [BatchRecordItem(ReferenceA), BatchRecordItem(ReferenceB)]);

        // Detection must not depend on the wording, which differs per variant.
        Assert.True(response.Results[0].IsIvReused);
        Assert.True(response.Results[1].IsIvReused);
        Assert.NotEqual(response.Results[0].Detail, response.Results[1].Detail);
    }

    [Fact]
    public async Task BatchDoesNotFlagOtherOutcomesAsIvReuse()
    {
        FakeHttpHandler handler = Responds(
            HttpStatusCode.OK,
            "{\"results\":[" +
            $"{{\"status\":\"created\",\"verifiabl_reference\":\"{ReferenceA}\"}}," +
            $"{{\"status\":\"duplicate\",\"verifiabl_reference\":\"{ReferenceB}\"}}," +
            $"{{\"status\":\"error\",\"verifiabl_reference\":\"{ReferenceA}\"," +
            "\"code\":\"CONFLICT\"," +
            "\"detail\":\"verifiabl_reference already registered with different data\"}," +
            $"{{\"status\":\"error\",\"verifiabl_reference\":\"{ReferenceB}\"," +
            "\"code\":\"VALIDATION_FAILED\",\"detail\":\"bad record\"}]}");
        VerifiablClient client = Client(handler);

        RegisterNonPiiBatchResponse response = await client.RegisterNonPiiBatchAsync(
            [BatchRecordItem(ReferenceA), BatchRecordItem(ReferenceB)]);

        Assert.All(response.Results, result => Assert.False(result.IsIvReused));
        Assert.Equal(BatchRecordStatuses.Created, response.Results[0].Status);
        Assert.Equal(BatchRecordStatuses.Duplicate, response.Results[1].Status);
        Assert.Equal(VerifiablErrorCodes.Conflict, response.Results[2].Code);
    }

    [Fact]
    public async Task BatchIvReuseDoesNotFailTheWholeBatch()
    {
        FakeHttpHandler handler = Responds(
            HttpStatusCode.OK,
            "{\"results\":[" +
            $"{{\"status\":\"created\",\"verifiabl_reference\":\"{ReferenceA}\"}}," +
            $"{{\"status\":\"error\",\"verifiabl_reference\":\"{ReferenceB}\"," +
            $"\"code\":\"IV_REUSED\",\"detail\":\"{WithinBatchDetail}\"}}]}}");
        VerifiablClient client = Client(handler);

        RegisterNonPiiBatchResponse response = await client.RegisterNonPiiBatchAsync(
            [BatchRecordItem(ReferenceA), BatchRecordItem(ReferenceB)]);

        Assert.False(response.Results[0].IsIvReused);
        Assert.True(response.Results[1].IsIvReused);
        Assert.Single(handler.ApiRequests);
    }
}
