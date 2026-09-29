using System.Text;
using System.Text.Json;
using Xunit;

namespace Verifiabl.Tests;

public class JurisdictionPiiTests
{
    private static readonly JsonElement Vectors = LoadVectors();

    public static TheoryData<string> ValidVectorIds() => VectorIds("valid");

    public static TheoryData<string> InvalidVectorIds() => VectorIds("invalid");

    [Theory]
    [MemberData(nameof(ValidVectorIds))]
    public void MatchesTheSharedValidVector(string id)
    {
        JsonElement vector = FindVector("valid", id);
        string plaintext = FormatVector(vector);

        Assert.Equal(vector.GetProperty("plaintext").GetString(), plaintext);
        Assert.Equal(
            vector.GetProperty("plaintextUtf8Hex").GetString(),
            string.Concat(Encoding.UTF8.GetBytes(plaintext).Select(item => item.ToString("x2"))));
    }

    [Theory]
    [MemberData(nameof(InvalidVectorIds))]
    public void RejectsTheSharedInvalidVector(string id)
    {
        JsonElement vector = FindVector("invalid", id);
        bool tooLarge = vector.GetProperty("expectedError").GetString() == "payload-too-large";

        ArgumentException exception = Assert.Throws<ArgumentException>(() => FormatVector(vector));
        Assert.Equal(tooLarge, exception.Message.Contains("exceeds"));
    }

    [Fact]
    public void FormatsAustralianPiiInThePermanentAu2Order()
    {
        string plaintext = Pii.FormatAustralian(new AustralianPiiFields
        {
            EmployeeName = "Jo Worker",
            Position = "Analyst",
            Department = "Finance",
            EmployerName = "Acme Pty Ltd",
            EmployerAbn = "12 345 678 901",
            Bsb = "062-000",
            AccountNumber = "****5678",
            AccountName = "J Worker",
            Address = new AustralianAddress
            {
                Lines = ["A204/11-17 Eve Street"],
                Suburb = "Erskineville",
                StateOrTerritory = "NSW",
                Postcode = "2043",
            },
        });

        Assert.Equal(
            "AU2|Jo Worker|Analyst|Finance|12 345 678 901|062-000|****5678|J Worker|" +
            "A204/11-17 Eve Street, Erskineville NSW 2043",
            plaintext);
    }

    [Fact]
    public void AustralianEmployerIdentityFallsBackToTheName()
    {
        string plaintext = Pii.FormatAustralian(new AustralianPiiFields
        {
            EmployerName = "Acme Pty Ltd",
        });

        Assert.Equal("AU2||||Acme Pty Ltd||||", plaintext);
    }

    [Fact]
    public void AustralianEmployerAbnWinsWhenBothInputsArePresent()
    {
        string plaintext = Pii.FormatAustralian(new AustralianPiiFields
        {
            EmployerName = "Acme Pty Ltd",
            EmployerAbn = "12 345 678 901",
        });

        Assert.Equal("AU2||||12 345 678 901||||", plaintext);
    }

    [Fact]
    public void FormatsNewZealandPiiInThePermanentNz2Order()
    {
        string plaintext = Pii.FormatNewZealand(new NewZealandPiiFields
        {
            EmployeeName = "Jo Worker",
            IrdNumber = "***-***-789",
            Position = "Analyst",
            Department = "Finance",
            EmployerName = "Acme Limited",
            AccountNumber = "**-****-****5678-**",
            AccountName = "J Worker",
            Address = new NewZealandAddress
            {
                Lines = ["Level 2", "10 Lambton Quay"],
                Suburb = "Wellington Central",
                City = "Wellington",
                Postcode = "6011",
            },
        });

        Assert.Equal(
            "NZ2|Jo Worker|***-***-789|Analyst|Finance|Acme Limited|**-****-****5678-**|" +
            "J Worker|Level 2, 10 Lambton Quay, Wellington Central, Wellington 6011",
            plaintext);
    }

    [Theory]
    [InlineData(true, "AU2||||||||")]
    [InlineData(false, "NZ2||||||||")]
    public void PreservesAllEightPositionsIncludingTrailingEmptyFields(
        bool australian,
        string expected)
    {
        string plaintext = australian
            ? Pii.FormatAustralian(new AustralianPiiFields())
            : Pii.FormatNewZealand(new NewZealandPiiFields());

        Assert.Equal(expected, plaintext);
        Assert.Equal(9, plaintext.Split('|').Length);
    }

    [Theory]
    [InlineData("bad|value")]
    [InlineData("bad\nvalue")]
    [InlineData("bad\u202Evalue")]
    public void AppliesTheCurrentTextRulesToEveryNewProfile(string value)
    {
        Assert.Throws<ArgumentException>(
            () => Pii.FormatAustralian(new AustralianPiiFields { AccountNumber = value }));
        Assert.Throws<ArgumentException>(
            () => Pii.FormatNewZealand(new NewZealandPiiFields { IrdNumber = value }));
        Assert.Throws<ArgumentException>(
            () => Pii.FormatAustralian(new AustralianPiiFields
            {
                Address = new AustralianAddress { Suburb = value },
            }));
    }

    [Fact]
    public void AllPiiFormattersNameInvalidFieldsWithoutEchoingValues()
    {
        const string value = "Synthetic|Value";
        (string Field, Action Format)[] cases =
        [
            ("EmployeeName", () => Pii.Format(new PiiFields { EmployeeName = value })),
            ("EmployeeName", () => Pii.FormatAustralian(new AustralianPiiFields { EmployeeName = value })),
            ("EmployeeName", () => Pii.FormatNewZealand(new NewZealandPiiFields { EmployeeName = value })),
            ("Lines[1]", () => Pii.FormatAustralian(new AustralianPiiFields { Address = new AustralianAddress { Lines = ["Valid line", value] } })),
            ("Lines[1]", () => Pii.FormatNewZealand(new NewZealandPiiFields { Address = new NewZealandAddress { Lines = ["Valid line", value] } })),
            ("Suburb", () => Pii.FormatAustralian(new AustralianPiiFields { Address = new AustralianAddress { Suburb = value } })),
            ("Suburb", () => Pii.FormatNewZealand(new NewZealandPiiFields { Address = new NewZealandAddress { Suburb = value } })),
            ("StateOrTerritory", () => Pii.FormatAustralian(new AustralianPiiFields { Address = new AustralianAddress { StateOrTerritory = value } })),
            ("City", () => Pii.FormatNewZealand(new NewZealandPiiFields { Address = new NewZealandAddress { City = value } })),
            ("Postcode", () => Pii.FormatAustralian(new AustralianPiiFields { Address = new AustralianAddress { Postcode = value } })),
            ("Postcode", () => Pii.FormatNewZealand(new NewZealandPiiFields { Address = new NewZealandAddress { Postcode = value } })),
        ];

        foreach (var (field, format) in cases)
        {
            ArgumentException error = Assert.Throws<ArgumentException>(format);
            Assert.Equal(field, error.ParamName);
            Assert.Contains(field, error.Message);
            Assert.DoesNotContain(value, error.Message);
        }
    }

    [Fact]
    public void EnforcesTheCompleteProfileByteLimit()
    {
        string boundaryValue = new('a', 1013);
        string plaintext = Pii.FormatAustralian(new AustralianPiiFields
        {
            EmployeeName = boundaryValue,
        });

        Assert.Equal(Pii.PayloadMaxBytes, Encoding.UTF8.GetByteCount(plaintext));
        Assert.Throws<ArgumentException>(() => Pii.FormatAustralian(new AustralianPiiFields
        {
            EmployeeName = boundaryValue + "a",
        }));
    }

    [Fact]
    public void PublishesTheFinalProfileMetadata()
    {
        Assert.Equal("io.verifiabl.au2-pii-text.v1", Pii.AustralianTextProfileId);
        Assert.Equal("io.verifiabl.nz2-pii-text.v1", Pii.NewZealandTextProfileId);
        Assert.Equal(
            ["employeeName", "position", "department", "employerIdentity", "bsb", "accountNumber", "accountName", "address"],
            Pii.AustralianFieldOrder);
        Assert.Equal(
            ["employeeName", "irdNumber", "position", "department", "employerName", "accountNumber", "accountName", "address"],
            Pii.NewZealandFieldOrder);
    }

    private static JsonElement LoadVectors()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(AppendRelativePath(
            AppendRelativePath(AppContext.BaseDirectory, "Fixtures"),
            "jurisdiction-pii-profile-vectors-v1.json")));
        return document.RootElement.Clone();
    }

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

    private static TheoryData<string> VectorIds(string group)
    {
        var ids = new TheoryData<string>();
        foreach (JsonElement vector in Vectors.GetProperty(group).EnumerateArray())
        {
            ids.Add(vector.GetProperty("id").GetString()!);
        }

        return ids;
    }

    private static JsonElement FindVector(string group, string id) =>
        Vectors.GetProperty(group).EnumerateArray().Single(vector => vector.GetProperty("id").GetString() == id);

    private static string FormatVector(JsonElement vector)
    {
        JsonElement fields = vector.GetProperty("fields");
        bool hasAddress = fields.TryGetProperty("address", out JsonElement address);
        return vector.GetProperty("profile").GetString() switch
        {
            "AU2" => Pii.FormatAustralian(new AustralianPiiFields
            {
                EmployeeName = OptionalString(fields, "employeeName"),
                Position = OptionalString(fields, "position"),
                Department = OptionalString(fields, "department"),
                EmployerName = OptionalString(fields, "employerName"),
                EmployerAbn = OptionalString(fields, "employerAbn"),
                Bsb = OptionalString(fields, "bsb"),
                AccountNumber = OptionalString(fields, "accountNumber"),
                AccountName = OptionalString(fields, "accountName"),
                Address = hasAddress
                    ? new AustralianAddress
                    {
                        Lines = OptionalLines(address),
                        Suburb = OptionalString(address, "suburb"),
                        StateOrTerritory = OptionalString(address, "stateOrTerritory"),
                        Postcode = OptionalString(address, "postcode"),
                    }
                    : null,
            }),
            "NZ2" => Pii.FormatNewZealand(new NewZealandPiiFields
            {
                EmployeeName = OptionalString(fields, "employeeName"),
                IrdNumber = OptionalString(fields, "irdNumber"),
                Position = OptionalString(fields, "position"),
                Department = OptionalString(fields, "department"),
                EmployerName = OptionalString(fields, "employerName"),
                AccountNumber = OptionalString(fields, "accountNumber"),
                AccountName = OptionalString(fields, "accountName"),
                Address = hasAddress
                    ? new NewZealandAddress
                    {
                        Lines = OptionalLines(address),
                        Suburb = OptionalString(address, "suburb"),
                        City = OptionalString(address, "city"),
                        Postcode = OptionalString(address, "postcode"),
                    }
                    : null,
            }),
            var profile => throw new InvalidOperationException($"Unknown vector profile {profile}."),
        };
    }

    private static string? OptionalString(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement property) ? property.GetString() : null;

    private static IReadOnlyList<string>? OptionalLines(JsonElement address) =>
        address.TryGetProperty("lines", out JsonElement lines)
            ? lines.EnumerateArray().Select(line => line.GetString()!).ToList()
            : null;
}
