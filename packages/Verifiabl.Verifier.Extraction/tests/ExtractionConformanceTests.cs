using System.Text.Json;
using Verifiabl.Verifier.Extraction.Internal;
using Xunit;

namespace Verifiabl.Verifier.Extraction.Tests;

/// <summary>
/// The shared extraction fixtures, which every host must read the same way.
/// ExtractionFixtures is a copy of the extraction module's fixtures; do not edit it.
/// </summary>
public class ExtractionConformanceTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "ExtractionFixtures");

    public static TheoryData<int> Expectations()
    {
        var indexes = new TheoryData<int>();
        for (int index = 0; index < Expected().GetArrayLength(); index++)
        {
            indexes.Add(index);
        }

        return indexes;
    }

    [Theory]
    [MemberData(nameof(Expectations))]
    public void ReadsEachFixtureAsExpected(int index)
    {
        JsonElement expected = Expected()[index];
        byte[] file = File.ReadAllBytes(Path.Combine(Fixtures, expected.GetProperty("file").GetString()!));
        string read = expected.GetProperty("read").GetString()!;

        (string status, SandboxedPdfPayloads? found) = read == "pixels"
            ? ReadPixels(file, expected.GetProperty("pixels"))
            : Read(file, pdf: read == "pdf");

        Assert.Equal(expected.GetProperty("status").GetString(), status);
        if (status == "ok")
        {
            Assert.Equal(Strings(expected, "codes"), found!.QrCodes);
            if (read == "pdf")
            {
                Assert.Equal(Strings(expected, "metadata"), found.MetadataPayloads);
                Assert.Equal(expected.GetProperty("pages").GetInt32(), found.Pages);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefusesAFileOverTheSizeLimitAndReadsOneAtIt(bool pdf)
    {
        Assert.Equal("fileTooLarge", Read(new byte[SandboxModule.MaxFileBytes + 1], pdf).Status);
        Assert.Equal("unreadable", Read(new byte[SandboxModule.MaxFileBytes], pdf).Status);
    }

    [Fact]
    public void RefusesPixelsOverTheSizeLimitAndReadsThoseAtIt()
    {
        // One pixel: the module reports a longer buffer as unreadable, so only the host reports too many pixels.
        Assert.Equal("tooManyPixels", ReadPixels(new byte[ExtractionModule.MaxPixelBytes + 1], 1, 1, PixelFormat.Gray8).Status);
        Assert.Equal("unreadable", ReadPixels(new byte[ExtractionModule.MaxPixelBytes], 1, 1, PixelFormat.Gray8).Status);
    }

    private static (string Status, SandboxedPdfPayloads? Found) Read(byte[] file, bool pdf)
    {
        SandboxedPdfPayloads? found = null;
        string status = ReadStatus.Of(() => found = pdf
            ? ExtractionModule.ReadPdf(file, CancellationToken.None)
            : new SandboxedPdfPayloads([], ExtractionModule.ReadImage(file, CancellationToken.None), 0));
        return (status, found);
    }

    // A pixel fixture has no header, so its expectation gives its shape.
    private static (string Status, SandboxedPdfPayloads? Found) ReadPixels(byte[] pixels, JsonElement shape) =>
        ReadPixels(
            pixels,
            shape.GetProperty("width").GetInt32(),
            shape.GetProperty("height").GetInt32(),
            (PixelFormat)Enum.Parse(typeof(PixelFormat), shape.GetProperty("format").GetString()!, ignoreCase: true));

    private static (string Status, SandboxedPdfPayloads? Found) ReadPixels(byte[] pixels, int width, int height, PixelFormat format)
    {
        SandboxedPdfPayloads? found = null;
        string status = ReadStatus.Of(() => found = new SandboxedPdfPayloads(
            [],
            ExtractionModule.ReadPixels(pixels, width, height, format, CancellationToken.None),
            0));
        return (status, found);
    }

    private static JsonElement Expected() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json"))).RootElement;

    private static string[] Strings(JsonElement expected, string property) =>
        expected.GetProperty(property).EnumerateArray().Select(element => element.GetString()!).ToArray();
}
