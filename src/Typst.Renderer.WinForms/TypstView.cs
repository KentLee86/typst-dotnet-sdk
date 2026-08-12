using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Typst.Renderer.Core;

namespace Typst.Renderer.WinForms;

/// <summary>
/// WinForms adapter for the common CeTZ document-view contract. The common
/// controller owns all document, zoom, navigation, and page-layout state; this
/// control owns only WinForms bitmap and scrolling resources.
/// </summary>
public sealed class TypstView : ScrollableControl, ITypstDocumentView
{
    private readonly Dictionary<int, Bitmap> _bitmaps = [];
    private readonly TypstDocumentViewController _controller = new();
    private bool _disposed;

    public TypstView()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        AutoScroll = true;
        BackColor = Color.FromArgb(221, 228, 238);
        _controller.Changed += Controller_Changed;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TypstRenderedDocument? Document
    {
        get => _controller.Document;
        set
        {
            if (value is null) ReleaseDocument();
            else SetDocument(value);
        }
    }

    [DefaultValue(TypstDocumentViewController.DefaultZoom)]
    public double Zoom
    {
        get => _controller.Zoom;
        set => SetZoom(value);
    }

    [DefaultValue(TypstZoomMode.Custom)]
    public TypstZoomMode ZoomMode
    {
        get => _controller.ZoomMode;
        set => SetZoomMode(value);
    }

    [DefaultValue(TypstPageViewMode.ContinuousSingle)]
    public TypstPageViewMode ViewMode
    {
        get => _controller.ViewMode;
        set => SetViewMode(value);
    }

    [DefaultValue(TypstDocumentViewController.DefaultPageSpacing)]
    public double PageSpacing
    {
        get => _controller.PageSpacing;
        set => SetPageSpacing(value);
    }

    [Browsable(false)]
    public int CurrentPageIndex => _controller.CurrentPageIndex;

    [Browsable(false)]
    public int PageCount => _controller.PageCount;

    [Browsable(false)]
    public new TypstDocumentViewLayout Layout => _controller.Layout;

    internal int BitmapCount => _bitmaps.Count;
    public int RealizedPageCount => _bitmaps.Count;
    public IReadOnlyCollection<int> RealizedPageIndices => _bitmaps.Keys.Order().ToArray();

    public event EventHandler? CurrentPageChanged;

    public void SetDocument(TypstRenderedDocument document)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(document);
        if (ReferenceEquals(_controller.Document, document)) return;

        DisposeBitmaps(_bitmaps.Values);
        _bitmaps.Clear();
        _controller.SetDocument(document);
        RefreshBitmaps();
    }

    public void SetZoom(double zoom)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _controller.SetZoom(zoom);
    }

    public void SetZoomMode(TypstZoomMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _controller.SetZoomMode(mode);
    }

    public void SetViewMode(TypstPageViewMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _controller.SetViewMode(mode);
        ScrollCurrentPageIntoView();
    }

    public void SetViewport(double width, double height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _controller.SetViewport(width, height);
    }

    public void SetPageSpacing(double pageSpacing)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _controller.SetPageSpacing(pageSpacing);
    }

    public void GoToPage(int pageIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _controller.GoToPage(pageIndex);
        ScrollCurrentPageIntoView();
    }

    public bool TrackCurrentPage(int pageIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _controller.TrackCurrentPage(pageIndex);
    }

    public bool MoveNext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var moved = _controller.MoveNext();
        if (moved) ScrollCurrentPageIntoView();
        return moved;
    }

    public bool MovePrevious()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var moved = _controller.MovePrevious();
        if (moved) ScrollCurrentPageIntoView();
        return moved;
    }

    public void ReleaseDocument()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DisposeBitmaps(_bitmaps.Values);
        _bitmaps.Clear();
        _controller.ReleaseDocument();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateControllerViewport();
        RefreshBitmaps();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateControllerViewport();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        RefreshBitmaps();
        TrackCurrentPageFromViewport();
    }

    protected override void RescaleConstantsForDpi(int deviceDpiOld, int deviceDpiNew)
    {
        base.RescaleConstantsForDpi(deviceDpiOld, deviceDpiNew);
        UpdateControllerViewport(deviceDpiNew);
    }

    protected override void OnPaddingChanged(EventArgs e)
    {
        base.OnPaddingChanged(e);
        UpdateControllerViewport();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var layout = _controller.Layout;
        if (_controller.Document is null) return;

        e.Graphics.CompositingMode = CompositingMode.SourceOver;
        e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var scale = EffectiveDpi / 96d;
        var scroll = AutoScrollPosition;
        foreach (var pageLayout in layout.Pages)
        {
            if (!_bitmaps.TryGetValue(pageLayout.PageIndex, out var bitmap)) continue;
            var destination = new RectangleF(
                (float)(Padding.Left + scroll.X + pageLayout.X * scale),
                (float)(Padding.Top + scroll.Y + pageLayout.Y * scale),
                (float)(pageLayout.Width * scale),
                (float)(pageLayout.Height * scale));
            if (destination.IntersectsWith(e.ClipRectangle))
                e.Graphics.DrawImage(bitmap, destination);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _controller.Changed -= Controller_Changed;
            DisposeBitmaps(_bitmaps.Values);
            _bitmaps.Clear();
            _controller.ReleaseDocument();
            _disposed = true;
        }
        base.Dispose(disposing);
    }

    private void Controller_Changed(object? sender, EventArgs e)
    {
        var scale = EffectiveDpi / 96d;
        AutoScrollMinSize = new Size(
            ToScrollDimension(_controller.Layout.ExtentWidth * scale + Padding.Horizontal),
            ToScrollDimension(_controller.Layout.ExtentHeight * scale + Padding.Vertical));
        RefreshBitmaps();
        Invalidate();
    }

    private void UpdateControllerViewport(int? dpiOverride = null)
    {
        if (_disposed) return;
        var scale = (dpiOverride ?? EffectiveDpi) / 96d;
        _controller.SetViewport(
            Math.Max(0, ClientSize.Width - Padding.Horizontal) / scale,
            Math.Max(0, ClientSize.Height - Padding.Vertical) / scale);
    }

    private void ScrollCurrentPageIntoView()
    {
        var current = _controller.Layout.Pages.FirstOrDefault(page => page.PageIndex == _controller.CurrentPageIndex);
        if (current.Width <= 0 || current.Height <= 0) return;
        var scale = EffectiveDpi / 96d;
        AutoScrollPosition = new Point(
            ToScrollDimension(current.X * scale),
            ToScrollDimension(current.Y * scale));
    }

    private void TrackCurrentPageFromViewport()
    {
        if (_disposed || _controller.Document is null) return;
        var scale = EffectiveDpi / 96d;
        var regionX = (-AutoScrollPosition.X - Padding.Left) / scale;
        var regionY = (-AutoScrollPosition.Y - Padding.Top) / scale;
        var pageIndex = TypstVisiblePageSelector.SelectCurrentPage(
            _controller.Layout,
            regionX,
            regionY,
            ClientSize.Width / scale,
            ClientSize.Height / scale);
        if (pageIndex is { } selected && _controller.TrackCurrentPage(selected))
            CurrentPageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshBitmaps()
    {
        var document = _controller.Document;
        if (_disposed || document is null) return;
        var desired = DesiredPageIndices().ToHashSet();
        foreach (var pageIndex in _bitmaps.Keys.Where(index => !desired.Contains(index)).ToArray())
        {
            _bitmaps[pageIndex].Dispose();
            _bitmaps.Remove(pageIndex);
        }
        foreach (var pageIndex in desired.Where(index => !_bitmaps.ContainsKey(index)))
            _bitmaps.Add(pageIndex, TypstBitmapConverter.CreateBitmap(document.Pages[pageIndex]));
    }

    private IReadOnlyList<int> DesiredPageIndices()
    {
        if (!IsHandleCreated)
            return _controller.Layout.Pages.Select(page => page.PageIndex).ToArray();
        var scale = EffectiveDpi / 96d;
        return TypstVisiblePageSelector.Select(
            _controller.Layout,
            (-AutoScrollPosition.X - Padding.Left) / scale,
            (-AutoScrollPosition.Y - Padding.Top) / scale,
            ClientSize.Width / scale,
            ClientSize.Height / scale,
            overscanPages: 1);
    }

    private static void DisposeBitmaps(IEnumerable<Bitmap> bitmaps)
    {
        foreach (var bitmap in bitmaps) bitmap.Dispose();
    }

    private int EffectiveDpi => IsHandleCreated ? DeviceDpi : 96;

    private static int ToScrollDimension(double value)
        => (int)Math.Clamp(Math.Ceiling(value), 0, int.MaxValue);
}

internal static class TypstBitmapConverter
{
    internal static Bitmap CreateBitmap(TypstRenderedPage page)
    {
        var bitmap = new Bitmap(page.PixelWidth, page.PixelHeight, PixelFormat.Format32bppPArgb);
        try
        {
            bitmap.SetResolution(page.Ppi, page.Ppi);
            var bounds = new Rectangle(0, 0, page.PixelWidth, page.PixelHeight);
            var data = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var convertedRow = new byte[checked(page.PixelWidth * 4)];
                var pixels = page.Pixels.Span;
                for (var y = 0; y < page.PixelHeight; y++)
                {
                    ConvertPremultipliedRgbaToBgra(
                        pixels.Slice(y * page.Stride, convertedRow.Length), convertedRow);
                    Marshal.Copy(convertedRow, 0, IntPtr.Add(data.Scan0, y * data.Stride), convertedRow.Length);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    internal static void ConvertPremultipliedRgbaToBgra(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (source.Length != destination.Length || source.Length % 4 != 0)
            throw new ArgumentException("Source and destination must contain the same number of RGBA pixels.");

        for (var offset = 0; offset < source.Length; offset += 4)
        {
            destination[offset] = source[offset + 2];
            destination[offset + 1] = source[offset + 1];
            destination[offset + 2] = source[offset];
            destination[offset + 3] = source[offset + 3];
        }
    }
}
