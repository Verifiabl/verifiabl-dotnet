namespace Verifiabl;

/// <summary>
/// Employee PII fields carried in the encrypted barcode payload. All fields are
/// optional; omitted fields are encoded as empty segments and skipped by Verifiabl.
/// </summary>
/// <remarks>
/// Values must not contain the pipe character <c>|</c> (the wire delimiter) or
/// control characters, and each is limited to 256 characters.
/// </remarks>
public sealed class PiiFields
{
    /// <summary>Employee full name as printed on the payslip.</summary>
    public string? EmployeeName { get; set; }

    /// <summary>Employee position or job title.</summary>
    public string? Position { get; set; }

    /// <summary>Employee department.</summary>
    public string? Department { get; set; }

    /// <summary>Employer Australian Business Number.</summary>
    public string? EmployerAbn { get; set; }

    /// <summary>Bank State Branch code of the payment account.</summary>
    public string? Bsb { get; set; }

    /// <summary>Payment account number.</summary>
    public string? AccountNumber { get; set; }

    /// <summary>Payment account name.</summary>
    public string? AccountName { get; set; }
}
