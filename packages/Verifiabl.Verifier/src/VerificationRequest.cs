namespace Verifiabl.Verifier;

/// <summary>What to verify: an extracted payload and its idempotency key.</summary>
/// <remarks>
/// <para>
/// To retry a verification later, keep the request and send the same instance
/// again. It sends the same <see cref="IdempotencyKey"/>, so Verifiabl charges
/// the verification once.
/// </para>
/// <para>
/// The barcode is encrypted, but treat it as sensitive and keep it out of logs.
/// <see cref="ToString"/> omits it.
/// </para>
/// </remarks>
public sealed class VerificationRequest
{
    private readonly Guid _idempotencyKey = Guid.NewGuid();

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

    /// <summary>
    /// The key that identifies this verification to Verifiabl. Each new request
    /// gets a new key. Every attempt sends it in the <c>Idempotency-Key</c> header.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A retry with the same key and payslip within 1 hour of the key's first
    /// use is not charged again.
    /// </para>
    /// <para>
    /// Set this property only to retry a verification from a new request, such
    /// as after a restart. Save the key before the first attempt. Do not use one
    /// key for different verifications. A key used for a different payslip, or
    /// first used more than 1 hour ago, causes
    /// <see cref="VerifierErrorCodes.IdempotencyKeyReused"/>.
    /// </para>
    /// <para>
    /// Store the key with your decision. Use it to ask Verifiabl about this
    /// verification or its charge.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The value is not a version 1 to 8 UUID, such as one from <see cref="Guid.NewGuid"/>.
    /// </exception>
    public Guid IdempotencyKey
    {
        get => _idempotencyKey;
        init => _idempotencyKey = ValidIdempotencyKey(value);
    }

    /// <summary>A description without the barcode, so the request is safe to log.</summary>
    public override string ToString() =>
        $"VerificationRequest {{ IdempotencyKey = {IdempotencyKey}, BarcodeLength = {Barcode.Length} }}";

    // The API also accepts the nil and max UUIDs. The SDK rejects them because
    // one fixed key cannot identify different verifications.
    private static Guid ValidIdempotencyKey(Guid value)
    {
        string text = value.ToString("D");
        char version = text[14];
        char variant = text[19];
        bool knownVersion = version >= '1' && version <= '8';
        bool rfcVariant = variant is '8' or '9' or 'a' or 'b';
        if (!knownVersion || !rfcVariant)
        {
            throw new ArgumentException(
                "The idempotency key must be a version 1 to 8 UUID, such as one from Guid.NewGuid().",
                nameof(IdempotencyKey));
        }

        return value;
    }
}
