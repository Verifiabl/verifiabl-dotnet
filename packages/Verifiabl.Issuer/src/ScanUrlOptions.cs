namespace Verifiabl;

/// <summary>Options for <see cref="VerifiablBarcode.BuildScanUrl"/>.</summary>
public sealed class ScanUrlOptions
{
    /// <summary>API environment for the public QR scan URL. Defaults to production.</summary>
    public VerifiablEnvironment Environment { get; set; } = VerifiablEnvironment.Production;

    /// <summary>
    /// Advanced override for the public QR scan URL origin. Defaults to the selected
    /// environment's scan URL origin. Must use https. This URL is printed on payslip
    /// documents and cannot be changed after issuance.
    /// </summary>
    public Uri? ScanBaseUrl { get; set; }
}
