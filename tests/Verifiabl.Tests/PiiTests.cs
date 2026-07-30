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
