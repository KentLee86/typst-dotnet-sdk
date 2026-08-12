using Typst.Renderer.Core;
using Xunit;

namespace Typst.Renderer.Avalonia.Sample;

public sealed class QuotationTemplateTests
{
    [Fact]
    public async Task EditedRecipientFieldsRenderAsAOnePageQuotation()
    {
        var fields = QuotationTemplate.Defaults with
        {
            RecipientName = "오픈AI 코리아",
            RegistrationNumber = "101-23-45678",
            ContactName = "이담당",
            Phone = "010-9876-5432",
            Email = "lee@example.kr",
            Address = "서울특별시 강남구 테헤란로 123",
            ProjectName = "실시간 견적서 렌더링",
            QuoteDate = "2026. 08. 12"
        };
        var source = QuotationTemplate.Build(fields);

        Assert.Contains("오픈AI 코리아", source, StringComparison.Ordinal);
        Assert.Contains("실시간 견적서 렌더링", source, StringComparison.Ordinal);

        using var renderer = new TypstDocumentRenderer(RendererOptions());
        var document = await renderer.RenderSourceAsync(
            source,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(document.Pages);
        Assert.True(document.Pages[0].PixelWidth > 0);
        Assert.True(document.Pages[0].PixelHeight > 0);
    }

    [Fact]
    public async Task QuotesBackslashesAndNewlinesAreEscapedBeforeRendering()
    {
        var fields = QuotationTemplate.Defaults with
        {
            RecipientName = "가나다 \"연구소\"",
            Address = "서울\\판교\n제2사옥"
        };
        var source = QuotationTemplate.Build(fields);

        Assert.Contains("가나다 \\\"연구소\\\"", source, StringComparison.Ordinal);
        Assert.Contains("서울\\\\판교\\n제2사옥", source, StringComparison.Ordinal);

        using var renderer = new TypstDocumentRenderer(RendererOptions());
        var document = await renderer.RenderSourceAsync(
            source,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(document.Pages);
    }

    private static TypstRendererOptions RendererOptions() => new()
    {
        NativeLibraryPath = NativePath(),
        BaseDirectory = Environment.CurrentDirectory,
        PackageResolution = TypstPackageResolution.EmbeddedOnly
    };

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
