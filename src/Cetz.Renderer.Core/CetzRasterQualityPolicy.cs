namespace Cetz.Renderer.Core;

public enum CetzRasterQualityMode
{
    Fixed = 0,
    HighResolution = 1,
    Automatic = 2
}

/// <summary>Selects raster density independently from logical document zoom.</summary>
public static class CetzRasterQualityPolicy
{
    public const float FixedPpi = 144;
    public const float HighResolutionPpi = 288;
    public const float MaximumAutomaticPpi = 768;

    private static readonly float[] AutomaticSteps = [144, 192, 288, 384, 576, 768];

    public static float ResolvePpi(CetzRasterQualityMode mode, double zoom)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual((int)mode, Math.Clamp((int)mode, 0, 2), nameof(mode));
        zoom = CetzDocumentViewController.NormalizeZoom(zoom);
        return mode switch
        {
            CetzRasterQualityMode.Fixed => FixedPpi,
            CetzRasterQualityMode.HighResolution => HighResolutionPpi,
            _ => AutomaticSteps.First(step => step >= Math.Min(MaximumAutomaticPpi, 96 * zoom))
        };
    }
}
