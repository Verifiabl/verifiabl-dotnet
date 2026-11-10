using System.Text.Json.Nodes;
using Verifiabl.Verifier.Internal;

namespace Verifiabl.Verifier;

/// <summary>Client for the Verifiabl verification API.</summary>
/// <remarks>
/// Create one client and reuse it. It is thread-safe and caches OAuth tokens.
/// The SDK sends extracted payload text unchanged, holds no decryption keys and
/// does not log the payload or result. It does not inspect document contents.
/// </remarks>
public sealed class VerifierClient : IVerifierClient
{
    /// <summary>
    /// The longest barcode text the API accepts. Longer text throws
    /// <see cref="VerifierRejectedException"/> without a request.
    /// </summary>
    /// <remarks>
    /// Read-only rather than constant, so applications use the limit of the
    /// SDK version they run with, not the one they compiled against.
    /// </remarks>
    public static readonly int MaxBarcodeLength = 13000;

    private const string VerifyPath = "/v1/verifications/payload";

    private readonly ApiTransport _transport;

    /// <summary>Create a client. See <see cref="VerifierClientOptions"/>.</summary>
    public VerifierClient(VerifierClientOptions options)
    {
        _transport = new ApiTransport(options);
    }

    /// <summary>The backoff sleep between retries. A seam so tests can skip real delays.</summary>
    internal Func<TimeSpan, CancellationToken, Task> DelayAsync
    {
        get => _transport.DelayAsync;
        set => _transport.DelayAsync = value;
    }

    /// <inheritdoc />
    public Task<VerificationResult> VerifyAsync(VerificationRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        string barcode = request.Barcode;

        // The API parses the barcode, so the SDK accepts every format it does.
        // Extracted text is untrusted input, so a bad length is not verified, in
        // the returned task like the API's own answer, not an ArgumentException.
        if (barcode.Length == 0 || barcode.Length > MaxBarcodeLength)
        {
            return Task.FromException<VerificationResult>(LengthRejection());
        }

        return SendAsync(request, cancellationToken);
    }

    private async Task<VerificationResult> SendAsync(VerificationRequest request, CancellationToken cancellationToken)
    {
        Guid key = request.IdempotencyKey;
        var body = new JsonObject { ["barcode"] = request.Barcode };
        try
        {
            return await _transport.PostAsync(
                VerifyPath,
                body,
                key,
                (json, requestId) => Wire.ResultFromWire(json, requestId, key),
                cancellationToken).ConfigureAwait(false);
        }
        catch (VerifierException exception)
        {
            exception.RecordIdempotencyKey(key);
            throw;
        }
    }

    private static VerifierRejectedException LengthRejection()
    {
        string message = $"The barcode must be between 1 and {MaxBarcodeLength} characters.";
        return new VerifierRejectedException(VerifierErrorCodes.NotVerified, message);
    }
}
