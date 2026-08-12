using Typst.Renderer.Core;
using Typst.Renderer.Demo.Shared;
using Xunit;

namespace Typst.Renderer.Tests;

public sealed class DemoCatalogTests
{
    [Fact]
    public void CatalogContainsNineSelfContainedProjects()
    {
        Assert.Equal(9, TypstDemoCatalog.All.Count);
        Assert.Equal(9, TypstDemoCatalog.All.Select(demo => demo.Id).Distinct().Count());

        foreach (var demo in TypstDemoCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(demo.Source));
            var project = demo.CreateProject();
            Assert.Equal(demo.MainPath, project.MainPath);
            Assert.Contains(demo.MainPath, project.Paths);
        }

        Assert.Equal(2, TypstDemoCatalog.Get("serial-protocol").Paths.Count);
        Assert.Equal(3, TypstDemoCatalog.Get("korean-business-forms").Paths.Count);
    }

    [Fact]
    public void EverySharedDemoRendersThroughCore()
    {
        using var renderer = new TypstDocumentRenderer(new TypstRendererOptions
        {
            NativeLibraryPath = NativePath(),
            BaseDirectory = Environment.CurrentDirectory,
            PackageResolution = TypstPackageResolution.EmbeddedOnly
        });

        foreach (var demo in TypstDemoCatalog.All)
        {
            var document = renderer.RenderProject(
                demo.CreateProject(),
                new TypstDocumentRenderOptions { Ppi = 72 });
            Assert.NotEmpty(document.Pages);
            Assert.All(document.Pages, page => Assert.NotEmpty(page.Pixels.ToArray()));
        }
    }

    private static string NativePath()
    {
        var configured = Environment.GetEnvironmentVariable("TYPST_NATIVE_LIBRARY");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return OperatingSystem.IsWindows()
            ? Path.Combine(root, "artifacts", "native", "win-x64", "typst_dotnet_native.dll")
            : Path.Combine(root, "artifacts", "native", "linux-x64", "libtypst_dotnet_native.so");
    }
}
