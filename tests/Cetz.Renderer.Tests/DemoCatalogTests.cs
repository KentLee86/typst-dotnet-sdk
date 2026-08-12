using Cetz.Renderer.Core;
using Cetz.Renderer.Demo.Shared;
using Xunit;

namespace Cetz.Renderer.Tests;

public sealed class DemoCatalogTests
{
    [Fact]
    public void CatalogContainsNineSelfContainedProjects()
    {
        Assert.Equal(9, CetzDemoCatalog.All.Count);
        Assert.Equal(9, CetzDemoCatalog.All.Select(demo => demo.Id).Distinct().Count());

        foreach (var demo in CetzDemoCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(demo.Source));
            var project = demo.CreateProject();
            Assert.Equal(demo.MainPath, project.MainPath);
            Assert.Contains(demo.MainPath, project.Paths);
        }

        Assert.Equal(2, CetzDemoCatalog.Get("serial-protocol").Paths.Count);
        Assert.Equal(3, CetzDemoCatalog.Get("korean-business-forms").Paths.Count);
    }

    [Fact]
    public void EverySharedDemoRendersThroughCore()
    {
        using var renderer = new CetzDocumentRenderer(new CetzRendererOptions
        {
            NativeLibraryPath = NativePath(),
            BaseDirectory = Environment.CurrentDirectory,
            PackageResolution = CetzPackageResolution.EmbeddedOnly
        });

        foreach (var demo in CetzDemoCatalog.All)
        {
            var document = renderer.RenderProject(
                demo.CreateProject(),
                new CetzDocumentRenderOptions { Ppi = 72 });
            Assert.NotEmpty(document.Pages);
            Assert.All(document.Pages, page => Assert.NotEmpty(page.Pixels.ToArray()));
        }
    }

    private static string NativePath()
    {
        var configured = Environment.GetEnvironmentVariable("CETZ_NATIVE_LIBRARY");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return OperatingSystem.IsWindows()
            ? Path.Combine(root, "artifacts", "native", "win-x64", "cetz_dotnet_native.dll")
            : Path.Combine(root, "artifacts", "native", "linux-x64", "libcetz_dotnet_native.so");
    }
}
