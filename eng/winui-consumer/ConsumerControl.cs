using Cetz.Renderer.Core;
using Cetz.Renderer.WinUI;

namespace CleanWinUiConsumer;

public sealed class ConsumerControl
{
    public ICetzDocumentView CreateView() => new CetzView
    {
        ZoomMode = CetzZoomMode.FitWidth,
        ViewMode = CetzPageViewMode.ContinuousFacing,
        PageSpacing = 20
    };
}
