namespace Typst.Renderer.Core;

/// <summary>Selects visible layout pages plus a bounded sequential overscan window.</summary>
public static class TypstVisiblePageSelector
{
    public static int? SelectCurrentPage(
        TypstDocumentViewLayout layout,
        double viewportX,
        double viewportY,
        double viewportWidth,
        double viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!IsFinitePositive(viewportWidth) || !IsFinitePositive(viewportHeight) ||
            !double.IsFinite(viewportX) || !double.IsFinite(viewportY))
            return null;

        var viewportRight = viewportX + viewportWidth;
        var viewportBottom = viewportY + viewportHeight;
        var viewportCenterX = viewportX + viewportWidth / 2;
        var viewportCenterY = viewportY + viewportHeight / 2;
        TypstPageViewLayout? best = null;
        var bestArea = 0d;
        var bestDistance = double.PositiveInfinity;
        foreach (var page in layout.Pages)
        {
            var width = Math.Max(0, Math.Min(page.X + page.Width, viewportRight) - Math.Max(page.X, viewportX));
            var height = Math.Max(0, Math.Min(page.Y + page.Height, viewportBottom) - Math.Max(page.Y, viewportY));
            var area = width * height;
            if (area <= 0) continue;
            var distance = Math.Pow(page.X + page.Width / 2 - viewportCenterX, 2) +
                Math.Pow(page.Y + page.Height / 2 - viewportCenterY, 2);
            if (area > bestArea || (area.Equals(bestArea) && distance < bestDistance))
            {
                best = page;
                bestArea = area;
                bestDistance = distance;
            }
        }
        return best?.PageIndex;
    }

    public static IReadOnlyList<int> Select(
        TypstDocumentViewLayout layout,
        double viewportX,
        double viewportY,
        double viewportWidth,
        double viewportHeight,
        int overscanPages = 1)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentOutOfRangeException.ThrowIfNegative(overscanPages);
        if (!IsFinitePositive(viewportWidth) || !IsFinitePositive(viewportHeight) ||
            !double.IsFinite(viewportX) || !double.IsFinite(viewportY) || layout.Pages.Count == 0)
            return [];

        var right = viewportX + viewportWidth;
        var bottom = viewportY + viewportHeight;
        var first = -1;
        var last = -1;
        for (var index = 0; index < layout.Pages.Count; index++)
        {
            var page = layout.Pages[index];
            if (page.X + page.Width <= viewportX || page.X >= right ||
                page.Y + page.Height <= viewportY || page.Y >= bottom)
                continue;
            if (first < 0) first = index;
            last = index;
        }

        if (first < 0) return [];
        first = Math.Max(0, first - overscanPages);
        last = Math.Min(layout.Pages.Count - 1, last + overscanPages);
        return layout.Pages.Skip(first).Take(last - first + 1).Select(page => page.PageIndex).ToArray();
    }

    private static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0;
}
