namespace Cetz.Renderer.Core;

/// <summary>
/// Framework-neutral state and layout controller used by every GUI adapter.
/// It is the single authority for zoom, fitting, page navigation, and page placement.
/// </summary>
public sealed class CetzDocumentViewController
{
    public const double MinimumZoom = 0.1d;
    public const double MaximumZoom = 8d;
    public const double DefaultZoom = 1d;
    public const double DefaultPageSpacing = 24d;
    public const double MaximumPageSpacing = 1000d;

    private CetzRenderedDocument? _document;
    private double _customZoom = DefaultZoom;
    private double _zoom = DefaultZoom;
    private double _pageSpacing = DefaultPageSpacing;
    private double _viewportWidth;
    private double _viewportHeight;
    private int _currentPageIndex;
    private CetzZoomMode _zoomMode;
    private CetzPageViewMode _viewMode = CetzPageViewMode.ContinuousSingle;
    private CetzDocumentViewLayout _layout = CetzDocumentViewLayout.Empty;

    public event EventHandler? Changed;

    public CetzRenderedDocument? Document => _document;
    public double Zoom => _zoom;
    public CetzZoomMode ZoomMode => _zoomMode;
    public CetzPageViewMode ViewMode => _viewMode;
    public double PageSpacing => _pageSpacing;
    public int CurrentPageIndex => _currentPageIndex;
    public int PageCount => _document?.Pages.Count ?? 0;
    public CetzDocumentViewLayout Layout => _layout;

    public void SetDocument(CetzRenderedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (ReferenceEquals(_document, document)) return;
        _document = document;
        _currentPageIndex = ClampPage(_currentPageIndex);
        RebuildLayout();
    }

    public void SetZoom(double zoom)
    {
        var normalized = NormalizeZoom(zoom);
        if (_zoomMode == CetzZoomMode.Custom && _customZoom.Equals(normalized)) return;
        _customZoom = normalized;
        _zoomMode = CetzZoomMode.Custom;
        RebuildLayout();
    }

    public void SetZoomMode(CetzZoomMode mode)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual((int)mode, Math.Clamp((int)mode, 0, 2), nameof(mode));
        if (_zoomMode == mode) return;
        _zoomMode = mode;
        RebuildLayout();
    }

    public void SetViewMode(CetzPageViewMode mode)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual((int)mode, Math.Clamp((int)mode, 0, 3), nameof(mode));
        if (_viewMode == mode) return;
        _viewMode = mode;
        _currentPageIndex = SpreadStart(_currentPageIndex);
        RebuildLayout();
    }

    public void SetViewport(double width, double height)
    {
        var normalizedWidth = NormalizeViewport(width);
        var normalizedHeight = NormalizeViewport(height);
        if (_viewportWidth.Equals(normalizedWidth) && _viewportHeight.Equals(normalizedHeight)) return;
        _viewportWidth = normalizedWidth;
        _viewportHeight = normalizedHeight;
        RebuildLayout();
    }

    public void SetPageSpacing(double pageSpacing)
    {
        var normalized = NormalizePageSpacing(pageSpacing);
        if (_pageSpacing.Equals(normalized)) return;
        _pageSpacing = normalized;
        RebuildLayout();
    }

    public void GoToPage(int pageIndex)
    {
        var normalized = SpreadStart(ClampPage(pageIndex));
        if (_currentPageIndex == normalized) return;
        _currentPageIndex = normalized;
        RebuildLayout();
    }

    public bool MoveNext() => MoveBy(IsFacing ? 2 : 1);
    public bool MovePrevious() => MoveBy(IsFacing ? -2 : -1);

    public void ReleaseDocument()
    {
        if (_document is null) return;
        _document = null;
        _currentPageIndex = 0;
        RebuildLayout();
    }

    public static double NormalizeZoom(double value)
        => double.IsFinite(value) ? Math.Clamp(value, MinimumZoom, MaximumZoom) : DefaultZoom;

    public static double NormalizePageSpacing(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0d, MaximumPageSpacing) : DefaultPageSpacing;

    private bool IsFacing => _viewMode is CetzPageViewMode.ContinuousFacing or CetzPageViewMode.FacingPages;

    private bool MoveBy(int delta)
    {
        var target = SpreadStart(ClampPage(_currentPageIndex + delta));
        if (target == _currentPageIndex) return false;
        _currentPageIndex = target;
        RebuildLayout();
        return true;
    }

    private int ClampPage(int pageIndex) => PageCount == 0 ? 0 : Math.Clamp(pageIndex, 0, PageCount - 1);
    private int SpreadStart(int pageIndex) => IsFacing ? pageIndex / 2 * 2 : pageIndex;

    private void RebuildLayout()
    {
        _zoom = ResolveZoom();
        _layout = CetzDocumentViewLayout.Create(_document, _zoom, _pageSpacing, _viewMode, _currentPageIndex);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private double ResolveZoom()
    {
        if (_zoomMode == CetzZoomMode.Custom || _document is null || _viewportWidth <= 0 || _viewportHeight <= 0)
            return _customZoom;

        var target = CetzDocumentViewLayout.MeasureFocus(_document, _pageSpacing, _viewMode, _currentPageIndex);
        if (target.Width <= 0 || target.Height <= 0) return _customZoom;
        var widthZoom = Math.Max(0, _viewportWidth - target.HorizontalSpacing) / target.Width;
        var value = _zoomMode == CetzZoomMode.FitWidth
            ? widthZoom
            : Math.Min(widthZoom, Math.Max(0, _viewportHeight - target.VerticalSpacing) / target.Height);
        return NormalizeZoom(value);
    }

    private static double NormalizeViewport(double value) => double.IsFinite(value) && value > 0 ? value : 0;
}

public sealed class CetzDocumentViewLayout
{
    internal static CetzDocumentViewLayout Empty { get; } = new([], 0, 0, 0, 0);

    private CetzDocumentViewLayout(IReadOnlyList<CetzPageViewLayout> pages, double logicalWidth,
        double logicalHeight, double extentWidth, double extentHeight)
        => (Pages, LogicalWidth, LogicalHeight, ExtentWidth, ExtentHeight) =
            (pages, logicalWidth, logicalHeight, extentWidth, extentHeight);

    public IReadOnlyList<CetzPageViewLayout> Pages { get; }
    public double LogicalWidth { get; }
    public double LogicalHeight { get; }
    public double ExtentWidth { get; }
    public double ExtentHeight { get; }

    internal static CetzDocumentViewLayout Create(CetzRenderedDocument? document, double zoom,
        double spacing, CetzPageViewMode mode, int currentPage)
    {
        if (document is null || document.Pages.Count == 0) return Empty;
        var indices = mode switch
        {
            CetzPageViewMode.SinglePage => [Math.Clamp(currentPage, 0, document.Pages.Count - 1)],
            CetzPageViewMode.FacingPages => Enumerable.Range(currentPage, Math.Min(2, document.Pages.Count - currentPage)).ToArray(),
            _ => Enumerable.Range(0, document.Pages.Count).ToArray()
        };
        var facing = mode is CetzPageViewMode.ContinuousFacing or CetzPageViewMode.FacingPages;
        return facing ? CreateFacing(document, indices, zoom, spacing) : CreateSingle(document, indices, zoom, spacing);
    }

    internal static (double Width, double Height, double HorizontalSpacing, double VerticalSpacing) MeasureFocus(
        CetzRenderedDocument document, double spacing, CetzPageViewMode mode, int currentPage)
    {
        var index = Math.Clamp(currentPage, 0, document.Pages.Count - 1);
        var first = document.Pages[index];
        if (mode is not (CetzPageViewMode.ContinuousFacing or CetzPageViewMode.FacingPages) || index + 1 >= document.Pages.Count)
            return (first.Width, first.Height, 0, 0);
        var second = document.Pages[index + 1];
        return (first.Width + second.Width, Math.Max(first.Height, second.Height), spacing, 0);
    }

    private static CetzDocumentViewLayout CreateSingle(CetzRenderedDocument document, int[] indices, double zoom, double spacing)
    {
        var logicalWidth = indices.Max(i => document.Pages[i].Width);
        var logicalHeight = indices.Sum(i => document.Pages[i].Height);
        var extentWidth = logicalWidth * zoom;
        var layouts = new List<CetzPageViewLayout>(indices.Length);
        var y = 0d;
        foreach (var index in indices)
        {
            var page = document.Pages[index];
            var width = page.Width * zoom;
            var height = page.Height * zoom;
            layouts.Add(new(index, (extentWidth - width) / 2d, y, width, height));
            y += height + spacing;
        }
        var extentHeight = y - (indices.Length > 0 ? spacing : 0);
        return new(layouts, logicalWidth, logicalHeight, extentWidth, extentHeight);
    }

    private static CetzDocumentViewLayout CreateFacing(CetzRenderedDocument document, int[] indices, double zoom, double spacing)
    {
        var rows = indices.Chunk(2).ToArray();
        var logicalWidth = rows.Max(row => row.Sum(i => document.Pages[i].Width));
        var logicalHeight = rows.Sum(row => row.Max(i => document.Pages[i].Height));
        var extentWidth = rows.Max(row => row.Sum(i => document.Pages[i].Width * zoom) + (row.Length - 1) * spacing);
        var layouts = new List<CetzPageViewLayout>(indices.Length);
        var y = 0d;
        foreach (var row in rows)
        {
            var rowWidth = row.Sum(i => document.Pages[i].Width * zoom) + (row.Length - 1) * spacing;
            var rowHeight = row.Max(i => document.Pages[i].Height * zoom);
            var x = (extentWidth - rowWidth) / 2d;
            foreach (var index in row)
            {
                var page = document.Pages[index];
                var width = page.Width * zoom;
                var height = page.Height * zoom;
                layouts.Add(new(index, x, y + (rowHeight - height) / 2d, width, height));
                x += width + spacing;
            }
            y += rowHeight + spacing;
        }
        var extentHeight = y - (rows.Length > 0 ? spacing : 0);
        return new(layouts, logicalWidth, logicalHeight, extentWidth, extentHeight);
    }
}

public readonly record struct CetzPageViewLayout(int PageIndex, double X, double Y, double Width, double Height);
