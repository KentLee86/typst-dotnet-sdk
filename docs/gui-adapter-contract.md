# GUI adapter contract

Every GUI package must implement `ICetzDocumentView` and use one
`CetzDocumentViewController` as the sole authority for document-view state.
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

Demo applications use `CetzRenderController`. Demo selection updates the source and
starts rendering immediately. Starting another render cancels the prior request;
only the newest successful result is shown. A failed render reports the error while
keeping the previous successful preview.

## Adapter acceptance checklist

1. The control is assignable to `ICetzDocumentView`.
2. Public properties and interface methods produce the same normalized Core state.
3. Painting uses `CetzDocumentViewController.Layout` page indices and bounds exactly.
4. All four view modes, all three zoom modes, and both navigation step sizes are tested.
5. Replacement, release, unload/reload, and disposal do not retain stale native images.
6. The demo exposes demo selection, zoom mode, view mode, previous/next, and page status.
7. Build and tests run in the feature worktree after both Core commits are applied.
