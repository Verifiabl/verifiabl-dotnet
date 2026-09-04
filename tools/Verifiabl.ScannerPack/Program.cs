using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Verifiabl;

(string? requestedOutput, bool stressMode) = ParseArgs(args);
string outputDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
    requestedOutput ?? Path.Join("artifacts", stressMode ? "qr-stress" : "scanner-pack")));
byte[] key = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
// Address ECC pairs intentionally reuse the same synthetic payload so scan differences
// are attributable to ECC level rather than reference/IV/ciphertext changes.
const int AddressExperimentNonceSlotStart = 5;
string[] finderStyles = ["rounded", "square"];
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
        new PiiFields { EmployeeName = "Jane Doe" },
        EncryptionNonceSlot: 0),
    new(
        "representative-no-address",
        "Representative P2 payload with the optional address absent",
        FixtureReference(0x11),
        CopyFields(sharedFields),
        EncryptionNonceSlot: 1),
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
        },
        EncryptionNonceSlot: 20),
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
        },
        EncryptionNonceSlot: 21),
    new(
        "au-address-p95",
        "AU representative P2 payload, address around the P95 length",
        FixtureReference(0x14),
        CopyFields(sharedFields, ExactAsciiAddress(48)),
        EncryptionNonceSlot: 22),
    new(
        "au-address-p99",
        "AU representative P2 payload, address around the P99 length",
        FixtureReference(0x15),
        CopyFields(sharedFields, ExactAsciiAddress(58)),
        EncryptionNonceSlot: 23),
    new(
        "international-address",
        "Realistic international P2 address",
        FixtureReference(0x22),
        CopyFields(sharedFields, "12 Rue de l’Église, Apt 4B, 75005 Paris, France 🇫🇷"),
        EncryptionNonceSlot: 2),
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
        },
        EncryptionNonceSlot: 3),
    new(
        "address-320-bytes",
        "Exact 320-byte UTF-8 P2 address boundary",
        FixtureReference(0x44),
        CopyFields(sharedFields, string.Concat(Enumerable.Repeat("東京", 53)) + "AB"),
        EncryptionNonceSlot: 4),
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
        foreach (string finderStyle in finderStyles)
        {
            BarcodeSvgOptions barcodeOptions = new()
            {
                Environment = VerifiablEnvironment.Sandbox,
                MaxErrorCorrection = fixture.MaxErrorCorrection,
            };
            string id = finderStyle == "rounded" ? fixture.Id : fixture.Id + "-square-finders";
            string file;
            string content;
            int qrVersion;
            BarcodeErrorCorrectionLevel errorCorrectionLevel;
            double width;
            double height;
            double modulePx;
            bool degraded;
            if (finderStyle == "rounded")
            {
                BarcodePngResult barcode = VerifiablBarcode.CreatePng(parts, barcodeOptions, 720);
                file = fixture.Id + ".png";
                File.WriteAllBytes(Path.Join(stagingDirectory, file), barcode.Png);
                content = barcode.Content;
                qrVersion = barcode.QrVersion;
                errorCorrectionLevel = barcode.ErrorCorrectionLevel;
                width = barcode.Width;
                height = barcode.Height;
                modulePx = barcode.ModulePx;
                degraded = barcode.Degraded;
            }
            else
            {
                BarcodeSvgResult barcode = VerifiablBarcode.CreateSvg(parts, barcodeOptions);
                file = id + ".svg";
                File.WriteAllText(
                    Path.Join(stagingDirectory, file),
                    AddSquareFinderOverlay(barcode.Svg, barcode),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                content = barcode.Content;
                qrVersion = barcode.QrVersion;
                errorCorrectionLevel = barcode.ErrorCorrectionLevel;
                width = barcode.Width;
                height = barcode.Height;
                modulePx = barcode.ModulePx;
                degraded = barcode.Degraded;
            }

            manifestFixtures.Add(new
            {
                Id = id,
                fixture.Description,
                FinderStyle = finderStyle,
                fixture.IncludeInIndex,
                AddressUtf8Bytes = Encoding.UTF8.GetByteCount(fixture.Fields.Address ?? string.Empty),
                PlaintextUtf8Bytes = Encoding.UTF8.GetByteCount(plaintext),
                VerifiablReference = fixture.Reference,
                Status = "rendered",
                MaxErrorCorrection = ToNodeLevel(fixture.MaxErrorCorrection),
                Ciphertext = new
                {
                    ByteLength = ciphertextBytes.Length,
                    Base64url = encryptedPii,
                    Hex = Convert.ToHexString(ciphertextBytes).ToLowerInvariant(),
                },
                Qr = new
                {
                    File = file,
                    Content = content,
                    Version = qrVersion,
                    ErrorCorrectionLevel = ToNodeLevel(errorCorrectionLevel),
                    Width = width,
                    Height = height,
                    ModuleCount = qrVersion * 4 + 17,
                    ModulePx = modulePx,
                    Degraded = degraded,
                    ContentUtf8Bytes = Encoding.UTF8.GetByteCount(content),
                    PhysicalModuleMm = new Dictionary<string, double>
                    {
                        ["19"] = PhysicalModuleMm(modulePx, width, 19),
                        ["22"] = PhysicalModuleMm(modulePx, width, 22),
                        ["25"] = PhysicalModuleMm(modulePx, width, 25),
                        ["28"] = PhysicalModuleMm(modulePx, width, 28),
                    },
                    Segments = new[] { "byte", "alphanumeric" },
                },
                XmpPayload = VerifiablBarcode.BuildPayload(parts),
            });
        }
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
        manifestFixtures
            .Where(ShouldIncludeInIndex)
            .Select(RenderCard));
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
  <div class="notice"><strong>Synthetic test data only.</strong> Compare scanner output with manifest.json. Do not use customer payslips. For a realistic single-code page, open <a href="sample-payslip.html">sample-payslip.html</a>.</div>
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
    File.WriteAllText(
        Path.Join(stagingDirectory, "sample-payslip.html"),
        RenderSyntheticPayslip(manifestFixtures),
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

static (string? OutputDirectory, bool StressMode) ParseArgs(string[] args)
{
    string? outputDirectory = null;
    bool stressMode = false;
    foreach (string arg in args)
    {
        if (arg == "--stress")
        {
            stressMode = true;
        }
        else if (arg.StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unknown option: {arg}");
        }
        else if (outputDirectory is null)
        {
            outputDirectory = arg;
        }
        else
        {
            throw new ArgumentException($"Unexpected extra output path: {arg}");
        }
    }

    return (outputDirectory, stressMode);
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
                string baseId = $"p2-{density}-{script}-addr-{addressBytes}";
                if (addressBytes > Pii.AddressMaxBytes)
                {
                    fixtures.Add(new ScannerFixture(
                        $"{baseId}-expected-reject",
                        $"Expected address rejection before encryption/render at {addressBytes} UTF-8 bytes",
                        FixtureReferenceFromId($"{baseId}-expected-reject"),
                        fields,
                        IncludeInIndex: false,
                        ExpectedFormatFailure: true));
                    continue;
                }

                string reference = FixtureReferenceFromId(baseId);
                int pairNonceSlot = nonce++;
                foreach (BarcodeErrorCorrectionLevel level in new[]
                {
                    BarcodeErrorCorrectionLevel.Medium,
                    BarcodeErrorCorrectionLevel.Low,
                })
                {
                    string suffix = level == BarcodeErrorCorrectionLevel.Medium ? "medium" : "low";
                    string id = $"{baseId}-{suffix}";
                    fixtures.Add(new ScannerFixture(
                        id,
                        $"P2 {density} fixture, {script} address, {addressBytes} UTF-8 address bytes, ECC {ToNodeLevel(level)}",
                        reference,
                        fields,
                        level,
                        IncludeInIndex: false,
                        EncryptionNonceSlot: pairNonceSlot));
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

static string AddSquareFinderOverlay(string svg, BarcodeSvgResult result)
{
    double scale = result.Width / 96.0;
    double moduleSize = result.ModulePx / scale;
    int size = result.QrVersion * 4 + 17;
    double lastFinderOrigin = (size - 7) * moduleSize;
    string overlay = SquareFinder(0, 0, moduleSize)
        + SquareFinder(lastFinderOrigin, 0, moduleSize)
        + SquareFinder(0, lastFinderOrigin, moduleSize);
    return svg.Replace("</g></svg>", $"<g shape-rendering=\"crispEdges\">{overlay}</g></g></svg>", StringComparison.Ordinal);
}

static string SquareFinder(double originX, double originY, double moduleSize)
{
    double outer = 7 * moduleSize;
    double innerOffset = moduleSize;
    double inner = 5 * moduleSize;
    double dotOffset = 2 * moduleSize;
    double dot = 3 * moduleSize;
    return $"<rect x=\"{F2(originX)}\" y=\"{F2(originY)}\" width=\"{F2(outer)}\" height=\"{F2(outer)}\" fill=\"#000000\"/>"
        + $"<rect x=\"{F2(originX + innerOffset)}\" y=\"{F2(originY + innerOffset)}\" width=\"{F2(inner)}\" height=\"{F2(inner)}\" fill=\"#FFFFFF\"/>"
        + $"<rect x=\"{F2(originX + dotOffset)}\" y=\"{F2(originY + dotOffset)}\" width=\"{F2(dot)}\" height=\"{F2(dot)}\" fill=\"#000000\"/>";
}

static string F2(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

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
    int chunkIndex = 0;
    var builder = new StringBuilder();
    while (remaining > 0)
    {
        string candidate = chunks[chunkIndex % chunks.Length];
        chunkIndex++;
        int candidateBytes = Encoding.UTF8.GetByteCount(candidate);
        string next = candidateBytes <= remaining ? candidate : "A";
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
    if (index < 0)
    {
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    byte[] iv = new byte[12];
    long slot = (long)index + 1;
    for (int offset = iv.Length - 1; slot > 0 && offset >= 0; offset--)
    {
        iv[offset] = (byte)(slot & 0xff);
        slot >>= 8;
    }
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

static double PhysicalModuleMm(double modulePx, double pixelWidth, int badgeMm) =>
    Math.Round(badgeMm * (modulePx / pixelWidth), 4);

static string RenderResultsCsv(List<object> manifestValues)
{
    var rows = new List<string>
    {
        "id,status,finderStyle,addressUtf8Bytes,plaintextUtf8Bytes,ciphertextBytes,contentUtf8Bytes,maxErrorCorrection,errorCorrectionLevel,qrVersion,moduleCount,modulePx,degraded,scanUrl",
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
            rows.Add(string.Join(',', [id, status, "", addressBytes, "", "", "", "", "", "", "", "", "", ""]));
            continue;
        }

        JsonElement ciphertext = value.GetProperty("ciphertext");
        rows.Add(string.Join(',',
        [
            id,
            status,
            Csv(value.GetProperty("finderStyle").GetString()!),
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
    IEnumerable<JsonElement> summaryRows = rendered
        .OrderBy(value => value.GetProperty("addressUtf8Bytes").GetInt32())
        .ThenBy(value => value.GetProperty("id").GetString(), StringComparer.Ordinal);
    if (!stressMode)
    {
        summaryRows = summaryRows.Take(60);
    }

    string rows = string.Join(Environment.NewLine, summaryRows.Select(value =>
        {
            JsonElement qr = value.GetProperty("qr");
            return $"| {value.GetProperty("addressUtf8Bytes").GetRawText()} | {H(value.GetProperty("id").GetString()!)} | {H(value.GetProperty("finderStyle").GetString()!)} | {H(value.GetProperty("maxErrorCorrection").GetString()!)} | {H(qr.GetProperty("errorCorrectionLevel").GetString()!)} | {qr.GetProperty("version").GetRawText()} | {qr.GetProperty("moduleCount").GetRawText()} | {qr.GetProperty("contentUtf8Bytes").GetRawText()} | {(qr.GetProperty("degraded").GetBoolean() ? "yes" : "no")} |";
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

| Address bytes | Fixture | Finder style | ECC ceiling | ECC used | QR version | Modules | URL bytes | Degraded |
| ---: | --- | --- | --- | --- | ---: | ---: | ---: | --- |
{{rows}}

## VER-373 notes

The .NET stress mode includes explicit Medium and Low error-correction rows, which complements the Node stress harness where Low is reached only by the degradation ladder. Address lengths 36/48/58/80 approximate the AU median/P95/P99/P99.9 bands; 120+ byte rows are stress cases, not representative records. Rounded and square finder variants share the same encoded payloads, so scanner differences isolate the visual finder treatment. Use the generated `address-size-matrix.html`, `sample-payslip.html`, `manifest.json`, and `results.csv` to decide the address cap, badge-size floor, finder style, and whether Low/degraded output is acceptable.
""";
}

static string Csv(string value) =>
    value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
        ? $"\"{value.Replace("\"", "\"\"")}\""
        : value;

static bool ShouldIncludeInIndex(object manifestValue)
{
    JsonElement value = JsonSerializer.SerializeToElement(
        manifestValue,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    return value.TryGetProperty("includeInIndex", out JsonElement includeInIndex)
        && includeInIndex.GetBoolean()
        && value.TryGetProperty("qr", out _);
}

static string RenderCard(object manifestValue)
{
    JsonElement value = JsonSerializer.SerializeToElement(
        manifestValue,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    JsonElement qr = value.GetProperty("qr");
    string id = value.GetProperty("id").GetString()!;
    return $$"""
      <article class="fixture">
        <h2>{{H(id)}}</h2>
        <p>{{H(value.GetProperty("description").GetString()!)}}</p>
        <img src="{{H(qr.GetProperty("file").GetString()!)}}" alt="{{H(id)}} QR fixture">
        <dl>
          <dt>QR</dt><dd>Version {{qr.GetProperty("version")}}, ECC {{H(qr.GetProperty("errorCorrectionLevel").GetString()!)}}</dd>
          <dt>Finder style</dt><dd>{{H(value.GetProperty("finderStyle").GetString()!)}}</dd>
          <dt>Address</dt><dd>{{value.GetProperty("addressUtf8Bytes")}} UTF-8 bytes</dd>
          <dt>Reference</dt><dd><code>{{H(value.GetProperty("verifiablReference").GetString()!)}}</code></dd>
          <dt>Expected scan</dt><dd><code>{{H(qr.GetProperty("content").GetString()!)}}</code></dd>
        </dl>
        <div class="fold-guide">Fold guide: fold on this line, away from the QR, for the fold test.</div>
      </article>
""";
}

static string RenderSyntheticPayslip(List<object> manifestValues)
{
    JsonElement? chosen = null;
    foreach (object manifestValue in manifestValues)
    {
        JsonElement value = JsonSerializer.SerializeToElement(
            manifestValue,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        if (!value.TryGetProperty("qr", out _))
        {
            continue;
        }

        chosen ??= value;
        if (value.GetProperty("id").GetString() == "au-address-median"
            && value.GetProperty("finderStyle").GetString() == "rounded")
        {
            chosen = value;
            break;
        }
    }

    if (chosen is null)
    {
        throw new InvalidOperationException("No rendered QR fixture available for sample payslip.");
    }

    JsonElement fixture = chosen.Value;
    JsonElement qr = fixture.GetProperty("qr");
    string fixtureId = fixture.GetProperty("id").GetString()!;
    string qrFile = qr.GetProperty("file").GetString()!;
    string scanUrl = qr.GetProperty("content").GetString()!;
    string reference = fixture.GetProperty("verifiablReference").GetString()!;

    return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Verifiabl synthetic payslip scanner fixture</title>
  <style>
    @page { size: A4; margin: 12mm; }
    * { box-sizing: border-box; }
    body { margin: 0; color: #111; font: 12px/1.35 Arial, Helvetica, sans-serif; background: #eee; }
    .page { width: 210mm; min-height: 297mm; margin: 0 auto; padding: 12mm; background: #fff; }
    .banner { margin-bottom: 8mm; padding: 3mm; border: 2px solid #010a4f; color: #010a4f; font-weight: 700; }
    header { display: flex; justify-content: space-between; gap: 12mm; border-bottom: 2px solid #222; padding-bottom: 5mm; }
    h1 { margin: 0 0 3mm; font-size: 24px; letter-spacing: .02em; }
    h2 { margin: 7mm 0 2mm; font-size: 15px; border-bottom: 1px solid #aaa; padding-bottom: 1mm; }
    table { width: 100%; border-collapse: collapse; margin-top: 2mm; }
    th, td { border: 1px solid #ccc; padding: 2mm; text-align: left; }
    th { background: #f2f3f7; }
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 7mm; }
    .qr-panel { margin-top: 7mm; display: grid; grid-template-columns: 1fr 28mm; gap: 8mm; align-items: start; padding: 4mm; border: 2px solid #010a4f; }
    .qr-panel img { display: block; width: 22mm; height: auto; image-rendering: pixelated; }
    .muted { color: #555; }
    code { font: 10px/1.25 ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; overflow-wrap: anywhere; }
    @media print { body { background: #fff; } .page { margin: 0; width: auto; min-height: auto; padding: 0; } }
  </style>
</head>
<body>
  <main class="page" data-qr-fixture="{{H(fixtureId)}}">
    <div class="banner">Synthetic scanner-pack payslip. Do not use customer payslips or customer data in scanner fixtures.</div>
    <header>
      <section>
        <h1>Payslip</h1>
        <div>Aurora Payroll Services Pty Ltd</div>
        <div>ABN 53 004 085 616</div>
        <div>Level 12, 100 Collins Street, Melbourne VIC 3000</div>
      </section>
      <section>
        <strong>Pay date</strong><br>5 Feb 2026<br><br>
        <strong>Pay period</strong><br>1 Jan 2026 – 31 Jan 2026<br><br>
        <strong>Payslip no.</strong><br>PAY-2026-00017
      </section>
    </header>

    <div class="grid">
      <section>
        <h2>Employee</h2>
        <table>
          <tr><th>Name</th><td>Mia Thompson</td></tr>
          <tr><th>Employee ID</th><td>EMP-0017</td></tr>
          <tr><th>Role</th><td>Accounts Assistant</td></tr>
          <tr><th>Department</th><td>Payroll</td></tr>
          <tr><th>Address</th><td>12 Example Street, Richmond VIC 3121</td></tr>
        </table>
      </section>
      <section>
        <h2>Payment summary</h2>
        <table>
          <tr><th>Gross pay</th><td>$6,000.00</td></tr>
          <tr><th>PAYG withholding</th><td>$1,500.00</td></tr>
          <tr><th>Net pay</th><td>$4,500.00</td></tr>
          <tr><th>YTD gross</th><td>$6,000.00</td></tr>
          <tr><th>YTD PAYG</th><td>$1,500.00</td></tr>
        </table>
      </section>
    </div>

    <h2>Earnings and deductions</h2>
    <table>
      <thead><tr><th>Description</th><th>Hours</th><th>Rate</th><th>Amount</th></tr></thead>
      <tbody>
        <tr><td>Ordinary hours</td><td>152.00</td><td>$39.47</td><td>$6,000.00</td></tr>
        <tr><td>PAYG withholding</td><td></td><td></td><td>-$1,500.00</td></tr>
      </tbody>
      <tfoot><tr><th colspan="3">Net payment</th><th>$4,500.00</th></tr></tfoot>
    </table>

    <section class="qr-panel" aria-label="Verifiabl verification barcode">
      <div>
        <h2>Verify this payslip</h2>
        <p>Scan the Verifiabl QR code with a lender verification flow. This sample embeds exactly one QR code image so it can be used for single-symbol document scanning tests.</p>
        <p><strong>Fixture:</strong> {{H(fixtureId)}}<br><strong>Reference:</strong> <code>{{H(reference)}}</code></p>
        <p class="muted">Expected scan URL: <code>{{H(scanUrl)}}</code></p>
      </div>
      <img src="{{H(qrFile)}}" alt="Verifiabl verification QR code">
    </section>
  </main>
</body>
</html>
""";
}

static string RenderAddressSizeMatrix(ScannerFixture[] fixtures, List<object> manifestValues)
{
    _ = fixtures;
    string rows = string.Join(
        Environment.NewLine,
        manifestValues
            .Select(value => JsonSerializer.SerializeToElement(
                value,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }))
            .Where(value =>
                value.TryGetProperty("includeInIndex", out JsonElement includeInIndex)
                && !includeInIndex.GetBoolean()
                && value.TryGetProperty("qr", out _))
            .Select(value =>
            {
                JsonElement qr = value.GetProperty("qr");
                string file = H(qr.GetProperty("file").GetString()!);
                string version = qr.GetProperty("version").GetRawText();
                string ecc = H(qr.GetProperty("errorCorrectionLevel").GetString()!);
                string addressBytes = value.GetProperty("addressUtf8Bytes").GetRawText();
                string plaintextBytes = value.GetProperty("plaintextUtf8Bytes").GetRawText();
                string finderStyle = H(value.GetProperty("finderStyle").GetString()!);
                string title = H(value.GetProperty("id").GetString()!);
                return $$"""
                  <tr>
                    <th scope="row">
                      <strong>{{title}}</strong><br>
                      {{addressBytes}} address bytes<br>
                      {{plaintextBytes}} plaintext bytes<br>
                      {{finderStyle}} finders<br>
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
