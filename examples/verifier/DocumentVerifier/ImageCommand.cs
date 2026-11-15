using Verifiabl.Verifier;
using Verifiabl.Verifier.Extraction.Images;

namespace DocumentVerifier;

/// <summary>Reads and verifies the QR code in image files with Verifiabl.Verifier.Extraction.</summary>
internal static class ImageCommand
{
    /// <summary>Read the QR code without calling the API.</summary>
    internal static async Task<int> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream file = File.OpenRead(path);
        ImagePayload payload = await PayslipImageReader.ReadAsync(file, cancellationToken: cancellationToken);

        // ToString() reports whether a QR code was found, never its text.
        Console.WriteLine($"Read: {payload}");
        if (payload.QrCode is null)
        {
            Console.Error.WriteLine("No Verifiabl QR code was found.");
            return 1;
        }

        Console.WriteLine("Not verified: read-only mode does not call the API.");
        return 0;
    }

    /// <summary>Read the QR code, then verify it.</summary>
    internal static async Task<int> VerifyAsync(IVerifierClient verifier, string path, CancellationToken cancellationToken)
    {
        await using FileStream file = File.OpenRead(path);
        ImageVerificationResult result = await verifier.VerifyImageAsync(file, cancellationToken: cancellationToken);

        // ToString() shows only the reference and schema. Employee details are
        // personal information, so this example never prints them.
        Console.WriteLine($"Verified: {result}");
        Console.WriteLine("The payslip's payload verified.");
        return 0;
    }
}
