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

        foreach (char c in value)
        {
            bool valid = (c >= 'A' && c <= 'Z')
                || (c >= 'a' && c <= 'z')
                || (c >= '0' && c <= '9')
                || c == '-'
                || c == '_';
            if (!valid)
            {
                return false;
            }
        }

        return true;
    }
}
