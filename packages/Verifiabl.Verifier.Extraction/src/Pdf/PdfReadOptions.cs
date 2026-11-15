namespace Verifiabl.Verifier.Extraction.Pdf;

/// <summary>Options for reading the payload in a PDF.</summary>
public sealed class PdfReadOptions
{
    /// <summary>
    /// Time budget for reading a PDF, including reading a stream. Defaults to 30
    /// seconds. It is separate from <see cref="VerifierClientOptions.Timeout"/>,
    /// which covers the verification call that follows.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
