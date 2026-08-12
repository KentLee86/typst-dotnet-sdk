using Cetz.Renderer.Core;

namespace Cetz.Renderer.WinUI;

internal static class WinUiPixelBuffer
{
    public static void WriteBgraPremultiplied(CetzRenderedPage page, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));

        WriteBgraPremultiplied(
            page.Pixels.Span,
            page.PixelWidth,
            page.PixelHeight,
            page.Stride,
            destination);
    }

    public static void WriteBgraPremultiplied(
        ReadOnlySpan<byte> source,
        int pixelWidth,
        int pixelHeight,
        int stride,
        Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        if (pixelWidth <= 0 || pixelHeight <= 0 || stride < checked(pixelWidth * 4))
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "The pixel dimensions and stride must describe RGBA8 rows.");
        if (source.Length != checked(stride * pixelHeight))
            throw new ArgumentException("The source length does not match its dimensions and stride.", nameof(source));

        var row = new byte[checked(pixelWidth * 4)];
        for (var y = 0; y < pixelHeight; y++)
        {
            var sourceRow = source.Slice(y * stride, row.Length);
            ConvertRgbaRowToBgra(sourceRow, row);
            destination.Write(row);
        }
    }

    public static void ConvertRgbaRowToBgra(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (source.Length % 4 != 0)
            throw new ArgumentException("The RGBA row length must be a multiple of four.", nameof(source));
        if (destination.Length < source.Length)
            throw new ArgumentException("The destination row is too small.", nameof(destination));

        for (var x = 0; x < source.Length; x += 4)
        {
            destination[x] = source[x + 2];
            destination[x + 1] = source[x + 1];
            destination[x + 2] = source[x];
            destination[x + 3] = source[x + 3];
        }
    }
}
