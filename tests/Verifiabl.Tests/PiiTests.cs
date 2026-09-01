using System.Text;
using Xunit;

namespace Verifiabl.Tests;

public class PiiTests
{
    [Fact]
    public void FormatsAllFieldsInWireOrder()
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
        });

        Assert.Equal(
            "P1|Jane A. Doe|Senior Developer|Engineering|12345678901|062-000|12345678|Jane A Doe",
            formatted);
    }

    [Fact]
    public void EncodesOmittedFieldsAsEmptySegments()
    {
        string formatted = Pii.Format(new PiiFields { EmployeeName = "Jane" });

        Assert.Equal("P1|Jane||||||", formatted);
    }

    [Fact]
    public void RoundTripsThroughParse()
    {
        var fields = new PiiFields
        {
            EmployeeName = "Jane A. Doe",
            Department = "Engineering",
            AccountNumber = "12345678",
        };

        PiiFields parsed = Pii.Parse(Pii.Format(fields));

        Assert.Equal(fields.EmployeeName, parsed.EmployeeName);
        Assert.Null(parsed.Position);
        Assert.Equal(fields.Department, parsed.Department);
        Assert.Null(parsed.EmployerAbn);
        Assert.Null(parsed.Bsb);
        Assert.Equal(fields.AccountNumber, parsed.AccountNumber);
        Assert.Null(parsed.AccountName);
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

        Assert.EndsWith(new string('a', 256), formatted);
    }

    private static PiiV2Fields V2Fields(string? address = null) => new()
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
    public void V2WritesExactBytesWithAnEmptyFinalAddress()
    {
        byte[] actual = Encoding.UTF8.GetBytes(Pii.FormatV2(V2Fields()));
        byte[] expected = Encoding.UTF8.GetBytes(
            "P2|Zoë Nguyễn|Ingénieure|R&D|53004085616|062-000|12345678|Zoë Nguyễn|");

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void V2PreservesARealisticInternationalAddressVerbatim()
    {
        const string address = "12 Rue de l’Église, Apt 4B, 75005 Paris, France 🇫🇷";
        Assert.EndsWith("|" + address, Pii.FormatV2(V2Fields(address)));
    }

    [Fact]
    public void V2AcceptsExactly320Utf8BytesAndRejectsOneOver()
    {
        string boundary = string.Concat(Enumerable.Repeat("東京", 53)) + "AB";
        Assert.Equal(320, Encoding.UTF8.GetByteCount(boundary));
        Assert.EndsWith("|" + boundary, Pii.FormatV2(V2Fields(boundary)));
        Assert.Throws<ArgumentException>(() => Pii.FormatV2(V2Fields(boundary + "C")));
    }

    [Theory]
    [InlineData("bad|address")]
    [InlineData("bad\naddress")]
    [InlineData("bad\u200Baddress")]
    public void V2RejectsDelimiterControlAndFormatCharacters(string address)
    {
        Assert.Throws<ArgumentException>(() => Pii.FormatV2(V2Fields(address)));
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
    public void FieldOrderIsTheWireContract()
    {
        Assert.Equal(
            ["employeeName", "position", "department", "employerAbn", "bsb", "accountNumber", "accountName"],
            Pii.FieldOrder);
    }
}
