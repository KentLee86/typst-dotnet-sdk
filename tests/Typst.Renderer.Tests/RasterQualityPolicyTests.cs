using Typst.Renderer.Core;
using Xunit;

namespace Typst.Renderer.Tests;

public sealed class RasterQualityPolicyTests
{
    [Theory]
    [InlineData(TypstRasterQualityMode.Fixed, 1, 144)]
    [InlineData(TypstRasterQualityMode.Fixed, 8, 144)]
    [InlineData(TypstRasterQualityMode.HighResolution, 1, 288)]
    [InlineData(TypstRasterQualityMode.Automatic, 1, 144)]
    [InlineData(TypstRasterQualityMode.Automatic, 2, 192)]
    [InlineData(TypstRasterQualityMode.Automatic, 3, 288)]
    [InlineData(TypstRasterQualityMode.Automatic, 8, 768)]
    public void ResolvesStableRasterDensity(TypstRasterQualityMode mode, double zoom, float expected) =>
        Assert.Equal(expected, TypstRasterQualityPolicy.ResolvePpi(mode, zoom));
}
