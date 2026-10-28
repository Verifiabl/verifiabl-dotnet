# Lender verifier example (.NET)

A console app that verifies an already-extracted payslip payload with `Verifiabl.Verifier` against the sandbox API. It reads the text from a payslip's QR code or PDF metadata on stdin: a scan link or a bare `2|…|…` payload. Reading stdin keeps the payload out of shell history and process listings.

## Run

Requires .NET 8 or later. Export the sandbox credentials issued during lender onboarding (see `.env.example`), then run this command from the example directory in the SDK checkout:

```sh
export VERIFIABL_CLIENT_ID='your-sandbox-client-id'
export VERIFIABL_CLIENT_SECRET='your-sandbox-client-secret'
dotnet run < payload.txt
```

`payload.txt` holds the extracted text. Trailing line endings are ignored; everything else is sent unchanged.

It prints the payslip's Verifiabl reference and schema, never the employee details. Failures print the request ID to quote to Verifiabl support when the response carried one. Press Ctrl+C to cancel a call in progress.

| Exit code | Meaning |
| --- | --- |
| 0 | Verified |
| 1 | Not verified, or the call failed. The message says whether to try again |
| 2 | No payload on stdin, unexpected arguments or missing credentials |
| 130 | Cancelled |

## Package source

By default this example references the SDK project in the same repository. Set `VerifiablVerifierVersion` to restore it from a locally packed `Verifiabl.Verifier` package instead. CI builds it that way and runs its offline checks: usage errors, missing credentials and the local empty-payload rejection, which needs no network.
