namespace Verifiabl.Client;

/// <summary>
/// Thrown for any non-2xx Verifiabl API response. Match on <see cref="Code"/>
/// (stable) rather than <see cref="Exception.Message"/> (may change).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Code"/> is <see cref="VerifiablErrorCodes.InternalError"/> when the
/// response carried no parseable Verifiabl error body (e.g. a gateway error
/// page); check <see cref="Status"/> for the raw HTTP status in that case.
/// </para>
/// <para>
/// A few codes have their own derived type, currently only
/// <see cref="VerifiablIvReuseException"/>. Catching this type still covers them.
/// The constructors are internal, so the SDK owns the whole hierarchy.
/// </para>
/// </remarks>
public class VerifiablApiException : VerifiablException
{
    internal VerifiablApiException(int status, VerifiablErrorBody? body, string? requestId)
        : this(status, body, requestId, body?.Error ?? $"Verifiabl API request failed with status {status}")
    {
    }

    private protected VerifiablApiException(
        int status,
        VerifiablErrorBody? body,
        string? requestId,
        string message)
        : base(message)
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

    /// <summary>
    /// Pick the exception type for a failed response. Dispatch is on the code
    /// alone, the part of the contract the API commits to keeping stable.
    /// </summary>
    internal static VerifiablApiException FromResponse(
        int status,
        VerifiablErrorBody? body,
        string? requestId)
    {
        if (body?.Code == VerifiablErrorCodes.IvReused)
        {
            return new VerifiablIvReuseException(status, body, requestId);
        }

        return new VerifiablApiException(status, body, requestId);
    }
}
