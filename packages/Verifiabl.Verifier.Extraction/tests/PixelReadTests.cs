using Verifiabl.Verifier.Extraction.Internal;
using Xunit;

namespace Verifiabl.Verifier.Extraction.Tests;

public class PixelReadTests
{
    [Fact]
    public void RefusesPixelsThatNeedMoreMemoryThanTheCap()
    {
        // A 2000x1500 RGBA buffer is 12 MB, more than an 8 MiB cap.
        byte[] pixels = new byte[2000 * 1500 * 4];

        var failure = Assert.Throws<PayloadExtractionException>(
            () => ExtractionModule.ReadPixels(pixels, 2000, 1500, PixelFormat.Rgba32, 8L * 1024 * 1024, CancellationToken.None));

        Assert.Equal(PayloadExtractionFailure.LimitExceeded, failure.Failure);
        Assert.Equal(ExtractionModule.FailureFor(ExtractionStatus.OutOfMemory).Message, failure.Message);
    }

    [Fact]
    public void ReadsNoCodesFromBlankPixels()
    {
        byte[] white = Enumerable.Repeat((byte)0xFF, 64 * 48).ToArray();

        Assert.Empty(ExtractionModule.ReadPixels(white, 64, 48, PixelFormat.Gray8, CancellationToken.None));
    }
}
