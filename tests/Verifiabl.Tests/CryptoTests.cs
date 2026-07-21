using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Verifiabl.Tests;

public class CryptoTests
{
    private const string KeyVersion = "0f8fad5b-d9cb-469f-a165-70867728950e.1";

    private static byte[] NewKey()
    {
        byte[] key = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(key);
        return key;
    }

    private static byte[] FromBase64Url(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2:
                padded += "==";
                break;
            case 3:
                padded += "=";
                break;
        }

        return Convert.FromBase64String(padded);
    }

    [Fact]
    public void ProducesTheVerifiablCiphertextShape()
    {
        byte[] key = NewKey();
        string plaintext = Pii.Format(new PiiFields { EmployeeName = "Jane A. Doe" });

        EncryptedPii encrypted = VerifiablCrypto.EncryptPii(plaintext, key, KeyVersion);

        // 96-bit IV = 16 base64url chars; 128-bit tag = 22 base64url chars.
        Assert.Equal(16, encrypted.Metadata.Iv!.Length);
        Assert.Equal(22, encrypted.Metadata.Tag!.Length);
        Assert.Equal(KeyVersion, encrypted.Metadata.KeyVersion);
        Assert.Matches("^[A-Za-z0-9_-]+$", encrypted.Ciphertext);
        Assert.Matches("^[A-Za-z0-9_-]+$", encrypted.Metadata.Iv);
        Assert.Matches("^[A-Za-z0-9_-]+$", encrypted.Metadata.Tag);
    }

    [Fact]
    public void CiphertextDecryptsBackToThePlaintext()
    {
        byte[] key = NewKey();
        string plaintext = Pii.Format(new PiiFields
        {
            EmployeeName = "Jane A. Doe",
            Bsb = "062-000",
            AccountNumber = "12345678",
        });

        EncryptedPii encrypted = VerifiablCrypto.EncryptPii(plaintext, key, KeyVersion);

        byte[] ciphertext = FromBase64Url(encrypted.Ciphertext);
        byte[] decrypted = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(
            FromBase64Url(encrypted.Metadata.Iv!),
            ciphertext,
            FromBase64Url(encrypted.Metadata.Tag!),
            decrypted);

        Assert.Equal(plaintext, Encoding.UTF8.GetString(decrypted));
    }

    [Fact]
    public void TamperedCiphertextFailsAuthentication()
    {
        byte[] key = NewKey();
        EncryptedPii encrypted = VerifiablCrypto.EncryptPii("P1|Jane||||||", key, KeyVersion);

        byte[] ciphertext = FromBase64Url(encrypted.Ciphertext);
        ciphertext[0] ^= 0xFF;
        byte[] decrypted = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, 16);

        Assert.ThrowsAny<CryptographicException>(() => aes.Decrypt(
            FromBase64Url(encrypted.Metadata.Iv!),
            ciphertext,
            FromBase64Url(encrypted.Metadata.Tag!),
            decrypted));
    }

    [Fact]
    public void GeneratesAUniqueIvPerCall()
    {
        byte[] key = NewKey();

        EncryptedPii first = VerifiablCrypto.EncryptPii("P1|Jane||||||", key, KeyVersion);
        EncryptedPii second = VerifiablCrypto.EncryptPii("P1|Jane||||||", key, KeyVersion);

        Assert.NotEqual(first.Metadata.Iv, second.Metadata.Iv);
        Assert.NotEqual(first.Ciphertext, second.Ciphertext);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void RejectsKeysThatAreNot32Bytes(int keyLength)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => VerifiablCrypto.EncryptPii("P1|||||||", new byte[keyLength], KeyVersion));

        Assert.Contains("32 bytes", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("provider.1")]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e")]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e.0")]
    [InlineData("0F8FAD5B-D9CB-469F-A165-70867728950E.1")]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e.1000000")]
    public void RejectsKeyVersionsOutsideTheDeployedContract(string keyVersion)
    {
        Assert.Throws<ArgumentException>(
            () => VerifiablCrypto.EncryptPii("P1|||||||", new byte[32], keyVersion));
    }
}
