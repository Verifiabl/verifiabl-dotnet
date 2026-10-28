# Verifiabl.Verifier

The Verifiabl verifier SDK for .NET. Pass an already-extracted payload from a QR code or PDF metadata and receive issuer-authenticated payslip data.

Verifiabl decrypts and checks the payload against its registered record. The SDK holds no decryption keys, parses no PDFs or images, and never fetches scan links.

## Requirements

.NET 8 or newer, or .NET Framework 4.7.2 or newer.

## Installation

```sh
dotnet add package Verifiabl.Verifier
```

## Usage

Create one client with the client ID and client secret from your lender onboarding, and reuse it:

```csharp
using Verifiabl.Verifier;

string clientId = Environment.GetEnvironmentVariable("VERIFIABL_CLIENT_ID")!;
string clientSecret = Environment.GetEnvironmentVariable("VERIFIABL_CLIENT_SECRET")!;

var verifier = new VerifierClient(new VerifierClientOptions
{
    Environment = VerifierEnvironment.Sandbox,
    Auth = VerifierAuth.ClientCredentials(clientId, clientSecret),
});

VerificationResult verified = await verifier.VerifyAsync(extractedPayload, cancellationToken);
```

Pass supported bare payloads (`1|…|…` or `2|…|…`) or scan URLs unchanged. Your existing document pipeline extracts the text; the API owns payload parsing and validation.

`verified.Payslip` holds the registered record and `verified.Employee` the decrypted employee details. JSON fields and unknown schemas are preserved; money values remain decimal strings.

**Success authenticates the payload's data, not the document's current visible contents.** Your integration decides whether to use the returned data directly or compare it with the document. Matching copies of a payload do not authenticate the printed text.

Keep payloads, employee details and payslip values out of logs. `ToString()` omits the data, but JSON serialization and structured-log destructuring do not.

### ASP.NET Core lifetime

Register one client for the application so concurrent requests share its token
cache. With `clientId` and `clientSecret` loaded from your secret configuration:

```csharp
builder.Services.AddSingleton<IVerifierClient>(_ =>
    new VerifierClient(new VerifierClientOptions
    {
        Environment = VerifierEnvironment.Sandbox,
        Auth = VerifierAuth.ClientCredentials(clientId, clientSecret),
    }));
```

Inject `IVerifierClient` and pass the request's cancellation token to
`VerifyAsync`. If the caller disconnects after the request was sent, the
verification may still be recorded and billed. The default shared HTTP client needs no disposal. If supplying
your own `HttpClient`, its lifetime remains yours and its timeout should be
infinite because the SDK applies its own deadline. Avoid layering HTTP retries
on top of the SDK's retries.

## Lender-specific mapping

Map after verification with an application-owned function. No lender-specific configuration or mapping runs inside the client:

```csharp
VerificationResult verified = await verifier.VerifyAsync(extractedPayload, cancellationToken);
LenderPayslip mapped = MapForLender(verified);

static LenderPayslip MapForLender(VerificationResult verified)
{
    if (verified.Schema != "au.payslip.v2")
    {
        throw new NotSupportedException("This mapping requires au.payslip.v2.");
    }

    var fields = verified.Payslip.GetProperty("payslip_non_pii");
    return new LenderPayslip(
        verified.VerifiablReference,
        fields.GetProperty("currency").GetString()!,
        fields.GetProperty("gross").GetString()!);
}

public sealed record LenderPayslip(string Reference, string Currency, string Gross);
```

This example preserves the exact monetary string and fails on missing fields or unsupported schemas. Define your own target model, required-field checks and date/decimal conversions. Do not silently substitute zero or round values. A mapping failure does not undo successful verification and should not trigger another API call. The client never invokes the mapper; richer adapters can remain optional.

## Errors, retries and deadlines

A barcode that fails verification throws `VerifierRejectedException`: it is not a Verifiabl payload, was altered, or has no registered payslip. Empty or over-long text is rejected the same way, without a request. Treat the document as unverified; its `Code` says why.

Every other failure is operational and derives from `VerifierException`:

- `VerifierAuthException`: authentication could not be completed. Token-endpoint outages can be retryable; for non-retryable failures, check the client ID, secret, permissions and environment.
- `VerifierServiceException`: Verifiabl returned an error, such as an outage, a rate limit or an unavailable key. Quote its `RequestId` to Verifiabl support.
- `VerifierTimeoutException` and `VerifierTransportException`: the deadline expired, the connection failed, or a successful HTTP response was malformed. Malformed responses are not retryable; escalate them rather than treating the document as rejected.

`IsRetryable` says whether the same call may succeed later, not whether the previous attempt completed or replay is free of additional verification events. Caller cancellation remains `OperationCanceledException`.

```csharp
try
{
    VerificationResult verified = await verifier.VerifyAsync(extractedPayload, cancellationToken);
}
catch (VerifierRejectedException rejected)
{
    // The document is unverified. Log rejected.Code, not the payload.
}
catch (VerifierException failure) when (failure.IsRetryable)
{
    // Verifiabl is unreachable or busy. Try again later.
}
catch (VerifierAuthException)
{
    // Fix the credentials, permissions or environment.
}
catch (VerifierServiceException failure)
{
    // Quote failure.RequestId to Verifiabl support.
}
catch (VerifierException failure)
{
    // Verification could not be completed. Quote failure.RequestId to support.
    // Do not treat this as a rejected document.
}
```

One deadline covers token acquisition, verification and retry backoff. Verification requests retry transient HTTP/network failures up to `MaxRetries` (default 2); token-endpoint failures surface as `VerifierAuthException`. Set `MaxRetries = 0` to disable transient retries. A rejected token is refreshed once per verification request attempt.

Retries can produce additional verification events. Invoice-time rating policy determines charges; the SDK does not provide cross-request billing deduplication.

### Diagnostics and application retries

Every `VerifierException` exposes nullable `RequestId` and `RetryAfter`.
`RetryAfter` is the server's requested delay, measured when the response was
handled; it is null for missing, invalid or non-positive headers. If you queue
the failure for later, store the time to retry at, not the delay. It does not
make a non-retryable failure retryable. Malformed verification responses retain
their request ID and HTTP `Status` on `VerifierTransportException`, without
exposing the response body. Network failures may have neither.

Prefer the built-in retries unless your application needs to schedule attempts.
For application-owned retries, construct the client with `MaxRetries = 0` to
avoid multiplying transient attempts. The one-time 401 token refresh still
applies. This helper permits only one additional call, honours a longer
`RetryAfter` without shortening it, and bounds both calls and the wait with a
shared deadline:

```csharp
static async Task<VerificationResult> VerifyWithOneRetryAsync(
    IVerifierClient verifier,
    string payload,
    CancellationToken cancellationToken)
{
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    deadline.CancelAfter(TimeSpan.FromMinutes(2));

    try
    {
        return await verifier.VerifyAsync(payload, deadline.Token);
    }
    catch (VerifierException failure) when (failure.IsRetryable)
    {
        TimeSpan delay = failure.RetryAfter ?? TimeSpan.FromSeconds(2);
        // Escalate to the caller for scheduling rather than shortening a long wait.
        if (delay > TimeSpan.FromMinutes(1))
        {
            throw;
        }

        await Task.Delay(delay, deadline.Token);
        return await verifier.VerifyAsync(payload, deadline.Token);
    }
}
```

Apply the failure handling above around this helper; its second failure is not
retried. Its overall deadline, like caller cancellation, produces
`OperationCanceledException`. A timeout or connection loss does not prove the
previous attempt failed to complete. Each retry can create another verification
event; invoice-time rating policy determines charges. Map a successful result
outside the retry helper so mapping failures never repeat verification.

## Testing and extension boundaries

Depend on `IVerifierClient` to fake or decorate verification. Public result and exception constructors support consumer tests without a live API. Exception constructors take only the defining values; set diagnostics with an object initializer:

```csharp
var failure = new VerifierServiceException(503, VerifierErrorCodes.ServiceUnavailable, "Unavailable")
{
    RequestId = "request-1",
    RetryAfter = TimeSpan.FromSeconds(30),
};
```

Constructing a `VerificationResult` does not perform verification; its JSON inputs are cloned so they outlive their source documents.

Future PDF/image extraction belongs in optional packages that compose with `VerifyAsync`, not additional methods required of every `IVerifierClient` implementation. Extraction owns its limits and provenance; lender mapping remains a separate projection of verified data.

## Support

Report issues at [github.com/Verifiabl/verifiabl-dotnet](https://github.com/Verifiabl/verifiabl-dotnet/issues).

## License

MIT. See [LICENSE](https://github.com/Verifiabl/verifiabl-dotnet/blob/main/LICENSE).
