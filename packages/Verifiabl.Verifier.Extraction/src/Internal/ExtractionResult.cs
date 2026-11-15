using System.Text.Json;

namespace Verifiabl.Verifier.Extraction.Internal;

/// <summary>
/// Parses the module's JSON results, which are untrusted like the file. Each
/// parser returns null for a result of another shape.
/// </summary>
internal static class ExtractionResult
{
    /// <summary>The strings in <c>{"codes":[...]}</c>.</summary>
    internal static IReadOnlyList<string>? ParseCodes(byte[] json) =>
        Parse(json, root => Strings(root, "codes"));

    /// <summary><c>{"metadata":[...],"codes":[...],"pages":n}</c>, with no more pages than the module renders.</summary>
    internal static SandboxedPdfPayloads? ParsePdf(byte[] json) =>
        Parse(json, root =>
        {
            List<string>? metadata = Strings(root, "metadata");
            List<string>? codes = Strings(root, "codes");
            return metadata is not null
                && codes is not null
                && root.TryGetProperty("pages", out JsonElement pages)
                && pages.ValueKind == JsonValueKind.Number
                && pages.TryGetInt32(out int count)
                && count is >= 0 and <= ExtractionModule.MaxPdfPages
                ? new SandboxedPdfPayloads(metadata, codes, count)
                : null;
        });

    private static T? Parse<T>(byte[] json, Func<JsonElement, T?> read)
        where T : class
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? read(document.RootElement) : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // GetString throws InvalidOperationException for an escaped lone surrogate.
            return null;
        }
    }

    private static List<string>? Strings(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var texts = new List<string>();
        foreach (JsonElement text in array.EnumerateArray())
        {
            if (text.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            texts.Add(text.GetString()!);
        }

        return texts;
    }
}
