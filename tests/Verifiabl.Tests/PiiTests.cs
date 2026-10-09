using System.Text.Json;
using Xunit;

namespace Verifiabl.Tests;

/// <summary>
/// AU2 and NZ2 apply the shared PII text profile to every field.
/// </summary>
public class PiiTests
{
    private static readonly Func<string, string>[] Formatters =
    [
        value => Pii.FormatAustralian(new AustralianPiiFields { EmployeeName = value }),
        value => Pii.FormatNewZealand(new NewZealandPiiFields { EmployeeName = value }),
        value => Pii.FormatAustralian(new AustralianPiiFields
        {
            Address = new AustralianAddress { Lines = [value] },
        }),
        value => Pii.FormatNewZealand(new NewZealandPiiFields
        {
            Address = new NewZealandAddress { Lines = [value] },
        }),
    ];

    private static string AppendRelativePath(string basePath, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Path must be relative.", nameof(relativePath));
        }

        return basePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar
            + relativePath;
    }

    private static IEnumerable<int> CodePoints(JsonElement ranges) =>
        ranges.EnumerateArray().SelectMany(range =>
        {
            int start = Convert.ToInt32(range[0].GetString(), 16);
            int end = Convert.ToInt32(range[1].GetString(), 16);
            return Enumerable.Range(start, end - start + 1);
        });

    [Fact]
    public void MatchesTheCanonicalTextProfile()
    {
        string fixturesDirectory = AppendRelativePath(AppContext.BaseDirectory, "Fixtures");
        string profilePath = AppendRelativePath(fixturesDirectory, "p2-pii-text-profile-v1.json");
        string vectorsPath = AppendRelativePath(fixturesDirectory, "p2-pii-text-profile-v1-vectors.json");
        using JsonDocument profileDocument = JsonDocument.Parse(File.ReadAllText(profilePath));
        using JsonDocument vectorsDocument = JsonDocument.Parse(File.ReadAllText(vectorsPath));
        JsonElement profile = profileDocument.RootElement;
        JsonElement vectors = vectorsDocument.RootElement;

        Assert.Equal(
            profile.GetProperty("profileId").GetString(),
            vectors.GetProperty("profileId").GetString());
        Assert.Equal(
            Pii.TextProfileUnicodeVersion,
            profile.GetProperty("unicodeVersion").GetString());

        int[] forbidden =
        [
            .. CodePoints(profile.GetProperty("controlCharacterRanges")),
            .. profile.GetProperty("lineSeparatorCodePoints")
                .EnumerateArray()
                .Select(value => Convert.ToInt32(value.GetString(), 16)),
            .. CodePoints(profile.GetProperty("formatCharacterRanges")),
        ];
        string[] validText =
        [
            .. vectors.GetProperty("validText")
                .EnumerateArray()
                .Select(vector => vector.GetProperty("value").GetString()!),
        ];
        string[] invalidText =
        [
            .. vectors.GetProperty("invalidText")
                .EnumerateArray()
                .Select(vector => string.Concat(
                    vector.GetProperty("codePoints")
                        .EnumerateArray()
                        .Select(codePoint => char.ConvertFromUtf32(
                            Convert.ToInt32(codePoint.GetString(), 16))))),
        ];
        string[] invalidUtf16 =
        [
            .. vectors.GetProperty("invalidUtf16")
                .EnumerateArray()
                .Select(vector => new string(vector.GetProperty("codeUnits")
                    .EnumerateArray()
                    .Select(value => (char)value.GetInt32())
                    .ToArray())),
        ];

        foreach (Func<string, string> format in Formatters)
        {
            foreach (int codePoint in forbidden)
            {
                Assert.Throws<ArgumentException>(() => format(char.ConvertFromUtf32(codePoint)));
            }

            foreach (string value in validText)
            {
                Assert.Contains(value, format(value));
            }

            foreach (string value in invalidText.Concat(invalidUtf16))
            {
                Assert.Throws<ArgumentException>(() => format(value));
            }
        }
    }

    [Fact]
    public void AcceptsFieldsOverTheFormer256Utf16CodeUnitLimit()
    {
        string value = new('x', 257);
        foreach (Func<string, string> format in Formatters)
        {
            Assert.Contains(value, format(value));
        }
    }

    [Fact]
    public void PreservesARealisticInternationalAddressLineVerbatim()
    {
        const string line = "12 Rue de l’Église, Apt 4B 🇫🇷";
        Assert.Equal(
            "AU2||||||||" + line,
            Pii.FormatAustralian(new AustralianPiiFields { Address = new AustralianAddress { Lines = [line] } }));
        Assert.Equal(
            "NZ2||||||||" + line,
            Pii.FormatNewZealand(new NewZealandPiiFields { Address = new NewZealandAddress { Lines = [line] } }));
    }

    [Fact]
    public void AcceptsAddressesOverTheFormer320Utf8ByteLimit()
    {
        string line = new('x', 321);
        Assert.EndsWith(
            "|" + line,
            Pii.FormatAustralian(new AustralianPiiFields { Address = new AustralianAddress { Lines = [line] } }));
    }

    [Theory]
    [InlineData("؀")]
    [InlineData("࢐")]
    [InlineData("\U00013430")]
    [InlineData("\U0001BCA0")]
    [InlineData("\U000E007F")]
    public void UsesTheFixedUnicode17FormatCharacterTable(string formatCharacter)
    {
        foreach (Func<string, string> format in Formatters)
        {
            Assert.Throws<ArgumentException>(() => format("Jane" + formatCharacter));
        }
    }

    [Fact]
    public void RejectsMalformedUtf16InsteadOfChangingItDuringEncryption()
    {
        foreach (Func<string, string> format in Formatters)
        {
            Assert.Throws<ArgumentException>(() => format("bad\uD800value"));
            Assert.Throws<ArgumentException>(() => format("bad\uDC00value"));
        }
    }
}
