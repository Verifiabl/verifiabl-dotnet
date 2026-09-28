namespace Verifiabl;

/// <summary>
/// Structured New Zealand employee address. Country is omitted because NZ2
/// identifies the jurisdiction.
/// </summary>
public sealed class NewZealandAddress
{
    /// <summary>
    /// Address lines before the suburb and city lines, such as a unit and street.
    /// Empty lines are omitted from the encoded display value.
    /// </summary>
    public IReadOnlyList<string>? Lines { get; set; }

    /// <summary>Suburb, when shown.</summary>
    public string? Suburb { get; set; }

    /// <summary>Town or city.</summary>
    public string? City { get; set; }

    /// <summary>Postcode as supplied by the provider.</summary>
    public string? Postcode { get; set; }
}
