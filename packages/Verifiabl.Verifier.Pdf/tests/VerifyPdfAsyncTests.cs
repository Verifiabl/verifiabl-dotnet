using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Verifiabl.Verifier.Extraction;
using Xunit;

namespace Verifiabl.Verifier.Pdf.Tests;

public class VerifyPdfAsyncTests
{
    private const string MetadataPayload = "2|n5waC35dPCoRtMXW5_gJqw|JVSWY3DP";

    [Fact]
    public async Task VerifiesTheMetadataEvenWhenTheQrCodeHoldsAnotherPayload()
    {
        (byte[] png, _) = TestPdfs.IssuerBadge(fill: 7);
        string metadata = TestPdfs.IssuerMetadataPayload(fill: 9);
        var handler = new RecordingHandler();
        using var pdf = new MemoryStream(TestPdfs.Build(pngs: [png], metadataPayload: metadata));

        PdfVerificationResult result = await Client(handler).VerifyPdfAsync(pdf);

        Assert.Equal(metadata, result.Payloads.Metadata);
        Assert.Null(result.Payloads.QrCode);
        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.ApiBodies));
        Assert.Equal(metadata, body.RootElement.GetProperty("barcode").GetString());
        Assert.Single(body.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task FallsBackToTheQrCodeWithoutMetadata()
    {
        (byte[] png, string content) = TestPdfs.IssuerBadge();
        var fake = new FakeVerifier();

        PdfVerificationResult result = await fake.VerifyPdfAsync(TestPdfs.Build(pngs: [png]));

        Assert.Equal(content, result.Payloads.QrCode);
        Assert.Equal(content, Assert.Single(fake.Barcodes));
    }

    [Fact]
    public async Task WorksWithAnyClientImplementation()
    {
        var fake = new FakeVerifier();

        PdfVerificationResult result = await fake.VerifyPdfAsync(TestPdfs.Build(metadataPayload: MetadataPayload));

        VerificationRequest request = Assert.Single(fake.Requests);
        Assert.Equal(MetadataPayload, request.Barcode);
        Assert.Same(fake.Result, result.Verification);
    }

    [Fact]
    public void AResultNeedsAPayload()
    {
        var fake = new FakeVerifier();

        Assert.Throws<ArgumentException>(() => new PdfVerificationResult(fake.Result, new PdfPayloads(null, null)));
        Assert.DoesNotContain(MetadataPayload, new PdfVerificationResult(fake.Result, new PdfPayloads(MetadataPayload, null)).ToString());
    }

    [Fact]
    public async Task SendsNothingWithoutAPayloadAndNamesSkippedImages()
    {
        var fake = new FakeVerifier();

        PayloadExtractionException error = await Assert.ThrowsAsync<PayloadExtractionException>(
            () => fake.VerifyPdfAsync(TestPdfs.Build(jpegs: [TestImages.JpegHeaderOnly(300, 300)])));

        Assert.Equal(PayloadExtractionFailure.NotFound, error.Failure);
        Assert.Contains("JpegDecoder", error.Message);
        Assert.Empty(fake.Barcodes);
    }

    [Fact]
    public async Task ReportsUnreadablePdfs()
    {
        var fake = new FakeVerifier();

        PayloadExtractionException error = await Assert.ThrowsAsync<PayloadExtractionException>(
            () => fake.VerifyPdfAsync(Encoding.ASCII.GetBytes("%PDF-1.7\nnot really a pdf")));

        Assert.Equal(PayloadExtractionFailure.Unreadable, error.Failure);
        Assert.Empty(fake.Barcodes);
    }

    [Fact]
    public async Task AStalledStreamTimesOutBeforeVerification()
    {
        var fake = new FakeVerifier();
        var options = new PdfReadOptions { Timeout = TimeSpan.FromMilliseconds(100) };

        PayloadExtractionException error = await Assert.ThrowsAsync<PayloadExtractionException>(
            () => fake.VerifyPdfAsync(new TestStreams.Stalled(), options));

        Assert.Equal(PayloadExtractionFailure.TimedOut, error.Failure);
        Assert.Empty(fake.Barcodes);
    }

    [Fact]
    public async Task VerificationFailuresPassThrough()
    {
        var fake = new FakeVerifier
        {
            Failure = new VerifierRejectedException(VerifierErrorCodes.NotVerified, "Not verified."),
        };

        await Assert.ThrowsAsync<VerifierRejectedException>(
            () => fake.VerifyPdfAsync(TestPdfs.Build(metadataPayload: MetadataPayload)));
    }

    [Fact]
    public async Task ARetryWithTheFailuresKeySendsTheSameKey()
    {
        byte[] pdf = TestPdfs.Build(metadataPayload: MetadataPayload);
        var handler = new RecordingHandler { UnavailableResponses = 1 };
        VerifierClient client = Client(handler, maxRetries: 0);

        VerifierServiceException failure = await Assert.ThrowsAsync<VerifierServiceException>(() => client.VerifyPdfAsync(pdf));
        PdfVerificationResult result = await client.VerifyPdfAsync(pdf, idempotencyKey: failure.IdempotencyKey);

        Assert.NotNull(failure.IdempotencyKey);
        string sent = failure.IdempotencyKey!.Value.ToString("D");
        Assert.Equal([sent, sent], handler.IdempotencyKeys);
        Assert.Equal(failure.IdempotencyKey, result.Verification.IdempotencyKey);
    }

    [Fact]
    public async Task EachCallWithoutAKeyIsANewVerification()
    {
        byte[] pdf = TestPdfs.Build(metadataPayload: MetadataPayload);
        var fake = new FakeVerifier();

        await fake.VerifyPdfAsync(pdf);
        await fake.VerifyPdfAsync(pdf);

        Assert.NotEqual(fake.Requests[0].IdempotencyKey, fake.Requests[1].IdempotencyKey);
    }

    [Fact]
    public async Task RejectsANullClient()
    {
        IVerifierClient client = null!;

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.VerifyPdfAsync(TestPdfs.Build()));
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
        return new VerificationResult(TestPdfs.Reference, null, empty.RootElement, empty.RootElement, new DateTimeOffset(2026, 6, 9, 9, 10, 11, TimeSpan.Zero));
    }

    /// <summary>A test double, as an integration's own fake or decorator would be.</summary>
    private sealed class FakeVerifier : IVerifierClient
    {
        internal List<VerificationRequest> Requests { get; } = [];

        internal IEnumerable<string> Barcodes => Requests.Select(request => request.Barcode);

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
