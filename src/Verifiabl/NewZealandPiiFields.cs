namespace Verifiabl;

/// <summary>
/// Employee PII for a New Zealand payslip. Every field is optional and is
/// carried only inside the encrypted NZ2 barcode payload.
/// </summary>
public sealed class NewZealandPiiFields
{
    /// <summary>Employee full name as printed on the payslip.</summary>
    public string? EmployeeName { get; set; }

    /// <summary>Employee IRD number as printed, including masking or separators.</summary>
    public string? IrdNumber { get; set; }

    /// <summary>Employee position or job title.</summary>
    public string? Position { get; set; }

    /// <summary>Employee department.</summary>
    public string? Department { get; set; }

    /// <summary>Employer name as printed on the payslip.</summary>
    public string? EmployerName { get; set; }

    /// <summary>Payment account number as printed, including masking or separators.</summary>
    public string? AccountNumber { get; set; }

    /// <summary>Payment account name as printed.</summary>
    public string? AccountName { get; set; }

    /// <summary>Structured New Zealand address collapsed into one NZ2 display field.</summary>
    public NewZealandAddress? Address { get; set; }
}
