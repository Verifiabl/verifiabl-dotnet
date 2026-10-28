namespace Verifiabl;

/// <summary>The two values embedded in every locally generated barcode.</summary>
public sealed class BarcodeParts
{
    /// <summary>Pairs a Verifiabl reference with its encrypted PII ciphertext.</summary>
    /// <param name="verifiablReference">
    /// Verifiabl reference returned by <see cref="Client.VerifiablClient.RegisterNonPiiAsync"/>
    /// (or generated with <see cref="VerifiablReference.Generate"/> for batches).
    /// </param>
    /// <param name="encryptedPii">
    /// Encrypted PII ciphertext bytes from <see cref="VerifiablCrypto.EncryptPii"/>.
    /// </param>
    public BarcodeParts(string verifiablReference, byte[] encryptedPii)
    {
        VerifiablReference = verifiablReference;
        EncryptedPii = encryptedPii;
    }

    /// <summary>Verifiabl reference registered for this payslip.</summary>
    public string VerifiablReference { get; }

    /// <summary>Encrypted PII ciphertext bytes.</summary>
    public byte[] EncryptedPii { get; }
}
