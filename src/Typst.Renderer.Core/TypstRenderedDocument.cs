namespace Typst.Renderer.Core;

/// <summary>An immutable display document containing one or more RGBA pages.</summary>
public sealed class TypstRenderedDocument
{
    internal TypstRenderedDocument(
        IReadOnlyList<TypstRenderedPage> pages,
        IReadOnlyList<TypstDiagnostic> diagnostics,
        TypstRenderTiming timing,
        string typstVersion,
        float ppi)
    {
        Pages = pages;
        Diagnostics = diagnostics;
        Timing = timing;
        TypstVersion = typstVersion;
        Ppi = ppi;
    }

    public IReadOnlyList<TypstRenderedPage> Pages { get; }
    public IReadOnlyList<TypstDiagnostic> Diagnostics { get; }
    public TypstRenderTiming Timing { get; }
    public string TypstVersion { get; }
    public float Ppi { get; }
}

/// <summary>A single premultiplied RGBA8 page with device-independent dimensions.</summary>
public sealed class TypstRenderedPage
{
    internal TypstRenderedPage(
        int pageNumber,
        int pixelWidth,
        int pixelHeight,
        int stride,
        float ppi,
        TypstPixelFormat pixelFormat,
        ReadOnlyMemory<byte> pixels)
    {
        if (pixelFormat != TypstPixelFormat.Rgba8Premultiplied)
            throw new TypstRenderException($"Unsupported display pixel format: {pixelFormat}.", "protocol");
        if (pixelWidth <= 0 || pixelHeight <= 0 || stride < checked(pixelWidth * 4))
            throw new TypstRenderException("Invalid RGBA page dimensions.", "protocol");
        if (pixels.Length != checked(stride * pixelHeight))
            throw new TypstRenderException("RGBA page byte count does not match its dimensions.", "protocol");

        PageNumber = pageNumber;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        Stride = stride;
        Ppi = ppi;
        Pixels = pixels;
    }

    public int PageNumber { get; }
    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public int Stride { get; }
    public float Ppi { get; }
    public ReadOnlyMemory<byte> Pixels { get; }
    public double Width => PixelWidth * 96d / Ppi;
    public double Height => PixelHeight * 96d / Ppi;
}
