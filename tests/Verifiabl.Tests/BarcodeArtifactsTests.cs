using Verifiabl;

namespace Verifiabl.Tests;

public sealed class BarcodeArtifactsTests
{
    private const string Reference = "AbCdEfGhIjKlMnOpQrStUv";
    private const string Ciphertext = "Zm9vYmFyYmF6cXV4XzEyMzQ1Njc4OTBhYmNkZWZnaGlqa2xtbm9w";

    [Fact]
    public void ReturnsMatchingSvgAndXmpMetadataInOneCall()
    {
        var parts = new BarcodeParts(Reference, Ciphertext);
        var options = new BarcodeSvgOptions { Environment = VerifiablEnvironment.Sandbox };

        BarcodeArtifactsResult result = VerifiablBarcode.CreateArtifacts(parts, options);

        Assert.Equal(VerifiablBarcode.CreateSvg(parts, options).Svg, result.Barcode.Svg);
        Assert.Equal(VerifiablBarcode.PdfPayloadXmpNamespace, result.PdfMetadata.XmpNamespace);
        Assert.Equal(VerifiablBarcode.PdfPayloadXmpProperty, result.PdfMetadata.XmpProperty);
        Assert.Equal(VerifiablBarcode.BuildPayload(parts), result.PdfMetadata.Payload);
        Assert.Equal(
            result.Barcode.Content.Split("#2.")[1],
            result.PdfMetadata.Payload.Split('|')[2]);
    }

    [Fact]
    public void AppliesTheSameRollbackFormatToQrAndXmpCopies()
    {
        var parts = new BarcodeParts(Reference, Ciphertext);

        BarcodeArtifactsResult result = VerifiablBarcode.CreateArtifacts(
            parts,
            new BarcodeSvgOptions { Format = BarcodePayloadFormat.V1 });

        Assert.Contains("#1.", result.Barcode.Content);
        Assert.Equal(
            VerifiablBarcode.BuildPayload(parts, BarcodePayloadFormat.V1),
            result.PdfMetadata.Payload);
    }
}
