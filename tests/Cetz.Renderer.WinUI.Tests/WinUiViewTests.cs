using Cetz.Renderer.WinUI;
using Xunit;

namespace Cetz.Renderer.WinUI.Tests;

public sealed class WinUiViewTests
{
    [Theory]
    [InlineData(0, 0.1)]
    [InlineData(0.05, 0.1)]
    [InlineData(1.25, 1.25)]
    [InlineData(9, 8)]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    public void ZoomIsFiniteAndBounded(double value, double expected)
        => Assert.Equal(expected, WinUiLayout.NormalizeZoom(value));

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(18.5, 18.5)]
    [InlineData(double.NaN, 24)]
    public void PageSpacingIsFiniteAndNonNegative(double value, double expected)
        => Assert.Equal(expected, WinUiLayout.NormalizeSpacing(value));

    [Fact]
    public void PremultipliedRgbaIsReorderedToWinUiBgraWithoutChangingAlpha()
    {
        byte[] source = [10, 20, 30, 40, 200, 150, 100, 250];
        var destination = new byte[source.Length];

        WinUiPixelBuffer.ConvertRgbaRowToBgra(source, destination);

        Assert.Equal(new byte[] { 30, 20, 10, 40, 100, 150, 200, 250 }, destination);
    }

    [Fact]
    public void PixelWriterCopiesEveryRowAndIgnoresSourceStridePadding()
    {
        byte[] source =
        [
            1, 2, 3, 4, 5, 6, 7, 8, 99, 98, 97, 96,
            9, 10, 11, 12, 13, 14, 15, 16, 95, 94, 93, 92
        ];
        using var destination = new MemoryStream();

        WinUiPixelBuffer.WriteBgraPremultiplied(source, 2, 2, 12, destination);

        Assert.Equal(
            new byte[] { 3, 2, 1, 4, 7, 6, 5, 8, 11, 10, 9, 12, 15, 14, 13, 16 },
            destination.ToArray());
    }
}
