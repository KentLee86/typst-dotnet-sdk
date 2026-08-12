# Cetz .NET SDK agent entrypoint

- Keep the native Rust core independent from `cetz-renderer` and external executables.
- Preserve ABI v1 ownership: all handles are opaque, every panic is contained, and
  every Rust-owned string or result is released exactly once.
- Keep `Cetz.Renderer` on `net8.0`; do not add third-party runtime dependencies.
- A RID package must depend on exactly the same version of `Cetz.Renderer` and place
  its library under `runtimes/{rid}/native/`.
- Create feature worktrees from the `main` worktree with
  `eng/new-worktree.ps1`. The script seeds reusable Rust and .NET outputs from
  `main`; treat them only as incremental-build caches, never as validation of the
  feature branch.
- Seed `target/`, `artifacts/native/`, managed `src`/`tests` `bin` and `obj`, and
  sample `obj`. Do not seed packed NuGet packages, clean-consumer results, or
  sample `bin`; recreate those in the feature worktree. If the seed is absent or
  incompatible, let Cargo/MSBuild rebuild it normally.
- Run Rust formatting, Clippy, tests, .NET tests, pack inspection, and a clean local
  NuGet consumer before committing.
