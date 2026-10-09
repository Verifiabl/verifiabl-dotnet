using System.Text;
using System.Text.Json;
using Xunit;

namespace Verifiabl.Tests;

public class IssuanceConformanceTests
{
    private static JsonDocument ReadVectors() => JsonDocument.Parse(
        File.ReadAllText(AppendRelativePath(
            AppendRelativePath(AppContext.BaseDirectory, "Fixtures"),
            "issuance-conformance-vectors-v1.json")));

    [Fact]
    public void MatchesEveryDeterministicIssuanceStageByteForByte()
    {
        using JsonDocument document = ReadVectors();
        foreach (JsonElement vector in document.RootElement.GetProperty("valid").EnumerateArray())
        {
            JsonElement fields = vector.GetProperty("fields");
            string plaintext = Pii.FormatAustralian(new AustralianPiiFields
            {
                EmployeeName = OptionalString(fields, "employeeName"),
                Position = OptionalString(fields, "position"),
                Department = OptionalString(fields, "department"),
                EmployerName = OptionalString(fields, "employerName"),
                EmployerAbn = OptionalString(fields, "employerAbn"),
                Bsb = OptionalString(fields, "bsb"),
                AccountNumber = OptionalString(fields, "accountNumber"),
                AccountName = OptionalString(fields, "accountName"),
                Address = fields.TryGetProperty("address", out JsonElement address)
                    ? new AustralianAddress
                    {
                        Lines = address.TryGetProperty("lines", out JsonElement lines)
                            ? lines.EnumerateArray().Select(line => line.GetString()!).ToArray()
                            : null,
                        Suburb = OptionalString(address, "suburb"),
                        StateOrTerritory = OptionalString(address, "stateOrTerritory"),
                        Postcode = OptionalString(address, "postcode"),
                    }
                    : null,
            });

            Assert.Equal(vector.GetProperty("plaintext").GetString(), plaintext);
            Assert.Equal(
                vector.GetProperty("plaintextUtf8Hex").GetString(),
                Hex(Encoding.UTF8.GetBytes(plaintext)));

            EncryptedPii encrypted = VerifiablCrypto.EncryptPiiWithIv(
                plaintext,
                FromHex(vector.GetProperty("keyHex").GetString()!),
                FromHex(vector.GetProperty("ivHex").GetString()!));
            Assert.Equal(vector.GetProperty("ciphertextHex").GetString(), Hex(encrypted.Ciphertext));
            Assert.Equal(vector.GetProperty("tagHex").GetString(), Hex(encrypted.Metadata.Tag));
            Assert.Equal(vector.GetProperty("ivHex").GetString(), Hex(encrypted.Metadata.Iv));

            var parts = new BarcodeParts(
                vector.GetProperty("reference").GetString()!,
                encrypted.Ciphertext);
            Assert.Equal(vector.GetProperty("xmpPayload").GetString(), VerifiablBarcode.BuildPayload(parts));
            Assert.Equal(
                vector.GetProperty("productionScanUrl").GetString(),
                VerifiablBarcode.BuildScanUrl(parts));
            Assert.Equal(
                vector.GetProperty("sandboxScanUrl").GetString(),
                VerifiablBarcode.BuildScanUrl(
                    parts,
                    new ScanUrlOptions { Environment = VerifiablEnvironment.Sandbox }));
        }
    }

    [Fact]
    public void RejectsEverySharedMalformedPayloadCase()
    {
        using JsonDocument document = ReadVectors();
        foreach (JsonElement vector in document.RootElement.GetProperty("invalidPayloads").EnumerateArray())
        {
            var parts = new BarcodeParts(
                vector.GetProperty("reference").GetString()!,
                Ciphertext(vector.GetProperty("ciphertext")));
            foreach (string operation in vector
                .GetProperty("operations")
                .EnumerateArray()
                .Select(operationElement => operationElement.GetString()!))
            {
                Action invoke = operation == "payload"
                    ? () => VerifiablBarcode.BuildPayload(parts)
                    : () => VerifiablBarcode.BuildScanUrl(parts, ScanOptions(vector));
                Exception error = Assert.ThrowsAny<ArgumentException>(invoke);
                string expectedError = vector.GetProperty("expectedError").GetString()!;
                string[] expectedMessageParts = expectedError switch
                {
                    "invalid-reference" => ["reference"],
                    "invalid-ciphertext" => ["ciphertext", "encryptedpii"],
                    "insecure-scan-base-url" => ["https"],
                    _ => throw new InvalidOperationException($"Unknown fixture error: {expectedError}"),
                };
                Assert.Contains(
                    expectedMessageParts,
                    part => error.Message.Contains(part, StringComparison.OrdinalIgnoreCase));
            }
        }
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

    private static ScanUrlOptions ScanOptions(JsonElement vector)
    {
        var options = new ScanUrlOptions();
        if (vector.TryGetProperty("scanBaseUrl", out JsonElement value))
        {
            options.ScanBaseUrl = new Uri(value.GetString()!);
        }
        return options;
    }

    private static byte[] Ciphertext(JsonElement value)
    {
        if (value.TryGetProperty("hex", out JsonElement hex))
        {
            return FromHex(hex.GetString()!);
        }
        byte repeated = FromHex(value.GetProperty("repeatByteHex").GetString()!)[0];
        return Enumerable.Repeat(repeated, value.GetProperty("length").GetInt32()).ToArray();
    }

    private static string? OptionalString(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement property) ? property.GetString() : null;

    private static byte[] FromHex(string value)
    {
        if (value.Length % 2 != 0)
        {
            throw new InvalidOperationException("Fixture hex must have an even length.");
        }
        var result = new byte[value.Length / 2];
        for (int index = 0; index < result.Length; index++)
        {
            result[index] = Convert.ToByte(value.Substring(index * 2, 2), 16);
        }
        return result;
    }

    private static string Hex(byte[] value) =>
        string.Concat(value.Select(item => item.ToString("x2")));
}
