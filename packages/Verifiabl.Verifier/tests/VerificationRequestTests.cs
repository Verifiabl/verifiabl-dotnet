using Xunit;

namespace Verifiabl.Verifier.Tests;

public class VerificationRequestTests
{
    private const string Reference = "n5waC35dPCoRtMXW5_gJqw";
    private const string BarePayload = "2|" + Reference + "|JVSWY3DPO5XXE3DE";
    private const string ScanLink = "https://v.sandbox.verifiabl.io/v/" + Reference + "#2.JVSWY3DPO5XXE3DE";

    [Fact]
    public void ToStringOmitsTheBarcode()
    {
        string text = new VerificationRequest(ScanLink).ToString();

        Assert.DoesNotContain(Reference, text);
        Assert.Contains($"BarcodeLength = {ScanLink.Length}", text);
    }

    [Fact]
    public void EachRequestHasANewVersion4IdempotencyKey()
    {
        VerificationRequest first = new(BarePayload);
        VerificationRequest second = new(ScanLink);

        Assert.NotEqual(first.IdempotencyKey, second.IdempotencyKey);
        Assert.Equal('4', first.IdempotencyKey.ToString("D")[14]);
    }

    [Theory]
    [InlineData("5f0c2a7e-8d4b-4c19-9e3a-7b6d1f2c4e80")]
    [InlineData("01890a5d-ac96-774b-bcce-b302099a8057")]
    [InlineData("5F0C2A7E-8D4B-1C19-AE3A-7B6D1F2C4E80")]
    public void ASuppliedIdempotencyKeyIsKept(string key)
    {
        VerificationRequest request = new(BarePayload) { IdempotencyKey = Guid.Parse(key) };

        Assert.Equal(Guid.Parse(key), request.IdempotencyKey);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    [InlineData("5f0c2a7e-8d4b-0c19-9e3a-7b6d1f2c4e80")]
    [InlineData("5f0c2a7e-8d4b-9c19-9e3a-7b6d1f2c4e80")]
    [InlineData("5f0c2a7e-8d4b-4c19-7e3a-7b6d1f2c4e80")]
    [InlineData("5f0c2a7e-8d4b-4c19-ce3a-7b6d1f2c4e80")]
    public void AnInvalidIdempotencyKeyThrows(string key)
    {
        Assert.Throws<ArgumentException>(() => new VerificationRequest(BarePayload) { IdempotencyKey = Guid.Parse(key) });
    }
}
