namespace Verifiabl.Verifier.Extraction.Internal;

/// <summary>How a <see cref="QrImage"/> stores each pixel: the codes of <c>pixelFormats</c> in the extraction module's abi/extraction-abi.json.</summary>
internal enum PixelFormat
{
    /// <summary>1 byte: grey.</summary>
    Gray8 = 0,

    /// <summary>4 bytes: red, green, blue, alpha.</summary>
    Rgba32 = 1,

    /// <summary>4 bytes: blue, green, red, alpha.</summary>
    Bgra32 = 2,
}
