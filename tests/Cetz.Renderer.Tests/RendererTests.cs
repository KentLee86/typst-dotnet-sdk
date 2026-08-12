using System.Security.Cryptography;
using Cetz.Renderer;
using Xunit;

namespace Cetz.Renderer.Tests;

public sealed class RendererTests
{
    private static CetzRenderer CreateRenderer(CetzPackageResolution mode = CetzPackageResolution.EmbeddedOnly)
        => new(new CetzRendererOptions
        {
            NativeLibraryPath = NativePath(),
            BaseDirectory = Environment.CurrentDirectory,
            PackageResolution = mode
        });

    [Fact]
    public void MemoryProjectRendersCeTZPdfAndRgbaWithoutProjectFilesOnDisk()
    {
        const string source = """
            #import "@preview/cetz:0.5.2": canvas, draw
            #set page(width: 120pt, height: 80pt, margin: 4pt)
            #canvas({ draw.rect((0, 0), (3, 2), fill: rgb("4f8cff")) })
            """;
        var project = new CetzProjectBuilder().WithMainFile("main.typ").AddText("main.typ", source).Build();
        using var renderer = CreateRenderer();
        var result = renderer.RenderProject(project, new CetzRenderSettings
        {
            Formats = [CetzOutputFormat.Pdf, CetzOutputFormat.Rgba], Ppi = 96
        });
        var pdf = Assert.Single(result.Artifacts, item => item.Format == CetzOutputFormat.Pdf);
        Assert.True(pdf.Data.Span.StartsWith("%PDF-"u8));
        var rgba = Assert.Single(result.Artifacts, item => item.Format == CetzOutputFormat.Rgba);
        Assert.Equal(CetzPixelFormat.Rgba8Premultiplied, rgba.PixelFormat);
        Assert.Equal(rgba.Width * 4, rgba.Stride);
        Assert.Equal(rgba.Stride * rgba.Height, rgba.Data.Length);
    }

    [Fact]
    public void MultipleTypstFilesAndMemoryImageResolve()
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        var project = new CetzProjectBuilder()
            .WithMainFile("docs/main.typ")
            .AddText("docs/main.typ", "#import \"parts.typ\": title\n#title\n#image(\"pixel.png\", width: 10pt)")
            .AddText("docs/parts.typ", "#let title = [Imported]")
            .AddBinary("docs/pixel.png", png)
            .Build();
        using var renderer = CreateRenderer();
        var result = renderer.RenderProject(project, new CetzRenderSettings { Formats = [CetzOutputFormat.Png] });
        Assert.True(Assert.Single(result.Artifacts).Data.Span.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47 }));
    }

    [Fact]
    public void MultiPageFormatsHaveExpectedShapeAndStreams()
    {
        using var renderer = CreateRenderer();
        var result = renderer.RenderSource("First#pagebreak()Second", settings: new CetzRenderSettings
        {
            Formats = [CetzOutputFormat.Svg, CetzOutputFormat.Png, CetzOutputFormat.Pdf, CetzOutputFormat.Rgba]
        });
        Assert.Equal(7, result.Artifacts.Count);
        foreach (var artifact in result.Artifacts)
        {
            using var stream = artifact.OpenRead();
            Assert.Equal(artifact.Data.Length, stream.Length);
        }
    }

    [Fact]
    public void CompilationFailureIsTypedAndRendererSurvives()
    {
        using var renderer = CreateRenderer();
        var exception = Assert.Throws<CetzCompilationException>(() => renderer.RenderSource("#this-function-does-not-exist()"));
        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(CetzDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.NotEmpty(diagnostic.Message);
        Assert.Equal("main.typ", diagnostic.File);
        Assert.Equal(1, diagnostic.Line);
        Assert.NotNull(diagnostic.Column);
        var valid = renderer.RenderSource("Still alive");
        Assert.True(Assert.Single(valid.Artifacts).Data.Span.StartsWith("%PDF-"u8));
    }

    [Fact]
    public async Task CallsAreSerializedAndPreCanceledWaitIsObserved()
    {
        using var renderer = CreateRenderer();
        var calls = Enumerable.Range(0, 4).Select(index => renderer.RenderSourceAsync($"Frame {index}", cancellationToken: CancellationToken.None));
        var results = await Task.WhenAll(calls);
        Assert.Equal(4, results.Length);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            renderer.RenderSourceAsync("Canceled", cancellationToken: new CancellationToken(canceled: true)));
    }

    [Fact]
    public void WriteToDirectoryPreservesBytes()
    {
        using var renderer = CreateRenderer();
        var result = renderer.RenderSource("Saved", settings: new CetzRenderSettings { Formats = [CetzOutputFormat.Pdf, CetzOutputFormat.Png] });
        var directory = Path.Combine(Path.GetTempPath(), "cetz-sdk-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = result.WriteToDirectory(directory);
            Assert.Equal(result.Artifacts.Count, paths.Count);
            foreach (var artifact in result.Artifacts)
            {
                var actual = File.ReadAllBytes(Path.Combine(directory, artifact.FileName));
                Assert.Equal(SHA256.HashData(artifact.Data.Span), SHA256.HashData(actual));
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ProjectBuilderRejectsTraversalAndDuplicates()
    {
        Assert.Throws<ArgumentException>(() => new CetzProjectBuilder().AddText("../main.typ", "x"));
        var builder = new CetzProjectBuilder().AddText("main.typ", "x");
        Assert.Throws<ArgumentException>(() => builder.AddText("main.typ", "y"));
    }

    [Fact]
    public void SysInputsAndMemoryFontAreUsable()
    {
        var root = RepositoryRoot();
        var options = new CetzRendererOptions
        {
            NativeLibraryPath = NativePath(),
            BaseDirectory = root,
            PackageResolution = CetzPackageResolution.EmbeddedOnly
        };
        options.MemoryFonts.Add(File.ReadAllBytes(Path.Combine(root, "native", "cetz-dotnet-native", "assets", "fonts", "NotoSansKR-Regular.otf")));
        using var renderer = new CetzRenderer(options);
        var first = renderer.RenderSource("#import sys: inputs\n#inputs.value", settings: new CetzRenderSettings
        {
            Formats = [CetzOutputFormat.Rgba], Inputs = new Dictionary<string, object?> { ["value"] = "one" }
        });
        var second = renderer.RenderSource("#import sys: inputs\n#inputs.value", settings: new CetzRenderSettings
        {
            Formats = [CetzOutputFormat.Rgba], Inputs = new Dictionary<string, object?> { ["value"] = "two" }
        });
        Assert.NotEqual(first.Artifacts[0].Data.ToArray(), second.Artifacts[0].Data.ToArray());
    }

    [Fact]
    public void FileApiAllowsAbsolutePathAndRestrictionRejectsOutside()
    {
        var root = RepositoryRoot();
        var input = Path.Combine(root, "tests", "absolute.typ");
        File.WriteAllText(input, "Absolute file");
        try
        {
            using (var renderer = CreateRenderer())
                Assert.True(renderer.RenderFile(input).Artifacts[0].Data.Span.StartsWith("%PDF-"u8));
            using var restricted = new CetzRenderer(new CetzRendererOptions
            {
                NativeLibraryPath = NativePath(), BaseDirectory = root,
                RestrictToDirectory = Path.Combine(root, "samples"),
                PackageResolution = CetzPackageResolution.EmbeddedOnly
            });
            Assert.Throws<CetzConfigurationException>(() => restricted.RenderFile(input));
        }
        finally { File.Delete(input); }
    }

    private static string NativePath()
    {
        var configured = Environment.GetEnvironmentVariable("CETZ_NATIVE_LIBRARY");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var root = RepositoryRoot();
        return OperatingSystem.IsWindows()
            ? Path.Combine(root, "artifacts", "native", "win-x64", "cetz_dotnet_native.dll")
            : Path.Combine(root, "artifacts", "native", "linux-x64", "libcetz_dotnet_native.so");
    }

    private static string RepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
