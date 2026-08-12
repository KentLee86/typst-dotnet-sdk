using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Cetz.Renderer.Core;

namespace Cetz.Renderer.WinForms;

/// <summary>
/// WinForms adapter for the common CeTZ document-view contract. The common
/// controller owns all document, zoom, navigation, and page-layout state; this
/// control owns only WinForms bitmap and scrolling resources.
/// </summary>
public sealed class CetzView : ScrollableControl, ICetzDocumentView
{
    private readonly List<Bitmap> _bitmaps = [];
    private readonly CetzDocumentViewController _controller = new();
    private bool _disposed;

    public CetzView()
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
    public CetzRenderedDocument? Document
    {
        get => _controller.Document;
        set
        {
            if (value is null) ReleaseDocument();
            else SetDocument(value);
        }
    }

    [DefaultValue(CetzDocumentViewController.DefaultZoom)]
    public double Zoom
    {
        get => _controller.Zoom;
        set => SetZoom(value);
    }

    [DefaultValue(CetzZoomMode.Custom)]
    public CetzZoomMode ZoomMode
    {
        get => _controller.ZoomMode;
        set => SetZoomMode(value);
    }

    [DefaultValue(CetzPageViewMode.ContinuousSingle)]
    public CetzPageViewMode ViewMode
    {
        get => _controller.ViewMode;
        set => SetViewMode(value);
    }

    [DefaultValue(CetzDocumentViewController.DefaultPageSpacing)]
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
    public new CetzDocumentViewLayout Layout => _controller.Layout;

    internal int BitmapCount => _bitmaps.Count;

    public void SetDocument(CetzRenderedDocument document)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(document);
        if (ReferenceEquals(_controller.Document, document)) return;

        // Convert first so a failed GDI allocation leaves the successful preview intact.
        var replacements = CreateBitmaps(document);
        var previous = _bitmaps.ToArray();
        _bitmaps.Clear();
        _bitmaps.AddRange(replacements);
        _controller.SetDocument(document);
        DisposeBitmaps(previous);
    }

    public void SetZoom(double zoom)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _controller.SetZoom(zoom);
    }

    public void SetZoomMode(CetzZoomMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _controller.SetZoomMode(mode);
    }

    public void SetViewMode(CetzPageViewMode mode)
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
        DisposeBitmaps(_bitmaps);
        _bitmaps.Clear();
        _controller.ReleaseDocument();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateControllerViewport();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateControllerViewport();
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
            if ((uint)pageLayout.PageIndex >= (uint)_bitmaps.Count) continue;
            var destination = new RectangleF(
                (float)(Padding.Left + scroll.X + pageLayout.X * scale),
                (float)(Padding.Top + scroll.Y + pageLayout.Y * scale),
                (float)(pageLayout.Width * scale),
                (float)(pageLayout.Height * scale));
            if (destination.IntersectsWith(e.ClipRectangle))
                e.Graphics.DrawImage(_bitmaps[pageLayout.PageIndex], destination);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _controller.Changed -= Controller_Changed;
            DisposeBitmaps(_bitmaps);
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

    private static List<Bitmap> CreateBitmaps(CetzRenderedDocument document)
    {
        var bitmaps = new List<Bitmap>(document.Pages.Count);
        try
        {
            foreach (var page in document.Pages)
                bitmaps.Add(CetzBitmapConverter.CreateBitmap(page));
            return bitmaps;
        }
        catch
        {
            DisposeBitmaps(bitmaps);
            throw;
        }
    }

    private static void DisposeBitmaps(IEnumerable<Bitmap> bitmaps)
    {
        foreach (var bitmap in bitmaps) bitmap.Dispose();
    }

    private int EffectiveDpi => IsHandleCreated ? DeviceDpi : 96;

    private static int ToScrollDimension(double value)
        => (int)Math.Clamp(Math.Ceiling(value), 0, int.MaxValue);
}

internal static class CetzBitmapConverter
{
    internal static Bitmap CreateBitmap(CetzRenderedPage page)
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
