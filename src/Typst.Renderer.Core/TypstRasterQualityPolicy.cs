namespace Typst.Renderer.Core;

public enum TypstRasterQualityMode
{
    Fixed = 0,
    HighResolution = 1,
    Automatic = 2
}

/// <summary>Selects raster density independently from logical document zoom.</summary>
public static class TypstRasterQualityPolicy
{
    public const float FixedPpi = 144;
    public const float HighResolutionPpi = 288;
    public const float MaximumAutomaticPpi = 768;

    private static readonly float[] AutomaticSteps = [144, 192, 288, 384, 576, 768];

    public static float ResolvePpi(TypstRasterQualityMode mode, double zoom)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual((int)mode, Math.Clamp((int)mode, 0, 2), nameof(mode));
        zoom = TypstDocumentViewController.NormalizeZoom(zoom);
        return mode switch
        {
            TypstRasterQualityMode.Fixed => FixedPpi,
            TypstRasterQualityMode.HighResolution => HighResolutionPpi,
            _ => AutomaticSteps.First(step => step >= Math.Min(MaximumAutomaticPpi, 96 * zoom))
        };
    }
}
