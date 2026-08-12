using Typst.Renderer.Core;
using Typst.Renderer.WinUI;

namespace CleanWinUiConsumer;

public sealed class ConsumerControl
{
    public ITypstDocumentView CreateView() => new TypstView
    {
        ZoomMode = TypstZoomMode.FitWidth,
        ViewMode = TypstPageViewMode.ContinuousFacing,
        PageSpacing = 20
    };
}
