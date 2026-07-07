namespace Verifiabl;

/// <summary>The two values embedded in every locally generated barcode.</summary>
public sealed class BarcodeParts
{
    /// <param name="verifiablReference">
    /// Verifiabl reference returned by <see cref="VerifiablClient.RegisterNonPiiAsync"/>
    /// (or generated with <see cref="VerifiablReference.Generate"/> for batches).
    /// </param>
    /// <param name="encryptedPii">
    /// Encrypted PII ciphertext (base64url) from <see cref="VerifiablCrypto.EncryptPii"/>.
    /// </param>
    public BarcodeParts(string verifiablReference, string encryptedPii)
    {
        VerifiablReference = verifiablReference;
        EncryptedPii = encryptedPii;
    }

    /// <summary>Verifiabl reference registered for this payslip.</summary>
    public string VerifiablReference { get; }

    /// <summary>Encrypted PII ciphertext (base64url).</summary>
    public string EncryptedPii { get; }
}
