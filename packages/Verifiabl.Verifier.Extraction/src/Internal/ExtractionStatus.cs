namespace Verifiabl.Verifier.Extraction.Internal;

/// <summary>How a read ended: the status codes in the extraction module's abi/extraction-abi.json.</summary>
internal enum ExtractionStatus
{
    /// <summary>The file was read; its result may hold no codes.</summary>
    Ok = 0,

    /// <summary>The image ended early and the rows it had were read.</summary>
    Partial = 1,

    /// <summary>The file is not one the module reads, or is damaged.</summary>
    Unreadable = 2,

    /// <summary>The image has more pixels than allowed.</summary>
    TooManyPixels = 3,

    /// <summary>Reading needed more memory than the instance's cap.</summary>
    OutOfMemory = 4,

    /// <summary>The PDF has too many pages and no metadata payload.</summary>
    TooManyPages = 5,

    /// <summary>The PDF holds more metadata than a payslip needs.</summary>
    TooMuchMetadata = 6,

    /// <summary>The QR codes hold more text than a result may.</summary>
    ResultTooLarge = 7,
}
