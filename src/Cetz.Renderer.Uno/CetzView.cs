using System.Runtime.InteropServices.WindowsRuntime;
using Cetz.Renderer.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Cetz.Renderer.Uno;

/// <summary>
/// Displays every page in a UI-neutral CeTZ document using WinUI/Uno images.
/// The view owns its bitmap resources and releases them whenever the document is
/// replaced, the view is unloaded, or <see cref="Dispose"/> is called.
/// </summary>
public sealed class CetzView : StackPanel, IDisposable
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document),
        typeof(CetzRenderedDocument),
        typeof(CetzView),
        new PropertyMetadata(null, OnDocumentChanged));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom),
        typeof(double),
        typeof(CetzView),
        new PropertyMetadata(1d, OnZoomChanged));

    public static readonly DependencyProperty PageSpacingProperty = DependencyProperty.Register(
        nameof(PageSpacing),
        typeof(double),
        typeof(CetzView),
        new PropertyMetadata(24d, OnPageSpacingChanged));

    private readonly List<PageResource> _pageResources = [];
    private bool _disposed;

    public CetzView()
    {
        Orientation = Orientation.Vertical;
        HorizontalAlignment = HorizontalAlignment.Left;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public CetzRenderedDocument? Document
    {
        get => (CetzRenderedDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, CetzUnoLayout.NormalizeZoom(value));
    }

    public double PageSpacing
    {
        get => (double)GetValue(PageSpacingProperty);
        set => SetValue(PageSpacingProperty, CetzUnoLayout.NormalizePageSpacing(value));
    }

    void IDisposable.Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        ReleasePageResources();
        ClearValue(DocumentProperty);
        GC.SuppressFinalize(this);
    }

    private void RebuildPages()
    {
        ReleasePageResources();
        if (_disposed || Document is null)
            return;

        foreach (var page in Document.Pages)
        {
            var pixels = CetzUnoPixelConverter.ToBgra8Premultiplied(
                page.Pixels.Span,
                page.PixelWidth,
                page.PixelHeight,
                page.Stride);
            var bitmap = new WriteableBitmap(page.PixelWidth, page.PixelHeight);
            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Position = 0;
                stream.Write(pixels, 0, pixels.Length);
            }
            bitmap.Invalidate();

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var resource = new PageResource(image, bitmap);
            _pageResources.Add(resource);
            Children.Add(image);
        }

        ApplyLayout();
    }

    private void ApplyLayout()
    {
        if (Document is null || _pageResources.Count != Document.Pages.Count)
            return;

        var spacing = CetzUnoLayout.NormalizePageSpacing(PageSpacing);
        for (var index = 0; index < _pageResources.Count; index++)
        {
            var (width, height) = CetzUnoLayout.GetPageSize(Document.Pages[index], Zoom);
            var image = _pageResources[index].Image;
            image.Width = width;
            image.Height = height;
            image.Margin = new Thickness(0, 0, 0, index == _pageResources.Count - 1 ? 0 : spacing);
        }
    }

    private void ReleasePageResources()
    {
        Children.Clear();
        foreach (var resource in _pageResources)
            resource.Release();
        _pageResources.Clear();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!_disposed && _pageResources.Count == 0 && Document is not null)
            RebuildPages();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => ReleasePageResources();

    private static void OnDocumentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((CetzView)sender).RebuildPages();

    private static void OnZoomChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)sender;
        var value = CetzUnoLayout.NormalizeZoom((double)args.NewValue);
        if (!value.Equals(args.NewValue))
            view.SetValue(ZoomProperty, value);
        else
            view.ApplyLayout();
    }

    private static void OnPageSpacingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)sender;
        var value = CetzUnoLayout.NormalizePageSpacing((double)args.NewValue);
        if (!value.Equals(args.NewValue))
            view.SetValue(PageSpacingProperty, value);
        else
            view.ApplyLayout();
    }

    private sealed class PageResource(Image image, WriteableBitmap bitmap)
    {
        private WriteableBitmap? _bitmap = bitmap;

        public Image Image { get; } = image;

        public void Release()
        {
            Image.Source = null;
            _bitmap = null;
        }
    }
}
