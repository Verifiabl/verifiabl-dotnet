namespace Verifiabl;

/// <summary>
/// Employee PII fields carried in the encrypted barcode payload. All fields are
/// optional; omitted fields are encoded as empty segments and skipped by Verifiabl.
/// </summary>
/// <remarks>
/// P2 values must contain valid Unicode and must not contain the pipe character
/// <c>|</c> (the wire delimiter) or Unicode General Categories Cc (control), Cf
/// (format), Zl (line separator), or Zp (paragraph separator). The complete P2
/// plaintext, including framing and delimiters, is limited to 1024 UTF-8 bytes
/// when written. Values are preserved without Unicode normalization.
/// </remarks>
public class PiiFields
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
