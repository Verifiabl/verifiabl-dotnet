# Verifiabl.Verifier.Extraction

Shared building blocks for the optional extraction add-ons to [`Verifiabl.Verifier`](https://www.nuget.org/packages/Verifiabl.Verifier), such as [`Verifiabl.Verifier.Pdf`](https://www.nuget.org/packages/Verifiabl.Verifier.Pdf). Most integrations install an add-on rather than this package directly.

Extraction finds payload text in a document. It does not verify it: pass the text unchanged to `IVerifierClient.VerifyAsync` for Verifiabl to verify. The package is managed code only and uses [ZXing.Net](https://github.com/micjahn/ZXing.Net) (Apache-2.0) to read QR codes.

## Read a QR code from pixels

Decode the image with your own imaging library, then pass its pixels:

```csharp
QrImage image = QrImage.FromRgba32(pixels, width, height);
string? payload = await ExtractionWork.RunAsync(token => QrPayloadReader.Read(image, token), cancellationToken);

if (payload is not null)
{
    VerificationResult verified = await verifier.VerifyAsync(new VerificationRequest(payload), cancellationToken);
}
```

Only QR codes shaped like a Verifiabl payload are read, so other codes on the page are ignored: a bare `1|…` or `2|…` payload, a scan link on a `verifiabl.io` host, or a scan link on an issuer's own origin that carries a complete payload (`https://<origin>/v/<reference>#<version>.<ciphertext>`). Scan links are never fetched. Two different Verifiabl codes throw `PayloadExtractionException` with `Failure = Ambiguous` instead of guessing. A scan link and a bare payload with the same contents count as one code, as `VerifiablPayloadText.AreSamePayload` in `Verifiabl.Verifier` compares them.

## Failures

`PayloadExtractionException`, defined in `Verifiabl.Verifier`, means nothing was sent to Verifiabl: no payload was verified, and this is not a failed verification. It derives from `VerifierException`, so one catch covers extraction and verification. Its `Failure` is `NotFound`, `Ambiguous`, `LimitExceeded`, `Unreadable` or `TimedOut`. `ArgumentException` is reserved for programming errors, such as a pixel buffer of the wrong length.

## Time and memory

QR decoding cannot be interrupted mid-pass. `ExtractionWork.RunAsync` runs it on the thread pool so a deadline or cancellation ends the wait, and allows at most `ExtractionWork.MaxConcurrency` (the processor count) extractions at once across the process. Abandoned work keeps its slot until it finishes.

An image may be up to `QrImage.MaxPixels` (50 megapixels). Reading one holds the pixel buffer plus a greyscale copy and an inverted copy, about 100 MB for the largest image, per concurrent extraction.
