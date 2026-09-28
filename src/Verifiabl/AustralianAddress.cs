namespace Verifiabl;

/// <summary>
/// Structured Australian employee address. Country is omitted because AU2
/// identifies the jurisdiction.
/// </summary>
public sealed class AustralianAddress
{
    /// <summary>
    /// Address lines before the locality line, such as a unit and street.
    /// Empty lines are omitted from the encoded display value.
    /// </summary>
    public IReadOnlyList<string>? Lines { get; set; }

    /// <summary>Suburb or locality.</summary>
    public string? Suburb { get; set; }

    /// <summary>State or territory as supplied by the provider.</summary>
    public string? StateOrTerritory { get; set; }

    /// <summary>Postcode as supplied by the provider.</summary>
    public string? Postcode { get; set; }
}
