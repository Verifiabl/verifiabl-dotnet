# Verifiabl.Verifier.Pdf

An optional add-on to [`Verifiabl.Verifier`](https://www.nuget.org/packages/Verifiabl.Verifier) that reads the Verifiabl payload in a payslip PDF and verifies it.

- The **metadata payload**, the XMP property `payload` in the `https://verifiabl.io/ns/` namespace, is read first.
- Only when the PDF has no metadata payload is the **QR code** read from its embedded images, such as an issuer's PNG badge or a scanned page.

The package is managed code only. [PdfPig](https://github.com/UglyToad/PdfPig) (Apache-2.0) parses the PDF; nothing is rendered. A QR code drawn as vector shapes is not found; the metadata payload covers most such PDFs.

## Verify a PDF

```csharp
using Verifiabl.Verifier;
using Verifiabl.Verifier.Pdf;

await using FileStream pdf = File.OpenRead("payslip.pdf");
PdfVerificationResult result = await verifier.VerifyPdfAsync(pdf, cancellationToken: cancellationToken);
VerificationResult verified = result.Verification;
```

`VerifyPdfAsync` is an extension on `IVerifierClient`, so it works the same with your fakes and decorators. It passes the metadata payload, or the QR code text when there is no metadata payload, unchanged to `VerifyAsync`. A PDF with a metadata payload never has its images decoded.

Success shows that the verified payload matches the record its issuer registered. Only the payload is checked, not the PDF's visible contents.

To read the payload without verifying, call `PayslipPdfReader.ReadAsync`. It sets `PdfPayloads.Metadata` or, when there is none, `PdfPayloads.QrCode`.

Each call is a new verification with a new idempotency key. To retry a failed call without a second charge, pass the failure's key within 1 hour. The first call read the stream to its end, so open the PDF again, or pass the same `byte[]` to both calls:

```csharp
catch (VerifierException failure) when (failure.IsRetryable)
{
    // Later, or from a queue: the same key makes this a retry, not a new verification.
    await using FileStream retryPdf = File.OpenRead("payslip.pdf");
    result = await verifier.VerifyPdfAsync(retryPdf, idempotencyKey: failure.IdempotencyKey, cancellationToken: cancellationToken);
}
```

To retry after a restart, save a key from `Guid.NewGuid()` before the first attempt and pass it to every call.

## Errors

| Exception | Meaning |
| --- | --- |
| `PayloadExtractionException` | No single payload could be read. Nothing was sent to Verifiabl. Check `Failure`: `NotFound`, `Ambiguous` (two different metadata payloads, or, without one, two different QR codes), `LimitExceeded`, `Unreadable` or `TimedOut`. |
| `VerifierException` and its subtypes | The payload could not be verified, or verification could not be completed, as for `VerifyAsync`. |
| `OperationCanceledException` | Your cancellation token was cancelled. |

`PayloadExtractionException` derives from `VerifierException`, so catch it before a general `catch (VerifierException)`. No payload was verified, and this is not a failed verification; `Failure` says why no payload was read.

## JPEG images

Scanners and phone scanning apps usually embed JPEG images. This package has no JPEG decoder of its own, so without one they are skipped and counted in `PdfPayloads.SkippedImages`. Plug in your own decoder:

```csharp
var options = new PdfReadOptions
{
    JpegDecoder = jpeg => DecodeToRgba(jpeg), // returns QrImage.FromRgba32(...)
};
```

The decoder receives a JPEG file's bytes only after its header was checked against the size the PDF declares. A JPEG wrapped in an ASCII85 or ASCIIHex filter is unwrapped first; one wrapped in a decompressing filter such as FlateDecode is skipped, because it could expand past any limit before its header is checked. Throw `ArgumentException` for a JPEG it cannot read; other exceptions propagate.

## Limits and time

PDFs are untrusted input, so reading is bounded. The page and image limits apply only when the PDF has no metadata payload and its images are read:

| Limit | Value |
| --- | --- |
| PDF size | `PayslipPdfReader.MaxPdfBytes`, 20 MiB |
| Pages | `PayslipPdfReader.MaxPages`, 50. A longer PDF without a metadata payload is refused, not read in part. |
| Embedded images | `PayslipPdfReader.MaxImages`, 200 |
| Pixels per image | `QrImage.MaxPixels`, 50 megapixels. Larger images are skipped. |
| Pixels across the PDF | `PayslipPdfReader.MaxTotalPixels`, 200 megapixels, charged before each image is decoded |
| Decoded bytes per stream | `PayslipPdfReader.MaxDecodedStreamBytes`, 200 MB, checked before a compressed stream is expanded |
| Decoded bytes across the PDF | `PayslipPdfReader.MaxDecodedBytes`, 256 MiB, including page content |
| Time | `PdfReadOptions.Timeout`, 30 seconds by default |

Reading takes up to `PdfReadOptions.Timeout` and verification up to `VerifierClientOptions.Timeout`, so `VerifyPdfAsync` can take their sum. The time budget covers reading a stream: a stream is read to its end and not disposed, and the call waits for an in-flight read to return before it ends, so the SDK never reads your stream after the call. Parsing runs through `ExtractionWork`, which allows one extraction per processor at a time across the process.

Every stream PdfPig decodes passes through these byte limits, so a small compressed PDF cannot expand past them; such a PDF is refused with `LimitExceeded`. A stream in a filter whose output cannot be bounded before decoding, such as LZWDecode, is skipped as unreadable.

Memory per extraction is roughly the PDF, the streams PdfPig has decoded (at most `MaxDecodedBytes` across the PDF), and up to about 100 MB for the largest single image while its QR code is read.
