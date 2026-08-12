#!/usr/bin/env python3
"""Read and validate the repository's central release metadata."""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
VERSIONS = ROOT / "eng/Versions.props"
SEMVER = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$")
FINGERPRINT = re.compile(r"^[0-9a-f]{64}$")
FIELDS = {
    "sdk": "SdkVersion",
    "native": "NativeVersion",
    "rust-toolchain": "RustToolchainVersion",
    "native-fingerprint": "NativeSourceFingerprint",
}


def metadata() -> dict[str, str]:
    root = ET.parse(VERSIONS).getroot()
    values = {child.tag: (child.text or "").strip() for group in root for child in group}
    missing = [name for name in FIELDS.values() if not values.get(name)]
    if missing:
        raise RuntimeError(f"Missing metadata: {', '.join(missing)}")
    if not SEMVER.fullmatch(values["SdkVersion"]):
        raise RuntimeError("SdkVersion is not a supported SemVer value")
    if not SEMVER.fullmatch(values["NativeVersion"]):
        raise RuntimeError("NativeVersion is not a supported SemVer value")
    if not FINGERPRINT.fullmatch(values["NativeSourceFingerprint"]):
        raise RuntimeError("NativeSourceFingerprint must be a lowercase SHA-256 value")
    return values


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("field", nargs="?", choices=[*FIELDS, "native-tag", "show"], default="show")
    args = parser.parse_args()
    values = metadata()
    if args.field == "show":
        print(f"SDK version:       {values['SdkVersion']}")
        print(f"Native version:    {values['NativeVersion']}")
        print(f"Native tag:        native-v{values['NativeVersion']}")
        print(f"Rust toolchain:    {values['RustToolchainVersion']}")
        print(f"Native fingerprint: {values['NativeSourceFingerprint']}")
    elif args.field == "native-tag":
        print(f"native-v{values['NativeVersion']}")
    else:
        print(values[FIELDS[args.field]])
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ET.ParseError, RuntimeError) as error:
        print(f"error: {error}", file=sys.stderr)
        raise SystemExit(1)
