# Typst.Renderer

[한국어](README.ko.md)

Production-oriented .NET 8 SDK for rendering Typst 0.14.2 and CeTZ 0.5.2
in-process. The package returns SVG, PNG, PDF, or premultiplied RGBA8 as managed
memory. It never starts `typst.exe`, `cetz-render`, Node, or Sharp.

## Packages

Reference one runtime package. It brings the exact matching managed package:

```xml
<PackageReference Include="Typst.Renderer.Native.win-x64" Version="0.1.0" />
```

Use `Typst.Renderer.Native.linux-x64` on Linux x64. Native assets follow NuGet's
`runtimes/{rid}/native/` convention.

GUI integrations are layered so the native SDK stays independent from every UI
framework:

- `Typst.Renderer.Core` turns renderer results into display-ready RGBA documents
  and owns the shared render, zoom, layout, view-mode, and navigation behavior.
- `Typst.Renderer.Avalonia` displays those documents with a reusable `TypstView`.
- `Typst.Renderer.Uno` displays those documents with a reusable WinUI/Uno
  `TypstView`.
- `Typst.Renderer.WinForms` provides a native Windows Forms `TypstView` with DPI-aware
  zooming and scrolling multi-page layout.
- `Typst.Renderer.Wpf` displays the same documents in a reusable WPF `TypstView`.
- `Typst.Renderer.WinUI` provides a reusable WinUI 3 `TypstView` on the current
  stable Windows App SDK. It targets `net8.0-windows10.0.19041.0`.
- GUI adapters share one view/rendering contract; see
  [GUI adapter contract](docs/gui-adapter-contract.md) for fitting, page modes,
  navigation, lifecycle, and demo requirements.

## GUI samples

The primary desktop sample screenshots below use the same window size and render
the same nine-page `Serial protocol` project from `Typst.Renderer.Demo.Shared`.
The second Avalonia screenshot shows its additional live quotation workflow.

| [Avalonia](samples/Typst.Renderer.Avalonia.Sample/) |
| --- |
| ![Avalonia sample rendering the Serial protocol demo](docs/assets/sample-avalonia.jpg) |

| [Avalonia — Dynamic quotation](samples/Typst.Renderer.Avalonia.Sample/) |
| --- |
| ![Avalonia sample rendering the live dynamic quotation](docs/assets/sample-avalonia-dynamic-quotation.jpg) |

| [Uno Platform](samples/Typst.Renderer.Uno.Sample/) |
| --- |
| ![Uno sample rendering the Serial protocol demo](docs/assets/sample-uno.jpg) |

| [Windows Forms](samples/Typst.Renderer.WinForms.Sample/) |
| --- |
| ![Windows Forms sample rendering the Serial protocol demo](docs/assets/sample-winforms.jpg) |

| [WPF](samples/Typst.Renderer.Wpf.Sample/) |
| --- |
| ![WPF sample rendering the Serial protocol demo](docs/assets/sample-wpf.jpg) |

| [WinUI 3](samples/Typst.Renderer.WinUI.Sample/) |
| --- |
| ![WinUI 3 sample rendering the Serial protocol demo](docs/assets/sample-winui3.jpg) |

## Avalonia

Render through the common Core layer and assign the result to the Avalonia view:

```csharp
using Typst.Renderer.Avalonia;
using Typst.Renderer.Core;

using var renderer = new TypstDocumentRenderer();
var document = await renderer.RenderSourceAsync(typstSource);

var view = new TypstView
{
    Document = document,
    Zoom = 1.0
};
```

Run the interactive editor and preview sample from the repository root:

```powershell
dotnet run --project samples/Typst.Renderer.Avalonia.Sample
dotnet run --project samples/Typst.Renderer.Avalonia.Sample -- --demo live-quotation
```

The sample's demo selector is backed by `Typst.Renderer.Demo.Shared`. Its nine
embedded examples are UI-independent in-memory projects, so every GUI demo can
reuse the same catalog without copying files again. The Avalonia-only
`Dynamic quotation` example also exposes editable recipient fields and
automatically refreshes both the Typst source and rendered preview.

## WPF

`Typst.Renderer.Wpf` targets `net8.0-windows` and has no third-party runtime
dependency. Place its view in a WPF `ScrollViewer` for multi-page scrolling:

```csharp
using Typst.Renderer.Core;
using Typst.Renderer.Wpf;

using var renderer = new TypstDocumentRenderer();
var document = await renderer.RenderSourceAsync(typstSource);

var view = new TypstView
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
dotnet run --project samples/Typst.Renderer.Wpf.Sample
```

Pass `-- --software-rendering` when a remote desktop or capture environment
cannot record WPF's hardware-composited client area.

## Uno Platform

Reference the UI adapter in addition to the native RID package:

```xml
<PackageReference Include="Typst.Renderer.Uno" Version="0.1.0" />
<PackageReference Include="Typst.Renderer.Native.win-x64" Version="0.1.0" />
```

The Uno adapter implements the shared `ITypstDocumentView` contract and delegates
zoom fitting, page modes, navigation, and exact placement to
`TypstDocumentViewController`. It converts premultiplied RGBA pages to WinUI's
premultiplied BGRA layout; the adapter owns only Uno bitmap and visual resources:

```csharp
using Typst.Renderer.Core;
using Typst.Renderer.Uno;

var view = new TypstView();
view.SetViewport(1200, 800);
using var renderController = new TypstRenderController(view);
await renderController.RenderSourceAsync(typstSource);
view.SetZoomMode(TypstZoomMode.FitWidth);
view.SetViewMode(TypstPageViewMode.ContinuousFacing);
view.MoveNext();
```

Run the desktop Uno editor and scrolling multi-page preview from the repository
root. It uses the same nine `Typst.Renderer.Demo.Shared` examples as the Avalonia
sample and exposes custom/width/page zoom, continuous/single/facing page modes,
and previous/next navigation:

```powershell
dotnet run --project samples/Typst.Renderer.Uno.Sample -f net8.0-desktop
```

Set `TYPST_NATIVE_LIBRARY` to a built `typst_dotnet_native.dll` when it is not
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
using Typst.Renderer.Core;
using Typst.Renderer.WinForms;

using var renderer = new TypstDocumentRenderer();
var document = await renderer.RenderSourceAsync(typstSource);

var view = new TypstView
{
    Dock = DockStyle.Fill,
    Document = document,
    Zoom = 1.0,
    PageSpacing = 24
};
```

`TypstView` owns its converted bitmaps and disposes them when the document changes
or the control is disposed. Its built-in scrolling surface paints only visible
pages. Run the Windows Forms editor and multi-page preview from the repository root:

```powershell
dotnet run --project samples/Typst.Renderer.WinForms.Sample
```

All GUI samples reuse all nine projects in `Typst.Renderer.Demo.Shared`, including
multi-file imports, embedded SVG assets, and multi-page documents.

## WinUI 3

`TypstView` implements `ITypstDocumentView` and delegates fitting, page modes,
navigation, and exact page bounds to `TypstDocumentViewController`. The WinUI
adapter owns only native image resources, UI dispatch, and scrolling:

```csharp
using Typst.Renderer.Core;
using Typst.Renderer.WinUI;

var view = new TypstView
{
    ZoomMode = TypstZoomMode.FitWidth,
    ViewMode = TypstPageViewMode.ContinuousFacing,
    PageSpacing = 24
};
await view.SetDocumentAsync(document);
view.MoveNext();
```

The unpackaged x64 sample uses `TypstRenderController`, the shared nine-demo
catalog, all fitting and page modes, navigation, and page status:

```powershell
$env:TYPST_NATIVE_LIBRARY = 'C:\path\to\typst_dotnet_native.dll'
dotnet run --project samples/Typst.Renderer.WinUI.Sample -c Release
```

## Memory rendering

```csharp
using Typst.Renderer;

using var renderer = new TypstRenderer(new TypstRendererOptions
{
    PackageResolution = TypstPackageResolution.EmbeddedOnly
});

var project = new TypstProjectBuilder()
    .WithMainFile("charts/main.typ")
    .AddText("charts/main.typ", """
        #import "@preview/cetz:0.5.2": canvas, draw
        #import "data.typ": values
        #canvas({ draw.rect((0, 0), (values.at(0), 2), fill: blue) })
        """)
    .AddText("charts/data.typ", "#let values = (3, 5, 8)")
    .Build();

var result = renderer.RenderProject(project, new TypstRenderSettings
{
    Formats = [TypstOutputFormat.Pdf, TypstOutputFormat.Rgba],
    Ppi = 96
});

ReadOnlyMemory<byte> pdf = result.Artifacts.Single(x => x.Format == TypstOutputFormat.Pdf).Data;
using Stream pdfStream = result.Artifacts.Single(x => x.Format == TypstOutputFormat.Pdf).OpenRead();
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

Install [just](https://just.systems/) and run:

```shell
just version
just native
just verify
just pack
just test-published
```

[`eng/Versions.props`](eng/Versions.props) is the single release metadata file.
It defines the SDK package version, pinned native binary version, Rust
toolchain, and native source fingerprint. MSBuild, `just`, package validation,
and GitHub release workflows all read this file. The native GitHub Release tag
is derived as `native-v{NativeVersion}`.

`just native` detects `win-x64` or `linux-x64`, builds the Rust release library,
and stages it under `artifacts/native/{rid}/`. `just verify` also runs Rust
formatting, Clippy, Rust tests, and the managed test suite. `just pack` performs
the RID package inspection and clean NuGet consumer validation and requires
Windows PowerShell on Windows or `pwsh` on Linux.

Samples use local `ProjectReference` projects by default. To verify an exact
published NuGet version with the same sample source, enable package mode:

```shell
just test-published             # uses SdkVersion from eng/Versions.props
just test-published version=0.1.0
dotnet run --project samples/Typst.Renderer.Sample -c Release -p:UsePublishedPackages=true -p:PublishedPackageVersion=0.1.0
dotnet build samples/Typst.Renderer.Avalonia.Sample -c Release -p:UsePublishedPackages=true -p:PublishedPackageVersion=0.1.0
```

On Windows and Linux, package mode automatically selects the matching x64
native package. Override it with `-p:PublishedNativePackageId=...` when needed.
Omit `UsePublishedPackages` to return to the normal local-development path.

Release preparation is automated as well:

```shell
just bump-version 0.2.0
just sync-readme-ko v0.1.0
# Or run both steps in order:
just release 0.2.0 v0.1.0
```

The base tag may be omitted after release tags exist; the latest previous tag is
then detected automatically. README synchronization invokes read-only
`codex exec` with `gpt-5.6-luna` and medium reasoning, validates its structured
output, and writes only `README.ko.md`.

The repository directly links Typst, CeTZ, and oxifmt in its Rust `cdylib`; it
does not depend on the `cetz-renderer` repository at build time or runtime.
