namespace Verifiabl.Client;

/// <summary>
/// Thrown for any non-2xx Verifiabl API response. Match on <see cref="Code"/>
/// (stable) rather than <see cref="Exception.Message"/> (may change).
/// </summary>
/// <remarks>
/// <see cref="Code"/> is <see cref="VerifiablErrorCodes.InternalError"/> when the
/// response carried no parseable Verifiabl error body (e.g. a gateway error
/// page); check <see cref="Status"/> for the raw HTTP status in that case.
/// </remarks>
public sealed class VerifiablApiException : VerifiablException
{
    internal VerifiablApiException(int status, VerifiablErrorBody? body, string? requestId)
        : base(body?.Error ?? $"Verifiabl API request failed with status {status}")
    {
        Status = status;
        Code = body?.Code ?? VerifiablErrorCodes.InternalError;
        Body = body;
        RequestId = requestId;
    }

    /// <summary>HTTP status code of the failed response.</summary>
    public int Status { get; }

    /// <summary>Stable machine-readable error code. See <see cref="VerifiablErrorCodes"/>.</summary>
    public string Code { get; }

    /// <summary>The parsed error body, when the response carried one.</summary>
    public VerifiablErrorBody? Body { get; }

    /// <summary>Request ID to quote to Verifiabl support, when the response carried one.</summary>
    public string? RequestId { get; }
}
