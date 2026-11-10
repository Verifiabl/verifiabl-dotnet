namespace Verifiabl.Verifier;

/// <summary>Recognises Verifiabl payload text found in documents.</summary>
/// <remarks>
/// These checks only pick Verifiabl candidates out of a page and tell whether
/// two candidates carry the same payload.
/// They do not validate a payload; send it to <see cref="IVerifierClient.VerifyAsync"/>
/// unchanged and let the API decide.
/// </remarks>
public static class VerifiablPayloadText
{
    private const int ReferenceLength = 22;
    private const string ScanPathPrefix = "/v/";

    /// <summary>
    /// Whether text is shaped like a Verifiabl payload: a bare <c>1|…</c> or
    /// <c>2|…</c> payload, an https scan link on a <c>verifiabl.io</c> host, or an
    /// https scan link on any host that carries a complete payload
    /// (<c>/v/&lt;reference&gt;#&lt;version&gt;.&lt;ciphertext&gt;</c>), as issuers
    /// produce with a custom scan origin. Other QR codes on a page, such as an
    /// issuer's own, are not. Scan links are never fetched.
    /// </summary>
    public static bool IsVerifiablShaped(string text)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if (IsBarePayload(text))
        {
            return true;
        }

        return TryParseScanLink(text, out _);
    }

    /// <summary>
    /// Whether two payload texts carry the same payload, such as two QR codes
    /// found on one document. A scan link and a bare payload match when their
    /// version, reference and ciphertext agree. Other texts must match exactly.
    /// </summary>
    public static bool AreSamePayload(string first, string second)
    {
        if (first is null)
        {
            throw new ArgumentNullException(nameof(first));
        }

        if (second is null)
        {
            throw new ArgumentNullException(nameof(second));
        }

        return string.Equals(Canonical(first), Canonical(second), StringComparison.Ordinal);
    }

    /// <summary><c>version|reference|ciphertext</c> for a scan link, or the text unchanged.</summary>
    private static string Canonical(string text)
    {
        if (!TryParseScanLink(text, out Uri? url))
        {
            return text;
        }

        // Older scan links carry the bare payload URL-encoded in the path,
        // such as /v/1%7C<reference>%7C<ciphertext>, with no fragment.
        string legacy = Uri.UnescapeDataString(Reference(url!));
        if (url!.Fragment.Length == 0 && IsBarePayload(legacy))
        {
            return legacy;
        }

        if (!CarriesPayload(url))
        {
            return text;
        }

        // The fragment is "<version>.<ciphertext>", the same ciphertext text the bare form carries.
        string fragment = Fragment(url!);
        return $"{fragment[0]}|{Reference(url!)}|{fragment.Substring(2)}";
    }

    private static bool TryParseScanLink(string text, out Uri? url)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out url)
            || url.Scheme != Uri.UriSchemeHttps
            || !url.AbsolutePath.StartsWith(ScanPathPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        // Issuers may serve scan links from their own origin, so another host
        // counts only when the link carries a complete payload.
        return IsVerifiablHost(url.Host) || CarriesPayload(url);
    }

    private static bool IsBarePayload(string text) =>
        text.StartsWith("1|", StringComparison.Ordinal) || text.StartsWith("2|", StringComparison.Ordinal);

    private static bool IsVerifiablHost(string host) =>
        host == "verifiabl.io" || host.EndsWith(".verifiabl.io", StringComparison.Ordinal);

    /// <summary>Whether the link is exactly <c>/v/&lt;reference&gt;#&lt;1|2&gt;.&lt;Base32 ciphertext&gt;</c>.</summary>
    private static bool CarriesPayload(Uri url)
    {
        string reference = Reference(url);
        string fragment = Fragment(url);
        return url.Query.Length == 0
            && reference.Length == ReferenceLength
            && reference.All(IsBase64Url)
            && fragment.Length > 2
            && (fragment[0] == '1' || fragment[0] == '2')
            && fragment[1] == '.'
            && fragment.Skip(2).All(IsBase32);
    }

    private static string Reference(Uri url) => url.AbsolutePath.Substring(ScanPathPrefix.Length);

    private static string Fragment(Uri url) => url.Fragment.TrimStart('#');

    private static bool IsBase64Url(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';

    private static bool IsBase32(char c) => c is (>= 'A' and <= 'Z') or (>= '2' and <= '7') or '=';
}
