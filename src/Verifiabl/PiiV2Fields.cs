namespace Verifiabl;

/// <summary>Employee PII fields carried by the opt-in P2 plaintext writer.</summary>
public sealed class PiiV2Fields
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

    /// <summary>
    /// Optional unstructured address, preserved verbatim. Maximum 320 UTF-8 bytes.
    /// </summary>
    public string? Address { get; set; }
}
