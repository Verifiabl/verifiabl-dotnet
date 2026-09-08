using System.Text;
using System.Text.Json;
using Xunit;

namespace Verifiabl.Tests;

public class PiiTests
{
    [Fact]
    public void FormatsP2ByDefaultInWireOrder()
    {
        string formatted = Pii.Format(new PiiFields
        {
            EmployeeName = "Jane A. Doe",
            Position = "Senior Developer",
            Department = "Engineering",
            EmployerAbn = "12345678901",
            Bsb = "062-000",
            AccountNumber = "12345678",
            AccountName = "Jane A Doe",
            Address = "12 Example St, Sydney NSW 2000",
        });

        Assert.Equal(
            "P2|Jane A. Doe|Senior Developer|Engineering|12345678901|062-000|12345678|Jane A Doe|12 Example St, Sydney NSW 2000",
            formatted);
    }

    [Fact]
    public void EncodesOmittedP2FieldsAsEmptySegments()
    {
        string formatted = Pii.Format(new PiiFields { EmployeeName = "Jane" });

        Assert.Equal("P2|Jane|||||||", formatted);
    }

    [Fact]
    public void RoundTripsP2ThroughParse()
    {
        var fields = new PiiFields
        {
            EmployeeName = "Jane A. Doe",
            Department = "Engineering",
            AccountNumber = "12345678",
            Address = "12 Example St",
        };

        PiiFields parsed = Pii.Parse(Pii.Format(fields));

        Assert.Equal(fields.EmployeeName, parsed.EmployeeName);
        Assert.Null(parsed.Position);
        Assert.Equal(fields.Department, parsed.Department);
        Assert.Null(parsed.EmployerAbn);
        Assert.Null(parsed.Bsb);
        Assert.Equal(fields.AccountNumber, parsed.AccountNumber);
        Assert.Null(parsed.AccountName);
        Assert.Equal(fields.Address, parsed.Address);
    }

    [Fact]
    public void FormatsAndParsesLegacyP1ForRollback()
    {
        var fields = new PiiFields { EmployeeName = "Jane", Address = "not emitted" };

        string plaintext = Pii.FormatV1(fields);
        PiiFields parsed = Pii.Parse(plaintext);

        Assert.Equal("P1|Jane||||||", plaintext);
        Assert.Equal("Jane", parsed.EmployeeName);
        Assert.Null(parsed.Address);
    }

    [Fact]
    public void RejectsPipeCharacters()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Pii.Format(new PiiFields { EmployeeName = "Jane|Doe" }));

        Assert.Contains("EmployeeName", exception.Message);
    }

    [Fact]
    public void RejectsControlCharacters()
    {
        Assert.Throws<ArgumentException>(
            () => Pii.Format(new PiiFields { Position = "Dev\nOps" }));
        Assert.Throws<ArgumentException>(
            () => Pii.Format(new PiiFields { Position = "Dev\tOps" }));
    }

    [Fact]
    public void RejectsOverlongFields()
    {
        Assert.Throws<ArgumentException>(
            () => Pii.Format(new PiiFields { AccountName = new string('a', 257) }));
    }

    [Fact]
    public void AcceptsFieldsAtTheLengthLimit()
    {
        string formatted = Pii.Format(new PiiFields { AccountName = new string('a', 256) });

        Assert.EndsWith(new string('a', 256) + "|", formatted);
    }

    private static PiiFields V2Fields(string? address = null) => new()
    {
        EmployeeName = "Zoë Nguyễn",
        Position = "Ingénieure",
        Department = "R&D",
        EmployerAbn = "53004085616",
        Bsb = "062-000",
        AccountNumber = "12345678",
        AccountName = "Zoë Nguyễn",
        Address = address,
    };

    [Fact]
    public void OptInV2NamesRemainCompatibilityAliasesForTheDefaultWriter()
    {
        var fields = new PiiV2Fields { EmployeeName = "Jane", Address = "12 Example St" };

        Assert.Equal(Pii.Format(fields), Pii.FormatV2(fields));
    }

    [Fact]
    public void V2WritesExactBytesWithAnEmptyFinalAddress()
    {
        byte[] actual = Encoding.UTF8.GetBytes(Pii.Format(V2Fields()));
        byte[] expected = Encoding.UTF8.GetBytes(
            "P2|Zoë Nguyễn|Ingénieure|R&D|53004085616|062-000|12345678|Zoë Nguyễn|");

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void V2PreservesARealisticInternationalAddressVerbatim()
    {
        const string address = "12 Rue de l’Église, Apt 4B, 75005 Paris, France 🇫🇷";
        Assert.EndsWith("|" + address, Pii.Format(V2Fields(address)));
    }

    [Fact]
    public void V2AcceptsExactly320Utf8BytesAndRejectsOneOver()
    {
        string boundary = string.Concat(Enumerable.Repeat("東京", 53)) + "AB";
        Assert.Equal(320, Encoding.UTF8.GetByteCount(boundary));
        Assert.EndsWith("|" + boundary, Pii.Format(V2Fields(boundary)));
        Assert.Throws<ArgumentException>(() => Pii.Format(V2Fields(boundary + "C")));
    }

    [Theory]
    [InlineData("bad|address")]
    [InlineData("bad\naddress")]
    [InlineData("bad\u200Baddress")]
    [InlineData("bad\u2028address")]
    [InlineData("bad\u2029address")]
    [InlineData("bad\U000E0001address")]
    public void V2RejectsDelimiterControlFormatAndSeparatorCharacters(string address)
    {
        Assert.Throws<ArgumentException>(() => Pii.Format(V2Fields(address)));
    }

    [Fact]
    public void V2MatchesTheCanonicalTextProfile()
    {
        string fixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        string profilePath = Path.Combine(fixturesDirectory, "p2-pii-text-profile-v1.json");
        string vectorsPath = Path.Combine(fixturesDirectory, "p2-pii-text-profile-v1-vectors.json");
        using JsonDocument profileDocument = JsonDocument.Parse(File.ReadAllText(profilePath));
        using JsonDocument vectorsDocument = JsonDocument.Parse(File.ReadAllText(vectorsPath));
        JsonElement profile = profileDocument.RootElement;
        JsonElement vectors = vectorsDocument.RootElement;

        Assert.Equal(Pii.TextProfileId, profile.GetProperty("profileId").GetString());
        Assert.Equal(
            profile.GetProperty("profileId").GetString(),
            vectors.GetProperty("profileId").GetString());
        Assert.Equal(
            Pii.TextProfileUnicodeVersion,
            profile.GetProperty("unicodeVersion").GetString());

        foreach (JsonElement range in profile.GetProperty("controlCharacterRanges").EnumerateArray())
        {
            int start = Convert.ToInt32(range[0].GetString(), 16);
            int end = Convert.ToInt32(range[1].GetString(), 16);
            for (int codePoint = start; codePoint <= end; codePoint++)
            {
                Assert.Throws<ArgumentException>(
                    () => Pii.Format(
                        new PiiFields { EmployeeName = char.ConvertFromUtf32(codePoint) }));
            }
        }

        foreach (JsonElement codePointValue in profile.GetProperty("lineSeparatorCodePoints").EnumerateArray())
        {
            int codePoint = Convert.ToInt32(codePointValue.GetString(), 16);
            Assert.Throws<ArgumentException>(
                () => Pii.Format(
                    new PiiFields { EmployeeName = char.ConvertFromUtf32(codePoint) }));
        }

        foreach (JsonElement range in profile.GetProperty("formatCharacterRanges").EnumerateArray())
        {
            int start = Convert.ToInt32(range[0].GetString(), 16);
            int end = Convert.ToInt32(range[1].GetString(), 16);
            for (int codePoint = start; codePoint <= end; codePoint++)
            {
                Assert.Throws<ArgumentException>(
                    () => Pii.Format(
                        new PiiFields { EmployeeName = char.ConvertFromUtf32(codePoint) }));
            }
        }

        foreach (JsonElement vector in vectors.GetProperty("validText").EnumerateArray())
        {
            string value = vector.GetProperty("value").GetString()!;
            Assert.Contains(value, Pii.Format(new PiiFields { EmployeeName = value }));
            Assert.EndsWith("|" + value, Pii.Format(new PiiFields { Address = value }));
        }

        foreach (JsonElement vector in vectors.GetProperty("invalidText").EnumerateArray())
        {
            string value = string.Concat(
                vector.GetProperty("codePoints")
                    .EnumerateArray()
                    .Select(codePoint => char.ConvertFromUtf32(
                        Convert.ToInt32(codePoint.GetString(), 16))));
            Assert.Throws<ArgumentException>(
                () => Pii.Format(new PiiFields { EmployeeName = value }));
            Assert.Throws<ArgumentException>(
                () => Pii.Format(new PiiFields { Address = value }));
        }

        foreach (JsonElement vector in vectors.GetProperty("invalidUtf16").EnumerateArray())
        {
            char[] codeUnits = vector.GetProperty("codeUnits")
                .EnumerateArray()
                .Select(value => (char)value.GetInt32())
                .ToArray();
            Assert.Throws<ArgumentException>(
                () => Pii.Format(new PiiFields { EmployeeName = new string(codeUnits) }));
        }
    }

    [Fact]
    public void V2AppliesTheCoreFieldLimitInUtf16CodeUnits()
    {
        string boundary = string.Concat(Enumerable.Repeat("😀", 128));
        Assert.Equal(256, boundary.Length);
        Assert.Contains("😀", Pii.Format(new PiiFields { EmployeeName = boundary }));
        Assert.Throws<ArgumentException>(
            () => Pii.Format(new PiiFields { EmployeeName = boundary + "x" }));
    }

    [Theory]
    [InlineData("\u0600")]
    [InlineData("\u0890")]
    [InlineData("\U00013430")]
    [InlineData("\U0001BCA0")]
    [InlineData("\U000E007F")]
    public void V2UsesTheFixedUnicode17FormatCharacterTable(string formatCharacter)
    {
        Assert.Throws<ArgumentException>(
            () => Pii.Format(new PiiFields { EmployeeName = "Jane" + formatCharacter }));
    }

    [Fact]
    public void V2RejectsMalformedUtf16InsteadOfChangingItDuringEncryption()
    {
        Assert.Throws<ArgumentException>(() => Pii.Format(V2Fields("bad\uD800address")));
        Assert.Throws<ArgumentException>(() => Pii.Format(new PiiFields { EmployeeName = "bad\uDC00name" }));
    }

    [Fact]
    public void ParseRejectsMissingPrefix()
    {
        Assert.Throws<FormatException>(() => Pii.Parse("P2|a|b|c|d|e|f|g"));
    }

    [Fact]
    public void ParseRejectsWrongFieldCount()
    {
        Assert.Throws<FormatException>(() => Pii.Parse("P1|a|b|c"));
        Assert.Throws<FormatException>(() => Pii.Parse("P1|a|b|c|d|e|f|g|extra"));
    }

    [Fact]
    public void FieldOrdersArePermanentWireContracts()
    {
        Assert.Equal(
            ["employeeName", "position", "department", "employerAbn", "bsb", "accountNumber", "accountName", "address"],
            Pii.FieldOrder);
        Assert.Equal(
            ["employeeName", "position", "department", "employerAbn", "bsb", "accountNumber", "accountName"],
            Pii.V1FieldOrder);
    }
}
