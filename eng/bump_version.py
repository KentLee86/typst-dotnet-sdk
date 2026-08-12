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


def current_version() -> str:
    props = read(ROOT / "eng/Versions.props")
    match = re.search(r"<SdkVersion>([^<]+)</SdkVersion>", props)
    if not match:
        raise RuntimeError("eng/Versions.props does not contain <SdkVersion>.")
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

    readme_files = [ROOT / "README.md", ROOT / "README.ko.md"]

    versions_props = ROOT / "eng/Versions.props"
    missing = [path.relative_to(ROOT) for path in [versions_props, *readme_files] if not path.is_file()]
    if missing:
        raise RuntimeError(f"Missing version-managed file(s): {', '.join(map(str, missing))}")

    print(f"Bumping repository version {old} -> {new}")
    print(f"  {versions_props.relative_to(ROOT)}")
    for path in readme_files:
        if f'Version="{old}"' not in read(path):
            raise RuntimeError(f"Expected package version {old} in {path.relative_to(ROOT)}")
        print(f"  {path.relative_to(ROOT)}")

    if args.dry_run:
        return 0

    versions_text = read(versions_props)
    sdk_version = f"<SdkVersion>{old}</SdkVersion>"
    if versions_text.count(sdk_version) != 1:
        raise RuntimeError("Expected exactly one current <SdkVersion> in eng/Versions.props")
    write(versions_props, versions_text.replace(sdk_version, f"<SdkVersion>{new}</SdkVersion>"))
    for path in readme_files:
        replace_required(path, f'Version="{old}"', f'Version="{new}"')
    print("Version bump complete.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except RuntimeError as error:
        print(f"error: {error}", file=sys.stderr)
        raise SystemExit(1)
