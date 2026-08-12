# Embedded Typst packages

`cetz/0.5.2` and its required `oxifmt/1.0.0` dependency are the unmodified
`@preview/cetz:0.5.2` and `@preview/oxifmt:1.0.0` packages used by the renderer
for fully offline CeTZ compilation. Their original manifests, READMEs, and
license files are preserved inside each package directory.

The Rust build script generates `include_bytes!` entries for every package file,
including `cetz-core/cetz_core.wasm`, so neither the executable nor the dynamic
library requires the user Typst package cache or a network request for these
versions.
