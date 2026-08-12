# Cetz.Renderer

Production-oriented .NET 8 SDK for rendering Typst 0.14.2 and CeTZ 0.5.2
in-process. The package returns SVG, PNG, PDF, or premultiplied RGBA8 as managed
memory. It never starts `typst.exe`, `cetz-render`, Node, or Sharp.

## Packages

Reference one runtime package. It brings the exact matching managed package:

```xml
<PackageReference Include="Cetz.Renderer.Native.win-x64" Version="0.1.0" />
```

Use `Cetz.Renderer.Native.linux-x64` on Linux x64. Native assets follow NuGet's
`runtimes/{rid}/native/` convention.

GUI integrations are layered so the native SDK stays independent from every UI
framework:

- `Cetz.Renderer.Core` turns renderer results into display-ready RGBA documents
  and owns the shared render, zoom, layout, view-mode, and navigation behavior.
- `Cetz.Renderer.Avalonia` displays those documents with a reusable `CetzView`.
- `Cetz.Renderer.WinUI` provides a reusable WinUI 3 `CetzView` on the current
  stable Windows App SDK. It targets `net8.0-windows10.0.19041.0`.

## Avalonia

Render through the common Core layer and assign the result to the Avalonia view:

```csharp
using Cetz.Renderer.Avalonia;
using Cetz.Renderer.Core;

using var renderer = new CetzDocumentRenderer();
var document = await renderer.RenderSourceAsync(typstSource);

var view = new CetzView
{
    Document = document,
    Zoom = 1.0
};
```

Run the interactive editor and preview sample from the repository root:

```powershell
dotnet run --project samples/Cetz.Renderer.Avalonia.Sample
```

The sample's demo selector is backed by `Cetz.Renderer.Demo.Shared`. Its nine
embedded examples are UI-independent in-memory projects shared by every GUI
sample without copying files again.

## WinUI 3

`CetzView` implements the common `ICetzDocumentView` contract. The shared
`CetzDocumentViewController` is the sole authority for zoom normalization,
fit-width/fit-page, continuous or paged single/facing layouts, current-page
navigation, and exact page bounds. The WinUI adapter only converts RGBA to BGRA,
owns page image resources, dispatches UI access, and scrolls continuous layouts.
Assign `Document` on the UI thread, or use `SetDocumentAsync` after rendering on
any thread:

```csharp
using Cetz.Renderer.Core;
using Cetz.Renderer.WinUI;

using var renderer = new CetzDocumentRenderer();
var document = await renderer.RenderSourceAsync(typstSource);

var view = new CetzView
{
    ZoomMode = CetzZoomMode.FitWidth,
    ViewMode = CetzPageViewMode.ContinuousFacing,
    PageSpacing = 24
};
await view.SetDocumentAsync(document);
view.MoveNext();
```

The unpackaged x64 sample uses `CetzRenderController` for latest-request-wins
rendering while retaining the previous preview after an error. It exposes the
shared nine-demo catalog, editable source, Custom/Fit width/Fit page,
continuous/single/facing page modes, previous/next navigation, and page status:

```powershell
$env:CETZ_NATIVE_LIBRARY = 'C:\path\to\cetz_dotnet_native.dll'
dotnet run --project samples/Cetz.Renderer.WinUI.Sample -c Release
```

Call `Dispose` on the view from its UI thread before detaching its containing
visual tree when the host continues running. This clears the document and every
page image reference. Replacing `Document` also releases the previous page
images immediately. The sample cancels in-flight work and disposes its renderer
when its sole application window closes.

## Memory rendering

```csharp
using Cetz.Renderer;

using var renderer = new CetzRenderer(new CetzRendererOptions
{
    PackageResolution = CetzPackageResolution.EmbeddedOnly
});

var project = new CetzProjectBuilder()
    .WithMainFile("charts/main.typ")
    .AddText("charts/main.typ", """
        #import "@preview/cetz:0.5.2": canvas, draw
        #import "data.typ": values
        #canvas({ draw.rect((0, 0), (values.at(0), 2), fill: blue) })
        """)
    .AddText("charts/data.typ", "#let values = (3, 5, 8)")
    .Build();

var result = renderer.RenderProject(project, new CetzRenderSettings
{
    Formats = [CetzOutputFormat.Pdf, CetzOutputFormat.Rgba],
    Ppi = 96
});

ReadOnlyMemory<byte> pdf = result.Artifacts.Single(x => x.Format == CetzOutputFormat.Pdf).Data;
using Stream pdfStream = result.Artifacts.Single(x => x.Format == CetzOutputFormat.Pdf).OpenRead();
await result.WriteToDirectoryAsync("rendered");
```

`RenderFile`, `RenderSource`, and `RenderProject` each have async counterparts.
One renderer serializes calls. Multiple renderer instances may run in parallel.
Cancellation stops waiting for an instance; it does not interrupt a Typst
compilation that has already entered native code.

Project paths are normalized relative paths. Absolute paths, `..`, duplicates,
and a missing text main file are rejected. Text and arbitrary binary files can be
mixed, so in-memory images and imported `.typ` modules need no temporary files.

## Configuration

System font discovery is off by default. `FontPaths` and `MemoryFonts` are
validated when the renderer is created. `BaseDirectory` supplies fallback files
for relative imports. Set `RestrictToDirectory` for untrusted documents; it
becomes the native filesystem root.

Package resolution always checks embedded CeTZ 0.5.2 and oxifmt 1.0.0 first:

- `CacheThenNetwork`: local Typst cache, then the Typst package service.
- `CacheOnly`: local cache with no download.
- `EmbeddedOnly`: embedded packages only.

`NativeLibraryPath` is intended for development and diagnostics. Normal NuGet
consumers rely on RID assets. Handles are owned by `SafeHandle`; Rust-owned
strings and result buffers are copied and released before public results return.

## Build and test

```powershell
cargo test --locked
cargo build --release --locked
New-Item -ItemType Directory -Force artifacts/native/win-x64 | Out-Null
Copy-Item target/release/cetz_dotnet_native.dll artifacts/native/win-x64/
dotnet test -c Release
dotnet build samples/Cetz.Renderer.WinUI.Sample -c Release
dotnet pack src/Cetz.Renderer/Cetz.Renderer.csproj -c Release -o artifacts/packages
dotnet pack src/Cetz.Renderer.Native.win-x64/Cetz.Renderer.Native.win-x64.csproj -c Release -o artifacts/packages
```

The repository directly links Typst, CeTZ, and oxifmt in its Rust `cdylib`; it
does not depend on the `cetz-renderer` repository at build time or runtime.
