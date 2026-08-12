namespace Cetz.Renderer.Uno;

/// <summary>Converts renderer RGBA8 premultiplied pixels to WinUI/Uno BGRA8 premultiplied pixels.</summary>
public static class CetzUnoPixelConverter
{
    /// <summary>
    /// Converts a potentially padded RGBA source to a tightly packed BGRA buffer.
    /// Alpha remains premultiplied; only the red and blue channels are exchanged.
    /// </summary>
    public static byte[] ToBgra8Premultiplied(
        ReadOnlySpan<byte> source,
        int pixelWidth,
        int pixelHeight,
        int sourceStride)
    {
        if (pixelWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (pixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelHeight));

        var rowBytes = checked(pixelWidth * 4);
        if (sourceStride < rowBytes)
            throw new ArgumentOutOfRangeException(nameof(sourceStride));

        var requiredSourceBytes = checked(sourceStride * pixelHeight);
        if (source.Length < requiredSourceBytes)
            throw new ArgumentException("The source buffer is shorter than its dimensions require.", nameof(source));

        var destination = new byte[checked(rowBytes * pixelHeight)];
        for (var row = 0; row < pixelHeight; row++)
        {
            var sourceRow = source.Slice(row * sourceStride, rowBytes);
            var destinationRow = destination.AsSpan(row * rowBytes, rowBytes);
            for (var offset = 0; offset < rowBytes; offset += 4)
            {
                destinationRow[offset] = sourceRow[offset + 2];
                destinationRow[offset + 1] = sourceRow[offset + 1];
                destinationRow[offset + 2] = sourceRow[offset];
                destinationRow[offset + 3] = sourceRow[offset + 3];
            }
        }

        return destination;
    }
}
