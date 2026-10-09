using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Verifiabl.Internal;

/// <summary>
/// Free-form pass-through values for schemas without a typed model. They are
/// validated and deep-copied when the payload is created, so a coding error
/// throws at the caller's own line and later changes to the caller's objects
/// are not sent.
/// </summary>
internal static class FreeFormValues
{
    internal static IReadOnlyDictionary<string, object?> SnapshotFields(
        IEnumerable<KeyValuePair<string, object?>> fields,
        string label)
    {
        var copy = new Dictionary<string, object?>();
        foreach (KeyValuePair<string, object?> field in fields)
        {
            AddField(copy, field.Key, field.Value, label);
        }
        return new ReadOnlyDictionary<string, object?>(copy);
    }

    private static void AddField(Dictionary<string, object?> copy, string? key, object? value, string label)
    {
        if (key is null)
        {
            throw new ArgumentException($"{label} has a null key; keys must be strings.", label);
        }
        string fieldLabel = $"{label}[\"{key}\"]";
        if (copy.ContainsKey(key))
        {
            throw new ArgumentException($"{fieldLabel} is repeated.", label);
        }
        copy.Add(key, Snapshot(value, fieldLabel, label));
    }

    private static object? Snapshot(object? value, string label, string paramName)
    {
        switch (value)
        {
            case null or string or bool:
            case sbyte or byte or short or ushort or int or uint or long or ulong:
            case float or double or decimal:
                return value;
            // Covers IDictionary<string, object?> and IReadOnlyDictionary-only
            // implementations, which would otherwise fall into the sequence arm as
            // KeyValuePair sequences.
            case IEnumerable<KeyValuePair<string, object?>> nested:
                {
                    var copy = new Dictionary<string, object?>();
                    foreach (KeyValuePair<string, object?> entry in nested)
                    {
                        AddField(copy, entry.Key, entry.Value, label);
                    }
                    return new ReadOnlyDictionary<string, object?>(copy);
                }
            case System.Collections.IDictionary rawMap:
                {
                    var copy = new Dictionary<string, object?>();
                    foreach (System.Collections.DictionaryEntry entry in rawMap)
                    {
                        if (entry.Key is not string key)
                        {
                            throw new ArgumentException(
                                $"{label} has a non-string key; nested objects must be keyed by string.",
                                paramName);
                        }
                        AddField(copy, key, entry.Value, label);
                    }
                    return new ReadOnlyDictionary<string, object?>(copy);
                }
            case System.Collections.IEnumerable items:
                {
                    var copy = new List<object?>();
                    foreach (object? item in items)
                    {
                        copy.Add(Snapshot(item, $"{label}[{copy.Count}]", paramName));
                    }
                    return new ReadOnlyCollection<object?>(copy);
                }
            default:
                throw new ArgumentException(
                    $"{label} has unsupported type {value.GetType().FullName}. Supported values are " +
                    "null, string, bool, numbers, nested dictionaries, and sequences of those.",
                    paramName);
        }
    }

    /// <summary>
    /// Maps a snapshot value onto the JSON tree, so the public surface never asks
    /// integrators to reference System.Text.Json.
    /// </summary>
    internal static JsonNode? ToJsonNode(object? value) => value switch
    {
        null => null,
        string text => JsonValue.Create(text),
        bool flag => JsonValue.Create(flag),
        sbyte or byte or short or ushort or int or uint or long =>
            JsonValue.Create(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
        ulong unsigned => JsonValue.Create(unsigned),
        double d => JsonValue.Create(d),
        // Widening a float to double would print its binary noise, so keep it single.
        float f => JsonValue.Create(f),
        decimal m => JsonValue.Create(m),
        IReadOnlyDictionary<string, object?> fields => ToJsonObject(fields),
        IReadOnlyList<object?> items => ToJsonArray(items),
        _ => throw new InvalidOperationException("Free-form values are validated when the payload is created."),
    };

    private static JsonObject ToJsonObject(IReadOnlyDictionary<string, object?> fields)
    {
        var obj = new JsonObject();
        foreach (KeyValuePair<string, object?> field in fields)
        {
            obj[field.Key] = ToJsonNode(field.Value);
        }
        return obj;
    }

    private static JsonArray ToJsonArray(IReadOnlyList<object?> items)
    {
        var array = new JsonArray();
        foreach (object? item in items)
        {
            array.Add(ToJsonNode(item));
        }
        return array;
    }
}
