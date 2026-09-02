namespace Verifiabl.Internal;

internal static class Base64Url
{
    internal static string Encode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    internal static byte[] DecodeCanonical(string value)
    {
        if (!IsBase64Url(value))
        {
            throw new ArgumentException("Ciphertext must be canonical unpadded base64url.", nameof(value));
        }

        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        byte[] bytes = Convert.FromBase64String(padded);
        if (!string.Equals(Encode(bytes), value, StringComparison.Ordinal))
        {
            throw new ArgumentException("Ciphertext must be canonical unpadded base64url.", nameof(value));
        }

        return bytes;
    }

    /// <summary>
    /// Matches decodable unpadded base64url: alphabet only, non-empty, and a length that a
    /// byte sequence can actually encode to (length % 4 == 1 is never decodable).
    /// </summary>
    internal static bool IsBase64Url(string value)
    {
        if (value.Length == 0 || value.Length % 4 == 1)
        {
            return false;
        }

        return value.All(IsBase64UrlChar);
    }

    private static bool IsBase64UrlChar(char c)
    {
        bool letter = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        bool digit = c >= '0' && c <= '9';
        return letter || digit || c == '-' || c == '_';
    }
}
