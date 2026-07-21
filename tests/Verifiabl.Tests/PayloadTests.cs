using Xunit;

namespace Verifiabl.Tests;

public class PayloadTests
{
    private const string Reference = "u0FE9WLIS7GYKQnpJPygBw";
    private const string Ciphertext = "abc123DEF456-_";

    [Fact]
    public void BuildsTheV1Payload()
    {
        string payload = VerifiablBarcode.BuildPayload(new BarcodeParts(Reference, Ciphertext));

        Assert.Equal($"1|{Reference}|{Ciphertext}", payload);
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
    public void BuildsTheProductionScanUrlByDefault()
    {
        string url = VerifiablBarcode.BuildScanUrl(new BarcodeParts(Reference, Ciphertext));

        Assert.Equal(
            $"https://verify.verifiabl.io/v/1%7C{Reference}%7C{Ciphertext}",
            url);
    }

    [Fact]
    public void BuildsTheSandboxScanUrl()
    {
        string url = VerifiablBarcode.BuildScanUrl(
            new BarcodeParts(Reference, Ciphertext),
            new ScanUrlOptions { Environment = VerifiablEnvironment.Sandbox });

        Assert.StartsWith("https://verify.sandbox.verifiabl.io/v/", url);
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
