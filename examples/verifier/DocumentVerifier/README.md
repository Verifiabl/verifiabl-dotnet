# Document verifier example (.NET)

A console app that verifies a payslip document with `Verifiabl.Verifier.Extraction`, the optional `Verifiabl.Verifier` add-on, against the sandbox API. Each type of document is one command. For already-extracted QR or metadata text, use the [payload verifier example](../PayloadVerifier/) instead.

| Command | Namespace | Reads |
| --- | --- | --- |
| `pdf` | `Verifiabl.Verifier.Extraction.Pdf` | A payslip PDF's metadata payload, or the QR code on its pages when it has none. |
| `image` | `Verifiabl.Verifier.Extraction.Images` | The QR code in a PNG, JPEG or WebP file, such as a photo or screenshot of a payslip. |

Both commands read files in the add-on's WebAssembly sandbox, so a damaged or hostile file cannot reach the process.

## Run

Requires .NET 8 or later. Export the sandbox credentials issued during verifier onboarding (see `.env.example`), then run this command from the example directory in the SDK checkout:

```sh
export VERIFIABL_CLIENT_ID='your-sandbox-client-id'
export VERIFIABL_CLIENT_SECRET='your-sandbox-client-secret'
dotnet run -- pdf payslip.pdf
dotnet run -- image payslip-qr.jpg
```

Add `--read-only` to read the document without credentials or calling the API. It reports whether a metadata payload or QR code was found.

It prints the payslip's Verifiabl reference and schema, never payloads or employee details. Failures print the request ID to quote to Verifiabl support when the response carried one. Press Ctrl+C to cancel.

| Exit code | Meaning |
| --- | --- |
| 0 | Verified, or read with `--read-only` |
| 1 | Not verified (no readable payload or a payload that could not be verified), or verification could not be completed. The message says which |
| 2 | Usage error, a file that is missing or cannot be read, or missing credentials |
| 130 | Cancelled |

## Add a command

Each type of document gets a `<Name>Command.cs` with a read-only and a verify method, called from `Program.cs`. Error handling, credentials and exit codes stay in `Program.cs`.

## Package source

By default this example references the SDK projects in the same repository. Set `VerifiablVerifierVersion` to restore the locally packed packages instead. CI builds it that way and runs its offline checks: usage errors, a missing or unreadable file, missing credentials, `--read-only` on a PDF carrying the issuer example's payload in its metadata, and `--read-only` on an image without a QR code and on a file that is not an image.
