namespace Verifiabl.Verifier.Extraction.Images;

/// <summary>Options for reading the QR code in an image file.</summary>
public sealed class ImageReadOptions
{
    /// <summary>
    /// Time budget for reading an image file, including reading a stream,
    /// decoding and reading the QR code. Defaults to 30 seconds. It is separate
    /// from <see cref="VerifierClientOptions.Timeout"/>, which covers the
    /// verification call that follows.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
