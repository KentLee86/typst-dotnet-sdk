using Cetz.Renderer.Core;
using Cetz.Renderer.WinUI;
using Xunit;

namespace Cetz.Renderer.WinUI.Tests;

public sealed class WinUiViewTests
{
    [Fact]
    public void ViewImplementsTheCommonDocumentContract()
        => Assert.True(typeof(ICetzDocumentView).IsAssignableFrom(typeof(CetzView)));

    [Fact]
    public void ViewExposesEveryCommonStateAndNavigationPath()
    {
        var contract = typeof(ICetzDocumentView);
        var implementation = typeof(CetzView);

        foreach (var property in contract.GetProperties())
            Assert.NotNull(implementation.GetProperty(property.Name));
        foreach (var method in contract.GetMethods().Where(method => !method.IsSpecialName))
            Assert.Contains(implementation.GetMethods(), candidate =>
                candidate.Name == method.Name &&
                candidate.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(method.GetParameters().Select(parameter => parameter.ParameterType)));

        Assert.NotNull(implementation.GetMethod(nameof(CetzView.SetDocumentAsync)));
        Assert.True(typeof(IDisposable).IsAssignableFrom(implementation));
    }

    [Fact]
    public void ViewKeepsDependencyPropertiesForBindingAllSharedOptions()
    {
        var view = typeof(CetzView);
        Assert.NotNull(view.GetField(nameof(CetzView.DocumentProperty)));
        Assert.NotNull(view.GetField(nameof(CetzView.ZoomProperty)));
        Assert.NotNull(view.GetField(nameof(CetzView.ZoomModeProperty)));
        Assert.NotNull(view.GetField(nameof(CetzView.ViewModeProperty)));
        Assert.NotNull(view.GetField(nameof(CetzView.PageSpacingProperty)));
    }

    [Fact]
    public void VisibleResourceSetReusesRetainedPagesAndDisposesRemovedOrReleasedPages()
    {
        var resources = new WinUiPageResourceSet<TrackedResource>();
        var first = resources.GetOrAdd(0, _ => new TrackedResource());
        var second = resources.GetOrAdd(1, _ => new TrackedResource());

        Assert.Same(first, resources.GetOrAdd(0, _ => throw new InvalidOperationException()));
        resources.RetainOnly([1, 2]);

        Assert.True(first.Disposed);
        Assert.False(second.Disposed);
        Assert.Equal(1, resources.Count);

        resources.Clear();
        Assert.True(second.Disposed);
        Assert.Equal(0, resources.Count);
    }

    [Fact]
    public void PremultipliedRgbaIsReorderedToWinUiBgraWithoutChangingAlpha()
    {
        byte[] source = [10, 20, 30, 40, 200, 150, 100, 250];
        var destination = new byte[source.Length];

        WinUiPixelBuffer.ConvertRgbaRowToBgra(source, destination);

        Assert.Equal(new byte[] { 30, 20, 10, 40, 100, 150, 200, 250 }, destination);
    }

    [Fact]
    public void PixelWriterCopiesEveryRowAndIgnoresSourceStridePadding()
    {
        byte[] source =
        [
            1, 2, 3, 4, 5, 6, 7, 8, 99, 98, 97, 96,
            9, 10, 11, 12, 13, 14, 15, 16, 95, 94, 93, 92
        ];
        using var destination = new MemoryStream();

        WinUiPixelBuffer.WriteBgraPremultiplied(source, 2, 2, 12, destination);

        Assert.Equal(
            new byte[] { 3, 2, 1, 4, 7, 6, 5, 8, 11, 10, 9, 12, 15, 14, 13, 16 },
            destination.ToArray());
    }

    private sealed class TrackedResource : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
