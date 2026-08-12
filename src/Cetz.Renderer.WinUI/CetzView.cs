using System.Runtime.InteropServices.WindowsRuntime;
using Cetz.Renderer.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Cetz.Renderer.WinUI;

/// <summary>
/// A scrollable WinUI 3 document view that displays every premultiplied RGBA page.
/// </summary>
public sealed class CetzView : Grid, IDisposable
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
        new PropertyMetadata(1d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty PageSpacingProperty = DependencyProperty.Register(
        nameof(PageSpacing),
        typeof(double),
        typeof(CetzView),
        new PropertyMetadata(24d, OnLayoutPropertyChanged));

    private readonly ScrollViewer _scrollViewer;
    private readonly StackPanel _pagePanel;
    private readonly List<Image> _pageImages = [];
    private readonly List<object> _imageSources = [];
    private bool _disposed;

    public CetzView()
    {
        _pagePanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };
        _scrollViewer = new ScrollViewer
        {
            Content = _pagePanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        Children.Add(_scrollViewer);
    }

    /// <summary>The immutable Core document displayed by this view.</summary>
    public CetzRenderedDocument? Document
    {
        get => (CetzRenderedDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>Page scale relative to its physical size. Values are clamped to 0.1–8.</summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, WinUiLayout.NormalizeZoom(value));
    }

    /// <summary>Vertical spacing between pages, in device-independent pixels.</summary>
    public double PageSpacing
    {
        get => (double)GetValue(PageSpacingProperty);
        set => SetValue(PageSpacingProperty, WinUiLayout.NormalizeSpacing(value));
    }

    /// <summary>
    /// Assigns a document from any thread. The returned task completes after the UI thread
    /// has created all page image sources.
    /// </summary>
    public Task SetDocumentAsync(CetzRenderedDocument? document)
    {
        ThrowIfDisposed();
        if (DispatcherQueue.HasThreadAccess)
        {
            Document = document;
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                try
                {
                    ThrowIfDisposed();
                    Document = document;
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            }))
        {
            completion.SetException(new InvalidOperationException("The WinUI dispatcher is shutting down."));
        }
        return completion.Task;
    }

    /// <summary>Releases page image sources and disconnects the view from its document.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        if (!DispatcherQueue.HasThreadAccess)
            throw new InvalidOperationException("CetzView must be disposed on its WinUI thread.");

        _disposed = true;
        ClearValue(DocumentProperty);
        ClearPageImages();
        _scrollViewer.Content = null;
        Children.Clear();
        GC.SuppressFinalize(this);
    }

    private static void OnDocumentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)sender;
        if (!view._disposed)
            view.RebuildPageImages((CetzRenderedDocument?)args.NewValue);
    }

    private static void OnLayoutPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (CetzView)sender;
        if (view._disposed)
            return;

        if (args.Property == ZoomProperty)
        {
            var normalized = WinUiLayout.NormalizeZoom((double)args.NewValue);
            if (normalized != (double)args.NewValue)
            {
                view.SetValue(ZoomProperty, normalized);
                return;
            }
        }
        else
        {
            var normalized = WinUiLayout.NormalizeSpacing((double)args.NewValue);
            if (normalized != (double)args.NewValue)
            {
                view.SetValue(PageSpacingProperty, normalized);
                return;
            }
        }
        view.ApplyPageLayout();
    }

    private void RebuildPageImages(CetzRenderedDocument? document)
    {
        ClearPageImages();
        if (document is null)
            return;

        try
        {
            foreach (var page in document.Pages)
            {
                var bitmap = new WriteableBitmap(page.PixelWidth, page.PixelHeight);
                using (var stream = bitmap.PixelBuffer.AsStream())
                {
                    WinUiPixelBuffer.WriteBgraPremultiplied(page, stream);
                }
                bitmap.Invalidate();

                var image = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Fill,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                _imageSources.Add(bitmap);
                _pageImages.Add(image);
                _pagePanel.Children.Add(image);
            }
            ApplyPageLayout();
        }
        catch
        {
            ClearPageImages();
            throw;
        }
    }

    private void ApplyPageLayout()
    {
        var document = Document;
        if (document is null || document.Pages.Count != _pageImages.Count)
            return;

        var zoom = WinUiLayout.NormalizeZoom(Zoom);
        var spacing = WinUiLayout.NormalizeSpacing(PageSpacing);
        for (var index = 0; index < _pageImages.Count; index++)
        {
            var page = document.Pages[index];
            var image = _pageImages[index];
            image.Width = page.Width * zoom;
            image.Height = page.Height * zoom;
            image.Margin = new Thickness(0, 0, 0, index + 1 < _pageImages.Count ? spacing : 0);
        }
    }

    private void ClearPageImages()
    {
        foreach (var image in _pageImages)
            image.Source = null;
        _pagePanel.Children.Clear();
        _pageImages.Clear();
        foreach (var source in _imageSources)
        {
            if (source is IDisposable disposable)
                disposable.Dispose();
        }
        _imageSources.Clear();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

internal static class WinUiLayout
{
    public static double NormalizeZoom(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0.1d, 8d) : 1d;

    public static double NormalizeSpacing(double value)
        => double.IsFinite(value) ? Math.Max(0d, value) : 24d;
}
