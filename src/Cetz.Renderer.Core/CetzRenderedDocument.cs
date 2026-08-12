namespace Cetz.Renderer.Core;

/// <summary>An immutable display document containing one or more RGBA pages.</summary>
public sealed class CetzRenderedDocument
{
    internal CetzRenderedDocument(
        IReadOnlyList<CetzRenderedPage> pages,
        IReadOnlyList<CetzDiagnostic> diagnostics,
        CetzRenderTiming timing,
        string typstVersion,
        float ppi)
    {
        Pages = pages;
        Diagnostics = diagnostics;
        Timing = timing;
        TypstVersion = typstVersion;
        Ppi = ppi;
    }

    public IReadOnlyList<CetzRenderedPage> Pages { get; }
    public IReadOnlyList<CetzDiagnostic> Diagnostics { get; }
    public CetzRenderTiming Timing { get; }
    public string TypstVersion { get; }
    public float Ppi { get; }
}

/// <summary>A single premultiplied RGBA8 page with device-independent dimensions.</summary>
public sealed class CetzRenderedPage
{
    internal CetzRenderedPage(
        int pageNumber,
        int pixelWidth,
        int pixelHeight,
        int stride,
        float ppi,
        CetzPixelFormat pixelFormat,
        ReadOnlyMemory<byte> pixels)
    {
        if (pixelFormat != CetzPixelFormat.Rgba8Premultiplied)
            throw new CetzRenderException($"Unsupported display pixel format: {pixelFormat}.", "protocol");
        if (pixelWidth <= 0 || pixelHeight <= 0 || stride < checked(pixelWidth * 4))
            throw new CetzRenderException("Invalid RGBA page dimensions.", "protocol");
        if (pixels.Length != checked(stride * pixelHeight))
            throw new CetzRenderException("RGBA page byte count does not match its dimensions.", "protocol");

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
