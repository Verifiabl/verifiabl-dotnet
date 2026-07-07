namespace Verifiabl;

/// <summary>A per-field validation error returned by the API.</summary>
public sealed class VerifiablFieldError
{
    internal VerifiablFieldError(string path, string message)
    {
        Path = path;
        Message = message;
    }

    /// <summary>Dot-delimited field path, or "" when not field-specific.</summary>
    public string Path { get; }

    /// <summary>Human-readable message for this field.</summary>
    public string Message { get; }
}

/// <summary>Body shape of every non-2xx JSON response from the Verifiabl API.</summary>
public sealed class VerifiablErrorBody
{
    internal VerifiablErrorBody(
        string error,
        string code,
        string? detail,
        IReadOnlyList<VerifiablFieldError>? fieldErrors)
    {
        Error = error;
        Code = code;
        Detail = detail;
        FieldErrors = fieldErrors;
    }

    /// <summary>Human-readable error summary. May change; match on <see cref="Code"/> instead.</summary>
    public string Error { get; }

    /// <summary>Stable machine-readable error code. See <see cref="VerifiablErrorCodes"/>.</summary>
    public string Code { get; }

    /// <summary>Optional human-readable detail.</summary>
    public string? Detail { get; }

    /// <summary>
    /// Per-field validation errors, present on validation failures. Null when the
    /// response carried none.
    /// </summary>
    public IReadOnlyList<VerifiablFieldError>? FieldErrors { get; }
}
