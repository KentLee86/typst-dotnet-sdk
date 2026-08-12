# GUI adapter contract

Every GUI package must implement `ITypstDocumentView` and use one
`TypstDocumentViewController` as the sole authority for document-view state.
Platform code may create and release native image resources, paint the page bounds
returned by `Layout`, dispatch to its UI thread, and connect to its native scrolling
host. It must not independently normalize zoom or spacing, calculate page positions,
choose navigation steps, or implement a different fitting policy.

## Required behavior

- `Custom`, `FitWidth`, and `FitPage` use the effective `Zoom` calculated by Core.
- `ContinuousSingle` lays out every page in one vertical column.
- `ContinuousFacing` lays out every page in two-page rows.
- `SinglePage` displays one page and moves one page at a time.
- `FacingPages` displays one two-page spread and moves two pages at a time.
- `GoToPage`, `MoveNext`, and `MovePrevious` must visibly navigate. Paged modes
  redraw their new layout subset; continuous modes scroll the current placement
  into view.
- `SetViewport` is updated from the native viewport, not from the document extent.
- `ReleaseDocument` clears platform image resources as well as the Core reference.
- Unloading may release native resources, but reloading must rebuild them while the
  Core document is still retained.

Demo applications use `TypstRenderController`. Demo selection updates the source and
starts rendering immediately. Starting another render cancels the prior request;
only the newest successful result is shown. A failed render reports the error while
keeping the previous successful preview.

Viewport hosts use `TypstViewportInteractionController` for left-button drag panning
and Ctrl+wheel zoom. Platforms supply pointer coordinates and current scroll offsets,
then apply the returned offset through their native scrolling API. Wheel zoom switches
to `Custom` mode and preserves the document position beneath the pointer.
Avalonia applications can use `TypstViewport`, which supplies the native scroll host,
four-direction workspace, routed input interception, and layout-synchronized anchoring.

`TypstRasterQualityPolicy` separates logical zoom from backing raster density. `Fixed`
uses 144 PPI, `HighResolution` uses 288 PPI, and `Automatic` selects a bounded density
step from 144 through 768 PPI. Hosts should debounce automatic rerenders and retain
the current view zoom and pointer anchor when replacing the document.

`TypstVisiblePageSelector` is the shared realization policy for continuous views.
Avalonia, Uno, WinForms, WPF, and WinUI keep native bitmap resources only for pages
intersecting the viewport plus one page of sequential overscan, and release resources
as pages leave that window. The immutable Core document still owns every rendered RGBA
page; reducing native render time and Core pixel memory requires a future page-lazy ABI.
Continuous scroll hosts select the page with the largest visible area and call
`TrackCurrentPage`; this updates navigation/status state without issuing a reciprocal
scroll request. Button and direct page navigation continue to use `GoToPage`.

## Adapter acceptance checklist

1. The control is assignable to `ITypstDocumentView`.
2. Public properties and interface methods produce the same normalized Core state.
3. Painting uses `TypstDocumentViewController.Layout` page indices and bounds exactly.
4. All four view modes, all three zoom modes, and both navigation step sizes are tested.
5. Replacement, release, unload/reload, and disposal do not retain stale native images.
6. The demo exposes demo selection, zoom mode, raster quality, view mode,
   previous/next, editable current page, and scroll-synchronized page status.
7. Build and tests run in the feature worktree after both Core commits are applied.
