using Cetz.Renderer.Core;

namespace Cetz.Renderer.Uno;

/// <summary>Framework-neutral layout calculations used by <see cref="CetzView"/>.</summary>
public static class CetzUnoLayout
{
    public const double MinimumZoom = 0.1;
    public const double MaximumZoom = 8;
    public const double MaximumPageSpacing = 1024;

    public static double NormalizeZoom(double value)
        => double.IsFinite(value) ? Math.Clamp(value, MinimumZoom, MaximumZoom) : 1;

    public static double NormalizePageSpacing(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0, MaximumPageSpacing) : 24;

    public static (double Width, double Height) GetPageSize(CetzRenderedPage page, double zoom)
    {
        ArgumentNullException.ThrowIfNull(page);
        var normalizedZoom = NormalizeZoom(zoom);
        return (page.Width * normalizedZoom, page.Height * normalizedZoom);
    }
}
