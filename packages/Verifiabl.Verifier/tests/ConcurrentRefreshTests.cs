using System.Net;
using System.Net.Http;
using Xunit;

namespace Verifiabl.Verifier.Tests;

public class ConcurrentRefreshTests
{
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
            Task<VerificationResult> first = client.VerifyAsync("first", cancellation.Token);
            Task<VerificationResult> second = client.VerifyAsync("second", cancellation.Token);
            await AwaitSignal(exchange.BothOldRequests.Task);

            exchange.FirstRejection.SetResult(true);
            await AwaitSignal(exchange.RefreshStarted.Task);
            if (refreshCompleted)
            {
                exchange.FinishRefresh.SetResult(true);
                await first;
            }

            exchange.SecondRejection.SetResult(true);
            await AwaitSignal(exchange.SecondRejected.Task);
            exchange.FinishRefresh.TrySetResult(true);
            await Task.WhenAll(first, second);
            await client.VerifyAsync("cached", cancellation.Token);

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

    private static async Task AwaitSignal(Task signal)
    {
        Assert.Same(signal, await Task.WhenAny(signal, Task.Delay(TimeSpan.FromSeconds(5))));
        await signal;
    }

    private sealed class RefreshExchange
    {
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
                    await FinishRefresh.Task;
                }

                return FakeHttpHandler.Token($"token-{number}");
            }

            if (request.Headers.Authorization!.Parameter != "token-1")
            {
                Assert.Equal("token-2", request.Headers.Authorization.Parameter);
                Interlocked.Increment(ref _freshRequests);
                return FakeHttpHandler.Json(HttpStatusCode.OK, VerifiedJson);
            }

            return await RejectOldTokenAsync();
        }

        private async Task<HttpResponseMessage> RejectOldTokenAsync()
        {
            int number = Interlocked.Increment(ref _oldRequests);
            if (number == 2)
            {
                BothOldRequests.SetResult(true);
                await SecondRejection.Task;
                SecondRejected.SetResult(true);
            }
            else
            {
                await FirstRejection.Task;
            }

            return FakeHttpHandler.Json(HttpStatusCode.Unauthorized, """{"error":"Unauthorized","code":"UNAUTHORIZED"}""");
        }

        private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
