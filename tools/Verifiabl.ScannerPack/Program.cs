using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Verifiabl;

bool stressMode = args.Contains("--stress", StringComparer.Ordinal);
string? requestedOutput = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal));
string outputDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
    requestedOutput ?? Path.Join("artifacts", stressMode ? "qr-stress" : "scanner-pack")));
byte[] key = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
// Address ECC pairs intentionally reuse the same synthetic payload so scan differences
// are attributable to ECC level rather than reference/IV/ciphertext changes.
const int AddressExperimentNonceSlotStart = 5;
var sharedFields = new PiiFields
{
    EmployeeName = "Zoë Nguyễn",
    Position = "Ingénieure systèmes",
    Department = "R&D International",
    EmployerAbn = "53004085616",
    Bsb = "062-000",
    AccountNumber = "12345678",
    AccountName = "Zoë Nguyễn",
};
ScannerFixture[] standardFixtures =
[
    new(
        "minimal",
        "Short P2 payload with only an employee name",
        FixtureReference(0x00),
        new PiiFields { EmployeeName = "Jane Doe" }),
    new(
        "representative-no-address",
        "Representative P2 payload with the optional address absent",
        FixtureReference(0x11),
        CopyFields(sharedFields)),
    new(
        "au-address-median",
        "AU representative P2 payload, address around the median length",
        FixtureReference(0x12),
        new PiiFields
        {
            EmployeeName = "Mia Thompson",
            Position = "Accounts Assistant",
            Department = "Payroll",
            EmployerAbn = "53004085616",
            Bsb = "062-000",
            AccountNumber = "12345678",
            AccountName = "Mia Thompson",
            Address = ExactAsciiAddress(36),
        }),
    new(
        "au-jobtitle-absent",
        "AU representative P2 payload with no job title and a typical address",
        FixtureReference(0x13),
        new PiiFields
        {
            EmployeeName = "Oliver Smith",
            Department = "Retail Operations",
            EmployerAbn = "53004085616",
            Bsb = "062-000",
            AccountNumber = "12345678",
            AccountName = "Oliver Smith",
            Address = ExactAsciiAddress(40),
        }),
    new(
        "au-address-p95",
        "AU representative P2 payload, address around the P95 length",
        FixtureReference(0x14),
        CopyFields(sharedFields, ExactAsciiAddress(48))),
    new(
        "au-address-p99",
        "AU representative P2 payload, address around the P99 length",
        FixtureReference(0x15),
        CopyFields(sharedFields, ExactAsciiAddress(58))),
    new(
        "international-address",
        "Realistic international P2 address",
        FixtureReference(0x22),
        CopyFields(sharedFields, "12 Rue de l’Église, Apt 4B, 75005 Paris, France 🇫🇷")),
    new(
        "dense-fields",
        "Dense P2 payload with long synthetic payroll fields",
        FixtureReference(0x33),
        new PiiFields
        {
            EmployeeName = "Alexandra Example-Synthetic",
            Position = "Principal International Payroll Systems Engineer",
            Department = "Global Payroll Operations and Compliance",
            EmployerAbn = "53004085616",
            Bsb = "062-000",
            AccountNumber = "12345678901234567890",
            AccountName = "Alexandra Example-Synthetic",
        }),
    new(
        "address-320-bytes",
        "Exact 320-byte UTF-8 P2 address boundary",
        FixtureReference(0x44),
        CopyFields(sharedFields, string.Concat(Enumerable.Repeat("東京", 53)) + "AB")),
    .. AddressExperimentFixtures(sharedFields, AddressExperimentNonceSlotStart),
];
ScannerFixture[] fixtures = stressMode ? StressFixtures() : standardFixtures;

if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
{
    throw new InvalidOperationException(
        $"Output path already exists: {outputDirectory}. Remove it first.");
}

string? outputParent = Path.GetDirectoryName(outputDirectory);
string outputName = Path.GetFileName(outputDirectory);
if (string.IsNullOrEmpty(outputParent) || string.IsNullOrEmpty(outputName))
{
    throw new InvalidOperationException($"Output path must name a directory: {outputDirectory}.");
}

Directory.CreateDirectory(outputParent);
string stagingDirectory = Path.Join(outputParent, $".{outputName}.tmp-{Guid.NewGuid():N}");
Directory.CreateDirectory(stagingDirectory);
bool published = false;
try
{
    var manifestFixtures = new List<object>();
    for (int index = 0; index < fixtures.Length; index++)
    {
        ScannerFixture fixture = fixtures[index];
        string plaintext;
        try
        {
            plaintext = Pii.Format(fixture.Fields);
        }
        catch (Exception exception) when (fixture.ExpectedFormatFailure && exception is ArgumentException)
        {
            manifestFixtures.Add(new
            {
                fixture.Id,
                fixture.Description,
                Status = "expected-reject",
                AddressUtf8Bytes = Encoding.UTF8.GetByteCount(fixture.Fields.Address ?? string.Empty),
                FailureStage = "Pii.Format",
                FailureMessage = exception.Message,
            });
            continue;
        }

        byte[] ciphertextBytes = EncryptDeterministically(plaintext, fixture.EncryptionNonceSlot ?? index, key);
        string encryptedPii = Base64Url(ciphertextBytes);
        var parts = new BarcodeParts(fixture.Reference, encryptedPii);
        BarcodePngResult barcode = VerifiablBarcode.CreatePng(
            parts,
            new BarcodeSvgOptions
            {
                Environment = VerifiablEnvironment.Sandbox,
                MaxErrorCorrection = fixture.MaxErrorCorrection,
            },
            720);
        string pngFile = fixture.Id + ".png";
        File.WriteAllBytes(Path.Join(stagingDirectory, pngFile), barcode.Png);

        manifestFixtures.Add(new
        {
            fixture.Id,
            fixture.Description,
            AddressUtf8Bytes = Encoding.UTF8.GetByteCount(fixture.Fields.Address ?? string.Empty),
            PlaintextUtf8Bytes = Encoding.UTF8.GetByteCount(plaintext),
            VerifiablReference = fixture.Reference,
            Status = "rendered",
            MaxErrorCorrection = ToNodeLevel(fixture.MaxErrorCorrection),
            Ciphertext = new
            {
                ByteLength = ciphertextBytes.Length,
                Base64url = encryptedPii,
            },
            Qr = new
            {
                File = pngFile,
                Content = barcode.Content,
                Version = barcode.QrVersion,
                ErrorCorrectionLevel = ToNodeLevel(barcode.ErrorCorrectionLevel),
                barcode.Width,
                barcode.Height,
                ModuleCount = barcode.QrVersion * 4 + 17,
                barcode.ModulePx,
                barcode.Degraded,
                ContentUtf8Bytes = Encoding.UTF8.GetByteCount(barcode.Content),
                PhysicalModuleMm = new Dictionary<string, double>
                {
                    ["19"] = PhysicalModuleMm(barcode.ModulePx, barcode.Width, 19),
                    ["22"] = PhysicalModuleMm(barcode.ModulePx, barcode.Width, 22),
                    ["25"] = PhysicalModuleMm(barcode.ModulePx, barcode.Width, 25),
                    ["28"] = PhysicalModuleMm(barcode.ModulePx, barcode.Width, 28),
                },
                Segments = new[] { "byte", "alphanumeric" },
            },
            XmpPayload = VerifiablBarcode.BuildPayload(parts),
        });
    }

    var manifest = new
    {
        Format = stressMode ? "verifiabl-qr-stress-v1" : "verifiabl-scanner-pack-v1",
        SyntheticDataOnly = true,
        Environment = "sandbox",
        Fixtures = manifestFixtures,
    };
    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    File.WriteAllText(
        Path.Join(stagingDirectory, "manifest.json"),
        JsonSerializer.Serialize(manifest, jsonOptions) + Environment.NewLine,
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    File.WriteAllText(
        Path.Join(stagingDirectory, "results.csv"),
        RenderResultsCsv(manifestFixtures),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    File.WriteAllText(
        Path.Join(stagingDirectory, "summary.md"),
        RenderSummary(stressMode, manifestFixtures),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    string cards = string.Join(
        Environment.NewLine,
        manifestFixtures.Select((value, index) => (Fixture: fixtures[index], Manifest: value))
            .Where(item => item.Fixture.IncludeInIndex)
            .Select(item => RenderCard(item.Fixture, item.Manifest)));
    string html = $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Verifiabl VER-460 scanner pack</title>
  <style>
    body { font: 14px/1.4 system-ui, sans-serif; margin: 24px; color: #111; }
    .notice { padding: 12px; border: 2px solid #010a4f; margin-bottom: 24px; }
    .fixture { break-after: page; page-break-after: always; max-width: 760px; }
    img { display: block; width: 45mm; height: auto; margin: 16px 0; image-rendering: pixelated; }
    dt { font-weight: 700; float: left; clear: left; width: 110px; }
    dd { margin-left: 120px; margin-bottom: 8px; overflow-wrap: anywhere; }
    code { font: 11px/1.3 ui-monospace, monospace; }
    .fold-guide { clear: both; margin-top: 30mm; border-top: 1px dashed #555; padding-top: 4px; }
    @media print { body { margin: 12mm; } .fixture { max-width: none; } }
  </style>
</head>
<body>
  <div class="notice"><strong>Synthetic test data only.</strong> Compare scanner output with manifest.json. Do not use customer payslips.</div>
  {{cards}}
</body>
</html>
""";
    File.WriteAllText(
        Path.Join(stagingDirectory, "index.html"),
        html,
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    File.WriteAllText(
        Path.Join(stagingDirectory, "address-size-matrix.html"),
        RenderAddressSizeMatrix(fixtures, manifestFixtures),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Directory.Move(stagingDirectory, outputDirectory);
    published = true;
    Console.WriteLine($"Wrote {manifestFixtures.Count} scanner fixtures to {outputDirectory}");
}
finally
{
    if (!published && Directory.Exists(stagingDirectory))
    {
        Directory.Delete(stagingDirectory, recursive: true);
    }
}

static ScannerFixture[] AddressExperimentFixtures(PiiFields sharedFields, int encryptionNonceSlotStart)
{
    var fixtures = new List<ScannerFixture>();
    byte referenceByte = 0x60;
    int encryptionNonceSlot = encryptionNonceSlotStart;
    foreach (int addressBytes in new[] { 160, 200, 240, 320 })
    {
        string reference = FixtureReference(referenceByte++);
        int pairNonceSlot = encryptionNonceSlot++;
        string address = FullAddressEdge(addressBytes);
        foreach (BarcodeErrorCorrectionLevel level in new[]
        {
            BarcodeErrorCorrectionLevel.Medium,
            BarcodeErrorCorrectionLevel.Low,
        })
        {
            string suffix = level == BarcodeErrorCorrectionLevel.Medium ? "medium" : "low";
            string label = level == BarcodeErrorCorrectionLevel.Medium ? "ECC M" : "ECC L";
            fixtures.Add(new ScannerFixture(
                $"address-{addressBytes}-{suffix}",
                $"{addressBytes}-byte UTF-8 P2 address edge case, {label}",
                reference,
                CopyFields(sharedFields, address),
                level,
                IncludeInIndex: false,
                EncryptionNonceSlot: pairNonceSlot));
        }
    }

    return fixtures.ToArray();
}

static ScannerFixture[] StressFixtures()
{
    var fixtures = new List<ScannerFixture>();
    int nonce = 0;
    // Low-end cuts mirror the AU employee address distribution used for VER-523:
    // median ≈36 chars, P95 ≈48, P99 ≈58, P99.9 ≈80. Higher cuts are stress/capacity rows.
    foreach (int addressBytes in new[] { 0, 32, 36, 40, 48, 58, 80, 120, 160, 200, 240, 280, 320, 321 })
    {
        string[] scripts = addressBytes == 0 ? ["none"] : ["ascii", "latin", "cjk", "mixed"];
        foreach (string script in scripts)
        {
            foreach ((string density, PiiFields fields) in StressDensityProfiles(addressBytes, script))
            {
                if (addressBytes > Pii.AddressMaxBytes)
                {
                    fixtures.Add(new ScannerFixture(
                        $"p2-{density}-{script}-addr-{addressBytes}-expected-reject",
                        $"Expected address rejection before encryption/render at {addressBytes} UTF-8 bytes",
                        FixtureReferenceFromId($"p2-{density}-{script}-addr-{addressBytes}-expected-reject"),
                        fields,
                        IncludeInIndex: false,
                        ExpectedFormatFailure: true));
                    nonce++;
                    continue;
                }

                foreach (BarcodeErrorCorrectionLevel level in new[]
                {
                    BarcodeErrorCorrectionLevel.Medium,
                    BarcodeErrorCorrectionLevel.Low,
                })
                {
                    string suffix = level == BarcodeErrorCorrectionLevel.Medium ? "medium" : "low";
                    string id = $"p2-{density}-{script}-addr-{addressBytes}-{suffix}";
                    fixtures.Add(new ScannerFixture(
                        id,
                        $"P2 {density} fixture, {script} address, {addressBytes} UTF-8 address bytes, ECC {ToNodeLevel(level)}",
                        FixtureReferenceFromId(id),
                        fields,
                        level,
                        IncludeInIndex: false,
                        EncryptionNonceSlot: nonce));
                    nonce++;
                }
            }
        }
    }

    return fixtures.ToArray();
}

static IEnumerable<(string Density, PiiFields Fields)> StressDensityProfiles(int addressBytes, string script)
{
    string? address = addressBytes == 0 ? null : ExactUtf8String(addressBytes, script);
    yield return ("minimal", new PiiFields { EmployeeName = "Ava Example", Address = address });
    yield return ("au-typical", new PiiFields
    {
        EmployeeName = "Mia Thompson",
        Position = "Accounts Assistant",
        Department = "Payroll",
        EmployerAbn = "53-004-085-616",
        Bsb = "062-000",
        AccountNumber = "12345678",
        AccountName = "Mia Thompson",
        Address = address,
    });
    yield return ("jobtitle-absent", new PiiFields
    {
        EmployeeName = "Oliver Smith",
        Department = "Retail Operations",
        EmployerAbn = "53-004-085-616",
        Bsb = "062-000",
        AccountNumber = "12345678",
        AccountName = "Oliver Smith",
        Address = address,
    });
    yield return ("representative", new PiiFields
    {
        EmployeeName = "Zoë Nguyễn",
        Position = "Senior Registered Nurse",
        Department = "Emergency Department",
        EmployerAbn = "53-004-085-616",
        Bsb = "062-000",
        AccountNumber = "12345678",
        AccountName = "Zoe Nguyen",
        Address = address,
    });
    yield return ("long-fields", new PiiFields
    {
        EmployeeName = "Dr Alexandra Catherine Example-Synthetic",
        Position = "Principal International Payroll Systems Engineer",
        Department = "Global Payroll Operations and Compliance",
        EmployerAbn = "53-004-085-616",
        Bsb = "062-000",
        AccountNumber = "12345678901234567890",
        AccountName = "Alexandra Catherine Example Synthetic",
        Address = address,
    });
    yield return ("dense-fields", new PiiFields
    {
        EmployeeName = RepeatToLength("Alexandra Example ", 128),
        Position = RepeatToLength("Principal Payroll Systems Engineer ", 180),
        Department = RepeatToLength("International Payroll Compliance Operations ", 180),
        EmployerAbn = "53-004-085-616",
        Bsb = "062-000",
        AccountNumber = RepeatToLength("1234567890", 80),
        AccountName = RepeatToLength("Alexandra Catherine Example Synthetic ", 180),
        Address = address,
    });
}

static string ExactUtf8String(int bytes, string script)
{
    string[] chunks = script switch
    {
        "ascii" => ["A"],
        "latin" => ["é", "ø", "A"],
        "cjk" => ["東", "京", "A"],
        "mixed" => ["é", "東", "A", "ø", "京", "Ж", "한"],
        _ => throw new ArgumentOutOfRangeException(nameof(script), script, "Unknown address script."),
    };
    int remaining = bytes;
    var builder = new StringBuilder();
    while (remaining > 0)
    {
        string next = chunks.First(chunk => Encoding.UTF8.GetByteCount(chunk) <= remaining);
        builder.Append(next);
        remaining -= Encoding.UTF8.GetByteCount(next);
    }

    string value = builder.ToString();
    if (Encoding.UTF8.GetByteCount(value) != bytes)
    {
        throw new InvalidOperationException("Address fixture byte count mismatch.");
    }

    return value;
}

static string RepeatToLength(string seed, int length)
{
    var builder = new StringBuilder();
    while (builder.Length < length)
    {
        builder.Append(seed);
    }

    return builder.ToString()[..length];
}

static string ExactAsciiAddress(int chars)
{
    const string seed = "12 Representative Street Sydney NSW 2000 ";
    string value = RepeatToLength(seed, chars);
    if (value.Length != chars || Encoding.UTF8.GetByteCount(value) != chars)
    {
        throw new InvalidOperationException("ASCII address fixture length mismatch.");
    }

    return value;
}

static string FullAddressEdge(int utf8Bytes)
{
    if (utf8Bytes <= 0)
    {
        throw new ArgumentOutOfRangeException(nameof(utf8Bytes));
    }

    int cjkPairs = utf8Bytes / 6;
    int asciiRemainder = utf8Bytes - cjkPairs * 6;
    string address = string.Concat(Enumerable.Repeat("東京", cjkPairs))
        + new string('A', asciiRemainder);
    if (Encoding.UTF8.GetByteCount(address) != utf8Bytes)
    {
        throw new InvalidOperationException("Address fixture byte count mismatch.");
    }

    return address;
}

static PiiFields CopyFields(PiiFields source, string? address = null) => new()
{
    EmployeeName = source.EmployeeName,
    Position = source.Position,
    Department = source.Department,
    EmployerAbn = source.EmployerAbn,
    Bsb = source.Bsb,
    AccountNumber = source.AccountNumber,
    AccountName = source.AccountName,
    Address = address,
};

static byte[] EncryptDeterministically(string plaintext, int index, byte[] key)
{
    // Fixed synthetic key and distinct fixed IVs make the pack reproducible across SDKs.
    // This helper is test-only. Production encryption must use a fresh random IV.
    byte[] iv = new byte[12];
    iv[11] = (byte)(index + 1);
    byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
    byte[] ciphertext = new byte[plaintextBytes.Length];
    byte[] tag = new byte[16];
    using var aes = new AesGcm(key, tag.Length);
    aes.Encrypt(iv, plaintextBytes, ciphertext, tag);
    return ciphertext;
}

static string FixtureReference(byte value) => Base64Url(Enumerable.Repeat(value, 16).ToArray());

static string FixtureReferenceFromId(string value) => Base64Url(
    SHA256.HashData(Encoding.UTF8.GetBytes("reference:" + value))[..16]);

static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
    .TrimEnd('=')
    .Replace('+', '-')
    .Replace('/', '_');

static string ToNodeLevel(BarcodeErrorCorrectionLevel level) => level switch
{
    BarcodeErrorCorrectionLevel.Quartile => "Q",
    BarcodeErrorCorrectionLevel.Medium => "M",
    _ => "L",
};

static double PhysicalModuleMm(double modulePx, int pixelWidth, int badgeMm) =>
    Math.Round(badgeMm * (modulePx / pixelWidth), 4);

static string RenderResultsCsv(List<object> manifestValues)
{
    var rows = new List<string>
    {
        "id,status,addressUtf8Bytes,plaintextUtf8Bytes,ciphertextBytes,contentUtf8Bytes,maxErrorCorrection,errorCorrectionLevel,qrVersion,moduleCount,modulePx,degraded,scanUrl",
    };
    foreach (object manifestValue in manifestValues)
    {
        JsonElement value = JsonSerializer.SerializeToElement(
            manifestValue,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        string id = Csv(value.GetProperty("id").GetString()!);
        string status = Csv(value.TryGetProperty("status", out JsonElement statusValue)
            ? statusValue.GetString()!
            : "rendered");
        string addressBytes = value.GetProperty("addressUtf8Bytes").GetRawText();
        if (!value.TryGetProperty("qr", out JsonElement qr))
        {
            rows.Add(string.Join(',', [id, status, addressBytes, "", "", "", "", "", "", "", "", "", ""]));
            continue;
        }

        JsonElement ciphertext = value.GetProperty("ciphertext");
        rows.Add(string.Join(',',
        [
            id,
            status,
            addressBytes,
            value.GetProperty("plaintextUtf8Bytes").GetRawText(),
            ciphertext.GetProperty("byteLength").GetRawText(),
            qr.GetProperty("contentUtf8Bytes").GetRawText(),
            Csv(value.GetProperty("maxErrorCorrection").GetString()!),
            Csv(qr.GetProperty("errorCorrectionLevel").GetString()!),
            qr.GetProperty("version").GetRawText(),
            qr.GetProperty("moduleCount").GetRawText(),
            qr.GetProperty("modulePx").GetRawText(),
            qr.GetProperty("degraded").GetRawText(),
            Csv(qr.GetProperty("content").GetString()!),
        ]));
    }

    return string.Join(Environment.NewLine, rows) + Environment.NewLine;
}

static string RenderSummary(bool stressMode, List<object> manifestValues)
{
    var rendered = manifestValues.Select(value => JsonSerializer.SerializeToElement(
            value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }))
        .Where(value => value.TryGetProperty("qr", out _))
        .ToArray();
    int expectedRejects = manifestValues.Count - rendered.Length;
    string rows = string.Join(Environment.NewLine, rendered
        .OrderBy(value => value.GetProperty("addressUtf8Bytes").GetInt32())
        .ThenBy(value => value.GetProperty("id").GetString(), StringComparer.Ordinal)
        .Take(60)
        .Select(value =>
        {
            JsonElement qr = value.GetProperty("qr");
            return $"| {value.GetProperty("addressUtf8Bytes").GetRawText()} | {H(value.GetProperty("id").GetString()!)} | {H(value.GetProperty("maxErrorCorrection").GetString()!)} | {H(qr.GetProperty("errorCorrectionLevel").GetString()!)} | {qr.GetProperty("version").GetRawText()} | {qr.GetProperty("moduleCount").GetRawText()} | {qr.GetProperty("contentUtf8Bytes").GetRawText()} | {(qr.GetProperty("degraded").GetBoolean() ? "yes" : "no")} |";
        }));

    return $$"""
# Verifiabl QR scanner pack summary

Synthetic data only. Generated by the .NET SDK ScannerPack{{(stressMode ? " stress mode" : string.Empty)}}. The manifest includes ciphertext-bearing scan URLs for representative/manual comparison; do not use customer data in this corpus. No ciphertext hashes or other ciphertext derivatives are written.

## Run metadata

- Mode: {{(stressMode ? "stress" : "standard")}}
- Environment: sandbox
- Fixtures: {{manifestValues.Count}}
- Rendered: {{rendered.Length}}
- Expected format rejects: {{expectedRejects}}

## QR density sample

| Address bytes | Fixture | ECC ceiling | ECC used | QR version | Modules | URL bytes | Degraded |
| ---: | --- | --- | --- | ---: | ---: | ---: | --- |
{{rows}}

## VER-373 notes

The .NET stress mode includes explicit Medium and Low error-correction rows, which complements the Node stress harness where Low is reached only by the degradation ladder. Address lengths 36/48/58/80 approximate the AU median/P95/P99/P99.9 bands; 120+ byte rows are stress cases, not representative records. Use the generated `address-size-matrix.html`, `manifest.json`, and `results.csv` to decide the address cap, badge-size floor, and whether Low/degraded output is acceptable.
""";
}

static string Csv(string value) =>
    value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
        ? $"\"{value.Replace("\"", "\"\"")}\""
        : value;

static string RenderCard(ScannerFixture fixture, object manifestValue)
{
    JsonElement value = JsonSerializer.SerializeToElement(
        manifestValue,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    JsonElement qr = value.GetProperty("qr");
    return $$"""
      <article class="fixture">
        <h2>{{H(fixture.Id)}}</h2>
        <p>{{H(fixture.Description)}}</p>
        <img src="{{H(qr.GetProperty("file").GetString()!)}}" alt="{{H(fixture.Id)}} QR fixture">
        <dl>
          <dt>QR</dt><dd>Version {{qr.GetProperty("version")}}, ECC {{H(qr.GetProperty("errorCorrectionLevel").GetString()!)}}</dd>
          <dt>Address</dt><dd>{{value.GetProperty("addressUtf8Bytes")}} UTF-8 bytes</dd>
          <dt>Reference</dt><dd><code>{{H(fixture.Reference)}}</code></dd>
          <dt>Expected scan</dt><dd><code>{{H(qr.GetProperty("content").GetString()!)}}</code></dd>
        </dl>
        <div class="fold-guide">Fold guide: fold on this line, away from the QR, for the fold test.</div>
      </article>
""";
}

static string RenderAddressSizeMatrix(ScannerFixture[] fixtures, List<object> manifestValues)
{
    string rows = string.Join(
        Environment.NewLine,
        fixtures.Select((fixture, index) => (Fixture: fixture, Index: index))
            .Where(item => !item.Fixture.IncludeInIndex)
            .Select(item =>
            {
                ScannerFixture fixture = item.Fixture;
                JsonElement value = JsonSerializer.SerializeToElement(
                    manifestValues[item.Index],
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                if (!value.TryGetProperty("qr", out JsonElement qr))
                {
                    return string.Empty;
                }
                string file = H(qr.GetProperty("file").GetString()!);
                string version = qr.GetProperty("version").GetRawText();
                string ecc = H(qr.GetProperty("errorCorrectionLevel").GetString()!);
                string addressBytes = value.GetProperty("addressUtf8Bytes").GetRawText();
                string plaintextBytes = value.GetProperty("plaintextUtf8Bytes").GetRawText();
                string title = H(fixture.Id);
                return $$"""
                  <tr>
                    <th scope="row">
                      <strong>{{title}}</strong><br>
                      {{addressBytes}} address bytes<br>
                      {{plaintextBytes}} plaintext bytes<br>
                      QR v{{version}}, ECC {{ecc}}
                    </th>
                    <td><img class="qr size-19" src="{{file}}" alt="{{title}} at 19mm badge width"></td>
                    <td><img class="qr size-22" src="{{file}}" alt="{{title}} at 22mm badge width"></td>
                    <td><img class="qr size-25" src="{{file}}" alt="{{title}} at 25mm badge width"></td>
                    <td><img class="qr size-28" src="{{file}}" alt="{{title}} at 28mm badge width"></td>
                  </tr>
""";
            }));

    return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Verifiabl address QR ECC and badge-size matrix</title>
  <style>
    body { font: 14px/1.4 system-ui, sans-serif; margin: 16px; color: #111; }
    .notice { padding: 10px; border: 2px solid #010a4f; margin-bottom: 14px; }
    table { border-collapse: collapse; width: 100%; }
    th, td { border: 1px solid #bbb; padding: 6px; text-align: left; vertical-align: top; }
    thead th { background: #f2f3f7; }
    tbody th { width: 54mm; }
    .qr { display: block; image-rendering: pixelated; }
    .size-19 { width: 19mm; height: auto; }
    .size-22 { width: 22mm; height: auto; }
    .size-25 { width: 25mm; height: auto; }
    .size-28 { width: 28mm; height: auto; }
    @media print {
      body { margin: 6mm; font-size: 10px; }
      th, td { padding: 3px; }
    }
  </style>
</head>
<body>
  <div class="notice"><strong>Address QR comparison page.</strong> Full-address byte caps crossed with ECC Medium/Low and 19/22/25/28mm badge widths (the QR box is 80/96 of each badge). Synthetic test data only.</div>
  <table>
    <thead>
      <tr>
        <th scope="col">Address + ECC case</th>
        <th scope="col">19mm badge</th>
        <th scope="col">22mm badge</th>
        <th scope="col">25mm badge</th>
        <th scope="col">28mm badge</th>
      </tr>
    </thead>
    <tbody>
{{rows}}
    </tbody>
  </table>
</body>
</html>
""";
}

static string H(string value) => WebUtility.HtmlEncode(value);

internal sealed record ScannerFixture(
    string Id,
    string Description,
    string Reference,
    PiiFields Fields,
    BarcodeErrorCorrectionLevel MaxErrorCorrection = BarcodeErrorCorrectionLevel.Medium,
    bool IncludeInIndex = true,
    int? EncryptionNonceSlot = null,
    bool ExpectedFormatFailure = false);
