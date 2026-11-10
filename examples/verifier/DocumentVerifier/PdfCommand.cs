using Verifiabl.Verifier;
using Verifiabl.Verifier.Pdf;

namespace DocumentVerifier;

/// <summary>Reads and verifies payslip PDFs with Verifiabl.Verifier.Pdf.</summary>
internal static class PdfCommand
{
    // No JPEG decoder until an image add-on is available, so JPEG images are
    // skipped and counted. Set PdfReadOptions.JpegDecoder to read them.
    private static readonly PdfReadOptions Options = new();

    /// <summary>Read the payload without calling the API.</summary>
    internal static async Task<int> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream pdf = File.OpenRead(path);
        PdfPayloads payloads = await PayslipPdfReader.ReadAsync(pdf, Options, cancellationToken);

        // ToString() reports which payload was found and skipped images, never the payload.
        Console.WriteLine($"Read: {payloads}");
        return payloads.Metadata is null && payloads.QrCode is null
            ? Report(1, "No Verifiabl payload was found.", payloads)
            : Report(0, "Not verified: read-only mode does not call the API.", payloads);
    }

    /// <summary>Read the metadata payload, or the QR code when there is none, then verify it.</summary>
    internal static async Task<int> VerifyAsync(IVerifierClient verifier, string path, CancellationToken cancellationToken)
    {
        await using FileStream pdf = File.OpenRead(path);
        PdfVerificationResult result = await verifier.VerifyPdfAsync(pdf, Options, cancellationToken: cancellationToken);

        // ToString() shows only the reference and schema. Employee details are
        // personal information, so this example never prints them.
        Console.WriteLine($"Verified: {result}");
        return Report(0, "The payslip's payload verified.", result.Payloads);
    }

    private static int Report(int exitCode, string message, PdfPayloads payloads)
    {
        (exitCode == 0 ? Console.Out : Console.Error).WriteLine(message);
        if (payloads.SkippedImages > 0)
        {
            Console.Error.WriteLine(
                $"{payloads.SkippedImages} image(s) could not be read, such as JPEG images without a decoder.");
        }

        return exitCode;
    }
}
