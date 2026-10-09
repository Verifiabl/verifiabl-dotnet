using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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

    private static readonly JsonSerializerOptions TypedV2PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

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
        body["encrypted_pii"] = Base64Url.Encode(Validation.ValidateCiphertext(
            request.EncryptedPii,
            "request.EncryptedPii"));
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

    internal static (JsonObject Body, IReadOnlyList<int> SentIndices, IReadOnlyDictionary<int, BatchRecordResult> Errors)
        PrepareBatch(IReadOnlyList<BatchRecord> records)
    {
        if (records.Count is < 1 or > MaxBatchRecords)
        {
            throw new ArgumentException($"records must contain between 1 and {MaxBatchRecords} records.", nameof(records));
        }

        var sent = new List<int>();
        var errors = new Dictionary<int, BatchRecordResult>();
        var wire = new JsonArray();
        for (int index = 0; index < records.Count; index++)
        {
            BatchRecord record = records[index]
                ?? throw new ArgumentException($"records[{index}] must not be null.", nameof(records));
            string label = $"records[{index}]";
            string reference = VerifiablReference.Validate(record.VerifiablReference, $"{label}.VerifiablReference");
            string selectedSchema = Validation.ValidateSchema(record.Schema, $"{label}.Schema");
            if (record.IssuedAt == default)
            {
                throw new ArgumentException($"{label}.IssuedAt is required.", nameof(records));
            }
            Validation.ValidateEncryptionMetadata(record.EncryptionMetadata, $"{label}.EncryptionMetadata");
            if (record.ExternalId is not null)
            {
                Validation.ValidateExternalId(record.ExternalId, $"{label}.ExternalId");
            }
            // A payload built for another schema is a coding error, so it throws as the
            // single-record methods do. Only payslip data errors become per-record results.
            if (record.PayslipNonPii is null)
            {
                throw new ArgumentException($"{label}.PayslipNonPii is required.", nameof(records));
            }
            RequirePayloadForSchema(record.PayslipNonPii, selectedSchema, $"{label}.PayslipNonPii", nameof(records));

            try
            {
                JsonObject body = RegistrationFields(label, reference, selectedSchema,
                    record.IssuedAt, record.PayslipNonPii, record.EncryptionMetadata);
                if (record.ExternalId is not null)
                {
                    body["external_id"] = record.ExternalId;
                }
                wire.Add(body);
                sent.Add(index);
            }
            catch (ArgumentException exception)
            {
                errors[index] = LocalError(record, exception.Message);
            }
        }
        return (new JsonObject { ["records"] = wire }, sent, errors);
    }

    private static BatchRecordResult LocalError(BatchRecord record, string detail) =>
        new(BatchRecordStatuses.Error, record.VerifiablReference, record.ExternalId,
            VerifiablErrorCodes.ValidationFailed, detail);

    private static JsonObject RegistrationFields(
        string label,
        string? verifiablReference,
        string? schema,
        DateTimeOffset issuedAt,
        PayslipNonPii? payslipNonPii,
        EncryptionMetadata? encryptionMetadata)
    {
        string validatedSchema = Validation.ValidateSchema(schema, $"{label}.Schema");
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

        body["schema"] = validatedSchema;
        // Millisecond-precision UTC, matching JavaScript's Date.toISOString(): the
        // API accepts arbitrary sub-second precision, but this keeps the wire value
        // identical to the Node SDK's.
        body["issued_at"] = issuedAt.ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        body["payslip_non_pii"] = PayslipNonPiiFields(
            payslipNonPii,
            validatedSchema,
            $"{label}.PayslipNonPii");
        body["encryption_metadata"] = new JsonObject
        {
            ["iv"] = Base64Url.Encode(encryptionMetadata!.Iv),
            ["tag"] = Base64Url.Encode(encryptionMetadata.Tag),
        };
        return body;
    }

    private static JsonObject PayslipNonPiiFields(
        PayslipNonPii data,
        string schema,
        string label)
    {
        RequirePayloadForSchema(data, schema, label, label);
        if (data.TypedV2Payload is not null)
        {
            string? currency = data.TypedV2Payload switch
            {
                AustralianPayslipV2 australian => australian.Currency,
                NewZealandPayslipV2 newZealand => newZealand.Currency,
                _ => null,
            };
            if (currency is null || !PayslipCurrencies.All.Contains(currency))
            {
                throw new ArgumentException(
                    $"{label}.Currency must be a supported ISO 4217 currency code, for example AUD.",
                    $"{label}.Currency");
            }
#if NET6_0_OR_GREATER
            // `required DateOnly` accepts an explicit `default`, which would serialize as year 0001.
            foreach ((DateOnly? date, string field) in TypedV2Dates(data.TypedV2Payload))
            {
                if (date == default(DateOnly))
                {
                    throw new ArgumentException($"{label}.{field} is required.", $"{label}.{field}");
                }
            }
#endif

            return JsonSerializer.SerializeToNode(data.TypedV2Payload, data.TypedV2Payload.GetType(),
                TypedV2PayloadOptions)!.AsObject();
        }
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

                body[field.Key] = FreeFormValues.ToJsonNode(field.Value);
            }
        }

        body["period_start"] = data.PeriodStart;
        body["period_end"] = data.PeriodEnd;
        return body;
    }

    private static void RequirePayloadForSchema(PayslipNonPii data, string schema, string label, string paramName)
    {
        if (data.TypedV2Schema is { } typedSchema && schema != typedSchema)
        {
            throw new ArgumentException($"{label} was created for {typedSchema}, not {schema}.", paramName);
        }
        if (data.TypedV2Payload is null && schema is PayslipSchemas.AustralianV2 or PayslipSchemas.NewZealandV2)
        {
            throw new ArgumentException($"{label} requires PayslipNonPii.FromAustralianV2 or FromNewZealandV2.", paramName);
        }
    }

#if NET6_0_OR_GREATER
    private static (DateOnly? Date, string Field)[] TypedV2Dates(object payload) => payload switch
    {
        AustralianPayslipV2 australian =>
            [(australian.PeriodStart, "PeriodStart"), (australian.PeriodEnd, "PeriodEnd"), (australian.PaymentDate, "PaymentDate")],
        NewZealandPayslipV2 newZealand =>
            [(newZealand.PeriodStart, "PeriodStart"), (newZealand.PeriodEnd, "PeriodEnd"), (newZealand.PaymentDate, "PaymentDate")],
        _ => [],
    };
#endif

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
