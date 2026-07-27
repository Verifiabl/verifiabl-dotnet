using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Verifiabl.Client;
using Xunit;

namespace Verifiabl.Tests;

public class ClientRetryTests
{
    private const string Reference = "u0FE9WLIS7GYKQnpJPygBw";
    private const string KeyVersion = "0f8fad5b-d9cb-469f-a165-70867728950e.1";

    private static EncryptionMetadata Metadata() => new()
    {
        Iv = "AAAAAAAAAAAAAAAA",
        Tag = "AAAAAAAAAAAAAAAAAAAAAA",
        KeyVersion = KeyVersion,
    };

    private static RegisterNonPiiRequest SingleRequest() => new()
    {
        Schema = "au.payslip.v1",
        IssuedAt = new DateTimeOffset(2026, 5, 31, 0, 0, 0, TimeSpan.Zero),
        PayslipNonPii = new PayslipNonPii { PeriodStart = "2026-05-01", PeriodEnd = "2026-05-31" },
        EncryptionMetadata = Metadata(),
    };

    private static BatchRecord BatchRecordItem() => new()
    {
        VerifiablReference = Reference,
        Schema = "au.payslip.v1",
        IssuedAt = new DateTimeOffset(2026, 5, 31, 0, 0, 0, TimeSpan.Zero),
        PayslipNonPii = new PayslipNonPii { PeriodStart = "2026-05-01", PeriodEnd = "2026-05-31" },
        EncryptionMetadata = Metadata(),
    };

    /// <summary>A client whose retry backoff is captured instead of awaited.</summary>
    private static VerifiablClient Client(
        FakeHttpHandler handler,
        List<TimeSpan> delays,
        Action<VerifiablClientOptions>? configure = null)
    {
        var options = new VerifiablClientOptions
        {
            Auth = VerifiablAuth.ApiKey("static-key"),
            HttpClient = new HttpClient(handler),
        };
        configure?.Invoke(options);
        return new VerifiablClient(options)
        {
            DelayAsync = (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
        };
    }

    /// <summary>Responds from a queue, so successive attempts see successive responses.</summary>
    private static FakeHttpHandler QueuedHandler(params Func<HttpResponseMessage>[] responses)
    {
        var queue = new Queue<Func<HttpResponseMessage>>(responses);
        return new FakeHttpHandler
        {
            Responder = (_, _, _) => Task.FromResult(queue.Dequeue()()),
        };
    }

    private static HttpResponseMessage BatchOk() =>
        FakeHttpHandler.Json(HttpStatusCode.OK, "{\"results\":[]}");

    private static HttpResponseMessage RegistrationOk() =>
        FakeHttpHandler.Json(HttpStatusCode.OK, $"{{\"verifiabl_reference\":\"{Reference}\"}}");

    [Fact]
    public async Task RetriesBatchOn503ThenSucceeds()
    {
        var delays = new List<TimeSpan>();
        FakeHttpHandler handler = QueuedHandler(
            () => FakeHttpHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"),
            BatchOk);
        VerifiablClient client = Client(handler, delays);

        await client.RegisterNonPiiBatchAsync([BatchRecordItem()]);

        Assert.Equal(2, handler.ApiRequests.Count());
        Assert.Single(delays);
    }

    [Fact]
    public async Task RetriesBatchOn5xxAndNetworkFaultUpToTheLimit()
    {
        var delays = new List<TimeSpan>();
        FakeHttpHandler handler = QueuedHandler(
            () => FakeHttpHandler.Json(HttpStatusCode.InternalServerError, "{}"),
            () => throw new HttpRequestException("connection reset"),
            BatchOk);
        VerifiablClient client = Client(handler, delays, o => o.MaxRetries = 3);

        await client.RegisterNonPiiBatchAsync([BatchRecordItem()]);

        Assert.Equal(3, handler.ApiRequests.Count());
        Assert.Equal(2, delays.Count);
        // Exponential: the second backoff is at least as long as the first.
        Assert.True(delays[1] >= delays[0]);
    }

    [Fact]
    public async Task DoesNotRetryBatchBeyondMaxRetries()
    {
        var delays = new List<TimeSpan>();
        FakeHttpHandler handler = QueuedHandler(
            () => FakeHttpHandler.Json(HttpStatusCode.InternalServerError, "{}"),
            () => FakeHttpHandler.Json(HttpStatusCode.InternalServerError, "{}"),
            () => FakeHttpHandler.Json(HttpStatusCode.InternalServerError, "{}"));
        VerifiablClient client = Client(handler, delays, o => o.MaxRetries = 2);

        VerifiablApiException error = await Assert.ThrowsAsync<VerifiablApiException>(
            () => client.RegisterNonPiiBatchAsync([BatchRecordItem()]));

        Assert.Equal(500, error.Status);
        Assert.Equal(3, handler.ApiRequests.Count()); // 1 initial + 2 retries
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task DoesNotRetrySingleRegistrationOnAmbiguous5xx()
    {
        var delays = new List<TimeSpan>();
        FakeHttpHandler handler = QueuedHandler(
            () => FakeHttpHandler.Json(HttpStatusCode.InternalServerError, "{}"));
        VerifiablClient client = Client(handler, delays);

        VerifiablApiException error = await Assert.ThrowsAsync<VerifiablApiException>(
            () => client.RegisterNonPiiAsync(SingleRequest()));

        Assert.Equal(500, error.Status);
        // A 500 might mean the record was created; retrying could duplicate it.
        Assert.Single(handler.ApiRequests);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task DoesNotRetrySingleRegistrationOnNetworkFault()
    {
        var delays = new List<TimeSpan>();
        FakeHttpHandler handler = QueuedHandler(
            () => throw new HttpRequestException("connection reset"));
        VerifiablClient client = Client(handler, delays);

        await Assert.ThrowsAsync<VerifiablTransportException>(
            () => client.RegisterNonPiiAsync(SingleRequest()));

        Assert.Single(handler.ApiRequests);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task RetriesSingleRegistrationOn429WhichLeavesItUnprocessed()
    {
        var delays = new List<TimeSpan>();
        FakeHttpHandler handler = QueuedHandler(
            () => FakeHttpHandler.Json((HttpStatusCode)429, "{}"),
            RegistrationOk);
        VerifiablClient client = Client(handler, delays);

        RegisterNonPiiResponse response = await client.RegisterNonPiiAsync(SingleRequest());

        Assert.Equal(Reference, response.VerifiablReference);
        Assert.Equal(2, handler.ApiRequests.Count());
    }

    [Fact]
    public async Task HonoursRetryAfterHeader()
    {
        var delays = new List<TimeSpan>();
        FakeHttpHandler handler = QueuedHandler(
            () =>
            {
                HttpResponseMessage response = FakeHttpHandler.Json((HttpStatusCode)429, "{}");
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
                return response;
            },
            BatchOk);
        VerifiablClient client = Client(handler, delays);

        await client.RegisterNonPiiBatchAsync([BatchRecordItem()]);

        Assert.Single(delays);
        Assert.Equal(TimeSpan.FromSeconds(2), delays[0]);
    }

    [Fact]
    public async Task HonoursARetryAfterLongerThanTheBackoffCap()
    {
        var delays = new List<TimeSpan>();
        FakeHttpHandler handler = QueuedHandler(
            () =>
            {
                HttpResponseMessage response = FakeHttpHandler.Json((HttpStatusCode)429, "{}");
                response.Headers.RetryAfter =
                    new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
                return response;
            },
            BatchOk);
        VerifiablClient client = Client(handler, delays);

        await client.RegisterNonPiiBatchAsync([BatchRecordItem()]);

        // The server's backpressure request wins over the internal backoff cap.
        Assert.Single(delays);
        Assert.Equal(TimeSpan.FromSeconds(60), delays[0]);
    }

    [Fact]
    public async Task DisablingRetriesSurfacesTheFirstFailure()
    {
        var delays = new List<TimeSpan>();
        FakeHttpHandler handler = QueuedHandler(
            () => FakeHttpHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"));
        VerifiablClient client = Client(handler, delays, o => o.MaxRetries = 0);

        VerifiablApiException error = await Assert.ThrowsAsync<VerifiablApiException>(
            () => client.RegisterNonPiiBatchAsync([BatchRecordItem()]));

        Assert.Equal(503, error.Status);
        Assert.Single(handler.ApiRequests);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task SendsUserAgentWithoutTelemetryOnEveryRequest()
    {
        var delays = new List<TimeSpan>();
        FakeHttpHandler handler = QueuedHandler(RegistrationOk);
        VerifiablClient client = Client(handler, delays);

        await client.RegisterNonPiiAsync(SingleRequest());

        CapturedRequest sent = Assert.Single(handler.Requests);
        Assert.NotNull(sent.UserAgent);
        Assert.StartsWith("verifiabl-dotnet/", sent.UserAgent);
    }

    [Fact]
    public void RejectsNegativeMaxRetries()
    {
        Assert.Throws<ArgumentException>(() => new VerifiablClient(new VerifiablClientOptions
        {
            Auth = VerifiablAuth.ApiKey("static-key"),
            MaxRetries = -1,
        }));
    }
}
