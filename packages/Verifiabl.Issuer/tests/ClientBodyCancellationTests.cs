#if NET8_0_OR_GREATER
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Verifiabl.Client;
using Xunit;

namespace Verifiabl.Tests;

public class ClientBodyCancellationTests
{
    // The SDK deadline also covers connecting and the token fetch. A cold or
    // busy runner can spend the short deadline before the server stalls, so
    // that attempt proves nothing; retry it with a longer deadline.
    private static readonly TimeSpan[] DeadlineAttempts =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
    ];

    private static readonly TimeSpan CallerTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan CompletionWait = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task StalledResponseBodyHonoursCancellation(bool stallTokenResponse, bool cancelFromCaller)
    {
        if (cancelFromCaller)
        {
            await RunAsync(stallTokenResponse, cancelFromCaller, CallerTimeout);
            return;
        }

        foreach (TimeSpan timeout in DeadlineAttempts)
        {
            if (await RunAsync(stallTokenResponse, cancelFromCaller, timeout))
            {
                return;
            }
        }
        Assert.Fail(
            $"The SDK deadline elapsed before the server stalled the response body on every attempt, " +
            $"up to {DeadlineAttempts[^1].TotalSeconds} seconds.");
    }

    // Returns false if the SDK deadline elapsed before the server stalled the body.
    private static async Task<bool> RunAsync(bool stallTokenResponse, bool cancelFromCaller, TimeSpan timeout)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var baseUrl = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
        using var serverCancellation = new CancellationTokenSource(timeout + CompletionWait + CompletionWait);
        using var callerCancellation = new CancellationTokenSource();
        var bodyStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task server = ServeAsync(listener, stallTokenResponse, bodyStarted, serverCancellation.Token);

        using var handler = new HttpClientHandler { UseProxy = false };
        // Ensure only the SDK deadline or the caller's token can cancel the read.
        using var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var client = new VerifiablClient(new VerifiablClientOptions
        {
            Auth = VerifiablAuth.ClientCredentials("client-id", "client-secret", new Uri(baseUrl, "/oauth/token")),
            IssuerBaseUrl = baseUrl,
            HttpClient = httpClient,
            Timeout = timeout,
            MaxRetries = 0,
        });
        var request = new RegisterNonPiiRequest
        {
            Schema = PayslipSchemas.AustralianV2,
            IssuedAt = new DateTimeOffset(2026, 5, 31, 1, 2, 3, TimeSpan.Zero),
            PayslipNonPii = TestPayslips.Australian(),
            EncryptionMetadata = new EncryptionMetadata { Iv = new byte[12], Tag = new byte[16] },
        };

        Task<RegisterNonPiiResponse> operation = client.RegisterNonPiiAsync(request, callerCancellation.Token);
        bool reachedStall = false;
        try
        {
            // Cancel only after the server has sent headers and a partial body.
            // The remaining bytes never arrive; headers alone must not end the deadline.
            Task first = await Task.WhenAny(bodyStarted.Task, operation).WaitAsync(timeout + CompletionWait);
            if (first == operation)
            {
                if (!cancelFromCaller && operation.Exception?.InnerException is VerifiablTimeoutException)
                {
                    return false;
                }
                await operation;
                Assert.Fail("The call completed before the server stalled the response body.");
            }
            reachedStall = true;

            if (cancelFromCaller)
            {
                callerCancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => operation.WaitAsync(CompletionWait));
            }
            else
            {
                VerifiablTimeoutException exception = await Assert.ThrowsAsync<VerifiablTimeoutException>(
                    () => operation.WaitAsync(timeout + CompletionWait));
                Assert.Equal(timeout, exception.Timeout);
            }
            return true;
        }
        finally
        {
            callerCancellation.Cancel();
            serverCancellation.Cancel();
            try
            {
                await server;
            }
            catch (OperationCanceledException) when (serverCancellation.IsCancellationRequested)
            {
            }
            // An abandoned attempt may have dropped the connection mid-request.
            catch (IOException) when (!reachedStall)
            {
            }
        }
    }

    private static async Task ServeAsync(
        TcpListener listener,
        bool stallTokenResponse,
        TaskCompletionSource<bool> bodyStarted,
        CancellationToken cancellationToken)
    {
        // If testing the issuer body, first complete the OAuth exchange normally.
        int responseCount = stallTokenResponse ? 1 : 2;
        for (int index = 0; index < responseCount; index++)
        {
            using TcpClient connection = await listener.AcceptTcpClientAsync(cancellationToken);
            using NetworkStream stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            int contentLength = 0;
            while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 } line)
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    contentLength = int.Parse(line.Substring("Content-Length:".Length).Trim(),
                        System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            // These test requests are ASCII JSON, so character and byte counts match.
            var buffer = new char[1024];
            while (contentLength > 0)
            {
                int read = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, contentLength)), cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException("Request ended before its body was received.");
                }
                contentLength -= read;
            }

            bool stall = index == responseCount - 1;
            string body = stall ? "{\"" : "{\"access_token\":\"test-token\",\"token_type\":\"Bearer\",\"expires_in\":3600}";
            int declaredLength = stall ? 1024 : Encoding.UTF8.GetByteCount(body);
            byte[] response = Encoding.UTF8.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {declaredLength}\r\nConnection: close\r\n\r\n{body}");
            await stream.WriteAsync(response, cancellationToken);
            if (stall)
            {
                bodyStarted.SetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }
    }
}
#endif
