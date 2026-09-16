using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Verifiabl.Tests;

public class CryptoTests
{
    private static byte[] NewKey()
    {
        byte[] key = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(key);
        return key;
    }

    [Fact]
    public void ProducesTheVerifiablCiphertextShape()
    {
        byte[] key = NewKey();
        string plaintext = Pii.Format(new PiiFields { EmployeeName = "Jane A. Doe" });

        EncryptedPii encrypted = VerifiablCrypto.EncryptPii(plaintext, key);

        Assert.Equal(12, encrypted.Metadata.Iv.Length);
        Assert.Equal(16, encrypted.Metadata.Tag.Length);
        Assert.Equal(Encoding.UTF8.GetByteCount(plaintext), encrypted.Ciphertext.Length);
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

        EncryptedPii encrypted = VerifiablCrypto.EncryptPii(plaintext, key);

        byte[] decrypted = new byte[encrypted.Ciphertext.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(
            encrypted.Metadata.Iv,
            encrypted.Ciphertext,
            encrypted.Metadata.Tag,
            decrypted);

        Assert.Equal(plaintext, Encoding.UTF8.GetString(decrypted));
    }

    [Fact]
    public void TamperedCiphertextFailsAuthentication()
    {
        byte[] key = NewKey();
        EncryptedPii encrypted = VerifiablCrypto.EncryptPii("P1|Jane||||||", key);

        encrypted.Ciphertext[0] ^= 0xFF;
        byte[] decrypted = new byte[encrypted.Ciphertext.Length];
        using var aes = new AesGcm(key, 16);

        Assert.ThrowsAny<CryptographicException>(() => aes.Decrypt(
            encrypted.Metadata.Iv,
            encrypted.Ciphertext,
            encrypted.Metadata.Tag,
            decrypted));
    }

    [Fact]
    public void GeneratesAUniqueIvPerCall()
    {
        byte[] key = NewKey();

        EncryptedPii first = VerifiablCrypto.EncryptPii("P1|Jane||||||", key);
        EncryptedPii second = VerifiablCrypto.EncryptPii("P1|Jane||||||", key);

        Assert.False(first.Metadata.Iv.SequenceEqual(second.Metadata.Iv));
        Assert.False(first.Ciphertext.SequenceEqual(second.Ciphertext));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void RejectsKeysThatAreNot32Bytes(int keyLength)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => VerifiablCrypto.EncryptPii("P1|||||||", new byte[keyLength]));

        Assert.Contains("32 bytes", exception.Message);
    }
}
