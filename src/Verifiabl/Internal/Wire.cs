using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Verifiabl.Client;

namespace Verifiabl.Internal;

/// <summary>
/// Wire translation. The HTTP API speaks snake_case (<c>issued_at</c>,
/// <c>payslip_non_pii</c>, <c>verifiabl_reference</c>, ...); the SDK surface is
/// PascalCase throughout and translates to and from the wire shape here, at the
/// network boundary.
/// </summary>
/// <remarks>
/// Requests are validated strictly so integration mistakes fail fast and
/// locally. Response parsing is deliberately tolerant: it validates the fields
/// this SDK version knows about and ignores any the API adds later, so an
/// additive API change never breaks a deployed integration.
/// </remarks>
internal static class Wire
{
    internal const int MaxBatchRecords = 1000;

    internal static JsonObject ToWire(RegisterNonPiiRequest request, string verifiablReference)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return RegistrationFields(
            "request",
            verifiablReference,
            request.Schema,
            request.IssuedAt,
            request.PayslipNonPii,
            request.EncryptionMetadata);
    }

    internal static JsonObject ToWire(RegisterAndBuildBarcodeRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        JsonObject body = RegistrationFields(
            "request",
            null,
            request.Schema,
            request.IssuedAt,
            request.PayslipNonPii,
            request.EncryptionMetadata);
        body["encrypted_pii"] = Validation.ValidateCiphertext(
            request.EncryptedPii,
            "request.EncryptedPii");
        return body;
    }

    internal static JsonObject ToWire(IReadOnlyList<BatchRecord> records)
    {
        if (records.Count == 0)
        {
            throw new ArgumentException("records must contain at least one record.", "records");
        }

        if (records.Count > MaxBatchRecords)
        {
            throw new ArgumentException(
                $"records must contain at most {MaxBatchRecords} records.",
                "records");
        }

        var wireRecords = new JsonArray();
        for (int i = 0; i < records.Count; i++)
        {
            BatchRecord record = records[i]
                ?? throw new ArgumentException($"records[{i}] must not be null.", "records");
            string label = $"records[{i}]";
            string reference = VerifiablReference.Validate(
                record.VerifiablReference,
                $"{label}.VerifiablReference");
            JsonObject recordBody = RegistrationFields(
                label,
                reference,
                record.Schema,
                record.IssuedAt,
                record.PayslipNonPii,
                record.EncryptionMetadata);
            if (record.ExternalId is not null)
            {
                recordBody["external_id"] = Validation.ValidateExternalId(
                    record.ExternalId,
                    $"{label}.ExternalId");
            }

            wireRecords.Add(recordBody);
        }

        return new JsonObject { ["records"] = wireRecords };
    }

    private static JsonObject RegistrationFields(
        string label,
        string? verifiablReference,
        string? schema,
        DateTimeOffset issuedAt,
        PayslipNonPii? payslipNonPii,
        EncryptionMetadata? encryptionMetadata)
    {
        Validation.ValidateSchema(schema, $"{label}.Schema");

        // `required DateTimeOffset` stops a forgotten IssuedAt at compile time,
        // but an explicit `default` would otherwise serialize as year 0001.
        if (issuedAt == default)
        {
            throw new ArgumentException(
                $"{label}.IssuedAt is required.",
                $"{label}.IssuedAt");
        }

        if (payslipNonPii is null)
        {
            throw new ArgumentException(
                $"{label}.PayslipNonPii is required.",
                $"{label}.PayslipNonPii");
        }

        Validation.ValidateEncryptionMetadata(
            encryptionMetadata,
            $"{label}.EncryptionMetadata");

        var body = new JsonObject();
        if (verifiablReference is not null)
        {
            body["verifiabl_reference"] = verifiablReference;
        }

        body["schema"] = schema;
        // Millisecond-precision UTC, matching JavaScript's Date.toISOString(): the
        // API accepts arbitrary sub-second precision, but this keeps the wire value
        // identical to the Node SDK's.
        body["issued_at"] = issuedAt.ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        body["payslip_non_pii"] = PayslipNonPiiFields(payslipNonPii, $"{label}.PayslipNonPii");
        body["encryption_metadata"] = new JsonObject
        {
            ["iv"] = encryptionMetadata!.Iv,
            ["tag"] = encryptionMetadata.Tag,
            ["key_version"] = encryptionMetadata.KeyVersion,
        };
        return body;
    }

    private static JsonObject PayslipNonPiiFields(PayslipNonPii data, string label)
    {
        Validation.ValidateIsoDate(data.PeriodStart, $"{label}.PeriodStart");
        Validation.ValidateIsoDate(data.PeriodEnd, $"{label}.PeriodEnd");

        var body = new JsonObject();
        if (data.AdditionalData is not null)
        {
            foreach (KeyValuePair<string, object?> field in data.AdditionalData)
            {
                // The SDK-mapped period keys always win, even if a caller put a
                // stray snake_case copy in AdditionalData.
                if (field.Key is "period_start" or "period_end")
                {
                    continue;
                }

                body[field.Key] = ToJsonNode(field.Value, $"{label}.AdditionalData[\"{field.Key}\"]");
            }
        }

        body["period_start"] = data.PeriodStart;
        body["period_end"] = data.PeriodEnd;
        return body;
    }

    /// <summary>
    /// Maps a caller-supplied pass-through value onto the JSON tree, so the
    /// public surface never asks integrators to reference System.Text.Json.
    /// </summary>
    private static JsonNode? ToJsonNode(object? value, string label)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                return JsonValue.Create(text);
            case bool flag:
                return JsonValue.Create(flag);
            case sbyte or byte or short or ushort or int or uint or long:
                return JsonValue.Create(Convert.ToInt64(value, CultureInfo.InvariantCulture));
            case ulong unsigned:
                return JsonValue.Create(unsigned);
            case double d:
                return JsonValue.Create(d);
            // Widening a float to double would print its binary noise, so keep it single.
            case float f:
                return JsonValue.Create(f);
            case decimal m:
                return JsonValue.Create(m);
            // Covers IDictionary<string, object?> and IReadOnlyDictionary-only
            // implementations (immutable/frozen wrappers) that would otherwise
            // fall into the array arm as KeyValuePair sequences.
            case IEnumerable<KeyValuePair<string, object?>> nested:
                {
                    var obj = new JsonObject();
                    foreach (KeyValuePair<string, object?> entry in nested)
                    {
                        obj[entry.Key] = ToJsonNode(entry.Value, $"{label}[\"{entry.Key}\"]");
                    }

                    return obj;
                }

            case System.Collections.IDictionary rawMap:
                {
                    var obj = new JsonObject();
                    foreach (System.Collections.DictionaryEntry entry in rawMap)
                    {
                        if (entry.Key is not string key)
                        {
                            throw new ArgumentException(
                                $"{label} has a non-string key; nested objects must be keyed by string.",
                                label);
                        }

                        obj[key] = ToJsonNode(entry.Value, $"{label}[\"{key}\"]");
                    }

                    return obj;
                }

            case System.Collections.IEnumerable items:
                {
                    var array = new JsonArray();
                    int index = 0;
                    foreach (object? item in items)
                    {
                        array.Add(ToJsonNode(item, $"{label}[{index}]"));
                        index++;
                    }

                    return array;
                }

            default:
                throw new ArgumentException(
                    $"{label} has unsupported type {value.GetType().FullName}. Supported values are " +
                    "null, string, bool, numbers, nested dictionaries, and sequences of those.",
                    label);
        }
    }

    internal static RegisterNonPiiResponse RegistrationFromWire(JsonElement root)
    {
        return new RegisterNonPiiResponse(ReadReference(root, "verifiabl_reference"));
    }

    internal static RegisterAndBuildBarcodeResponse RegisterAndBuildBarcodeFromWire(JsonElement root)
    {
        string reference = ReadReference(root, "verifiabl_reference");
        if (!TryGetObject(root, "barcode", out JsonElement barcode))
        {
            throw UnexpectedShape("barcode");
        }

        string format = ReadString(barcode, "format") ?? throw UnexpectedShape("barcode.format");
        if (format != "png")
        {
            throw UnexpectedShape("barcode.format");
        }

        string data = ReadString(barcode, "data") ?? throw UnexpectedShape("barcode.data");
        if (data.Length == 0)
        {
            throw UnexpectedShape("barcode.data");
        }

        return new RegisterAndBuildBarcodeResponse(reference, new BarcodeImage(format, data));
    }

    internal static RegisterNonPiiBatchResponse BatchFromWire(JsonElement root)
    {
        if (!root.TryGetProperty("results", out JsonElement resultsElement)
            || resultsElement.ValueKind != JsonValueKind.Array)
        {
            throw UnexpectedShape("results");
        }

        var results = new List<BatchRecordResult>(resultsElement.GetArrayLength());
        foreach (JsonElement item in resultsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw UnexpectedShape("results[]");
            }

            // Tolerant on purpose: an unknown status must pass through, not throw
            // and discard the whole batch response. Known values are listed in
            // BatchRecordStatuses for callers to branch on.
            string status = ReadString(item, "status") ?? throw UnexpectedShape("results[].status");
            string reference = ReadReference(item, "verifiabl_reference");
            results.Add(new BatchRecordResult(
                status,
                reference,
                ReadString(item, "external_id"),
                ReadString(item, "code"),
                ReadString(item, "detail")));
        }

        return new RegisterNonPiiBatchResponse(results.AsReadOnly());
    }

    internal static VerifiablErrorBody? ErrorBodyFromWire(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? error = ReadString(root, "error");
        string? code = ReadString(root, "code");
        if (error is null || code is null)
        {
            return null;
        }

        IReadOnlyList<VerifiablFieldError>? fieldErrors = null;
        if (root.TryGetProperty("field_errors", out JsonElement fieldErrorsElement))
        {
            if (fieldErrorsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var parsed = new List<VerifiablFieldError>(fieldErrorsElement.GetArrayLength());
            foreach (JsonElement item in fieldErrorsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                string? path = ReadString(item, "path");
                string? message = ReadString(item, "message");
                if (path is null || message is null)
                {
                    return null;
                }

                parsed.Add(new VerifiablFieldError(path, message));
            }

            fieldErrors = parsed.AsReadOnly();
        }

        return new VerifiablErrorBody(error, code, ReadString(root, "detail"), fieldErrors);
    }

    internal sealed class TokenResponse
    {
        internal TokenResponse(string accessToken, double expiresInSeconds)
        {
            AccessToken = accessToken;
            ExpiresInSeconds = expiresInSeconds;
        }

        internal string AccessToken { get; }

        internal double ExpiresInSeconds { get; }
    }

    internal static TokenResponse? TokenFromWire(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? accessToken = ReadString(root, "access_token");
        string? tokenType = ReadString(root, "token_type");
        if (accessToken is null
            || accessToken.Length == 0
            // RFC 6749 §7.1: token_type is case-insensitive.
            || !string.Equals(tokenType, "Bearer", StringComparison.OrdinalIgnoreCase)
            || !root.TryGetProperty("expires_in", out JsonElement expiresElement)
            || expiresElement.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        double expiresIn = expiresElement.GetDouble();
        if (double.IsNaN(expiresIn) || double.IsInfinity(expiresIn) || expiresIn <= 0)
        {
            return null;
        }

        return new TokenResponse(accessToken, expiresIn);
    }

    private static string ReadReference(JsonElement obj, string name)
    {
        string? value = ReadString(obj, name);
        if (value is null || !VerifiablReference.IsValid(value))
        {
            throw UnexpectedShape(name);
        }

        return value;
    }

    private static string? ReadString(JsonElement obj, string name)
    {
        if (obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out JsonElement element)
            && element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        return null;
    }

    private static bool TryGetObject(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static FormatException UnexpectedShape(string field) =>
        new($"Verifiabl API response had an unexpected shape (field '{field}').");
}
