# Cetz .NET SDK agent entrypoint

- Keep the native Rust core independent from `cetz-renderer` and external executables.
- Preserve ABI v1 ownership: all handles are opaque, every panic is contained, and
  every Rust-owned string or result is released exactly once.
- Keep `Cetz.Renderer` on `net8.0`; do not add third-party runtime dependencies.
- A RID package must depend on exactly the same version of `Cetz.Renderer` and place
  its library under `runtimes/{rid}/native/`.
- Run Rust formatting, Clippy, tests, .NET tests, pack inspection, and a clean local
  NuGet consumer before committing.
