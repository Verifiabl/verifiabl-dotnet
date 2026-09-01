using Xunit;

namespace Verifiabl.Tests;

public class PayloadTests
{
    private const string Reference = "u0FE9WLIS7GYKQnpJPygBw";
    private const string Ciphertext = "abc123DEF456-_";

    [Fact]
    public void BuildsTheV1PayloadForRollback()
    {
        string payload = VerifiablBarcode.BuildPayload(
            new BarcodeParts(Reference, Ciphertext),
            BarcodePayloadFormat.V1);

        Assert.Equal($"1|{Reference}|{Ciphertext}", payload);
    }

    [Fact]
    public void BuildsTheV2XmpPayloadByDefault()
    {
        string payload = VerifiablBarcode.BuildPayload(
            new BarcodeParts(Reference, "Zm9vYmFy"));

        Assert.Equal($"2|{Reference}|MZXW6YTBOI", payload);
    }

    [Fact]
    public void RejectsNonCanonicalBase64UrlBeforeWritingV2()
    {
        Assert.Throws<ArgumentException>(() => VerifiablBarcode.BuildPayload(
            new BarcodeParts(Reference, "Zh"),
            BarcodePayloadFormat.V2));
    }

    [Fact]
    public void BuildsTheOptInV2XmpPayloadFromExactCiphertextBytes()
    {
        const string ciphertext = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8";
        string payload = VerifiablBarcode.BuildPayload(
            new BarcodeParts(Reference, ciphertext),
            BarcodePayloadFormat.V2);

        Assert.Equal(
            $"2|{Reference}|AAAQEAYEAUDAOCAJBIFQYDIOB4IBCEQTCQKRMFYYDENBWHA5DYPQ",
            payload);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("u0FE9WLIS7GYKQnpJPygBwX")]
    [InlineData("u0FE9WLIS7GYKQnpJPygB+")]
    [InlineData("")]
    public void RejectsMalformedReferences(string reference)
    {
        Assert.Throws<ArgumentException>(
            () => VerifiablBarcode.BuildPayload(new BarcodeParts(reference, Ciphertext)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64url!")]
    [InlineData("has=padding")]
    [InlineData("abcde")] // length % 4 == 1 can never decode
    public void RejectsMalformedCiphertext(string ciphertext)
    {
        Assert.Throws<ArgumentException>(
            () => VerifiablBarcode.BuildPayload(new BarcodeParts(Reference, ciphertext)));
    }

    [Fact]
    public void RejectsOverlongCiphertext()
    {
        Assert.Throws<ArgumentException>(() => VerifiablBarcode.BuildPayload(
            new BarcodeParts(Reference, new string('a', 10_001))));
    }

    [Fact]
    public void BuildsTheV2ProductionScanUrlByDefault()
    {
        string url = VerifiablBarcode.BuildScanUrl(
            new BarcodeParts(Reference, "Zm9vYmFy"));

        Assert.Equal($"https://v.verifiabl.io/v/{Reference}#2.MZXW6YTBOI", url);
    }

    // The ciphertext must never sit in a part of the URL that a client sends to
    // a server, or it lands in request logs we do not control (VER-369).
    [Fact]
    public void KeepsTheCiphertextOutOfEverythingTheServerReceives()
    {
        var url = new Uri(VerifiablBarcode.BuildScanUrl(
            new BarcodeParts(Reference, "Zm9vYmFy")));

        Assert.DoesNotContain("MZXW6YTBOI", url.AbsolutePath);
        Assert.Empty(url.Query);
        Assert.Equal("#2.MZXW6YTBOI", url.Fragment);
    }

    [Fact]
    public void BuildsTheExplicitV2ShortHostScanUrl()
    {
        string url = VerifiablBarcode.BuildScanUrl(
            new BarcodeParts(Reference, "Zm9vYmFy"),
            new ScanUrlOptions { Format = BarcodePayloadFormat.V2 });

        Assert.Equal($"https://v.verifiabl.io/v/{Reference}#2.MZXW6YTBOI", url);
        Assert.Equal(
            VerifiablBarcode.BuildPayload(
                new BarcodeParts(Reference, "Zm9vYmFy"),
                BarcodePayloadFormat.V2).Split('|')[2],
            url.Split(new[] { "#2." }, StringSplitOptions.None)[1]);
    }

    [Fact]
    public void BuildsTheV2SandboxShortHostScanUrl()
    {
        string url = VerifiablBarcode.BuildScanUrl(
            new BarcodeParts(Reference, "Zm9vYmFy"),
            new ScanUrlOptions
            {
                Format = BarcodePayloadFormat.V2,
                Environment = VerifiablEnvironment.Sandbox,
            });

        Assert.Equal($"https://v.sandbox.verifiabl.io/v/{Reference}#2.MZXW6YTBOI", url);
    }

    [Fact]
    public void BuildsTheV1SandboxScanUrlForRollback()
    {
        string url = VerifiablBarcode.BuildScanUrl(
            new BarcodeParts(Reference, Ciphertext),
            new ScanUrlOptions
            {
                Environment = VerifiablEnvironment.Sandbox,
                Format = BarcodePayloadFormat.V1,
            });

        Assert.StartsWith("https://verify.sandbox.verifiabl.io/v/", url);
        Assert.Equal($"#1.{Ciphertext}", new Uri(url).Fragment);
    }

    [Fact]
    public void ScanBaseUrlOverrideKeepsOnlyTheOrigin()
    {
        string url = VerifiablBarcode.BuildScanUrl(
            new BarcodeParts(Reference, Ciphertext),
            new ScanUrlOptions { ScanBaseUrl = new Uri("https://verify.example.com/some/path") });

        Assert.StartsWith("https://verify.example.com/v/", url);
    }

    [Fact]
    public void RejectsNonHttpsScanBaseUrl()
    {
        Assert.Throws<ArgumentException>(() => VerifiablBarcode.BuildScanUrl(
            new BarcodeParts(Reference, Ciphertext),
            new ScanUrlOptions { ScanBaseUrl = new Uri("http://verify.example.com") }));
    }

    [Fact]
    public void GeneratedReferencesAreWellFormedAndUnique()
    {
        var seen = new HashSet<string>();
        for (int i = 0; i < 1000; i++)
        {
            string reference = VerifiablReference.Generate();
            Assert.Equal(22, reference.Length);
            Assert.Matches("^[A-Za-z0-9_-]{22}$", reference);
            Assert.True(seen.Add(reference), "generated references must not repeat");
        }
    }

    [Fact]
    public void GeneratedReferencesValidate()
    {
        Assert.True(VerifiablReference.IsValid(VerifiablReference.Generate()));
        Assert.False(VerifiablReference.IsValid(null));
        Assert.False(VerifiablReference.IsValid("tooShort"));
    }

    [Fact]
    public void PdfMetadataContractIsPermanent()
    {
        // Embedded in already-issued PDFs; changing either is a breaking change.
        Assert.Equal("https://verifiabl.io/ns/", VerifiablBarcode.PdfPayloadXmpNamespace);
        Assert.Equal("payload", VerifiablBarcode.PdfPayloadXmpProperty);
    }
}
