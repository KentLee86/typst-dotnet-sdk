using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Cetz.Renderer.Core;

namespace Cetz.Renderer.WinForms;

/// <summary>
/// Displays and scrolls every page of a UI-neutral CeTZ rendered document.
/// Set <see cref="Document"/> on the control's UI thread.
/// </summary>
public sealed class CetzView : ScrollableControl
{
    private const double MinimumZoom = 0.1;
    private const double MaximumZoom = 8;
    private readonly List<Bitmap> _bitmaps = [];
    private CetzRenderedDocument? _document;
    private double _zoom = 1;
    private double _pageSpacing = 24;
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
    }

    /// <summary>
    /// Gets or sets the immutable rendered document. Page pixels are copied into
    /// control-owned bitmaps, so replacing or releasing the source document is safe.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CetzRenderedDocument? Document
    {
        get => _document;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (ReferenceEquals(_document, value))
                return;

            var replacements = CreateBitmaps(value);
            var previous = _bitmaps.ToArray();
            _bitmaps.Clear();
            _bitmaps.AddRange(replacements);
            _document = value;

            foreach (var bitmap in previous)
                bitmap.Dispose();

            UpdateScrollExtent();
            Invalidate();
        }
    }

    /// <summary>Gets or sets page magnification, clamped to 0.1 through 8.</summary>
    [DefaultValue(1d)]
    public double Zoom
    {
        get => _zoom;
        set
        {
            var coerced = double.IsFinite(value) ? Math.Clamp(value, MinimumZoom, MaximumZoom) : 1;
            if (_zoom.Equals(coerced))
                return;
            _zoom = coerced;
            UpdateScrollExtent();
            Invalidate();
        }
    }

    /// <summary>Gets or sets the device-independent gap between consecutive pages.</summary>
    [DefaultValue(24d)]
    public double PageSpacing
    {
        get => _pageSpacing;
        set
        {
            if (!double.IsFinite(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Page spacing must be finite and non-negative.");
            if (_pageSpacing.Equals(value))
                return;
            _pageSpacing = value;
            UpdateScrollExtent();
            Invalidate();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateScrollExtent();
    }

    protected override void RescaleConstantsForDpi(int deviceDpiOld, int deviceDpiNew)
    {
        base.RescaleConstantsForDpi(deviceDpiOld, deviceDpiNew);
        UpdateScrollExtent(deviceDpiNew);
        Invalidate();
    }

    protected override void OnPaddingChanged(EventArgs e)
    {
        base.OnPaddingChanged(e);
        UpdateScrollExtent();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var document = _document;
        if (document is null || document.Pages.Count != _bitmaps.Count)
            return;

        e.Graphics.CompositingMode = CompositingMode.SourceOver;
        e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var dpi = EffectiveDpi;
        var contentWidth = GetContentWidth(document, dpi);
        var layoutWidth = Math.Max(contentWidth, ClientSize.Width - Padding.Horizontal);
        var scrollOffset = AutoScrollPosition;
        var y = (float)(Padding.Top + scrollOffset.Y);

        for (var index = 0; index < document.Pages.Count; index++)
        {
            var page = document.Pages[index];
            var size = CetzViewLayout.GetScaledPageSize(page.PixelWidth, page.PixelHeight, page.Ppi, dpi, Zoom);
            var x = Padding.Left + ((layoutWidth - size.Width) / 2f) + scrollOffset.X;
            var destination = new RectangleF(x, y, size.Width, size.Height);

            if (destination.IntersectsWith(e.ClipRectangle))
            {
                e.Graphics.DrawImage(
                    _bitmaps[index],
                    destination,
                    new RectangleF(0, 0, page.PixelWidth, page.PixelHeight),
                    GraphicsUnit.Pixel);
            }

            y += size.Height + ScaleDip(PageSpacing, dpi);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            foreach (var bitmap in _bitmaps)
                bitmap.Dispose();
            _bitmaps.Clear();
            _document = null;
        }
        base.Dispose(disposing);
    }

    private static List<Bitmap> CreateBitmaps(CetzRenderedDocument? document)
    {
        var bitmaps = new List<Bitmap>(document?.Pages.Count ?? 0);
        try
        {
            if (document is not null)
            {
                foreach (var page in document.Pages)
                    bitmaps.Add(CetzBitmapConverter.CreateBitmap(page));
            }
            return bitmaps;
        }
        catch
        {
            foreach (var bitmap in bitmaps)
                bitmap.Dispose();
            throw;
        }
    }

    private void UpdateScrollExtent(int? dpiOverride = null)
    {
        var document = _document;
        if (document is null || document.Pages.Count == 0)
        {
            AutoScrollMinSize = Size.Empty;
            return;
        }

        var dpi = dpiOverride ?? EffectiveDpi;
        var width = GetContentWidth(document, dpi) + Padding.Horizontal;
        var height = document.Pages.Sum(page =>
            (double)CetzViewLayout.GetScaledPageSize(page.PixelWidth, page.PixelHeight, page.Ppi, dpi, Zoom).Height);
        height += ScaleDip(PageSpacing, dpi) * Math.Max(0, document.Pages.Count - 1);
        height += Padding.Vertical;

        AutoScrollMinSize = new Size(ToScrollDimension(width), ToScrollDimension(height));
    }

    private float GetContentWidth(CetzRenderedDocument document, int dpi)
        => document.Pages.Max(page =>
            CetzViewLayout.GetScaledPageSize(page.PixelWidth, page.PixelHeight, page.Ppi, dpi, Zoom).Width);

    private int EffectiveDpi => IsHandleCreated ? DeviceDpi : 96;

    private static float ScaleDip(double value, int dpi) => (float)(value * dpi / 96d);

    private static int ToScrollDimension(double value)
        => (int)Math.Clamp(Math.Ceiling(value), 0, int.MaxValue);
}

internal static class CetzViewLayout
{
    internal static SizeF GetScaledPageSize(
        int pixelWidth,
        int pixelHeight,
        float pagePpi,
        int deviceDpi,
        double zoom)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "Page dimensions must be positive.");
        if (!float.IsFinite(pagePpi) || pagePpi <= 0)
            throw new ArgumentOutOfRangeException(nameof(pagePpi), "Page PPI must be finite and positive.");
        if (deviceDpi <= 0)
            throw new ArgumentOutOfRangeException(nameof(deviceDpi));
        if (!double.IsFinite(zoom) || zoom <= 0)
            throw new ArgumentOutOfRangeException(nameof(zoom));

        var scale = deviceDpi / (double)pagePpi * zoom;
        return new SizeF((float)(pixelWidth * scale), (float)(pixelHeight * scale));
    }
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
                        pixels.Slice(y * page.Stride, convertedRow.Length),
                        convertedRow);
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
