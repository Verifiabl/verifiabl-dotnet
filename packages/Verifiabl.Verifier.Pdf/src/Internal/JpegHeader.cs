namespace Verifiabl.Verifier.Pdf.Internal;

/// <summary>Reads a JPEG file's frame size from its header without decoding it.</summary>
internal static class JpegHeader
{
    /// <summary>The width and height in the JPEG's start-of-frame segment, or null.</summary>
    internal static (int Width, int Height)? Dimensions(byte[] jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return null;
        }

        int offset = 2;
        while (offset + 4 <= jpeg.Length)
        {
            if (jpeg[offset] != 0xFF)
            {
                return null;
            }

            byte marker = jpeg[offset + 1];
            if (marker == 0xFF)
            {
                // Fill byte before a marker.
                offset++;
                continue;
            }

            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD8)
            {
                // Markers without a length.
                offset += 2;
                continue;
            }

            int length = (jpeg[offset + 2] << 8) | jpeg[offset + 3];
            if (length < 2 || marker == 0xDA)
            {
                // A scan before any frame header: not a JPEG we can size.
                return null;
            }

            if (IsStartOfFrame(marker))
            {
                if (offset + 9 > jpeg.Length)
                {
                    return null;
                }

                int height = (jpeg[offset + 5] << 8) | jpeg[offset + 6];
                int width = (jpeg[offset + 7] << 8) | jpeg[offset + 8];
                return (width, height);
            }

            offset += 2 + length;
        }

        return null;
    }

    /// <summary>SOF0 to SOF15, except DHT (C4), JPG (C8) and DAC (CC), which share the range.</summary>
    private static bool IsStartOfFrame(byte marker) =>
        marker is >= 0xC0 and <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
}
