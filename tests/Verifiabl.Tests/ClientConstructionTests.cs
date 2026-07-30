using System.Net.Http;
using Verifiabl.Client;
using Xunit;

namespace Verifiabl.Tests;

public class ClientConstructionTests
{
    [Fact]
    public void RequiresAuth()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new VerifiablClient(new VerifiablClientOptions()));

        Assert.Contains("Auth is required", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RequiresANonEmptyApiKey(string apiKey)
    {
        Assert.Throws<ArgumentException>(() => VerifiablAuth.ApiKey(apiKey));
    }

    [Theory]
    [InlineData("", "secret")]
    [InlineData("id", "")]
    [InlineData("  ", "secret")]
    public void RequiresClientIdAndClientSecret(string clientId, string clientSecret)
    {
        Assert.Throws<ArgumentException>(
            () => VerifiablAuth.ClientCredentials(clientId, clientSecret));
    }

    [Fact]
    public void RejectsNonHttpsIssuerBaseUrls()
    {
        Assert.Throws<ArgumentException>(() => new VerifiablClient(new VerifiablClientOptions
        {
            Auth = VerifiablAuth.ApiKey("key"),
            IssuerBaseUrl = new Uri("http://register.example.com"),
        }));
    }

    [Fact]
    public void AllowsHttpForLoopbackIssuerDevelopment()
    {
        var client = new VerifiablClient(new VerifiablClientOptions
        {
            Auth = VerifiablAuth.ApiKey("key"),
            IssuerBaseUrl = new Uri("http://localhost:8080"),
        });

        Assert.NotNull(client);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void RejectsNonPositiveTimeouts(int seconds)
    {
        Assert.Throws<ArgumentException>(() => new VerifiablClient(new VerifiablClientOptions
        {
            Auth = VerifiablAuth.ApiKey("key"),
            Timeout = TimeSpan.FromSeconds(seconds),
        }));
    }

    [Fact]
    public void RejectsTokenUrlsOutsideVerifiablAuthHosts()
    {
        Assert.Throws<ArgumentException>(() => new VerifiablClient(new VerifiablClientOptions
        {
            Auth = VerifiablAuth.ClientCredentials(
                "id",
                "secret",
                new Uri("https://auth.attacker.example.com/oauth/token")),
        }));
    }

    [Fact]
    public void AllowsLoopbackTokenUrlsForDevelopment()
    {
        var client = new VerifiablClient(new VerifiablClientOptions
        {
            Auth = VerifiablAuth.ClientCredentials(
                "id",
                "secret",
                new Uri("http://127.0.0.1:9000/oauth/token")),
        });

        Assert.NotNull(client);
    }

    [Fact]
    public void TrimsCredentials()
    {
        // Trimming guards against stray whitespace from environment variables.
        var auth = (VerifiablAuth.ClientCredentialsAuth)VerifiablAuth.ClientCredentials(
            "  id  ",
            " secret \n");

        Assert.Equal("id", auth.ClientId);
        Assert.Equal("secret", auth.ClientSecret);
    }
}
