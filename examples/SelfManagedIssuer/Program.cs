using System.Security.Cryptography;
using System.Text.Json;
using Verifiabl;
using Verifiabl.Client;

return await IssuerExample.RunAsync(args);

internal static class IssuerExample
{
    private const string Schema = "au.payslip.v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly IReadOnlyList<ExamplePayslip> Payslips =
    [
        new(
            "PAY-1001",
            new PiiFields
            {
                EmployeeName = "Jane A. Doe",
                Position = "Senior Developer",
                Department = "Engineering",
                EmployerAbn = "12345678901",
                Bsb = "062-000",
                AccountNumber = "12345678",
                AccountName = "Jane A Doe",
                Address = "12 Example St, Sydney NSW 2000",
            },
            PayslipData(
                grossCents: 900_000,
                paygwCents: 225_000,
                netCents: 675_000,
                ytdGrossCents: 5_400_000,
                ytdPaygwCents: 1_350_000)),
        new(
            "PAY-1002",
            new PiiFields
            {
                EmployeeName = "Zoë Nguyễn",
                Position = "Product Designer",
                Department = "Product",
                EmployerAbn = "12345678901",
                Bsb = "062-000",
                AccountNumber = "87654321",
                AccountName = "Zoë Nguyễn",
                Address = "44 Harbour Rd, Melbourne VIC 3000",
            },
            PayslipData(
                grossCents: 760_000,
                paygwCents: 171_000,
                netCents: 589_000,
                ytdGrossCents: 4_560_000,
                ytdPaygwCents: 1_026_000)),
    ];

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            Mode mode = ReadMode(args);
            VerifiablClient? client = mode == Mode.Live ? CreateLiveClient() : null;
            byte[] key = mode == Mode.Offline ? RandomNumberGenerator.GetBytes(32) : ReadLiveKey();
            try
            {
                string outputBase = Path.GetFullPath(
                    OptionalEnvironment("VERIFIABL_EXAMPLE_OUTPUT_DIR") ?? "output");
                string outputRoot = Path.Join(
                    outputBase,
                    $"run-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}");

                IReadOnlyList<ExamplePayslip> payslips = Payslips;
                PreparedPayslip single = Prepare(payslips[0], key);
                ExamplePayslip payslip = single.Payslip;
                string verifiablReference = single.VerifiablReference;
                EncryptedPii encrypted = single.Encrypted;
                DateTimeOffset issuedAt = single.IssuedAt;
                // snippet:start:dotnet.self-managed.prepare-batch
                List<PreparedPayslip> batch = payslips.Select(payslip =>
                {
                    string plaintext = Pii.Format(payslip.Pii);
                    EncryptedPii encrypted = VerifiablCrypto.EncryptPii(plaintext, key);

                    string reference = VerifiablReference.Generate();
                    DateTimeOffset recordIssuedAt = DateTimeOffset.UtcNow;

                    // Persist these values with the payslip before registration.
                    return new PreparedPayslip(payslip, reference, recordIssuedAt, encrypted);
                }).ToList();
                // snippet:end:dotnet.self-managed.prepare-batch
                IReadOnlyList<BatchOutcome> outcomes;
                IReadOnlyList<PreparedPayslip> batchToWrite = batch;

                // Persist each fixed registration request and encrypted barcode payload
                // before network access so an ambiguous failure can be retried with the
                // same reference, issued time, non-PII data, IV, and authentication tag.
                string initialRegistration = mode == Mode.Live
                    ? "sandbox-registration-pending"
                    : "offline-only-unregistered";
                await WriteArtifactsAsync(outputRoot, "single", single, initialRegistration);
                foreach (PreparedPayslip record in batch)
                {
                    await WriteArtifactsAsync(outputRoot, "batch", record, initialRegistration);
                }

                if (mode == Mode.Live)
                {
                    if (client is null)
                    {
                        throw new InvalidOperationException("Live client was not initialized");
                    }

                    // snippet:start:dotnet.self-managed.register-single
                    await client.RegisterNonPiiAsync(new RegisterNonPiiRequest
                    {
                        VerifiablReference = verifiablReference,
                        Schema = "au.payslip.v1",
                        IssuedAt = issuedAt,
                        PayslipNonPii = payslip.NonPii,
                        EncryptionMetadata = encrypted.Metadata,
                    });
                    // snippet:end:dotnet.self-managed.register-single

                    // snippet:start:dotnet.self-managed.register-batch
                    RegisterNonPiiBatchResponse batchResult = await client.RegisterNonPiiBatchAsync(
                        batch.Select(record => new BatchRecord
                        {
                            VerifiablReference = record.VerifiablReference,
                            ExternalId = record.Payslip.ExternalId,
                            Schema = "au.payslip.v1",
                            IssuedAt = record.IssuedAt,
                            PayslipNonPii = record.Payslip.NonPii,
                            EncryptionMetadata = record.Encrypted.Metadata,
                        }));

                    List<BatchOutcome> registeredOutcomes = batchResult.Results
                        .Select(result => new BatchOutcome(
                            result.ExternalId ?? "unknown",
                            result.VerifiablReference,
                            result.Status,
                            result.Code,
                            result.Detail))
                        .ToList();
                    // snippet:end:dotnet.self-managed.register-batch
                    outcomes = registeredOutcomes;
                    batchToWrite = batch.Where((_, index) =>
                    {
                        string? status = batchResult.Results.ElementAtOrDefault(index)?.Status;
                        return status == BatchRecordStatuses.Created
                            || status == BatchRecordStatuses.Duplicate;
                    }).ToList();
                }
                else
                {
                    outcomes = batch.Select(record => new BatchOutcome(
                        record.Payslip.ExternalId,
                        record.VerifiablReference,
                        "registration-skipped-offline",
                        null,
                        null)).ToList();
                }

                if (mode == Mode.Live)
                {
                    await WriteArtifactsAsync(outputRoot, "single", single, "sandbox-registered");
                    foreach (PreparedPayslip record in batchToWrite)
                    {
                        await WriteArtifactsAsync(outputRoot, "batch", record, "sandbox-registered");
                    }
                }

                await WriteJsonAsync(Path.Join(outputRoot, "batch", "outcomes.json"), outcomes);

                Console.WriteLine($"{mode.ToString().ToLowerInvariant()} issuer example completed");
                if (mode == Mode.Offline)
                {
                    Console.WriteLine("Used an in-memory ephemeral key and made no network requests");
                }

                Console.WriteLine($"Generated artifacts under {outputRoot}");
                return 0;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        catch (InvalidOperationException exception)
        {
            return ReportFailure(exception);
        }
        catch (VerifiablException exception)
        {
            return ReportFailure(exception);
        }
        catch (IOException exception)
        {
            return ReportFailure(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return ReportFailure(exception);
        }
        catch (CryptographicException exception)
        {
            return ReportFailure(exception);
        }
    }

    private static int ReportFailure(Exception exception)
    {
        Console.Error.WriteLine($"Issuer example failed: {exception.Message}");
        return 1;
    }

    private static PayslipNonPii PayslipData(
        long grossCents,
        long paygwCents,
        long netCents,
        long ytdGrossCents,
        long ytdPaygwCents)
    {
        return new PayslipNonPii
        {
            PeriodStart = "2026-08-01",
            PeriodEnd = "2026-08-31",
            AdditionalData = new Dictionary<string, object?>
            {
                ["payment_date"] = "2026-09-04",
                ["currency"] = "AUD",
                ["gross_cents"] = grossCents,
                ["paygw_cents"] = paygwCents,
                ["net_cents"] = netCents,
                ["ytd_gross_cents"] = ytdGrossCents,
                ["ytd_paygw_cents"] = ytdPaygwCents,
            },
        };
    }

    private static PreparedPayslip Prepare(ExamplePayslip payslip, byte[] key)
    {
        // snippet:start:dotnet.self-managed.format-and-encrypt
        string plaintext = Pii.Format(payslip.Pii);
        EncryptedPii encrypted = VerifiablCrypto.EncryptPii(plaintext, key);
        // snippet:end:dotnet.self-managed.format-and-encrypt

        // snippet:start:dotnet.self-managed.prepare-registration
        string verifiablReference = VerifiablReference.Generate();
        DateTimeOffset issuedAt = DateTimeOffset.UtcNow;

        // Persist these values with the payslip before registration.
        // snippet:end:dotnet.self-managed.prepare-registration
        return new PreparedPayslip(payslip, verifiablReference, issuedAt, encrypted);
    }

    private static async Task WriteArtifactsAsync(
        string outputRoot,
        string group,
        PreparedPayslip prepared,
        string registration)
    {
        string verifiablReference = prepared.VerifiablReference;
        EncryptedPii encrypted = prepared.Encrypted;
        // snippet:start:dotnet.self-managed.build-qr
        var parts = new BarcodeParts(verifiablReference, encrypted.Ciphertext);
        BarcodeSvgResult badge = VerifiablBarcode.CreateSvg(
            parts,
            new BarcodeSvgOptions { Environment = VerifiablEnvironment.Sandbox });
        string svg = badge.Svg;
        // snippet:end:dotnet.self-managed.build-qr

        string scanUrl = VerifiablBarcode.BuildScanUrl(
            parts,
            new ScanUrlOptions { Environment = VerifiablEnvironment.Sandbox });

        // snippet:start:dotnet.self-managed.build-xmp
        string xmpPayload = VerifiablBarcode.BuildPayload(
            new BarcodeParts(verifiablReference, encrypted.Ciphertext));
        // snippet:end:dotnet.self-managed.build-xmp
        if (badge.Content != scanUrl
            || !scanUrl.Contains("#2.", StringComparison.Ordinal)
            || !xmpPayload.StartsWith("2|", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Generated QR and XMP artifacts do not use the matching v2 contract");
        }

        string directory = Path.Join(outputRoot, group, prepared.Payslip.ExternalId);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Join(directory, "badge.svg"), svg);
        await File.WriteAllTextAsync(Path.Join(directory, "xmp-payload.txt"), $"{xmpPayload}{Environment.NewLine}");
        await WriteJsonAsync(
            Path.Join(directory, "manifest.json"),
            new
            {
                prepared.Payslip.ExternalId,
                prepared.VerifiablReference,
                Environment = "sandbox",
                Registration = registration,
                RegistrationRequest = new
                {
                    Kind = group,
                    ExternalId = group == "batch" ? prepared.Payslip.ExternalId : null,
                    prepared.VerifiablReference,
                    Schema,
                    prepared.IssuedAt,
                    PayslipNonPii = prepared.Payslip.NonPii,
                    EncryptionMetadataEncoding = "base64",
                    EncryptionMetadata = new
                    {
                        Iv = Convert.ToBase64String(prepared.Encrypted.Metadata.Iv),
                        Tag = Convert.ToBase64String(prepared.Encrypted.Metadata.Tag),
                    },
                },
                BarcodeFormat = "v2",
                Badge = "badge.svg",
                Xmp = new
                {
                    PayloadFile = "xmp-payload.txt",
                    Namespace = VerifiablBarcode.PdfPayloadXmpNamespace,
                    Property = VerifiablBarcode.PdfPayloadXmpProperty,
                },
            });
    }

    private static async Task WriteJsonAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(value, JsonOptions);
        await File.WriteAllTextAsync(path, $"{json}{Environment.NewLine}");
    }

    private static Mode ReadMode(string[] args)
    {
        if (args.Length == 1 && args[0] == "offline")
        {
            return Mode.Offline;
        }

        if (args.Length == 1 && args[0] == "live")
        {
            return Mode.Live;
        }

        throw new InvalidOperationException("Usage: dotnet run -- <offline|live>");
    }

    private static VerifiablClient CreateLiveClient()
    {
        // snippet:start:dotnet.self-managed.create-client
        string clientId = Environment.GetEnvironmentVariable("VERIFIABL_CLIENT_ID")?.Trim()
            ?? throw new InvalidOperationException("Set VERIFIABL_CLIENT_ID");
        string clientSecret = Environment.GetEnvironmentVariable("VERIFIABL_CLIENT_SECRET")?.Trim()
            ?? throw new InvalidOperationException("Set VERIFIABL_CLIENT_SECRET");

        if (clientId.Length == 0 || clientSecret.Length == 0)
        {
            throw new InvalidOperationException("Set VERIFIABL_CLIENT_ID and VERIFIABL_CLIENT_SECRET");
        }

        var client = new VerifiablClient(new VerifiablClientOptions
        {
            Environment = VerifiablEnvironment.Sandbox,
            Auth = VerifiablAuth.ClientCredentials(clientId, clientSecret),
        });
        // snippet:end:dotnet.self-managed.create-client
        return client;
    }

    private static byte[] ReadLiveKey()
    {
        string encoded = OptionalEnvironment("VERIFIABL_ENCRYPTION_KEY_BASE64")
            ?? throw new InvalidOperationException(
                "Live mode requires VERIFIABL_ENCRYPTION_KEY_BASE64");
        byte[] key;
        try
        {
            key = Convert.FromBase64String(encoded);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "VERIFIABL_ENCRYPTION_KEY_BASE64 must be canonical base64",
                exception);
        }

        if (key.Length != 32 || Convert.ToBase64String(key) != encoded)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException(
                "VERIFIABL_ENCRYPTION_KEY_BASE64 must encode exactly 32 bytes");
        }

        return key;
    }

    private static string? OptionalEnvironment(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name)?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private enum Mode
    {
        Offline,
        Live,
    }

    private sealed record ExamplePayslip(
        string ExternalId,
        PiiFields Pii,
        PayslipNonPii NonPii);

    private sealed class PreparedPayslip(
        ExamplePayslip payslip,
        string verifiablReference,
        DateTimeOffset issuedAt,
        EncryptedPii encrypted)
    {
        public ExamplePayslip Payslip { get; } = payslip;

        public string VerifiablReference { get; } = verifiablReference;

        public DateTimeOffset IssuedAt { get; } = issuedAt;

        public EncryptedPii Encrypted { get; } = encrypted;
    }

    private sealed record BatchOutcome(
        string ExternalId,
        string VerifiablReference,
        string Status,
        string? Code,
        string? Detail);
}
