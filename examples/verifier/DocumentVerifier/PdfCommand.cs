using Verifiabl.Verifier;
using Verifiabl.Verifier.Extraction.Pdf;

namespace DocumentVerifier;

/// <summary>Reads and verifies payslip PDFs with Verifiabl.Verifier.Extraction.</summary>
internal static class PdfCommand
{
    /// <summary>Read the payload without calling the API.</summary>
    internal static async Task<int> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream pdf = File.OpenRead(path);
        PdfPayloads payloads = await PayslipPdfReader.ReadAsync(pdf, cancellationToken: cancellationToken);

        // ToString() reports which payload was found and how many pages were rendered, never the payload.
        Console.WriteLine($"Read: {payloads}");
        return payloads.Metadata is null && payloads.QrCode is null
            ? Report(1, "No Verifiabl payload was found.")
            : Report(0, "Not verified: read-only mode does not call the API.");
    }

    /// <summary>Read the metadata payload, or the QR code when there is none, then verify it.</summary>
    internal static async Task<int> VerifyAsync(IVerifierClient verifier, string path, CancellationToken cancellationToken)
    {
        await using FileStream pdf = File.OpenRead(path);
        PdfVerificationResult result = await verifier.VerifyPdfAsync(pdf, cancellationToken: cancellationToken);

        // ToString() shows only the reference and schema. Employee details are
        // personal information, so this example never prints them.
        Console.WriteLine($"Verified: {result}");
        return Report(0, "The payslip's payload verified.");
    }

    private static int Report(int exitCode, string message)
    {
        (exitCode == 0 ? Console.Out : Console.Error).WriteLine(message);
        return exitCode;
    }
}
