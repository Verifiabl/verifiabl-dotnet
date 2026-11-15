# Verifiabl.Verifier

Verifiabl.Verifier is the Verifiabl verifier SDK for .NET. You give it a payload from a QR code or PDF metadata. Verifiabl checks the payload and returns the payslip data that its issuer registered.

The SDK holds no decryption keys, parses no PDFs or images, and never fetches scan links. To read payloads from documents, use [`Verifiabl.Verifier.Extraction`](#read-payloads-from-documents) or your own libraries.

A successful verification shows that the payload matches the record that its issuer registered. It does not examine the rest of the document. It does not show that the data of the issuer is correct or current, or that the applicant is the person named.

## Install

.NET 8 or newer, or .NET Framework 4.7.2 or newer.

```sh
dotnet add package Verifiabl.Verifier
```

## Verify a payload

Make one client with the client ID and client secret from your verifier onboarding, and use it again for each verification:

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

Give bare payloads (`1|…|…` or `2|…|…`) or scan links without change. The API parses and validates them.

| Property | Contents |
| --- | --- |
| `Payslip` | The registered record. JSON fields and unknown schemas are kept. Money values are decimal strings. |
| `Employee` | The decrypted employee details |
| `IdempotencyKey` | Store it with your decision. Use it to ask Verifiabl about the verification or its charge. |
| `RequestId` | Give it to Verifiabl support to find the request |

Keep payloads, employee details and payslip values out of logs. `ToString()` omits the data. JSON serialization and structured-log destructuring do not.

### ASP.NET Core

Register one client for the application, so that concurrent requests share its token cache:

```csharp
builder.Services.AddSingleton<IVerifierClient>(_ =>
    new VerifierClient(new VerifierClientOptions
    {
        Environment = VerifierEnvironment.Sandbox,
        Auth = VerifierAuth.ClientCredentials(clientId, clientSecret),
    }));
```

Inject `IVerifierClient` and give the cancellation token of the request to `VerifyAsync`. If the caller disconnects after the SDK sends the request, Verifiabl can still record and charge the verification.

The default HTTP client needs no disposal. If you give your own `HttpClient`, you own its lifetime. Set its timeout to infinite, because the SDK applies its own deadline. Do not add HTTP retries on top of the retries of the SDK.

## Map the result

Map the verified data after verification, with a function that your application owns. The client runs no mapping:

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

This example keeps the exact money string, and fails on a missing field or an unsupported schema. A mapping failure does not undo the verification. Do not call the API again because of it.

## Errors

| Exception | Meaning | Retry |
| --- | --- | --- |
| `VerifierRejectedException`, code `NOT_VERIFIED` | The payload could not be verified. | No |
| `PayloadExtractionException` | Only from `Verifiabl.Verifier.Extraction`. No single payload was read from the document, so the SDK sent nothing. `Failure` gives the reason. This is not a failed verification. | If `IsRetryable`: only `TimedOut`, which can succeed with less load |
| `VerifierAuthException` | Authentication failed. If it is not retryable, check the client ID, the secret, the permissions and the environment. | If `IsRetryable` |
| `VerifierServiceException` | Verifiabl returned an error, such as an outage, a rate limit or a malformed request. Give `RequestId` to Verifiabl support. | If `IsRetryable` |
| `VerifierTimeoutException`, `VerifierTransportException` | The deadline expired, the connection failed, or a successful response was malformed. A malformed response is not retryable: report it. It is not a failed verification. | If `IsRetryable` |
| `OperationCanceledException` | Your token was cancelled. The verification can still have completed. | With the same request |

All exceptions except `OperationCanceledException` derive from `VerifierException`.

`NOT_VERIFIED` has many causes, and the API does not say which: the text cannot be read, Verifiabl has no record of it, its issuer has no active key, or it does not decrypt to a valid payslip payload. A damaged scan, or a code from a different product or environment, gives the same result. Empty or too-long text gets the same answer, without a request.

A client for one environment cannot verify payslips from the other. Check the environment before you act on many `NOT_VERIFIED` results.

Some `VerifierServiceException` codes need attention:

- `VALIDATION_FAILED`, `PAYLOAD_TOO_LARGE` and `UNSUPPORTED_MEDIA_TYPE`: the SDK and the API do not agree about the request. These are not retryable, and are not a failed verification.
- `IDEMPOTENCY_KEY_REUSED`: the key was used for a different payslip, or first used more than 1 hour ago. Nothing was verified or charged. To verify again, use a new `VerificationRequest`.

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
    // The extraction package only: no payload was read, so nothing was verified.
}
catch (VerifierAuthException)
{
    // Fix the credentials, permissions or environment.
}
catch (VerifierServiceException failure)
{
    // Give failure.RequestId to Verifiabl support.
}
catch (VerifierException failure)
{
    // The verification could not be completed. This is not a failed verification.
}
```

### Diagnostics

Each `VerifierException` has these nullable properties:

| Property | Contents |
| --- | --- |
| `IdempotencyKey` | The key of the failed call. A timeout or a dropped connection does not prove that the verification failed. Give the key when you ask Verifiabl if the verification completed or was charged. It is null if the SDK did not start a verification call, such as for `PayloadExtractionException` or for empty or too-long text. |
| `RequestId` | The `X-Request-Id` of the last attempt. Give it to Verifiabl support. A token-endpoint failure has none. |
| `RetryAfter` | The delay that the server asked for, measured when the SDK handled the response. It is null for a missing, invalid or non-positive header. If you queue the failure, store the time to retry, not the delay. It does not make a failure retryable. |

`VerifierTransportException` for a malformed response also has `RequestId` and the HTTP `Status`, but not the response body. A network failure can have neither.

## Retries

Each `VerificationRequest` has its own `IdempotencyKey`. Each attempt sends it in the `Idempotency-Key` header, so Verifiabl charges a retried verification once. A new request is a new verification.

### Built-in retries

One deadline covers the token, the verification and the retry delays. The SDK retries a transient HTTP or network failure up to `MaxRetries` times (default 2). Set `MaxRetries = 0` to stop these retries. A token-endpoint failure gives `VerifierAuthException`. The SDK refreshes a rejected token once for each attempt.

If a `Retry-After` or a delay goes past the deadline, the SDK does not retry. The call fails with the last response and its `RetryAfter`, not with a timeout.

### Your own retries

`IsRetryable` tells you that the same call can succeed later. It does not tell you if the previous attempt completed. Thus, retry in less than 1 hour with the same `VerificationRequest`, or with a new request that has the `IdempotencyKey` of the failure.

Use the built-in retries, unless your application must schedule attempts. For your own retries, set `MaxRetries = 0`, so that the attempts do not multiply. The one token refresh after a 401 still occurs.

This helper makes one more call at most. It does not shorten a long `RetryAfter`, and one deadline covers the two calls and the delay. It sends the same request again, so the retry sends the same key:

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

Put the error handling above around this helper. It does not retry a second failure. Its deadline gives `OperationCanceledException`, as your cancellation does. Map the result outside the helper, so that a mapping failure does not cause a second verification.

To retry from a new request, such as after a restart or from a queue, save the key with your work item before the first attempt. Then set it on the new request:

```csharp
var request = new VerificationRequest(extractedPayload) { IdempotencyKey = savedKey };
```

A key is valid for one payslip, for 1 hour from its first use. Do not use one key for different verifications.

## Read payloads from documents

`Verifiabl.Verifier.Extraction` is an optional package that reads the payload in a payslip PDF, an image file or pixels that you decoded, and verifies it through any `IVerifierClient`. It reads documents in a WebAssembly sandbox. It is not yet published on NuGet.

If you read PDFs or QR codes with your own libraries, you do not need it. Send the metadata payload of the PDF, or the QR code text if there is no metadata payload, without change:

```csharp
// metadata and qrCode come from your own PDF and QR libraries, unchanged.
// Read the QR code only when the PDF has no metadata payload.
var request = new VerificationRequest(metadata ?? qrCode);
VerificationResult verified = await verifier.VerifyAsync(request, cancellationToken);
```

The metadata payload is the XMP property `payload` in the `https://verifiabl.io/ns/` namespace. `VerifiablPayloadText.IsVerifiablShaped` finds the Verifiabl codes in the other text that your QR reader returns, so that other codes do not become API calls.

## Test with fakes

Depend on `IVerifierClient` to fake or decorate verification. It has one method, `VerifyAsync(VerificationRequest, CancellationToken)`. Results and exceptions have public constructors for tests. Set the diagnostics of an exception with an object initializer:

```csharp
var failure = new VerifierServiceException(503, VerifierErrorCodes.ServiceUnavailable, "Unavailable")
{
    RequestId = "request-1",
    RetryAfter = TimeSpan.FromSeconds(30),
};
```

A `VerificationResult` that you make does not verify anything. It copies its JSON inputs, so it does not depend on the source documents.

## Support

Report issues at [github.com/Verifiabl/verifiabl-dotnet](https://github.com/Verifiabl/verifiabl-dotnet/issues).

## License

MIT. See [LICENSE](https://github.com/Verifiabl/verifiabl-dotnet/blob/main/LICENSE).
