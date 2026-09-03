using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Verifiabl;

string outputDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
    args.Length > 0 ? args[0] : Path.Join("artifacts", "scanner-pack")));
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
ScannerFixture[] fixtures =
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
        string plaintext = Pii.Format(fixture.Fields);
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
            Ciphertext = new
            {
                ByteLength = ciphertextBytes.Length,
                Base64url = encryptedPii,
                Hex = Convert.ToHexString(ciphertextBytes).ToLowerInvariant(),
            },
            Qr = new
            {
                File = pngFile,
                Content = barcode.Content,
                Version = barcode.QrVersion,
                ErrorCorrectionLevel = ToNodeLevel(barcode.ErrorCorrectionLevel),
                barcode.Width,
                barcode.Height,
                Segments = new[] { "byte", "alphanumeric" },
            },
            XmpPayload = VerifiablBarcode.BuildPayload(parts),
        });
    }

    var manifest = new
    {
        Format = "verifiabl-scanner-pack-v1",
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
                JsonElement qr = value.GetProperty("qr");
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
    int? EncryptionNonceSlot = null);
