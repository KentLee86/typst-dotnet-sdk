using System.Collections.ObjectModel;

namespace Cetz.Renderer.Core;

/// <summary>UI-neutral settings for rendering a document into display-ready pages.</summary>
public sealed class CetzDocumentRenderOptions
{
    public float Ppi { get; set; } = 144;

    public IReadOnlyDictionary<string, object?> Inputs { get; set; } =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
}

/// <summary>
/// Renders Typst and CeTZ projects into immutable premultiplied RGBA pages that
/// can be consumed by any GUI adapter without depending on a UI framework.
/// </summary>
public sealed class CetzDocumentRenderer : IDisposable
{
    private readonly CetzRenderer _renderer;

    public CetzDocumentRenderer(CetzRendererOptions? options = null)
        => _renderer = new CetzRenderer(options);

    public CetzRenderedDocument RenderSource(
        string source,
        string virtualPath = "main.typ",
        CetzDocumentRenderOptions? options = null)
        => CreateDocument(
            _renderer.RenderSource(source, virtualPath, CreateSettings(options)),
            ResolvePpi(options));

    public async Task<CetzRenderedDocument> RenderSourceAsync(
        string source,
        string virtualPath = "main.typ",
        CetzDocumentRenderOptions? options = null,
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

    public CetzRenderedDocument RenderProject(
        CetzProject project,
        CetzDocumentRenderOptions? options = null)
        => CreateDocument(
            _renderer.RenderProject(project, CreateSettings(options)),
            ResolvePpi(options));

    public async Task<CetzRenderedDocument> RenderProjectAsync(
        CetzProject project,
        CetzDocumentRenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var ppi = ResolvePpi(options);
        var result = await _renderer.RenderProjectAsync(
            project,
            CreateSettings(options),
            cancellationToken).ConfigureAwait(false);
        return CreateDocument(result, ppi);
    }

    public CetzRenderedDocument RenderFile(
        string path,
        CetzDocumentRenderOptions? options = null)
        => CreateDocument(
            _renderer.RenderFile(path, CreateSettings(options)),
            ResolvePpi(options));

    public async Task<CetzRenderedDocument> RenderFileAsync(
        string path,
        CetzDocumentRenderOptions? options = null,
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

    private static CetzRenderSettings CreateSettings(CetzDocumentRenderOptions? options)
    {
        var ppi = ResolvePpi(options);
        return new CetzRenderSettings
        {
            Formats = [CetzOutputFormat.Rgba],
            Ppi = ppi,
            Inputs = options?.Inputs ??
                new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>())
        };
    }

    private static float ResolvePpi(CetzDocumentRenderOptions? options)
    {
        var ppi = options?.Ppi ?? 144;
        if (!float.IsFinite(ppi) || ppi <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "PPI must be finite and greater than zero.");
        return ppi;
    }

    private static CetzRenderedDocument CreateDocument(CetzRenderResult result, float ppi)
    {
        var pages = result.Artifacts
            .Where(artifact => artifact.Format == CetzOutputFormat.Rgba)
            .OrderBy(artifact => artifact.Page ?? int.MaxValue)
            .Select(artifact => new CetzRenderedPage(
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

        return new CetzRenderedDocument(
            pages,
            result.Diagnostics,
            result.Timing,
            result.TypstVersion,
            ppi);
    }

    private static CetzRenderException ProtocolError(string message)
        => new(message, "protocol");
}
