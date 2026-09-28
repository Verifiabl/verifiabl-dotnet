using System.Globalization;
using System.Text.RegularExpressions;

namespace Verifiabl.Client;

/// <summary>
/// A v2 amount, rate, or quantity with its machine-readable decimal value and
/// optional printed representation.
/// </summary>
public sealed class PayslipNumber
{
    // \z, not $: .NET's $ also matches before a trailing "\n", which the API rejects.
    private static readonly Regex DecimalValue = new(
        "\\A-?[0-9]+(?:\\.[0-9]+)?\\z",
        RegexOptions.CultureInvariant);

    /// <summary>Create a number from an exact decimal string.</summary>
    /// <param name="value">
    /// Plain decimal value without symbols, separators, an exponent, or a leading plus sign.
    /// Its scale is preserved exactly.
    /// </param>
    /// <param name="display">Printed form when it differs from <paramref name="value"/>.</param>
    public PayslipNumber(string value, string? display = null)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (!DecimalValue.IsMatch(value))
        {
            throw new ArgumentException(
                "value must be a plain decimal number, for example 1234.56.",
                nameof(value));
        }

        if (display is not null && display.Length == 0)
        {
            throw new ArgumentException("display must not be empty when supplied.", nameof(display));
        }

        Value = value;
        Display = display;
    }

    /// <summary>
    /// Create a number from a .NET decimal. The invariant representation,
    /// including the decimal value's scale, is sent on the wire.
    /// </summary>
    public PayslipNumber(decimal value, string? display = null)
        : this(value.ToString(CultureInfo.InvariantCulture), display)
    {
    }

    /// <summary>Exact machine-readable decimal string sent as <c>value</c>.</summary>
    public string Value { get; }

    /// <summary>Printed form sent as <c>display</c>, or <see langword="null"/> when omitted.</summary>
    public string? Display { get; }
}
