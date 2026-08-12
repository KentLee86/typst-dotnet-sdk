using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

namespace Cetz.Renderer;

public sealed class CetzRenderer : IDisposable
{
    private readonly NativeApi _native;
    private readonly RendererSafeHandle _handle;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _baseDirectory;
    private readonly string? _restrictedDirectory;
    private bool _disposed;

    public CetzRenderer(CetzRendererOptions? options = null)
    {
        options ??= new CetzRendererOptions();
        _restrictedDirectory = options.RestrictToDirectory is null ? null : Path.GetFullPath(options.RestrictToDirectory);
        _baseDirectory = _restrictedDirectory ?? Path.GetFullPath(options.BaseDirectory);
        try
        {
            _native = NativeApi.Load(options.NativeLibraryPath);
            if (_native.AbiVersion() != 1)
                throw new CetzNativeLibraryException($"Native ABI {_native.AbiVersion()} is incompatible; expected 1.");
            _handle = CreateRenderer(options);
        }
        catch (CetzRenderException) { throw; }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            throw new CetzNativeLibraryException("Unable to load the CeTZ native runtime for this process architecture.", exception);
        }
    }

    public CetzRenderResult RenderFile(string path, CetzRenderSettings? settings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var absolute = Path.GetFullPath(path);
        ValidateFileAccess(absolute);
        var source = File.ReadAllText(absolute);
        return RenderSource(source, VirtualPathFor(absolute), settings);
    }

    public Task<CetzRenderResult> RenderFileAsync(string path, CetzRenderSettings? settings = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var absolute = Path.GetFullPath(path);
        ValidateFileAccess(absolute);
        return RenderSourceAsync(File.ReadAllText(absolute), VirtualPathFor(absolute), settings, cancellationToken);
    }

    public CetzRenderResult RenderSource(string source, string virtualPath = "main.typ", CetzRenderSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var project = new CetzProjectBuilder().WithMainFile(virtualPath).AddText(virtualPath, source).Build();
        return RenderProject(project, settings);
    }

    public Task<CetzRenderResult> RenderSourceAsync(string source, string virtualPath = "main.typ", CetzRenderSettings? settings = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var project = new CetzProjectBuilder().WithMainFile(virtualPath).AddText(virtualPath, source).Build();
        return RenderProjectAsync(project, settings, cancellationToken);
    }

    public CetzRenderResult RenderProject(CetzProject project, CetzRenderSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ThrowIfDisposed();
        _gate.Wait();
        try
        {
            ThrowIfDisposed();
            return RenderProjectCore(project, settings ?? new CetzRenderSettings());
        }
        finally { _gate.Release(); }
    }

    public async Task<CetzRenderResult> RenderProjectAsync(CetzProject project, CetzRenderSettings? settings = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            // Cancellation applies to waiting for this renderer. Once native compilation starts,
            // Typst is allowed to finish and the result is returned.
            return await Task.Run(() => RenderProjectCore(project, settings ?? new CetzRenderSettings()), CancellationToken.None).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private RendererSafeHandle CreateRenderer(CetzRendererOptions options)
    {
        var baseDirectory = options.RestrictToDirectory is null
            ? Path.GetFullPath(options.BaseDirectory)
            : Path.GetFullPath(options.RestrictToDirectory);
        if (!Directory.Exists(baseDirectory)) throw new CetzConfigurationException($"BaseDirectory does not exist: {baseDirectory}");
        var config = JsonSerializer.Serialize(new
        {
            base_directory = baseDirectory,
            system_fonts = options.EnableSystemFonts,
            font_paths = options.FontPaths.Select(Path.GetFullPath).ToArray(),
            cache_generations = options.CacheGenerations,
            package_resolution = options.PackageResolution switch
            {
                CetzPackageResolution.CacheThenNetwork => "cache_then_network",
                CetzPackageResolution.CacheOnly => "cache_only",
                _ => "embedded_only"
            },
            restrict_to_directory = options.RestrictToDirectory is null ? null : Path.GetFullPath(options.RestrictToDirectory)
        });
        using var configUtf8 = new Utf8String(config);
        var status = _native.BuilderCreate(configUtf8.Pointer, out var builderPointer, out var error);
        _native.ThrowIfError(status, error);
        using var builder = new BuilderSafeHandle(_native, builderPointer);
        foreach (var font in options.MemoryFonts)
        {
            unsafe
            {
                fixed (byte* data = font.Span)
                {
                    status = _native.BuilderAddFont(builder.DangerousGetHandle(), (IntPtr)data, (nuint)font.Length, out error);
                }
            }
            _native.ThrowIfError(status, error);
        }
        status = _native.BuilderBuild(builder.DangerousGetHandle(), out var rendererPointer, out error);
        _native.ThrowIfError(status, error);
        return new RendererSafeHandle(_native, rendererPointer);
    }

    private CetzRenderResult RenderProjectCore(CetzProject project, CetzRenderSettings settings)
    {
        using var nativeProject = CreateProject(project);
        var formats = settings.Formats.Count == 0
            ? throw new ArgumentException("At least one output format is required.", nameof(settings))
            : settings.Formats.Select(FormatName).ToArray();
        var request = JsonSerializer.Serialize(new { formats, ppi = settings.Ppi, inputs = settings.Inputs });
        using var requestUtf8 = new Utf8String(request);
        var status = _native.RenderProject(_handle.DangerousGetHandle(), nativeProject.DangerousGetHandle(), requestUtf8.Pointer,
            out var resultPointer, out var jsonPointer, out var errorPointer);
        _native.ThrowIfError(status, errorPointer);
        using var result = new ResultSafeHandle(_native, resultPointer);
        var json = _native.TakeString(jsonPointer);
        var metadata = JsonSerializer.Deserialize<NativeResponse>(json, NativeApi.JsonOptions)
            ?? throw new CetzRenderException("Native runtime returned empty result metadata.", "protocol");
        var count = checked((int)_native.ResultCount(result.DangerousGetHandle()));
        if (count != metadata.Outputs.Count)
            throw new CetzRenderException("Native artifact count does not match metadata.", "protocol");
        var artifacts = new List<CetzArtifact>(count);
        for (var index = 0; index < count; index++)
        {
            status = _native.ResultGet(result.DangerousGetHandle(), (nuint)index, out var data, out var length);
            _native.ThrowIfError(status, IntPtr.Zero);
            if (length > int.MaxValue) throw new CetzRenderException("Artifact is too large for a managed byte array.", "protocol");
            var bytes = new byte[(int)length];
            if (bytes.Length > 0) Marshal.Copy(data, bytes, 0, bytes.Length);
            var item = metadata.Outputs[index];
            if (item.Bytes != bytes.Length) throw new CetzRenderException("Native artifact byte count does not match metadata.", "protocol");
            artifacts.Add(new CetzArtifact(ParseFormat(item.Format), item.Page, item.FileName, bytes,
                item.Width, item.Height, item.Stride,
                item.PixelFormat == "rgba8_premultiplied" ? CetzPixelFormat.Rgba8Premultiplied : CetzPixelFormat.None));
        }
        var diagnostics = metadata.Diagnostics.Select(item => new CetzDiagnostic(
            item.Severity == "error" ? CetzDiagnosticSeverity.Error : CetzDiagnosticSeverity.Warning,
            item.Message, item.File, item.Line, item.Column, item.Hints)).ToArray();
        return new CetzRenderResult(metadata.TypstVersion, artifacts, diagnostics,
            new CetzRenderTiming(metadata.EngineInitMs, metadata.CompileMs, metadata.ExportMs, metadata.TotalMs));
    }

    private ProjectSafeHandle CreateProject(CetzProject project)
    {
        using var main = new Utf8String(project.MainPath);
        var status = _native.ProjectCreate(main.Pointer, out var pointer, out var error);
        _native.ThrowIfError(status, error);
        var handle = new ProjectSafeHandle(_native, pointer);
        try
        {
            foreach (var pair in project.Files)
            {
                using var path = new Utf8String(pair.Key);
                unsafe
                {
                    fixed (byte* data = pair.Value.Data)
                    {
                        status = pair.Value.IsText
                            ? _native.ProjectAddText(handle.DangerousGetHandle(), path.Pointer, (IntPtr)data, (nuint)pair.Value.Data.Length, out error)
                            : _native.ProjectAddBinary(handle.DangerousGetHandle(), path.Pointer, (IntPtr)data, (nuint)pair.Value.Data.Length, out error);
                    }
                }
                _native.ThrowIfError(status, error);
            }
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    private static string FormatName(CetzOutputFormat format) => format switch
    {
        CetzOutputFormat.Svg => "svg", CetzOutputFormat.Png => "png",
        CetzOutputFormat.Pdf => "pdf", CetzOutputFormat.Rgba => "rgba",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private string VirtualPathFor(string absolutePath)
    {
        var relative = Path.GetRelativePath(_baseDirectory, absolutePath);
        return relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? Path.GetFileName(absolutePath)
            : relative.Replace('\\', '/');
    }

    private void ValidateFileAccess(string absolutePath)
    {
        if (_restrictedDirectory is null) return;
        var relative = Path.GetRelativePath(_restrictedDirectory, absolutePath);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new CetzConfigurationException($"Input '{absolutePath}' is outside restricted directory '{_restrictedDirectory}'.");
    }

    private static CetzOutputFormat ParseFormat(string format) => format switch
    {
        "svg" => CetzOutputFormat.Svg, "png" => CetzOutputFormat.Png,
        "pdf" => CetzOutputFormat.Pdf, "rgba" => CetzOutputFormat.Rgba,
        _ => throw new CetzRenderException($"Unknown native artifact format: {format}", "protocol")
    };

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Wait();
        try
        {
            _handle.Dispose();
            _native.Dispose();
        }
        finally { _gate.Release(); }
    }
}

internal sealed class Utf8String : IDisposable
{
    public Utf8String(string value) => Pointer = Marshal.StringToCoTaskMemUTF8(value);
    public IntPtr Pointer { get; private set; }
    public void Dispose() { if (Pointer != IntPtr.Zero) { Marshal.FreeCoTaskMem(Pointer); Pointer = IntPtr.Zero; } }
}

internal abstract class NativeSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    protected NativeSafeHandle(NativeApi api, IntPtr pointer) : base(true) { Api = api; SetHandle(pointer); }
    protected NativeApi Api { get; }
}
internal sealed class BuilderSafeHandle(NativeApi api, IntPtr pointer) : NativeSafeHandle(api, pointer)
{ protected override bool ReleaseHandle() { Api.BuilderDestroy(handle); return true; } }
internal sealed class RendererSafeHandle(NativeApi api, IntPtr pointer) : NativeSafeHandle(api, pointer)
{ protected override bool ReleaseHandle() { Api.RendererDestroy(handle); return true; } }
internal sealed class ProjectSafeHandle(NativeApi api, IntPtr pointer) : NativeSafeHandle(api, pointer)
{ protected override bool ReleaseHandle() { Api.ProjectDestroy(handle); return true; } }
internal sealed class ResultSafeHandle(NativeApi api, IntPtr pointer) : NativeSafeHandle(api, pointer)
{ protected override bool ReleaseHandle() { Api.ResultDestroy(handle); return true; } }

internal sealed class NativeResponse
{
    [JsonPropertyName("typst_version")] public string TypstVersion { get; set; } = "";
    [JsonPropertyName("engine_init_ms")] public double EngineInitMs { get; set; }
    [JsonPropertyName("compile_ms")] public double CompileMs { get; set; }
    [JsonPropertyName("export_ms")] public double ExportMs { get; set; }
    [JsonPropertyName("total_ms")] public double TotalMs { get; set; }
    [JsonPropertyName("diagnostics")] public List<NativeDiagnostic> Diagnostics { get; set; } = [];
    [JsonPropertyName("outputs")] public List<NativeArtifact> Outputs { get; set; } = [];
}
internal sealed class NativeDiagnostic
{
    [JsonPropertyName("severity")] public string Severity { get; set; } = "warning";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("file")] public string? File { get; set; }
    [JsonPropertyName("line")] public int? Line { get; set; }
    [JsonPropertyName("column")] public int? Column { get; set; }
    [JsonPropertyName("hints")] public List<string> Hints { get; set; } = [];
}
internal sealed class NativeArtifact
{
    [JsonPropertyName("format")] public string Format { get; set; } = "";
    [JsonPropertyName("page")] public int? Page { get; set; }
    [JsonPropertyName("file_name")] public string FileName { get; set; } = "";
    [JsonPropertyName("bytes")] public int Bytes { get; set; }
    [JsonPropertyName("width")] public int? Width { get; set; }
    [JsonPropertyName("height")] public int? Height { get; set; }
    [JsonPropertyName("stride")] public int? Stride { get; set; }
    [JsonPropertyName("pixel_format")] public string? PixelFormat { get; set; }
}
