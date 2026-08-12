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
- `Cetz.Renderer.Uno` displays those documents with a reusable WinUI/Uno
  `CetzView`.
- `Cetz.Renderer.WinForms` provides a native Windows Forms `CetzView` with DPI-aware
  zooming and scrolling multi-page layout.
- `Cetz.Renderer.Wpf` displays the same documents in a reusable WPF `CetzView`.
- `Cetz.Renderer.WinUI` provides a reusable WinUI 3 `CetzView` on the current
  stable Windows App SDK. It targets `net8.0-windows10.0.19041.0`.
- GUI adapters share one view/rendering contract; see
  [GUI adapter contract](docs/gui-adapter-contract.md) for fitting, page modes,
  navigation, lifecycle, and demo requirements.

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
embedded examples are UI-independent in-memory projects, so every GUI demo can
reuse the same catalog without copying files again.

## WPF

`Cetz.Renderer.Wpf` targets `net8.0-windows` and has no third-party runtime
dependency. Place its view in a WPF `ScrollViewer` for multi-page scrolling:

```csharp
using Cetz.Renderer.Core;
using Cetz.Renderer.Wpf;

using var renderer = new CetzDocumentRenderer();
var document = await renderer.RenderSourceAsync(typstSource);

var view = new CetzView
{
    Document = document,
    Zoom = 1.0,
    PageSpacing = 24
};
var preview = new System.Windows.Controls.ScrollViewer { Content = view };
```

The adapter converts premultiplied RGBA pages to WPF's premultiplied BGRA
format, preserves each page PPI, and sizes pages in device-independent pixels.
Dispose the view to release its cached images and document reference
deterministically.

Run the editable nine-demo WPF sample from the repository root:

```powershell
dotnet run --project samples/Cetz.Renderer.Wpf.Sample
```

Pass `-- --software-rendering` when a remote desktop or capture environment
cannot record WPF's hardware-composited client area.

## Uno Platform

Reference the UI adapter in addition to the native RID package:

```xml
<PackageReference Include="Cetz.Renderer.Uno" Version="0.1.0" />
<PackageReference Include="Cetz.Renderer.Native.win-x64" Version="0.1.0" />
```

The Uno adapter implements the shared `ICetzDocumentView` contract and delegates
zoom fitting, page modes, navigation, and exact placement to
`CetzDocumentViewController`. It converts premultiplied RGBA pages to WinUI's
premultiplied BGRA layout; the adapter owns only Uno bitmap and visual resources:

```csharp
using Cetz.Renderer.Core;
using Cetz.Renderer.Uno;

var view = new CetzView();
view.SetViewport(1200, 800);
using var renderController = new CetzRenderController(view);
await renderController.RenderSourceAsync(typstSource);
view.SetZoomMode(CetzZoomMode.FitWidth);
view.SetViewMode(CetzPageViewMode.ContinuousFacing);
view.MoveNext();
```

Run the desktop Uno editor and scrolling multi-page preview from the repository
root. It uses the same nine `Cetz.Renderer.Demo.Shared` examples as the Avalonia
sample and exposes custom/width/page zoom, continuous/single/facing page modes,
and previous/next navigation:

```powershell
dotnet run --project samples/Cetz.Renderer.Uno.Sample -f net8.0-desktop
```

Set `CETZ_NATIVE_LIBRARY` to a built `cetz_dotnet_native.dll` when it is not
available under `artifacts/native/win-x64/` before building the sample.
The verified targets are `net8.0-desktop` (Skia Desktop) and
`net8.0-windows10.0.26100` (Windows App SDK). The adapter package also contains
the framework-neutral `net8.0` Uno asset for other Uno heads.

## Windows Forms

The Windows Forms adapter targets `net8.0-windows` and has no third-party runtime
dependency. It copies Core's premultiplied RGBA pages into control-owned GDI+
premultiplied BGRA bitmaps, preserves alpha, and combines render PPI with the
monitor DPI and `Zoom` when laying out pages:

```csharp
using Cetz.Renderer.Core;
using Cetz.Renderer.WinForms;

using var renderer = new CetzDocumentRenderer();
var document = await renderer.RenderSourceAsync(typstSource);

var view = new CetzView
{
    Dock = DockStyle.Fill,
    Document = document,
    Zoom = 1.0,
    PageSpacing = 24
};
```

`CetzView` owns its converted bitmaps and disposes them when the document changes
or the control is disposed. Its built-in scrolling surface paints only visible
pages. Run the Windows Forms editor and multi-page preview from the repository root:

```powershell
dotnet run --project samples/Cetz.Renderer.WinForms.Sample
```

Both GUI samples reuse all nine projects in `Cetz.Renderer.Demo.Shared`, including
multi-file imports, embedded SVG assets, and multi-page documents.

## WinUI 3

`CetzView` implements `ICetzDocumentView` and delegates fitting, page modes,
navigation, and exact page bounds to `CetzDocumentViewController`. The WinUI
adapter owns only native image resources, UI dispatch, and scrolling:

```csharp
using Cetz.Renderer.Core;
using Cetz.Renderer.WinUI;

var view = new CetzView
{
    ZoomMode = CetzZoomMode.FitWidth,
    ViewMode = CetzPageViewMode.ContinuousFacing,
    PageSpacing = 24
};
await view.SetDocumentAsync(document);
view.MoveNext();
```

The unpackaged x64 sample uses `CetzRenderController`, the shared nine-demo
catalog, all fitting and page modes, navigation, and page status:

```powershell
$env:CETZ_NATIVE_LIBRARY = 'C:\path\to\cetz_dotnet_native.dll'
dotnet run --project samples/Cetz.Renderer.WinUI.Sample -c Release
```

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
dotnet pack src/Cetz.Renderer.Wpf/Cetz.Renderer.Wpf.csproj -c Release -o artifacts/packages
dotnet build samples/Cetz.Renderer.WinUI.Sample -c Release
dotnet pack src/Cetz.Renderer/Cetz.Renderer.csproj -c Release -o artifacts/packages
dotnet pack src/Cetz.Renderer.WinForms/Cetz.Renderer.WinForms.csproj -c Release -o artifacts/packages
dotnet pack src/Cetz.Renderer.Native.win-x64/Cetz.Renderer.Native.win-x64.csproj -c Release -o artifacts/packages
./eng/pack-and-verify.ps1 -Rid win-x64
```

The repository directly links Typst, CeTZ, and oxifmt in its Rust `cdylib`; it
does not depend on the `cetz-renderer` repository at build time or runtime.
