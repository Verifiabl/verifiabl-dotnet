namespace Verifiabl.Verifier.Extraction;

/// <summary>Options for reading the QR code in an image's pixels.</summary>
public sealed class QrReadOptions
{
    /// <summary>
    /// Time budget for reading the pixels. Defaults to 30 seconds. It is
    /// separate from <see cref="VerifierClientOptions.Timeout"/>, which covers
    /// the verification call that follows.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
