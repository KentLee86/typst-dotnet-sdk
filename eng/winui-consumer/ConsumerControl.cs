using Cetz.Renderer.WinUI;

namespace CleanWinUiConsumer;

public sealed class ConsumerControl
{
    public CetzView CreateView() => new() { Zoom = 1.25, PageSpacing = 20 };
}
