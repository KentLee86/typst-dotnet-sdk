using Cetz.Renderer.Core;
using Xunit;

namespace Cetz.Renderer.Tests;

public sealed class RasterQualityPolicyTests
{
    [Theory]
    [InlineData(CetzRasterQualityMode.Fixed, 1, 144)]
    [InlineData(CetzRasterQualityMode.Fixed, 8, 144)]
    [InlineData(CetzRasterQualityMode.HighResolution, 1, 288)]
    [InlineData(CetzRasterQualityMode.Automatic, 1, 144)]
    [InlineData(CetzRasterQualityMode.Automatic, 2, 192)]
    [InlineData(CetzRasterQualityMode.Automatic, 3, 288)]
    [InlineData(CetzRasterQualityMode.Automatic, 8, 768)]
    public void ResolvesStableRasterDensity(CetzRasterQualityMode mode, double zoom, float expected) =>
        Assert.Equal(expected, CetzRasterQualityPolicy.ResolvePpi(mode, zoom));
}
