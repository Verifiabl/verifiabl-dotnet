namespace Verifiabl.Verifier;

/// <summary>A request field that failed validation.</summary>
public sealed class VerifierFieldError
{
    /// <summary>Create an instance for an adapter or test fake. Keep messages and causes free of sensitive data.</summary>
    public VerifierFieldError(string path, string message)
    {
        Path = path;
        Message = message;
    }

    /// <summary>The field, such as <c>barcode</c>.</summary>
    public string Path { get; }

    /// <summary>Why it failed.</summary>
    public string Message { get; }
}
