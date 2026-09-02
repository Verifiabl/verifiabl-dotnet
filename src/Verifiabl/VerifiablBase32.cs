using System.Text;

namespace Verifiabl;

/// <summary>Canonical RFC 4648 Base32 encoding used by v2 barcode writers.</summary>
public static class VerifiablBase32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Encode bytes as uppercase, unpadded RFC 4648 Base32.</summary>
    public static string Encode(byte[] bytes)
    {
        if (bytes is null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        var output = new StringBuilder(GetEncodedLength(bytes.Length));
        int accumulator = 0;
        int bits = 0;
        foreach (byte value in bytes)
        {
            accumulator = (accumulator << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                output.Append(Alphabet[(accumulator >> bits) & 31]);
            }

            accumulator &= (1 << bits) - 1;
        }

        if (bits > 0)
        {
            output.Append(Alphabet[(accumulator << (5 - bits)) & 31]);
        }

        return output.ToString();
    }

    internal static int GetEncodedLength(int byteLength)
    {
        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        long outputLength = ((long)byteLength * 8 + 4) / 5;
        if (outputLength > int.MaxValue)
        {
            throw new ArgumentException("The Base32 output exceeds the maximum string length.", nameof(byteLength));
        }

        return (int)outputLength;
    }
}
