using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Verifiabl;
using Verifiabl.Client;

return await IssuerExample.RunAsync(args);

internal static class IssuerExample
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions WireJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly IReadOnlyList<ExamplePayslip> Payslips =
    [
        new(
            "PAY-1001",
            PayslipSchemas.AustralianV2,
            () => Pii.FormatAustralian(new AustralianPiiFields
            {
                EmployeeName = "Jane A. Doe",
                Position = "Senior Developer",
                Department = "Engineering",
                EmployerName = "Example Payroll Pty Ltd",
                EmployerAbn = "12 345 678 901",
                Bsb = "062-000",
                AccountNumber = "****5678",
                AccountName = "Jane A Doe",
                Address = new AustralianAddress
                {
                    Lines = ["12 Example St"],
                    Suburb = "Sydney",
                    StateOrTerritory = "NSW",
                    Postcode = "2000",
                },
            }),
            new AustralianPayslipV2
            {
                // v2 allows a payslip that prints only the period end.
                PeriodEnd = "2026-08-31",
                PaymentDate = "2026-09-04",
                Currency = PayslipCurrencies.Aud,
                Gross = 9000.00m,
                Paygw = 2250.00m,
                Net = 6750.00m,
            }),
        new(
            "PAY-1002",
            PayslipSchemas.NewZealandV2,
            () => Pii.FormatNewZealand(new NewZealandPiiFields
            {
                EmployeeName = "Zoë Nguyễn",
                IrdNumber = "***-***-***",
                Position = "Product Designer",
                Department = "Product",
                EmployerName = "Example Payroll NZ Ltd",
                AccountNumber = "**-****-*******-**",
                AccountName = "Zoë Nguyễn",
                Address = new NewZealandAddress
                {
                    Lines = ["44 Harbour Rd"],
                    Suburb = "Parnell",
                    City = "Auckland",
                    Postcode = "1052",
                },
            }),
            new NewZealandPayslipV2
            {
                PeriodEnd = "2026-08-31",
                PaymentDate = "2026-09-04",
                Currency = PayslipCurrencies.Nzd,
                Gross = 7600.00m,
                Paye = 1710.00m,
                Net = 5890.00m,
            }),
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
                var batch = payslips.Select(payslip =>
                {
                    string plaintext = payslip.FormatPii();
                    EncryptedPii encrypted = VerifiablCrypto.EncryptPii(plaintext, key);

                    string reference = VerifiablReference.Generate();
                    DateTimeOffset recordIssuedAt = DateTimeOffset.UtcNow;

                    // Persist these values with the payslip before registration.
                    return (
                        Payslip: payslip,
                        VerifiablReference: reference,
                        IssuedAt: recordIssuedAt,
                        Encrypted: encrypted);
                }).ToList();
                // snippet:end:dotnet.self-managed.prepare-batch
                IReadOnlyList<BatchOutcome> outcomes;
                IReadOnlyList<PreparedPayslip> batchToWrite = batch
                    .Select(record => new PreparedPayslip(
                        record.Payslip,
                        record.VerifiablReference,
                        record.IssuedAt,
                        record.Encrypted))
                    .ToList();

                // Persist each fixed registration request and encrypted barcode payload
                // before network access so an ambiguous failure can be retried with the
                // same reference, issued time, non-PII data, IV, and authentication tag.
                string initialRegistration = mode == Mode.Live
                    ? "sandbox-registration-pending"
                    : "offline-only-unregistered";
                await WriteArtifactsAsync(outputRoot, "single", single, initialRegistration);
                foreach (PreparedPayslip record in batchToWrite)
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
                        Schema = payslip.Schema,
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
                            Schema = record.Payslip.Schema,
                            IssuedAt = record.IssuedAt,
                            PayslipNonPii = record.Payslip.NonPii,
                            EncryptionMetadata = record.Encrypted.Metadata,
                        }));

                    var registeredOutcomes = batchResult.Results
                        .Select(result => new
                        {
                            ExternalId = result.ExternalId ?? "unknown",
                            result.VerifiablReference,
                            result.Status,
                            result.Code,
                            result.Detail,
                        })
                        .ToList();
                    // snippet:end:dotnet.self-managed.register-batch
                    outcomes = registeredOutcomes
                        .Select(result => new BatchOutcome(
                            result.ExternalId,
                            result.VerifiablReference,
                            result.Status,
                            result.Code,
                            result.Detail))
                        .ToList();
                    batchToWrite = batch
                        .Where((_, index) =>
                        {
                            string? status = batchResult.Results.ElementAtOrDefault(index)?.Status;
                            return status == BatchRecordStatuses.Created
                                || status == BatchRecordStatuses.Duplicate;
                        })
                        .Select(record => new PreparedPayslip(
                            record.Payslip,
                            record.VerifiablReference,
                            record.IssuedAt,
                            record.Encrypted))
                        .ToList();
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

    private static PreparedPayslip Prepare(ExamplePayslip payslip, byte[] key)
    {
        // snippet:start:dotnet.self-managed.format-and-encrypt
        string plaintext = payslip.FormatPii();
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
                    Schema = prepared.Payslip.Schema,
                    prepared.IssuedAt,
                    // Persist the actual wire fields, not the SDK's typed wrapper:
                    // its internal payload is not serialized as public properties.
                    PayslipNonPii = JsonSerializer.SerializeToElement(
                        prepared.Payslip.Payload,
                        prepared.Payslip.Payload.GetType(),
                        WireJsonOptions),
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
        string Schema,
        Func<string> FormatPii,
        object Payload)
    {
        public PayslipNonPii NonPii => Payload switch
        {
            AustralianPayslipV2 au => PayslipNonPii.FromAustralianV2(au),
            NewZealandPayslipV2 nz => PayslipNonPii.FromNewZealandV2(nz),
            _ => throw new InvalidOperationException("Unsupported example payslip payload"),
        };
    }

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
