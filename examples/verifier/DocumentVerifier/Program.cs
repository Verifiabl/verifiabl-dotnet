using DocumentVerifier;
using Verifiabl.Verifier;

const string ReadOnlyFlag = "--read-only";
const string PdfCommandName = "pdf";

if (args.Length is < 2 or > 3
    || (args.Length == 3 && args[2] != ReadOnlyFlag)
    || args[0] != PdfCommandName)
{
    PrintUsage();
    return 2;
}

string path = args[1];
bool readOnly = args.Length == 3;

if (!File.Exists(path))
{
    Console.Error.WriteLine($"No file at {path}.");
    return 2;
}

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    // Stop the call cleanly instead of letting Ctrl+C kill the process.
    e.Cancel = true;
    cts.Cancel();
};

try
{
    if (readOnly)
    {
        return await PdfCommand.ReadAsync(path, cts.Token);
    }

    IVerifierClient? verifier = CreateVerifier();
    if (verifier is null)
    {
        Console.Error.WriteLine("Export VERIFIABL_CLIENT_ID and VERIFIABL_CLIENT_SECRET (see .env.example), or pass --read-only.");
        return 2;
    }

    return await PdfCommand.VerifyAsync(verifier, path, cts.Token);
}
catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
{
    // The file exists but cannot be opened, such as one locked by another program
    // or without read permission.
    Console.Error.WriteLine($"Could not read {path}: {failure.Message}");
    return 2;
}
catch (PayloadExtractionException failure)
{
    // Nothing was sent to Verifiabl, so this is not a failed verification.
    Console.Error.WriteLine($"Could not read a payload ({failure.Failure}): {failure.Message}");
    return 1;
}
catch (VerifierRejectedException rejected)
{
    Console.Error.WriteLine($"Not verified ({rejected.Code}): {rejected.Message}");
    PrintRequestId(rejected);
    return 1;
}
catch (VerifierException failure) when (failure.IsRetryable)
{
    Console.Error.WriteLine($"Verifiabl is unavailable; try again later. {failure.Message}");
    PrintRequestId(failure);
    return 1;
}
catch (VerifierAuthException failure)
{
    Console.Error.WriteLine($"Check VERIFIABL_CLIENT_ID, VERIFIABL_CLIENT_SECRET and their permissions. {failure.Message}");
    PrintRequestId(failure);
    return 1;
}
catch (VerifierServiceException failure)
{
    Console.Error.WriteLine($"Verifiabl returned an error ({failure.Code}): {failure.Message}");
    PrintRequestId(failure);
    return 1;
}
catch (VerifierException failure)
{
    // A response the SDK could not read. Retrying would likely get the same answer,
    // and the document is not rejected, so escalate it.
    Console.Error.WriteLine($"Could not complete verification: {failure.Message}");
    PrintRequestId(failure);
    return 1;
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}

static IVerifierClient? CreateVerifier()
{
    string? clientId = Environment.GetEnvironmentVariable("VERIFIABL_CLIENT_ID");
    string? clientSecret = Environment.GetEnvironmentVariable("VERIFIABL_CLIENT_SECRET");
    if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
    {
        return null;
    }

    return new VerifierClient(new VerifierClientOptions
    {
        Environment = VerifierEnvironment.Sandbox,
        Auth = VerifierAuth.ClientCredentials(clientId, clientSecret),
    });
}

static void PrintUsage()
{
    Console.Error.WriteLine($"Usage: dotnet run -- <command> <file> [{ReadOnlyFlag}]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Commands:");
    Console.Error.WriteLine(
        $"  {PdfCommandName,-6} A payslip PDF: verifies its metadata payload, or its QR code when it has none.");
}

static void PrintRequestId(VerifierException failure)
{
    if (failure.RequestId is not null)
    {
        Console.Error.WriteLine($"Quote request ID {failure.RequestId} to Verifiabl support.");
    }
}
