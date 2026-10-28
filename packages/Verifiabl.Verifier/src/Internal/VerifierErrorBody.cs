namespace Verifiabl.Verifier.Internal;

/// <summary>The parsed JSON body of a non-2xx verification API response.</summary>
internal sealed class VerifierErrorBody
{
    internal VerifierErrorBody(
        string error,
        string code,
        string? reason,
        string? detail,
        IReadOnlyList<VerifierFieldError> fieldErrors)
    {
        Error = error;
        Code = code;
        Reason = reason;
        Detail = detail;
        FieldErrors = fieldErrors;
    }

    internal string Error { get; }

    internal string Code { get; }

    internal string? Reason { get; }

    internal string? Detail { get; }

    internal IReadOnlyList<VerifierFieldError> FieldErrors { get; }
}
