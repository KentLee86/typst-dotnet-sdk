#!/usr/bin/env python3
"""Update repository-owned version references from one version to another."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
VERSION_PATTERN = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$")


def read(path: Path) -> str:
    return path.read_bytes().decode("utf-8")


def write(path: Path, text: str) -> None:
    path.write_bytes(text.encode("utf-8"))


def replace_required(path: Path, old: str, new: str, *, count: int = 0) -> bool:
    text = read(path)
    replacements = text.count(old)
    if replacements == 0:
        raise RuntimeError(f"Expected version reference not found in {path.relative_to(ROOT)}")
    if count and replacements != count:
        raise RuntimeError(
            f"Expected {count} version reference(s) in {path.relative_to(ROOT)}, found {replacements}"
        )
    write(path, text.replace(old, new))
    return True


def update_package_version(text: str, package_name: str, old: str, new: str) -> str:
    pattern = re.compile(
        rf'(name\s*=\s*"{re.escape(package_name)}"\s*\r?\nversion\s*=\s*")'
        rf"{re.escape(old)}"
        rf'(")'
    )
    updated, count = pattern.subn(rf"\g<1>{new}\g<2>", text, count=1)
    if count != 1:
        raise RuntimeError(f"Could not update package version for {package_name}")
    return updated


def current_version() -> str:
    props = read(ROOT / "Directory.Build.props")
    match = re.search(r"<Version>([^<]+)</Version>", props)
    if not match:
        raise RuntimeError("Directory.Build.props does not contain <Version>.")
    return match.group(1)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("version", help="New SemVer package version, for example 0.2.0")
    parser.add_argument("--dry-run", action="store_true", help="Validate and list files without writing")
    args = parser.parse_args()

    if not VERSION_PATTERN.fullmatch(args.version):
        parser.error("version must be SemVer-like: X.Y.Z or X.Y.Z-prerelease")

    old = current_version()
    new = args.version
    if old == new:
        print(f"Version is already {new}; nothing to update.")
        return 0

    direct_files = [
        ROOT / "Directory.Build.props",
        ROOT / "eng/pack-and-verify.ps1",
        ROOT / "eng/consumer/CleanConsumer.csproj",
        ROOT / "eng/consumer-wpf/CleanWpfConsumer.csproj",
        ROOT / "eng/uno-consumer/UnoConsumer.csproj",
        ROOT / "eng/winforms-consumer/WinFormsConsumer.csproj",
        ROOT / "eng/winui-consumer/CleanWinUiConsumer.csproj",
        ROOT / "samples/Typst.Renderer.Uno.Sample/Typst.Renderer.Uno.Sample.csproj",
        ROOT / "src/Typst.Renderer.Native.win-x64/Typst.Renderer.Native.win-x64.nuspec",
        ROOT / "src/Typst.Renderer.Native.linux-x64/Typst.Renderer.Native.linux-x64.nuspec",
    ]
    readme_files = [ROOT / "README.md", ROOT / "README.ko.md"]

    missing = [path.relative_to(ROOT) for path in direct_files + readme_files if not path.is_file()]
    if missing:
        raise RuntimeError(f"Missing version-managed file(s): {', '.join(map(str, missing))}")

    print(f"Bumping repository version {old} -> {new}")
    for path in direct_files:
        if old not in read(path):
            raise RuntimeError(f"Expected {old} in {path.relative_to(ROOT)}")
        print(f"  {path.relative_to(ROOT)}")
    for path in readme_files:
        if f'Version="{old}"' not in read(path):
            raise RuntimeError(f"Expected package version {old} in {path.relative_to(ROOT)}")
        print(f"  {path.relative_to(ROOT)}")

    cargo_toml = ROOT / "native/typst-dotnet-native/Cargo.toml"
    cargo_lock = ROOT / "Cargo.lock"
    cargo_toml_updated = update_package_version(read(cargo_toml), "typst-dotnet-native", old, new)
    cargo_lock_updated = update_package_version(read(cargo_lock), "typst-dotnet-native", old, new)
    print(f"  {cargo_toml.relative_to(ROOT)}")
    print(f"  {cargo_lock.relative_to(ROOT)}")

    if args.dry_run:
        return 0

    for path in direct_files:
        replace_required(path, old, new)
    for path in readme_files:
        replace_required(path, f'Version="{old}"', f'Version="{new}"')
    write(cargo_toml, cargo_toml_updated)
    write(cargo_lock, cargo_lock_updated)
    print("Version bump complete.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except RuntimeError as error:
        print(f"error: {error}", file=sys.stderr)
        raise SystemExit(1)
