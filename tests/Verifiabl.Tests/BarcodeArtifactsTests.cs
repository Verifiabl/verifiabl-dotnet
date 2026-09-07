using System.Text;
using Verifiabl;

namespace Verifiabl.Tests;

public sealed class BarcodeArtifactsTests
{
    private const string Reference = "AbCdEfGhIjKlMnOpQrStUv";
    private const string Ciphertext = "Zm9vYmFyYmF6cXV4XzEyMzQ1Njc4OTBhYmNkZWZnaGlqa2xtbm9w";

    [Fact]
    public void ReturnsMatchingSvgAndXmpMetadataByDefault()
    {
        var parts = new BarcodeParts(Reference, Ciphertext);
        var options = new BarcodeArtifactsOptions { Environment = VerifiablEnvironment.Sandbox };

        BarcodeArtifactsResult result = VerifiablBarcode.CreateArtifacts(parts, options);
        BarcodeSvgResult expected = VerifiablBarcode.CreateSvg(
            parts,
            new BarcodeSvgOptions { Environment = VerifiablEnvironment.Sandbox });

        Assert.Equal(BarcodeImageFormat.Svg, result.Barcode.Format);
        Assert.Equal(expected.Svg, Encoding.UTF8.GetString(result.Barcode.Data));
        Assert.Equal(expected.Content, result.Barcode.Content);
        Assert.Equal(VerifiablBarcode.PdfPayloadXmpNamespace, result.PdfMetadata.XmpNamespace);
        Assert.Equal(VerifiablBarcode.PdfPayloadXmpProperty, result.PdfMetadata.XmpProperty);
        Assert.Equal(VerifiablBarcode.BuildPayload(parts), result.PdfMetadata.Payload);
        Assert.Equal(
            result.Barcode.Content.Split("#2.")[1],
            result.PdfMetadata.Payload.Split('|')[2]);
    }

    [Fact]
    public void ReturnsPngBytesAtTheRequestedWidth()
    {
        var parts = new BarcodeParts(Reference, Ciphertext);

        BarcodeArtifactsResult result = VerifiablBarcode.CreateArtifacts(
            parts,
            new BarcodeArtifactsOptions
            {
                ImageFormat = BarcodeImageFormat.Png,
                PixelWidth = 480,
            });

        Assert.Equal(BarcodeImageFormat.Png, result.Barcode.Format);
        Assert.Equal(480, result.Barcode.Width);
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        Assert.Equal(signature, result.Barcode.Data.Take(signature.Length).ToArray());
    }

    [Fact]
    public void AppliesTheSameRollbackFormatToQrAndXmpCopies()
    {
        var parts = new BarcodeParts(Reference, Ciphertext);

        BarcodeArtifactsResult result = VerifiablBarcode.CreateArtifacts(
            parts,
            new BarcodeArtifactsOptions
            {
                Format = BarcodePayloadFormat.V1,
                ImageFormat = BarcodeImageFormat.Png,
            });

        Assert.Contains("#1.", result.Barcode.Content);
        Assert.Equal(
            VerifiablBarcode.BuildPayload(parts, BarcodePayloadFormat.V1),
            result.PdfMetadata.Payload);
    }
}
