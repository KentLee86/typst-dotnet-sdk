using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cetz.Renderer.Core;

namespace Cetz.Renderer.Wpf;

/// <summary>Creates immutable WPF images from renderer pages.</summary>
public static class CetzBitmapSource
{
    /// <summary>
    /// Copies a premultiplied RGBA page into WPF's premultiplied BGRA layout.
    /// The returned bitmap carries the page PPI and is frozen for safe sharing.
    /// </summary>
    public static BitmapSource Create(CetzRenderedPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var destinationStride = checked(page.PixelWidth * 4);
        var destination = new byte[checked(destinationStride * page.PixelHeight)];
        var source = page.Pixels.Span;

        for (var row = 0; row < page.PixelHeight; row++)
        {
            var sourceRow = source.Slice(row * page.Stride, destinationStride);
            var destinationRow = destination.AsSpan(row * destinationStride, destinationStride);
            for (var column = 0; column < destinationStride; column += 4)
            {
                destinationRow[column] = sourceRow[column + 2];
                destinationRow[column + 1] = sourceRow[column + 1];
                destinationRow[column + 2] = sourceRow[column];
                destinationRow[column + 3] = sourceRow[column + 3];
            }
        }

        var bitmap = BitmapSource.Create(
            page.PixelWidth,
            page.PixelHeight,
            page.Ppi,
            page.Ppi,
            PixelFormats.Pbgra32,
            palette: null,
            destination,
            destinationStride);
        bitmap.Freeze();
        return bitmap;
    }
}
