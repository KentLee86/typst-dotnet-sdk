using Cetz.Renderer.WinForms;
using Xunit;

namespace Cetz.Renderer.WinForms.Tests;

public sealed class CetzViewTests
{
    [Fact]
    public void PremultipliedRgbaConversionUsesGdiBgraChannelOrder()
    {
        byte[] rgba = [10, 20, 30, 40, 0, 0, 0, 0, 90, 80, 70, 100];
        var bgra = new byte[rgba.Length];

        CetzBitmapConverter.ConvertPremultipliedRgbaToBgra(rgba, bgra);

        Assert.Equal([30, 20, 10, 40, 0, 0, 0, 0, 70, 80, 90, 100], bgra);
    }

    [Fact]
    public void PageLayoutCombinesRenderPpiDisplayDpiAndZoom()
    {
        var size = CetzViewLayout.GetScaledPageSize(
            pixelWidth: 600,
            pixelHeight: 300,
            pagePpi: 144,
            deviceDpi: 192,
            zoom: 0.75);

        Assert.Equal(600, size.Width);
        Assert.Equal(300, size.Height);
    }

    [Fact]
    public void ZoomIsBoundedAndInvalidSpacingIsRejected()
    {
        using var view = new CetzView { Zoom = 100 };
        Assert.Equal(8, view.Zoom);

        view.Zoom = double.NaN;
        Assert.Equal(1, view.Zoom);
        Assert.Throws<ArgumentOutOfRangeException>(() => view.PageSpacing = -1);
    }
}
