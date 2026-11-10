using Xunit;

namespace Verifiabl.Verifier.Tests;

public class VerifiablPayloadTextTests
{
    private const string Reference = "n5waC35dPCoRtMXW5_gJqw";

    [Theory]
    [InlineData("https://verifiabl.io/v/abc", true)]
    [InlineData("https://v.sandbox.verifiabl.io/v/abc#2.AB", true)]
    [InlineData("1|ref|ct", true)]
    [InlineData("2|ref|ct", true)]
    [InlineData("http://v.verifiabl.io/v/abc", false)]
    [InlineData("https://evilverifiabl.io/v/abc", false)]
    [InlineData("https://v.verifiabl.io.example.com/v/abc", false)]
    [InlineData("https://v.verifiabl.io/other/abc", false)]
    [InlineData("https://verify.example.com/v/" + Reference + "#2.JVSWY3DP", true)]
    [InlineData("https://verify.example.com/v/" + Reference + "#1.JVSWY3DP", true)]
    [InlineData("https://verify.example.com/v/abc#2.JVSWY3DP", false)]
    [InlineData("https://verify.example.com/v/" + Reference, false)]
    [InlineData("https://verify.example.com/v/" + Reference + "#3.JVSWY3DP", false)]
    [InlineData("https://verify.example.com/v/" + Reference + "#2.jvswy3dp", false)]
    [InlineData("https://verify.example.com/v/" + Reference + "?utm=1#2.JVSWY3DP", false)]
    [InlineData("https://verify.example.com/v/" + Reference + "/extra#2.JVSWY3DP", false)]
    [InlineData("http://verify.example.com/v/" + Reference + "#2.JVSWY3DP", false)]
    [InlineData("https://shop.example.com/products/123", false)]
    [InlineData("3|ref|ct", false)]
    [InlineData("", false)]
    public void RecognisesVerifiablPayloadShapes(string text, bool expected)
    {
        Assert.Equal(expected, VerifiablPayloadText.IsVerifiablShaped(text));
    }

    [Theory]
    [InlineData("https://v.verifiabl.io/v/" + Reference + "#2.JVSWY3DP", "2|" + Reference + "|JVSWY3DP", true)]
    [InlineData("https://v.sandbox.verifiabl.io/v/" + Reference + "#1.JVSWY3DP", "1|" + Reference + "|JVSWY3DP", true)]
    [InlineData("https://v.verifiabl.io/v/" + Reference + "#2.JVSWY3DP", "https://v.sandbox.verifiabl.io/v/" + Reference + "#2.JVSWY3DP", true)]
    [InlineData("https://verify.example.com/v/" + Reference + "#2.JVSWY3DP", "2|" + Reference + "|JVSWY3DP", true)]
    [InlineData("2|" + Reference + "|JVSWY3DP", "2|" + Reference + "|JVSWY3DP", true)]
    [InlineData("https://v.verifiabl.io/v/" + Reference + "#2.JVSWY3DP", "1|" + Reference + "|JVSWY3DP", false)]
    [InlineData("https://v.verifiabl.io/v/" + Reference + "#2.JVSWY3DP", "2|" + Reference + "|AAAAAAAA", false)]
    [InlineData("https://v.verifiabl.io/v/" + Reference + "#2.JVSWY3DP", "2|AAAAAAAAAAAAAAAAAAAAAA|JVSWY3DP", false)]
    [InlineData("https://example.com/v/" + Reference + "?x=1#2.JVSWY3DP", "2|" + Reference + "|JVSWY3DP", false)]
    [InlineData("https://v.verifiabl.io/v/1%7C" + Reference + "%7CYWJj", "1|" + Reference + "|YWJj", true)]
    [InlineData("https://v.verifiabl.io/v/1%7C" + Reference + "%7CYWJj", "1|" + Reference + "|AAAA", false)]
    public void ComparesPayloadsByContent(string first, string second, bool expected)
    {
        Assert.Equal(expected, VerifiablPayloadText.AreSamePayload(first, second));
        Assert.Equal(expected, VerifiablPayloadText.AreSamePayload(second, first));
    }

    [Fact]
    public void RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => VerifiablPayloadText.IsVerifiablShaped(null!));
        Assert.Throws<ArgumentNullException>(() => VerifiablPayloadText.AreSamePayload(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => VerifiablPayloadText.AreSamePayload("x", null!));
    }
}
