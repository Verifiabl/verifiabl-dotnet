using System.Text;
using Xunit;

namespace Verifiabl.Tests;

public class Base32Tests
{
    [Theory]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void MatchesRfc4648Section10(string plaintext, string expected)
    {
        Assert.Equal(expected, VerifiablBase32.Encode(Encoding.ASCII.GetBytes(plaintext)));
    }

    [Fact]
    public void EmitsCanonicalUppercaseTextWithoutPadding()
    {
        Assert.Equal("74AAC", VerifiablBase32.Encode([0xff, 0x00, 0x01]));
    }
}
