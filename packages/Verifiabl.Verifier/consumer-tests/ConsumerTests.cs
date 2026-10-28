using System.Text.Json;
using Xunit;

namespace Verifiabl.Verifier.Consumer.Tests;

// No InternalsVisibleTo: this assembly has the same access as an integrator.
public class ConsumerTests
{
    private const string Reference = "n5waC35dPCoRtMXW5_gJqw";

    [Fact]
    public async Task ConsumerCanFakeVerificationAndMapWithoutChangingTheResult()
    {
        VerificationResult verified = Result("au.payslip.v2");
        IVerifierClient client = new FakeVerifier(verified);

        VerificationResult result = await client.VerifyAsync("already-extracted-payload");
        LenderPayslip mapped = MapForLender(result);

        Assert.Same(verified, result);
        Assert.Equal(Reference, mapped.Reference);
        Assert.Equal("AUD", mapped.Currency);
        Assert.Equal("9000.00", mapped.Gross);
        Assert.Equal("9000.00", result.Payslip.GetProperty("payslip_non_pii").GetProperty("gross").GetString());
    }

    [Fact]
    public void ResultPreservesFutureSchemasAndOutlivesSourceDocuments()
    {
        VerificationResult result = Result("future.payslip.v3");

        Assert.Equal("future.payslip.v3", result.Schema);
        Assert.Equal("preserved", result.Payslip.GetProperty("future_field").GetString());
        Assert.Equal("Ana Leota", result.Employee.GetProperty("employee_name").GetString());
        Assert.DoesNotContain("Ana Leota", result.ToString());
        Assert.DoesNotContain("9000.00", result.ToString());
    }

    [Fact]
    public void MappingFailureIsNotVerificationFailure()
    {
        VerificationResult verified = Result("future.payslip.v3");

        Assert.Throws<NotSupportedException>(() => MapForLender(verified));
        Assert.Equal(Reference, verified.VerifiablReference);
    }

    [Fact]
    public void MissingMappingFieldsDoNotDefaultToZero()
    {
        using JsonDocument empty = JsonDocument.Parse("{}");
        var verified = new VerificationResult(Reference, "au.payslip.v2", empty.RootElement, empty.RootElement, DateTimeOffset.UtcNow);

        Assert.Throws<KeyNotFoundException>(() => MapForLender(verified));
    }

    [Fact]
    public async Task ConsumerCanFakeTypedServiceFailures()
    {
        var failure = new VerifierServiceException(
            422,
            VerifierErrorCodes.KeyVersionUnavailable,
            "Decryption is not available for this record")
        {
            RequestId = "request-1",
            Detail = "Contact support.",
        };
        IVerifierClient client = new FakeVerifier(failure);

        VerifierServiceException actual = await Assert.ThrowsAsync<VerifierServiceException>(() => client.VerifyAsync("payload"));

        Assert.Same(failure, actual);
        Assert.Equal(422, actual.Status);
        Assert.Equal(VerifierErrorCodes.KeyVersionUnavailable, actual.Code);
        Assert.Equal("request-1", actual.RequestId);
        Assert.Empty(actual.FieldErrors);
    }

    [Fact]
    public async Task ConsumerCanFakeRejectionsWithFieldErrors()
    {
        var fields = new List<VerifierFieldError> { new("barcode", "Unrecognised barcode format") };
        var failure = new VerifierRejectedException(VerifierErrorCodes.ValidationFailed, "Validation failed")
        {
            Status = 400,
            RequestId = "request-1",
            FieldErrors = fields,
        };
        fields.Clear();
        IVerifierClient client = new FakeVerifier(failure);

        VerifierRejectedException actual = await Assert.ThrowsAsync<VerifierRejectedException>(() => client.VerifyAsync("invalid"));

        Assert.Same(failure, actual);
        Assert.Single(actual.FieldErrors);
    }

    [Fact]
    public async Task ConsumerCanFakeRejections()
    {
        var failure = new VerifierRejectedException(VerifierErrorCodes.DecryptionFailed, "Decryption failed")
        {
            Status = 400,
        };
        IVerifierClient client = new FakeVerifier(failure);

        VerifierRejectedException actual = await Assert.ThrowsAsync<VerifierRejectedException>(() => client.VerifyAsync("altered"));

        Assert.Same(failure, actual);
        Assert.False(actual.IsRetryable);
        Assert.Empty(actual.FieldErrors);
    }

    [Fact]
    public void ConsumerCanConstructInfrastructureFailures()
    {
        Assert.Equal(403, new VerifierAuthException("Token denied") { Status = 403 }.Status);
        Assert.Equal(TimeSpan.FromSeconds(1), new VerifierTimeoutException("Deadline exceeded", TimeSpan.FromSeconds(1)).Timeout);
        Assert.IsAssignableFrom<VerifierException>(new VerifierTransportException("Transport failed"));
    }

    [Fact]
    public void RetryabilityFollowsTheFailure()
    {
        Assert.True(new VerifierServiceException(503, VerifierErrorCodes.ServiceUnavailable, "Unavailable").IsRetryable);
        Assert.False(new VerifierServiceException(422, VerifierErrorCodes.KeyVersionUnavailable, "Unavailable").IsRetryable);
        Assert.False(new VerifierAuthException("Forbidden") { Status = 403, Code = VerifierErrorCodes.Forbidden }.IsRetryable);
        Assert.True(new VerifierAuthException("Unreachable").IsRetryable);
        Assert.False(new VerifierAuthException("Token denied") { Status = 401 }.IsRetryable);
        Assert.True(new VerifierTimeoutException("Deadline exceeded", TimeSpan.FromSeconds(1)).IsRetryable);
        Assert.True(new VerifierTransportException("Connection reset").IsRetryable);
        Assert.False(new VerifierTransportException("Malformed response", null, isRetryable: false).IsRetryable);
    }

    [Fact]
    public void ConsumersCanReadDiagnosticsWithoutKnowingTheFailureSubtype()
    {
        VerifierException service = new VerifierServiceException(429, VerifierErrorCodes.RateLimited, "Busy")
        {
            RequestId = "service-1",
            RetryAfter = TimeSpan.FromSeconds(60),
        };
        VerifierException auth = new VerifierAuthException("Busy")
        {
            Status = 503,
            RequestId = "auth-1",
            RetryAfter = TimeSpan.FromSeconds(30),
        };
        VerifierException malformed = new VerifierTransportException("Malformed response", null, isRetryable: false)
        {
            Status = 200,
            RequestId = "malformed-1",
        };

        Assert.Equal("service-1", service.RequestId);
        Assert.Equal(TimeSpan.FromSeconds(60), service.RetryAfter);
        Assert.Equal("auth-1", auth.RequestId);
        Assert.Equal(TimeSpan.FromSeconds(30), auth.RetryAfter);
        Assert.Equal("malformed-1", malformed.RequestId);
        Assert.Null(malformed.RetryAfter);
        Assert.Null(new VerifierTransportException("Network fault").RequestId);
        Assert.Null(new VerifierTimeoutException("Timed out", TimeSpan.FromSeconds(30)).RetryAfter);
    }

    [Fact]
    public async Task DocumentedCatchSequenceHandlesMalformedResponses()
    {
        IVerifierClient client = new FakeVerifier(
            new VerifierTransportException("Malformed response", null, isRetryable: false));

        string outcome;
        try
        {
            await client.VerifyAsync("payload");
            outcome = "verified";
        }
        catch (VerifierRejectedException)
        {
            outcome = "rejected";
        }
        catch (VerifierException failure) when (failure.IsRetryable)
        {
            outcome = "retry-later";
        }
        catch (VerifierAuthException)
        {
            outcome = "fix-auth";
        }
        catch (VerifierServiceException)
        {
            outcome = "contact-support";
        }
        catch (VerifierException)
        {
            outcome = "could-not-complete";
        }

        Assert.Equal("could-not-complete", outcome);
    }

    private static VerificationResult Result(string schema)
    {
        using JsonDocument payslip = JsonDocument.Parse(
            """{"payslip_non_pii":{"currency":"AUD","gross":"9000.00"},"future_field":"preserved"}""");
        using JsonDocument employee = JsonDocument.Parse("""{"employee_name":"Ana Leota"}""");
        return new VerificationResult(Reference, schema, payslip.RootElement, employee.RootElement, DateTimeOffset.UtcNow);
    }

    private static LenderPayslip MapForLender(VerificationResult verified)
    {
        if (verified.Schema != "au.payslip.v2")
        {
            throw new NotSupportedException("This mapping requires au.payslip.v2.");
        }

        JsonElement fields = verified.Payslip.GetProperty("payslip_non_pii");
        return new LenderPayslip(
            verified.VerifiablReference,
            fields.GetProperty("currency").GetString()!,
            fields.GetProperty("gross").GetString()!);
    }

    private sealed class LenderPayslip
    {
        internal LenderPayslip(string reference, string currency, string gross)
        {
            Reference = reference;
            Currency = currency;
            Gross = gross;
        }

        internal string Reference { get; }
        internal string Currency { get; }
        internal string Gross { get; }
    }

    private sealed class FakeVerifier : IVerifierClient
    {
        private readonly VerificationResult? _result;
        private readonly VerifierException? _failure;

        internal FakeVerifier(VerificationResult result) => _result = result;

        internal FakeVerifier(VerifierException failure) => _failure = failure;

        public Task<VerificationResult> VerifyAsync(string barcode, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _failure is null
                ? Task.FromResult(_result!)
                : Task.FromException<VerificationResult>(_failure);
        }
    }
}
