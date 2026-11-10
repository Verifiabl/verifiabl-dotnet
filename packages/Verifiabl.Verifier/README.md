# Verifiabl.Verifier

The Verifiabl verifier SDK for .NET. Pass an already-extracted payload from a QR code or PDF metadata and receive the payslip data its issuer registered.

Verifiabl decrypts and checks the payload against its registered record. The SDK holds no decryption keys, parses no PDFs or images, and never fetches scan links.

A successful verification shows that the payload matches the record its issuer registered, and returns that registered data. Only the payload is checked, not the rest of the document. A verification does not show that the issuer's data is correct or current, or that the applicant is the person named.

## Requirements

.NET 8 or newer, or .NET Framework 4.7.2 or newer.

## Installation

```sh
dotnet add package Verifiabl.Verifier
```

## Usage

Create one client with the client ID and client secret from your verifier onboarding, and reuse it:

```csharp
using Verifiabl.Verifier;

string clientId = Environment.GetEnvironmentVariable("VERIFIABL_CLIENT_ID")!;
string clientSecret = Environment.GetEnvironmentVariable("VERIFIABL_CLIENT_SECRET")!;

var verifier = new VerifierClient(new VerifierClientOptions
{
    Environment = VerifierEnvironment.Sandbox,
    Auth = VerifierAuth.ClientCredentials(clientId, clientSecret),
});

var request = new VerificationRequest(extractedPayload);
VerificationResult verified = await verifier.VerifyAsync(request, cancellationToken);
```

Pass supported bare payloads (`1|…|…` or `2|…|…`) or scan URLs unchanged. Your existing document pipeline extracts the text; the API owns payload parsing and validation.

Each `VerificationRequest` has its own `IdempotencyKey`. Every attempt sends it in the `Idempotency-Key` header, so Verifiabl charges a retried verification once. To retry, send the same request again; a new request is a new verification. See [Application retries](#diagnostics-and-application-retries).

`verified.Payslip` holds the registered record and `verified.Employee` the decrypted employee details. JSON fields and unknown schemas are preserved; money values remain decimal strings. Store `verified.IdempotencyKey` with your decision. Use it to ask Verifiabl about the verification or its charge. Keep `verified.RequestId` so Verifiabl support can find the request.

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

## Verifier-specific mapping

Map after verification with an application-owned function. No verifier-specific configuration or mapping runs inside the client:

```csharp
VerificationResult verified = await verifier.VerifyAsync(request, cancellationToken);
MappedPayslip mapped = MapPayslip(verified);

static MappedPayslip MapPayslip(VerificationResult verified)
{
    if (verified.Schema != "au.payslip.v2")
    {
        throw new NotSupportedException("This mapping requires au.payslip.v2.");
    }

    var fields = verified.Payslip.GetProperty("payslip_non_pii");
    return new MappedPayslip(
        verified.VerifiablReference,
        fields.GetProperty("currency").GetString()!,
        fields.GetProperty("gross").GetString()!);
}

public sealed record MappedPayslip(string Reference, string Currency, string Gross);
```

This example preserves the exact monetary string and fails on missing fields or unsupported schemas. Define your own target model, required-field checks and date/decimal conversions. Do not silently substitute zero or round values. A mapping failure does not undo successful verification and should not trigger another API call. The client never invokes the mapper; richer adapters can remain optional.

## Errors, retries and deadlines

A payload that could not be verified throws `VerifierRejectedException` with code `NOT_VERIFIED`. The text cannot be read, Verifiabl has no record of it, its issuer has no active key, or it does not decrypt to a valid payslip payload; the API does not say which. A damaged scan or a code from another product or environment gives the same result. Empty or over-long text gets the same answer without a request.

A client configured for the wrong environment cannot verify payslips registered in the other one: a production payslip verified against `Sandbox`, or a sandbox one against `Production`, is not verified. Check the environment before acting on a run of `NOT_VERIFIED` results.

`PayloadExtractionException` comes from the optional extraction packages: no single payload could be read from a document, so nothing was sent to Verifiabl. No payload was verified, and this is not a failed verification; its `Failure` says why no payload was read.

Every other failure is operational and derives from `VerifierException`:

- `VerifierAuthException`: authentication could not be completed. Token-endpoint outages can be retryable; for non-retryable failures, check the client ID, secret, permissions and environment.
- `VerifierServiceException`: Verifiabl returned an error, such as an outage, a rate limit or a malformed request. Quote its `RequestId` to Verifiabl support. `VALIDATION_FAILED`, `PAYLOAD_TOO_LARGE` and `UNSUPPORTED_MEDIA_TYPE` mean the SDK and API disagree about the request: they are not retryable and are not a failed verification. `IDEMPOTENCY_KEY_REUSED` means the key was used for a different payslip or first used more than 1 hour ago. Nothing was verified or charged. To verify again, use a new `VerificationRequest`.
- `VerifierTimeoutException` and `VerifierTransportException`: the deadline expired, the connection failed, or a successful HTTP response was malformed. Malformed responses are not retryable; escalate them rather than treating them as a failed verification.

`IsRetryable` says whether the same call may succeed later, not whether the previous attempt completed. Retry with the same `VerificationRequest`, or a new one carrying the failure's `IdempotencyKey`, within 1 hour, so a verification that completed is not charged again. Caller cancellation remains `OperationCanceledException`. The verification may still have completed, so if you send it again, send the same request.

```csharp
try
{
    VerificationResult verified = await verifier.VerifyAsync(request, cancellationToken);
}
catch (VerifierRejectedException rejected)
{
    // The payload could not be verified. Log rejected.Code, not the payload.
}
catch (VerifierException failure) when (failure.IsRetryable)
{
    // Verifiabl is unreachable or busy. Send the same request again later.
}
catch (PayloadExtractionException unread)
{
    // Extraction packages only: no payload was read, so nothing was verified.
    // unread.Failure says why no payload was read.
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
    // This is not a failed verification.
}
```

One deadline covers token acquisition, verification and retry backoff. Verification requests retry transient HTTP/network failures up to `MaxRetries` (default 2); token-endpoint failures surface as `VerifierAuthException`. Set `MaxRetries = 0` to disable transient retries. A rejected token is refreshed once per verification request attempt. A retry whose `Retry-After` or backoff would outlast the deadline is skipped: the call fails with the last response and its `RetryAfter`, not a timeout.

All attempts of a call send the request's idempotency key. A retry of a completed verification is recorded, but not charged again.

### Diagnostics and application retries

Every `VerifierException` exposes nullable `IdempotencyKey`, `RequestId` and `RetryAfter`.
`IdempotencyKey` is the key of the failed call. A timeout or a dropped
connection does not prove that the verification failed. Quote the key when you
ask Verifiabl if the verification completed or was charged. It is null when the
SDK did not start a verification call: for `PayloadExtractionException`, and for
empty or over-long text.
`RequestId` is the API response's `X-Request-Id`, from the last attempt; quote
it to Verifiabl support. Token-endpoint failures have none.
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
shared deadline. It sends the same request instance again, so the retry sends
the same idempotency key:

```csharp
static async Task<VerificationResult> VerifyWithOneRetryAsync(
    IVerifierClient verifier,
    VerificationRequest request,
    CancellationToken cancellationToken)
{
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    deadline.CancelAfter(TimeSpan.FromMinutes(2));

    try
    {
        return await verifier.VerifyAsync(request, deadline.Token);
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
        return await verifier.VerifyAsync(request, deadline.Token);
    }
}
```

Apply the failure handling above around this helper; its second failure is not
retried. Its overall deadline, like caller cancellation, produces
`OperationCanceledException`. A timeout or connection loss does not prove that
the previous attempt failed. Either way, Verifiabl charges the verification
once. Map a successful result outside the retry helper so mapping failures never
repeat verification.

To retry from a new request, such as after a restart or from a queue, save the
key with your work item before the first attempt. Then set it on the new request:

```csharp
var request = new VerificationRequest(extractedPayload) { IdempotencyKey = savedKey };
```

A key is valid for one payslip for 1 hour from its first use. After that, or
with a different payslip, the API rejects it with `IDEMPOTENCY_KEY_REUSED`. A new
request gets a new key and is a new verification. Do not use one key for
different verifications.

## Testing and extension boundaries

Depend on `IVerifierClient` to fake or decorate verification. It has one method, `VerifyAsync(VerificationRequest, CancellationToken)`. Public result and exception constructors support consumer tests without a live API. Exception constructors take only the defining values; set diagnostics with an object initializer:

```csharp
var failure = new VerifierServiceException(503, VerifierErrorCodes.ServiceUnavailable, "Unavailable")
{
    RequestId = "request-1",
    RetryAfter = TimeSpan.FromSeconds(30),
};
```

Constructing a `VerificationResult` does not perform verification; its JSON inputs are cloned so they outlive their source documents.

### Extraction packages

PDF and image extraction live in optional packages that read a document and pass one payload to `IVerifierClient.VerifyAsync`. They work with any `IVerifierClient`, including your fakes and decorators, and follow the same contract:

- **Failures.** A document with no single readable payload throws `PayloadExtractionException`, never `VerifierRejectedException`: only Verifiabl decides that a payload could not be verified. Verification failures pass through unchanged, so the catch sequence above covers extraction too.
- **Retries.** Each verify call takes an optional idempotency key, so a caller can retry a failed call with the failure's `IdempotencyKey` instead of starting a new, chargeable verification.
- **One verification per document.** Every verification can be billed. When a document carries its payload in metadata and in a QR code, the package verifies the metadata payload, and reads the QR code only when there is no metadata payload.
- **Results.** Results wrap `VerificationResult` with the payload the package read.
- **Bounds.** Input size, decoding work and time are limited separately from `VerifierClientOptions.Timeout`, and cancellation is honoured.
- **Privacy.** Payload text and document images never appear in messages, logs or `ToString()`.

`Verifiabl.Verifier.Pdf` will verify a payslip PDF's metadata payload, or its QR code when it has none, through any `IVerifierClient`. It is not yet published on NuGet; until then, use [your own extraction](#your-own-extraction).

Verifier-specific mapping remains a separate projection of verified data and runs after any extraction package.

### Your own extraction

If you already read PDFs or QR codes with your own libraries, you need no extraction package. Send the PDF's metadata payload, or the QR code text when there is no metadata payload, unchanged:

```csharp
// metadata and qrCode come from your own PDF and QR libraries, unchanged.
// Read the QR code only when the PDF has no metadata payload.
var request = new VerificationRequest(metadata ?? qrCode);
VerificationResult verified = await verifier.VerifyAsync(request, cancellationToken);
```

To retry with a saved key, set `IdempotencyKey` on the request as shown in [Diagnostics and application retries](#diagnostics-and-application-retries).

The metadata payload is the XMP property `payload` in the `https://verifiabl.io/ns/` namespace. `VerifiablPayloadText.IsVerifiablShaped` picks Verifiabl codes out of the other text your QR reader returns, so codes that cannot be a payload never become API calls.

## Support

Report issues at [github.com/Verifiabl/verifiabl-dotnet](https://github.com/Verifiabl/verifiabl-dotnet/issues).

## License

MIT. See [LICENSE](https://github.com/Verifiabl/verifiabl-dotnet/blob/main/LICENSE).
