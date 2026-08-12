namespace Cetz.Renderer.Core;

/// <summary>
/// Framework-neutral mouse/touchpad interaction state for a document viewport.
/// GUI hosts provide pointer positions and scroll offsets, then apply the returned
/// offsets with their native scrolling API.
/// </summary>
public sealed class CetzViewportInteractionController
{
    public const double DefaultWheelZoomStep = 0.25d;

    private readonly ICetzDocumentView _view;
    private CetzViewportPoint _panStart;
    private CetzViewportOffset _offsetStart;

    public CetzViewportInteractionController(ICetzDocumentView view)
        => _view = view ?? throw new ArgumentNullException(nameof(view));

    public bool IsPanning { get; private set; }

    public void BeginPan(double pointerX, double pointerY, double horizontalOffset, double verticalOffset)
    {
        _panStart = new CetzViewportPoint(NormalizeCoordinate(pointerX), NormalizeCoordinate(pointerY));
        _offsetStart = new CetzViewportOffset(NormalizeOffset(horizontalOffset), NormalizeOffset(verticalOffset));
        IsPanning = true;
    }

    public bool TryPanTo(double pointerX, double pointerY, out CetzViewportOffset offset)
    {
        if (!IsPanning)
        {
            offset = default;
            return false;
        }

        var point = new CetzViewportPoint(NormalizeCoordinate(pointerX), NormalizeCoordinate(pointerY));
        offset = new CetzViewportOffset(
            Math.Max(0, _offsetStart.X - (point.X - _panStart.X)),
            Math.Max(0, _offsetStart.Y - (point.Y - _panStart.Y)));
        return true;
    }

    public void EndPan() => IsPanning = false;

    public CetzViewportOffset ZoomByWheel(
        double wheelDelta,
        double pointerX,
        double pointerY,
        double horizontalOffset,
        double verticalOffset,
        double step = DefaultWheelZoomStep)
    {
        if (!double.IsFinite(step) || step <= 0)
            throw new ArgumentOutOfRangeException(nameof(step), "Zoom step must be finite and positive.");

        var oldZoom = _view.Zoom;
        var oldLayout = _view.Layout;
        var oldOffset = new CetzViewportOffset(
            NormalizeOffset(horizontalOffset),
            NormalizeOffset(verticalOffset));
        if (!double.IsFinite(wheelDelta) || wheelDelta == 0)
            return oldOffset;

        _view.SetZoom(oldZoom + (wheelDelta > 0 ? step : -step));
        var newLayout = _view.Layout;
        return new CetzViewportOffset(
            AnchorAxis(oldOffset.X, pointerX, oldLayout.ExtentWidth, newLayout.ExtentWidth),
            AnchorAxis(oldOffset.Y, pointerY, oldLayout.ExtentHeight, newLayout.ExtentHeight));
    }

    private static double AnchorAxis(double offset, double pointer, double oldExtent, double newExtent)
    {
        pointer = NormalizeCoordinate(pointer);
        if (!double.IsFinite(oldExtent) || !double.IsFinite(newExtent) || oldExtent <= 0 || newExtent <= 0)
            return offset;
        var ratio = newExtent / oldExtent;
        return Math.Max(0, (offset + pointer) * ratio - pointer);
    }

    private static double NormalizeOffset(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
    private static double NormalizeCoordinate(double value) => double.IsFinite(value) ? value : 0;
}

public readonly record struct CetzViewportPoint(double X, double Y);
public readonly record struct CetzViewportOffset(double X, double Y);

/// <summary>
/// A pointer-relative position captured from a native scrolling extent before
/// zoom. Resolve it after the platform has completed its new layout pass.
/// </summary>
public readonly record struct CetzViewportAnchor(double RelativeX, double RelativeY, double PointerX, double PointerY)
{
    public static CetzViewportAnchor Capture(
        double pointerX,
        double pointerY,
        double horizontalOffset,
        double verticalOffset,
        double extentWidth,
        double extentHeight,
        double contentOriginX = 0,
        double contentOriginY = 0)
    {
        pointerX = NormalizeCoordinate(pointerX);
        pointerY = NormalizeCoordinate(pointerY);
        horizontalOffset = NormalizeOffset(horizontalOffset);
        verticalOffset = NormalizeOffset(verticalOffset);
        contentOriginX = NormalizeCoordinate(contentOriginX);
        contentOriginY = NormalizeCoordinate(contentOriginY);
        return new CetzViewportAnchor(
            (horizontalOffset + pointerX - contentOriginX) / NormalizeExtent(extentWidth),
            (verticalOffset + pointerY - contentOriginY) / NormalizeExtent(extentHeight),
            pointerX,
            pointerY);
    }

    public CetzViewportOffset Resolve(
        double extentWidth,
        double extentHeight,
        double contentOriginX = 0,
        double contentOriginY = 0) => new(
        Math.Max(0, NormalizeCoordinate(contentOriginX) + RelativeX * NormalizeExtent(extentWidth) - PointerX),
        Math.Max(0, NormalizeCoordinate(contentOriginY) + RelativeY * NormalizeExtent(extentHeight) - PointerY));

    private static double NormalizeExtent(double value) => double.IsFinite(value) && value > 0 ? value : 1;
    private static double NormalizeOffset(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
    private static double NormalizeCoordinate(double value) => double.IsFinite(value) ? value : 0;
}

/// <summary>Keeps the same point on the nearest rendered page beneath the pointer while zooming.</summary>
public readonly record struct CetzDocumentAnchor(
    int PageIndex,
    double RelativePageX,
    double RelativePageY,
    double PointerX,
    double PointerY)
{
    public static CetzDocumentAnchor Capture(
        CetzDocumentViewLayout layout,
        double pointerX,
        double pointerY,
        double horizontalOffset,
        double verticalOffset,
        double contentOriginX = 0,
        double contentOriginY = 0)
    {
        ArgumentNullException.ThrowIfNull(layout);
        pointerX = Normalize(pointerX);
        pointerY = Normalize(pointerY);
        var contentX = Normalize(horizontalOffset) + pointerX - NormalizeSigned(contentOriginX);
        var contentY = Normalize(verticalOffset) + pointerY - NormalizeSigned(contentOriginY);
        if (layout.Pages.Count == 0)
            return new(-1, 0, 0, pointerX, pointerY);

        var page = layout.Pages.MinBy(candidate => DistanceSquared(candidate, contentX, contentY));
        return new(
            page.PageIndex,
            (contentX - page.X) / Math.Max(1, page.Width),
            (contentY - page.Y) / Math.Max(1, page.Height),
            pointerX,
            pointerY);
    }

    public CetzViewportOffset Resolve(
        CetzDocumentViewLayout layout,
        double contentOriginX = 0,
        double contentOriginY = 0)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var pageIndex = PageIndex;
        var page = layout.Pages.FirstOrDefault(candidate => candidate.PageIndex == pageIndex);
        if (page.Width <= 0 || page.Height <= 0)
            return default;
        return new(
            Math.Max(0, NormalizeSigned(contentOriginX) + page.X + RelativePageX * page.Width - PointerX),
            Math.Max(0, NormalizeSigned(contentOriginY) + page.Y + RelativePageY * page.Height - PointerY));
    }

    private static double DistanceSquared(CetzPageViewLayout page, double x, double y)
    {
        var nearestX = Math.Clamp(x, page.X, page.X + page.Width);
        var nearestY = Math.Clamp(y, page.Y, page.Y + page.Height);
        return Math.Pow(x - nearestX, 2) + Math.Pow(y - nearestY, 2);
    }

    private static double Normalize(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
    private static double NormalizeSigned(double value) => double.IsFinite(value) ? value : 0;
}
