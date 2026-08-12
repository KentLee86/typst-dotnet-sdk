using System.Collections.ObjectModel;
using System.Text;

namespace Typst.Renderer;

public enum TypstOutputFormat { Svg, Png, Pdf, Rgba }
public enum TypstPixelFormat { None, Rgba8Premultiplied }
public enum TypstPackageResolution { CacheThenNetwork, CacheOnly, EmbeddedOnly }
public enum TypstDiagnosticSeverity { Warning, Error }

public sealed class TypstRendererOptions
{
    public string BaseDirectory { get; set; } = Environment.CurrentDirectory;
    public string? RestrictToDirectory { get; set; }
    public bool EnableSystemFonts { get; set; }
    public IList<string> FontPaths { get; } = new List<string>();
    public IList<ReadOnlyMemory<byte>> MemoryFonts { get; } = new List<ReadOnlyMemory<byte>>();
    public int CacheGenerations { get; set; } = 10;
    public TypstPackageResolution PackageResolution { get; set; } = TypstPackageResolution.CacheThenNetwork;
    public string? NativeLibraryPath { get; set; }
}

public sealed class TypstRenderSettings
{
    public IReadOnlyCollection<TypstOutputFormat> Formats { get; set; } = new[] { TypstOutputFormat.Pdf };
    public float Ppi { get; set; } = 144;
    public IReadOnlyDictionary<string, object?> Inputs { get; set; } = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
}

public sealed record TypstDiagnostic(
    TypstDiagnosticSeverity Severity,
    string Message,
    string? File,
    int? Line,
    int? Column,
    IReadOnlyList<string> Hints);

public sealed record TypstRenderTiming(
    double EngineInitializationMilliseconds,
    double CompileMilliseconds,
    double ExportMilliseconds,
    double TotalMilliseconds);

public sealed class TypstArtifact
{
    private readonly byte[] _data;

    internal TypstArtifact(TypstOutputFormat format, int? page, string fileName, byte[] data,
        int? width, int? height, int? stride, TypstPixelFormat pixelFormat)
    {
        Format = format;
        Page = page;
        FileName = fileName;
        _data = data;
        Width = width;
        Height = height;
        Stride = stride;
        PixelFormat = pixelFormat;
    }

    public TypstOutputFormat Format { get; }
    public int? Page { get; }
    public string FileName { get; }
    public ReadOnlyMemory<byte> Data => _data;
    public int? Width { get; }
    public int? Height { get; }
    public int? Stride { get; }
    public TypstPixelFormat PixelFormat { get; }
    public Stream OpenRead() => new MemoryStream(_data, writable: false);
}

public sealed class TypstRenderResult
{
    internal TypstRenderResult(string typstVersion, IReadOnlyList<TypstArtifact> artifacts,
        IReadOnlyList<TypstDiagnostic> diagnostics, TypstRenderTiming timing)
    {
        TypstVersion = typstVersion;
        Artifacts = artifacts;
        Diagnostics = diagnostics;
        Timing = timing;
    }

    public string TypstVersion { get; }
    public IReadOnlyList<TypstArtifact> Artifacts { get; }
    public IReadOnlyList<TypstDiagnostic> Diagnostics { get; }
    public TypstRenderTiming Timing { get; }

    public IReadOnlyList<string> WriteToDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        var paths = new List<string>(Artifacts.Count);
        foreach (var artifact in Artifacts)
        {
            var path = Path.Combine(directory, artifact.FileName);
            File.WriteAllBytes(path, artifact.Data.ToArray());
            paths.Add(path);
        }
        return paths;
    }

    public async Task<IReadOnlyList<string>> WriteToDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        var paths = new List<string>(Artifacts.Count);
        foreach (var artifact in Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, artifact.FileName);
            await File.WriteAllBytesAsync(path, artifact.Data.ToArray(), cancellationToken).ConfigureAwait(false);
            paths.Add(path);
        }
        return paths;
    }
}

public class TypstRenderException : Exception
{
    public TypstRenderException(string message, string errorKind, Exception? innerException = null) : base(message, innerException)
        => ErrorKind = errorKind;
    public string ErrorKind { get; }
}

public sealed class TypstCompilationException : TypstRenderException
{
    public TypstCompilationException(string message, IReadOnlyList<TypstDiagnostic> diagnostics)
        : base(message, "compilation") => Diagnostics = diagnostics;
    public IReadOnlyList<TypstDiagnostic> Diagnostics { get; }
}

public sealed class TypstConfigurationException : TypstRenderException
{
    public TypstConfigurationException(string message, Exception? innerException = null)
        : base(message, "configuration", innerException) { }
}

public sealed class TypstNativeLibraryException : TypstRenderException
{
    public TypstNativeLibraryException(string message, Exception? innerException = null)
        : base(message, "native_library", innerException) { }
}

public sealed class TypstProject
{
    internal TypstProject(string mainPath, IReadOnlyDictionary<string, ProjectFile> files)
    {
        MainPath = mainPath;
        Files = files;
    }
    public string MainPath { get; }
    public IReadOnlyCollection<string> Paths => Files.Keys.ToArray();
    internal IReadOnlyDictionary<string, ProjectFile> Files { get; }
}

internal sealed record ProjectFile(byte[] Data, bool IsText);

public sealed class TypstProjectBuilder
{
    private readonly Dictionary<string, ProjectFile> _files = new(StringComparer.Ordinal);
    private string? _mainPath;

    public TypstProjectBuilder WithMainFile(string path)
    {
        _mainPath = ValidatePath(path);
        return this;
    }

    public TypstProjectBuilder AddText(string path, string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Add(path, new ProjectFile(Encoding.UTF8.GetBytes(source), true));
        return this;
    }

    public TypstProjectBuilder AddBinary(string path, ReadOnlySpan<byte> data)
    {
        Add(path, new ProjectFile(data.ToArray(), false));
        return this;
    }

    public TypstProject Build()
    {
        var main = _mainPath ?? throw new InvalidOperationException("A main Typst file is required.");
        if (!_files.TryGetValue(main, out var file) || !file.IsText)
            throw new InvalidOperationException($"The main Typst file '{main}' must exist as text.");
        return new TypstProject(main, new ReadOnlyDictionary<string, ProjectFile>(new Dictionary<string, ProjectFile>(_files, StringComparer.Ordinal)));
    }

    private void Add(string path, ProjectFile file)
    {
        path = ValidatePath(path);
        if (!_files.TryAdd(path, file)) throw new ArgumentException($"Duplicate project path: {path}", nameof(path));
    }

    internal static string ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = path.Replace('\\', '/');
        if (Path.IsPathRooted(path) || path.Split('/').Any(segment => segment == ".."))
            throw new ArgumentException("Project paths must be relative and must not contain '..'.", nameof(path));
        return string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
