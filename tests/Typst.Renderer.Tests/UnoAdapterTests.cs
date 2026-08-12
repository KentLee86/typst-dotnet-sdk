using Typst.Renderer.Uno;
using Typst.Renderer.Core;
using Xunit;

namespace Typst.Renderer.Tests;

public sealed class UnoAdapterTests
{
    [Fact]
    public void ViewImplementsSharedDocumentContract()
        => Assert.True(typeof(ITypstDocumentView).IsAssignableFrom(typeof(TypstView)));

    [Fact]
    public void ConvertsPaddedPremultipliedRgbaRowsToTightlyPackedBgra()
    {
        byte[] source =
        [
            10, 20, 30, 40, 50, 60, 70, 80, 201, 202, 203, 204,
            90, 100, 110, 120, 130, 140, 150, 160, 211, 212, 213, 214
        ];

        var converted = TypstUnoPixelConverter.ToBgra8Premultiplied(source, 2, 2, 12);

        Assert.Equal(
            [30, 20, 10, 40, 70, 60, 50, 80, 110, 100, 90, 120, 150, 140, 130, 160],
            converted);
    }

    [Theory]
    [InlineData(0, 1, 4)]
    [InlineData(1, 0, 4)]
    [InlineData(1, 1, 3)]
    public void RejectsInvalidPixelDimensions(int width, int height, int stride)
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            TypstUnoPixelConverter.ToBgra8Premultiplied([], width, height, stride));

    [Fact]
    public void RejectsShortPixelBuffers()
        => Assert.Throws<ArgumentException>(() =>
            TypstUnoPixelConverter.ToBgra8Premultiplied([1, 2, 3, 4], 2, 1, 8));
}
