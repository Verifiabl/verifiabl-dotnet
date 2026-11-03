namespace Verifiabl.Verifier;

/// <summary>What to verify: an already-extracted payload and, in later versions, details about it.</summary>
/// <remarks>
/// <para>
/// Keep the request if your application retries a verification later, and
/// replay the same instance rather than building a new one. Later versions add
/// optional fields, such as an idempotency key, as init-only properties, and a
/// replayed request carries them unchanged.
/// </para>
/// <para>
/// The barcode is encrypted, but treat it as sensitive and keep it out of logs.
/// <see cref="ToString"/> omits it.
/// </para>
/// </remarks>
public sealed class VerificationRequest
{
    /// <summary>Create a request for barcode text extracted from a QR code or PDF metadata.</summary>
    /// <param name="barcode">
    /// A scan link such as <c>https://v.verifiabl.io/v/…#2.…</c>, or a bare payload
    /// such as <c>2|…|…</c>. Pass it unchanged; do not trim, decode or fetch the link.
    /// Empty or over-long text is rejected by <see cref="IVerifierClient.VerifyAsync"/>,
    /// not here, because extracted text is untrusted input rather than a programming error.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/> is null.</exception>
    public VerificationRequest(string barcode)
    {
        Barcode = barcode ?? throw new ArgumentNullException(nameof(barcode));
    }

    /// <summary>The extracted barcode text, sent to Verifiabl unchanged.</summary>
    public string Barcode { get; }

    /// <summary>A description without the barcode, so the request is safe to log.</summary>
    public override string ToString() => $"VerificationRequest {{ BarcodeLength = {Barcode.Length} }}";
}
