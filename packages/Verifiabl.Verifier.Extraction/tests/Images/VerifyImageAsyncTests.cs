using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using Xunit;

namespace Verifiabl.Verifier.Extraction.Images.Tests;

public class VerifyImageAsyncTests
{
    private const string Reference = "n5waC35dPCoRtMXW5_gJqw";

    [Fact]
    public async Task SendsOnlyTheQrTextUnchanged()
    {
        var handler = new RecordingHandler();
        using var file = new MemoryStream(TestImages.QrFile(TestImages.ScanLink, SKEncodedImageFormat.Webp));

        ImageVerificationResult result = await Client(handler).VerifyImageAsync(file);

        Assert.Equal(Reference, result.Verification.VerifiablReference);
        Assert.Equal(TestImages.ScanLink, result.Payload.QrCode);
        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.ApiBodies));
        Assert.Equal(TestImages.ScanLink, body.RootElement.GetProperty("barcode").GetString());
        Assert.Single(body.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task WorksWithAnyClientImplementation()
    {
        var fake = new FakeVerifier();

        ImageVerificationResult result = await fake.VerifyImageAsync(TestImages.QrFile(TestImages.ScanLink, SKEncodedImageFormat.Png));

        Assert.Equal(TestImages.ScanLink, Assert.Single(fake.Requests).Barcode);
        Assert.Same(fake.Result, result.Verification);
    }

    [Fact]
    public async Task SendsNothingWithoutAQrCode()
    {
        var fake = new FakeVerifier();

        PayloadExtractionException error = await Assert.ThrowsAsync<PayloadExtractionException>(
            () => fake.VerifyImageAsync(TestImages.BlankPng()));

        Assert.Equal(PayloadExtractionFailure.NotFound, error.Failure);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task SendsNothingForAnUnreadableFile()
    {
        var fake = new FakeVerifier();

        PayloadExtractionException error = await Assert.ThrowsAsync<PayloadExtractionException>(
            () => fake.VerifyImageAsync(Encoding.ASCII.GetBytes("not an image")));

        Assert.Equal(PayloadExtractionFailure.Unreadable, error.Failure);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task AStalledStreamTimesOutBeforeVerification()
    {
        var fake = new FakeVerifier();
        var options = new ImageReadOptions { Timeout = TimeSpan.FromMilliseconds(100) };

        PayloadExtractionException error = await Assert.ThrowsAsync<PayloadExtractionException>(
            () => fake.VerifyImageAsync(new TestStreams.Stalled(), options));

        Assert.Equal(PayloadExtractionFailure.TimedOut, error.Failure);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task VerificationFailuresPassThrough()
    {
        var fake = new FakeVerifier
        {
            Failure = new VerifierRejectedException(VerifierErrorCodes.NotVerified, "Not verified."),
        };

        await Assert.ThrowsAsync<VerifierRejectedException>(
            () => fake.VerifyImageAsync(TestImages.QrFile(TestImages.ScanLink, SKEncodedImageFormat.Png)));
    }

    [Fact]
    public async Task ARetryWithTheFailuresKeySendsTheSameKey()
    {
        byte[] file = TestImages.QrFile(TestImages.ScanLink, SKEncodedImageFormat.Png);
        var handler = new RecordingHandler { UnavailableResponses = 1 };
        VerifierClient client = Client(handler, maxRetries: 0);

        VerifierServiceException failure = await Assert.ThrowsAsync<VerifierServiceException>(() => client.VerifyImageAsync(file));
        ImageVerificationResult result = await client.VerifyImageAsync(file, idempotencyKey: failure.IdempotencyKey);

        Assert.NotNull(failure.IdempotencyKey);
        string sent = failure.IdempotencyKey!.Value.ToString("D");
        Assert.Equal([sent, sent], handler.IdempotencyKeys);
        Assert.Equal(failure.IdempotencyKey, result.Verification.IdempotencyKey);
    }

    [Fact]
    public async Task EachCallWithoutAKeyIsANewVerification()
    {
        byte[] file = TestImages.QrFile(TestImages.ScanLink, SKEncodedImageFormat.Png);
        var fake = new FakeVerifier();

        await fake.VerifyImageAsync(file);
        await fake.VerifyImageAsync(file);

        Assert.NotEqual(fake.Requests[0].IdempotencyKey, fake.Requests[1].IdempotencyKey);
    }

    [Fact]
    public void AResultNeedsAQrCodeAndIsSafeToLog()
    {
        var fake = new FakeVerifier();

        Assert.Throws<ArgumentException>(() => new ImageVerificationResult(fake.Result, new ImagePayload(null)));
        Assert.DoesNotContain(TestImages.ScanLink, new ImageVerificationResult(fake.Result, new ImagePayload(TestImages.ScanLink)).ToString());
    }

    [Fact]
    public async Task RejectsANullClient()
    {
        IVerifierClient client = null!;

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.VerifyImageAsync(TestImages.BlankPng()));
    }

    private static VerifierClient Client(RecordingHandler handler, int maxRetries = 2) => new(new VerifierClientOptions
    {
        Auth = VerifierAuth.ClientCredentials("id", "secret"),
        HttpClient = new HttpClient(handler),
        MaxRetries = maxRetries,
    });

    private static VerificationResult Verified()
    {
        using JsonDocument empty = JsonDocument.Parse("{}");
        return new VerificationResult(Reference, null, empty.RootElement, empty.RootElement, new DateTimeOffset(2026, 6, 9, 9, 10, 11, TimeSpan.Zero));
    }

    /// <summary>A test double, as an integration's own fake or decorator would be.</summary>
    private sealed class FakeVerifier : IVerifierClient
    {
        internal List<VerificationRequest> Requests { get; } = [];

        internal VerifierException? Failure { get; set; }

        internal VerificationResult Result { get; } = Verified();

        public Task<VerificationResult> VerifyAsync(VerificationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Failure is null ? Task.FromResult(Result) : Task.FromException<VerificationResult>(Failure);
        }
    }

    /// <summary>Answers token and verification requests and records the verification bodies and keys.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        internal List<string> ApiBodies { get; } = [];

        internal List<string> IdempotencyKeys { get; } = [];

        /// <summary>How many verification requests to answer with 503 before verifying.</summary>
        internal int UnavailableResponses { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bool isToken = request.RequestUri!.AbsolutePath.Contains("oauth");
            if (!isToken)
            {
                ApiBodies.Add(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
                IdempotencyKeys.Add(request.Headers.GetValues("Idempotency-Key").Single());
                if (UnavailableResponses > 0)
                {
                    UnavailableResponses--;
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent("""{"error":"Unavailable","code":"SERVICE_UNAVAILABLE"}""", Encoding.UTF8, "application/json"),
                    };
                }
            }

            string json = isToken
                ? """{"access_token":"token","token_type":"Bearer","expires_in":3600}"""
                : """{"verified":true,"verifiabl_reference":"n5waC35dPCoRtMXW5_gJqw","employee":{},"payslip":{},"decrypted_at":"2026-06-09T09:10:11Z"}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
