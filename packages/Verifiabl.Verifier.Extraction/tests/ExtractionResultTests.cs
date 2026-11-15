using System.Text;
using Verifiabl.Verifier.Extraction.Internal;
using Xunit;

namespace Verifiabl.Verifier.Extraction.Tests;

public class ExtractionResultTests
{
    [Fact]
    public void ParsesTheCodesOfAnImage()
    {
        Assert.Equal(["a", "b"], ExtractionResult.ParseCodes(Utf8("""{"codes":["a","b"]}""")));
        Assert.Empty(ExtractionResult.ParseCodes(Utf8("""{"codes":[]}"""))!);
    }

    [Fact]
    public void ParsesWhatAPdfHolds()
    {
        SandboxedPdfPayloads? found = ExtractionResult.ParsePdf(Utf8("""{"metadata":["m"],"codes":["c"],"pages":3}"""));

        Assert.Equal(["m"], found!.MetadataPayloads);
        Assert.Equal(["c"], found.QrCodes);
        Assert.Equal(3, found.Pages);
        Assert.Equal(ExtractionModule.MaxPdfPages, ExtractionResult.ParsePdf(Utf8("""{"metadata":[],"codes":[],"pages":50}"""))!.Pages);
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"codes":[""")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"codes":"a"}""")]
    [InlineData("""{"codes":[1]}""")]
    [InlineData("""{"codes":[null]}""")]
    [InlineData("""{"codes":[["a"]]}""")]
    [InlineData("""{"codes":["\ud800"]}""")]
    public void RefusesCodesOfAnotherShape(string json)
    {
        Assert.Null(ExtractionResult.ParseCodes(Utf8(json)));
    }

    [Theory]
    [InlineData("""{"metadata":[],"codes":[]}""")]
    [InlineData("""{"metadata":[],"codes":[],"pages":"1"}""")]
    [InlineData("""{"metadata":[],"codes":[],"pages":1.5}""")]
    [InlineData("""{"metadata":[],"codes":[],"pages":99999999999}""")]
    [InlineData("""{"metadata":[],"codes":[],"pages":-1}""")]
    [InlineData("""{"metadata":[],"codes":[],"pages":51}""")]
    [InlineData("""{"metadata":"m","codes":[],"pages":1}""")]
    [InlineData("""{"metadata":[],"pages":1}""")]
    public void RefusesAPdfResultOfAnotherShape(string json)
    {
        Assert.Null(ExtractionResult.ParsePdf(Utf8(json)));
    }

    [Fact]
    public void RefusesTextThatIsNotUtf8()
    {
        byte[] json = [.. Utf8("""{"codes":["""), (byte)'"', 0xFF, (byte)'"', .. Utf8("]}")];

        Assert.Null(ExtractionResult.ParseCodes(json));
    }

    [Fact]
    public void RefusesNestingDeeperThanAResultHas()
    {
        string json = new string('[', 1000) + new string(']', 1000);

        Assert.Null(ExtractionResult.ParseCodes(Utf8("{\"codes\":" + json + "}")));
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
}
