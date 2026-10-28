namespace Verifiabl;

/// <summary>
/// Employee PII for an Australian payslip. Every field is optional and is
/// carried only inside the encrypted AU2 barcode payload.
/// </summary>
public sealed class AustralianPiiFields
{
    /// <summary>Employee full name as printed on the payslip.</summary>
    public string? EmployeeName { get; set; }

    /// <summary>Employee position or job title.</summary>
    public string? Position { get; set; }

    /// <summary>Employee department.</summary>
    public string? Department { get; set; }

    /// <summary>
    /// Employer name as printed on the payslip. Used when <see cref="EmployerAbn"/>
    /// is absent.
    /// </summary>
    public string? EmployerName { get; set; }

    /// <summary>
    /// Employer ABN as printed on the payslip. When supplied, this is encoded
    /// instead of <see cref="EmployerName"/>.
    /// </summary>
    public string? EmployerAbn { get; set; }

    /// <summary>Bank State Branch code as printed, including masking or separators.</summary>
    public string? Bsb { get; set; }

    /// <summary>Payment account number as printed, including masking or separators.</summary>
    public string? AccountNumber { get; set; }

    /// <summary>Payment account name as printed.</summary>
    public string? AccountName { get; set; }

    /// <summary>Structured Australian address collapsed into one AU2 display field.</summary>
    public AustralianAddress? Address { get; set; }
}
