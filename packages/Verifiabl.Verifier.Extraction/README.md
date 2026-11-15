# Verifiabl.Verifier.Extraction

Verifiabl.Verifier.Extraction finds the Verifiabl payload in a payslip PDF, an image file or pixels that you decoded, and verifies it through any `IVerifierClient`. It is an optional add-on to [`Verifiabl.Verifier`](https://www.nuget.org/packages/Verifiabl.Verifier).

The package decodes each file in a WebAssembly [sandbox](#sandbox). A hostile file cannot reach the memory of your process, unless the runtime also has a bug.

The package finds payload text only. It sends the text, without change, to `IVerifierClient.VerifyAsync`. A verification shows that the payload matches the record that its issuer registered. It does not examine the visible contents of the document.

If you read the payload with your own libraries, you do not need this package. Without it, your application has no native dependency from Verifiabl: `Verifiabl.Verifier` is managed code only.

## Installation

```sh
dotnet add package Verifiabl.Verifier.Extraction
```

The package holds the extraction module, which is a WebAssembly module, and the native libraries of [Wasmtime](https://github.com/bytecodealliance/wasmtime), which runs the module. See [Platforms](#platforms). `extraction-release.json` in the package names the [extraction release](https://github.com/Verifiabl/verifiabl-extraction/releases) that the module came from, and its SHA-256.

## Verify a PDF

```csharp
using Verifiabl.Verifier;
using Verifiabl.Verifier.Extraction.Pdf;

await using FileStream pdf = File.OpenRead("payslip.pdf");
PdfVerificationResult result = await verifier.VerifyPdfAsync(pdf, cancellationToken: cancellationToken);
VerificationResult verified = result.Verification;
```

The package reads a PDF in this sequence:

1. It reads the **metadata payload**, which is the XMP property `payload` in the `https://verifiabl.io/ns/` namespace. A value longer than `VerifierClient.MaxBarcodeLength` cannot be a payload, so the package ignores it.
2. If the PDF has no metadata payload, it renders the pages with [PDFium](https://pdfium.googlesource.com/pdfium/) and reads their **QR code**. Thus, it finds a code in an image, on a scanned page, or drawn as vector shapes.

A PDF with a metadata payload is never rendered. `PayslipPdfReader.ReadAsync` reads the payload and does not verify it. It sets `PdfPayloads.Metadata` or, if there is none, `PdfPayloads.QrCode`.

## Verify an image

```csharp
using Verifiabl.Verifier;
using Verifiabl.Verifier.Extraction.Images;

await using FileStream photo = File.OpenRead("payslip-qr.jpg");
ImageVerificationResult result = await verifier.VerifyImageAsync(photo, cancellationToken: cancellationToken);
VerificationResult verified = result.Verification;
```

The package decodes a PNG, JPEG or WebP file, such as a photo or a screenshot of a payslip, and reads its QR code with ZXing-C++. `PayslipImageReader.ReadAsync` reads the QR code and does not verify it.

## Read a QR code from pixels

If you decode images with your own imaging library, give the pixels to `QrPayloadReader`:

```csharp
using Verifiabl.Verifier;
using Verifiabl.Verifier.Extraction;

QrImage image = QrImage.FromRgba32(pixels, width, height);
string? payload = await QrPayloadReader.ReadAsync(image, cancellationToken: cancellationToken);

if (payload is not null)
{
    VerificationResult verified = await verifier.VerifyAsync(new VerificationRequest(payload), cancellationToken);
}
```

`QrImage` takes RGBA, BGRA or greyscale pixels, in rows from top to bottom with no padding. The same QR reader reads pixels and image files, so a code reads the same from a file and from its pixels.

The sandbox covers only the QR read of your pixels. Your imaging library decodes the file in your process, and the sandbox does not contain a fault in that library. For a file from outside your organisation, use `VerifyImageAsync`, which decodes the file in the sandbox.

## Verifiabl codes

`VerifyPdfAsync` and `VerifyImageAsync` are extensions on `IVerifierClient`, so they work with your fakes and decorators. Each one sends the payload text to `VerifyAsync` without change.

The package reads only QR codes that have the shape of a Verifiabl payload:

- a bare `1|…` or `2|…` payload
- a scan link on a `verifiabl.io` host
- a scan link on an issuer's own origin that holds a complete payload (`https://<origin>/v/<reference>#<version>.<ciphertext>`)

It ignores other codes, and it never fetches a scan link. Two different Verifiabl codes in one document give `PayloadExtractionException` with `Failure = Ambiguous`. A scan link and a bare payload with the same contents count as one code, as `VerifiablPayloadText.AreSamePayload` in `Verifiabl.Verifier` compares them.

## Retries

Each call is a new verification with a new idempotency key. To retry a failed call without a second charge, give the key of the failure in less than 1 hour. The first call read the stream to its end, so open the file again, or give the same `byte[]` to both calls:

```csharp
catch (VerifierException failure) when (failure.IsRetryable)
{
    // Later, or from a queue: the same key makes this a retry, not a new verification.
    await using FileStream retryPdf = File.OpenRead("payslip.pdf");
    result = await verifier.VerifyPdfAsync(retryPdf, idempotencyKey: failure.IdempotencyKey, cancellationToken: cancellationToken);
}
```

To retry after a restart, save a key from `Guid.NewGuid()` before the first attempt and give it to each call. `VerifyImageAsync` takes the same `idempotencyKey`.

## Errors

| Exception | Meaning |
| --- | --- |
| `PayloadExtractionException` | The package read no single payload, and it sent nothing to Verifiabl. See `Failure`: `NotFound`, `Ambiguous` (two different payloads), `LimitExceeded`, `Unreadable` (not a file that can be read) or `TimedOut`. Only `TimedOut` is retryable: the read can succeed with less load. |
| `VerifierException` and its subtypes | The payload could not be verified, or the verification could not be completed, as for `VerifyAsync`. |
| `InvalidOperationException` | The sandbox could not start. For example, the native library of Wasmtime could not be loaded on this platform. No file causes this. |
| `OperationCanceledException` | Your cancellation token was cancelled. |

`PayloadExtractionException` is defined in `Verifiabl.Verifier` and derives from `VerifierException`, so catch it before a general `catch (VerifierException)`. It is not a failed verification: no payload was verified. `ArgumentException` shows a programming error, such as a timeout that is not positive or a pixel buffer of the wrong length.

## Limits and time

Payslip files are untrusted input, so each read has limits:

| Limit | Value |
| --- | --- |
| File size | `SandboxModule.MaxFileBytes`, 20 MiB, for a PDF or an image file. The package reads a stream only up to this limit. |
| Image formats | PNG, JPEG and WebP. Other formats, such as GIF and BMP, give `Unreadable`. |
| Pixels | `QrImage.MaxPixels`, 50 megapixels. For an image file, the package checks the header before it decodes pixels. |
| PDF pages | `PayslipPdfReader.MaxPages`, 50. The package refuses a longer PDF that has no metadata payload. It does not read a part of it. |
| PDF rendering | 200 dpi. A page has at most `QrImage.MaxPixels`, and all pages together have at most 200 megapixels. |
| PDF metadata | 64 metadata streams, 4 MiB of decoded metadata and approximately 1 MiB of payload text. More gives `LimitExceeded`. |
| QR codes | Approximately 1 MiB of QR code text. More gives `LimitExceeded`. |
| Memory | 512 MiB for each read, in the sandbox. A read that needs more gives `LimitExceeded`. |
| Time | `PdfReadOptions.Timeout`, `ImageReadOptions.Timeout` and `QrReadOptions.Timeout`, each 30 seconds by default |

A read has its own time budget, and the verification has the timeout of the client (`VerifierClientOptions.Timeout`). Thus, `VerifyPdfAsync` and `VerifyImageAsync` can take the sum of the two.

The time budget includes the time to read a stream. The package reads a stream to its end and does not dispose it. If a read of the stream is in progress when the call ends, the call waits for that read to return. Thus, the package never reads your stream after the call.

`ReadAsync` and the verify methods run at most one read for each processor at one time in the process, and they stop each read at its time budget. A read that the time budget stops releases its place in approximately 50 milliseconds. Each read can use 512 MiB in the sandbox, plus a copy of the file, so allow that memory for each processor.

The synchronous `Read` methods have no time budget and no limit on the number of reads. For files from outside your organisation, call `ReadAsync` or a verify method. As an alternative, give a cancellation token that times out, and limit the number of reads that run at one time.

`QrPayloadReader` copies your pixels into the sandbox when a read starts. Do not change the pixel array during the call.

## Sandbox

The decoders never run in the memory of your process. They are compiled to one WebAssembly module that imports nothing. The module cannot open files, use the network or read the memory of your process. Each read gets a new instance of the module, so one file cannot see a different file. Each read runs on its own thread, so the sandbox stops a decoder that recurses without end, even if the calling thread has little stack.

A hostile file can take control of the module, so the package trusts nothing that the module returns. A file that causes the decoders to crash, or to return something that is not a result, gives `Unreadable`, as a damaged file does.

Each Verifiabl verifier SDK uses the same module, so each SDK reads a file in the same way.

## Startup

The first read in a process compiles the module. This takes approximately 2 seconds, and longer on a busy machine. To prevent this delay for the first file, compile the module at startup:

```csharp
using Verifiabl.Verifier.Extraction;

await SandboxModule.LoadAsync();
```

`ReadAsync` and the verify methods wait for the module before the time budget starts. Thus, the time budget applies only to the file.

## Platforms

The package includes the native libraries of Wasmtime (Apache-2.0 WITH LLVM-exception), without change from its releases, for Windows, macOS and glibc Linux, on x64 and Arm64. Alpine (musl) and 32-bit processes are not supported. A read there throws `InvalidOperationException`.

On .NET Framework, reference the package with `PackageReference` and run the application as a 64-bit process. The build targets of the package copy the Wasmtime library to `wasmtime\x64\` and `wasmtime\arm64\` in your output folder. Deploy these folders with the application. The package loads Wasmtime only from these folders, never from the working directory or `PATH`.

The sandbox runs Wasmtime with explicit checks, not signal handlers. Thus:

- Wasmtime adds no process-wide signal handlers or exception handlers to your application.
- A file cannot end a Windows process that enforces hardware shadow stacks. A .NET 9 or later application that starts from its own executable enforces them on CPUs that have them.
- Large images and rendered PDF pages take approximately one third more time to read.

## Licences

The Verifiabl code in this package has the MIT licence. The module contains PDFium, ZXing-C++, Rust image decoders and the C, C++ and Rust runtimes. `THIRD-PARTY-NOTICES.txt` in the package holds their licences. The licence of Wasmtime is `licenses/wasmtime/LICENSE` in the package.
