using Verifiabl.Verifier;

// The payload comes from stdin, not argv, so it stays out of shell history and
// process listings.
if (args.Length != 0 || !Console.IsInputRedirected)
{
    Console.Error.WriteLine("Usage: dotnet run < payload.txt");
    return 2;
}

string? clientId = Environment.GetEnvironmentVariable("VERIFIABL_CLIENT_ID");
string? clientSecret = Environment.GetEnvironmentVariable("VERIFIABL_CLIENT_SECRET");

if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
{
    Console.Error.WriteLine("Export VERIFIABL_CLIENT_ID and VERIFIABL_CLIENT_SECRET (see .env.example).");
    return 2;
}

// Drop only the line ending a file or `echo` adds. Pass everything else unchanged;
// the API parses and validates the payload.
string payload = Console.In.ReadToEnd().TrimEnd('\r', '\n');

IVerifierClient verifier = new VerifierClient(new VerifierClientOptions
{
    Environment = VerifierEnvironment.Sandbox,
    Auth = VerifierAuth.ClientCredentials(clientId, clientSecret),
});

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    // Stop the call cleanly instead of letting Ctrl+C kill the process.
    e.Cancel = true;
    cts.Cancel();
};

try
{
    VerificationResult result = await verifier.VerifyAsync(payload, cts.Token);

    // ToString() shows only the reference and schema. Employee details are
    // personal information, so this example never prints them.
    Console.WriteLine($"Verified: {result}");
    Console.WriteLine($"Decrypted at {result.DecryptedAt:u}");
    return 0;
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
    Console.Error.WriteLine($"Verification failed: {failure.Message}");
    PrintRequestId(failure);
    return 1;
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}

static void PrintRequestId(VerifierException failure)
{
    if (failure.RequestId is not null)
    {
        Console.Error.WriteLine($"Quote request ID {failure.RequestId} to Verifiabl support.");
    }
}
