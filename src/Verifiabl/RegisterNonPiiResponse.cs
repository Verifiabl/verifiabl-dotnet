namespace Verifiabl;

/// <summary>Response from <see cref="VerifiablClient.RegisterNonPiiAsync"/>.</summary>
public sealed class RegisterNonPiiResponse
{
    internal RegisterNonPiiResponse(string verifiablReference)
    {
        VerifiablReference = verifiablReference;
    }

    /// <summary>22-character base64url Verifiabl reference to embed in the barcode.</summary>
    public string VerifiablReference { get; }
}
