using System.Net;
using System.Net.Http;
using Xunit;

namespace Verifiabl.Verifier.Tests;

public class ConcurrentRefreshTests
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateRejectionReusesTheConcurrentRefresh(bool refreshCompleted)
    {
        var exchange = new RefreshExchange();
        using var http = new HttpClient(new FakeHttpHandler { Responder = exchange.RespondAsync });
        var client = new VerifierClient(new VerifierClientOptions
        {
            Auth = VerifierAuth.ClientCredentials("id", "secret"),
            HttpClient = http,
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            Task<VerificationResult> first = client.VerifyAsync(RefreshExchange.FirstBarcode, cancellation.Token);
            Task<VerificationResult> second = client.VerifyAsync(RefreshExchange.SecondBarcode, cancellation.Token);
            await WithinStep(exchange.BothOldRequests.Task);

            exchange.FirstRejection.SetResult(true);
            await WithinStep(exchange.RefreshStarted.Task);
            if (refreshCompleted)
            {
                exchange.FinishRefresh.SetResult(true);
                await WithinStep(first);
            }

            exchange.SecondRejection.SetResult(true);
            await WithinStep(exchange.SecondRejected.Task);
            exchange.FinishRefresh.TrySetResult(true);
            await WithinStep(Task.WhenAll(first, second));
            await WithinStep(client.VerifyAsync("cached", cancellation.Token));

            Assert.Equal(2, exchange.TokenRequests);
            Assert.Equal(3, exchange.FreshTokenRequests);
        }
        finally
        {
            exchange.FirstRejection.TrySetResult(true);
            exchange.SecondRejection.TrySetResult(true);
            exchange.FinishRefresh.TrySetResult(true);
            cancellation.Cancel();
        }
    }

    // Fails the step instead of hanging the test host when a signal never arrives.
    private static async Task WithinStep(Task step)
    {
        Assert.Same(step, await Task.WhenAny(step, Task.Delay(StepTimeout)));
        await step;
    }

    private sealed class RefreshExchange
    {
        internal const string FirstBarcode = "first";
        internal const string SecondBarcode = "second";
        private const string VerifiedJson = """{"verified":true,"verifiabl_reference":"n5waC35dPCoRtMXW5_gJqw","employee":{},"payslip":{},"decrypted_at":"2026-06-09T09:10:11Z"}""";
        private int _tokenRequests;
        private int _oldRequests;
        private int _freshRequests;

        internal TaskCompletionSource<bool> BothOldRequests { get; } = Signal();
        internal TaskCompletionSource<bool> FirstRejection { get; } = Signal();
        internal TaskCompletionSource<bool> SecondRejection { get; } = Signal();
        internal TaskCompletionSource<bool> SecondRejected { get; } = Signal();
        internal TaskCompletionSource<bool> RefreshStarted { get; } = Signal();
        internal TaskCompletionSource<bool> FinishRefresh { get; } = Signal();
        internal int TokenRequests => Volatile.Read(ref _tokenRequests);
        internal int FreshTokenRequests => Volatile.Read(ref _freshRequests);

        internal async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, string body, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.Contains("oauth"))
            {
                int number = Interlocked.Increment(ref _tokenRequests);
                if (number == 2)
                {
                    RefreshStarted.SetResult(true);
                    await WaitAsync(FinishRefresh.Task, cancellationToken);
                }

                return FakeHttpHandler.Token($"token-{number}");
            }

            if (request.Headers.Authorization!.Parameter != "token-1")
            {
                Assert.Equal("token-2", request.Headers.Authorization.Parameter);
                Interlocked.Increment(ref _freshRequests);
                return FakeHttpHandler.Json(HttpStatusCode.OK, VerifiedJson);
            }

            return await RejectOldTokenAsync(body, cancellationToken);
        }

        // Keyed by barcode, not arrival order: the two calls can reach the
        // handler in either order, and the test awaits them by name.
        private async Task<HttpResponseMessage> RejectOldTokenAsync(string body, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _oldRequests) == 2)
            {
                BothOldRequests.SetResult(true);
            }

            if (body.Contains(SecondBarcode))
            {
                await WaitAsync(SecondRejection.Task, cancellationToken);
                SecondRejected.SetResult(true);
            }
            else
            {
                await WaitAsync(FirstRejection.Task, cancellationToken);
            }

            return FakeHttpHandler.Json(HttpStatusCode.Unauthorized, """{"error":"Unauthorized","code":"UNAUTHORIZED"}""");
        }

        // net472 has no Task.WaitAsync. Honouring the token lets the client's
        // deadline end a request whose signal never arrives.
        private static async Task WaitAsync(Task signal, CancellationToken cancellationToken)
        {
            await Task.WhenAny(signal, Task.Delay(Timeout.Infinite, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            await signal;
        }

        private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
