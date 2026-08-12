using System.Collections.ObjectModel;

namespace Typst.Renderer.Core;

/// <summary>UI-neutral settings for rendering a document into display-ready pages.</summary>
public sealed class TypstDocumentRenderOptions
{
    public float Ppi { get; set; } = 144;

    public IReadOnlyDictionary<string, object?> Inputs { get; set; } =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
}

/// <summary>
/// Renders Typst and CeTZ projects into immutable premultiplied RGBA pages that
/// can be consumed by any GUI adapter without depending on a UI framework.
/// </summary>
public sealed class TypstDocumentRenderer : IDisposable
{
    private readonly TypstRenderer _renderer;

    public TypstDocumentRenderer(TypstRendererOptions? options = null)
        => _renderer = new TypstRenderer(options);

    public TypstRenderedDocument RenderSource(
        string source,
        string virtualPath = "main.typ",
        TypstDocumentRenderOptions? options = null)
        => CreateDocument(
            _renderer.RenderSource(source, virtualPath, CreateSettings(options)),
            ResolvePpi(options));

    public async Task<TypstRenderedDocument> RenderSourceAsync(
        string source,
        string virtualPath = "main.typ",
        TypstDocumentRenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var ppi = ResolvePpi(options);
        var result = await _renderer.RenderSourceAsync(
            source,
            virtualPath,
            CreateSettings(options),
            cancellationToken).ConfigureAwait(false);
        return CreateDocument(result, ppi);
    }

    public TypstRenderedDocument RenderProject(
        TypstProject project,
        TypstDocumentRenderOptions? options = null)
        => CreateDocument(
            _renderer.RenderProject(project, CreateSettings(options)),
            ResolvePpi(options));

    public async Task<TypstRenderedDocument> RenderProjectAsync(
        TypstProject project,
        TypstDocumentRenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var ppi = ResolvePpi(options);
        var result = await _renderer.RenderProjectAsync(
            project,
            CreateSettings(options),
            cancellationToken).ConfigureAwait(false);
        return CreateDocument(result, ppi);
    }

    public TypstRenderedDocument RenderFile(
        string path,
        TypstDocumentRenderOptions? options = null)
        => CreateDocument(
            _renderer.RenderFile(path, CreateSettings(options)),
            ResolvePpi(options));

    public async Task<TypstRenderedDocument> RenderFileAsync(
        string path,
        TypstDocumentRenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var ppi = ResolvePpi(options);
        var result = await _renderer.RenderFileAsync(
            path,
            CreateSettings(options),
            cancellationToken).ConfigureAwait(false);
        return CreateDocument(result, ppi);
    }

    public void Dispose() => _renderer.Dispose();

    private static TypstRenderSettings CreateSettings(TypstDocumentRenderOptions? options)
    {
        var ppi = ResolvePpi(options);
        return new TypstRenderSettings
        {
            Formats = [TypstOutputFormat.Rgba],
            Ppi = ppi,
            Inputs = options?.Inputs ??
                new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>())
        };
    }

    private static float ResolvePpi(TypstDocumentRenderOptions? options)
    {
        var ppi = options?.Ppi ?? 144;
        if (!float.IsFinite(ppi) || ppi <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "PPI must be finite and greater than zero.");
        return ppi;
    }

    private static TypstRenderedDocument CreateDocument(TypstRenderResult result, float ppi)
    {
        var pages = result.Artifacts
            .Where(artifact => artifact.Format == TypstOutputFormat.Rgba)
            .OrderBy(artifact => artifact.Page ?? int.MaxValue)
            .Select(artifact => new TypstRenderedPage(
                artifact.Page ?? 1,
                artifact.Width ?? throw ProtocolError("RGBA page has no width."),
                artifact.Height ?? throw ProtocolError("RGBA page has no height."),
                artifact.Stride ?? throw ProtocolError("RGBA page has no stride."),
                ppi,
                artifact.PixelFormat,
                artifact.Data))
            .ToArray();

        if (pages.Length == 0)
            throw ProtocolError("The renderer returned no RGBA pages.");

        return new TypstRenderedDocument(
            pages,
            result.Diagnostics,
            result.Timing,
            result.TypstVersion,
            ppi);
    }

    private static TypstRenderException ProtocolError(string message)
        => new(message, "protocol");
}
