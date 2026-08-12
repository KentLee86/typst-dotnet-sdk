#!/usr/bin/env python3
"""Use Codex to synchronize README.ko.md from README.md changes since a tag."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ALLOWED_CHANGE = "README.ko.md"


def codex_executable() -> str:
    candidates = ["codex.cmd", "codex.exe", "codex"] if os.name == "nt" else ["codex"]
    for candidate in candidates:
        resolved = shutil.which(candidate)
        if resolved:
            return resolved
    raise RuntimeError("codex CLI was not found on PATH.")


def git(*args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", *args],
        cwd=ROOT,
        check=check,
        text=True,
        encoding="utf-8",
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )


def resolve_base_tag(explicit: str) -> str:
    if explicit:
        candidate = explicit
        result = git("rev-parse", "--verify", f"refs/tags/{candidate}^{{commit}}", check=False)
        if result.returncode != 0:
            raise RuntimeError(f"Tag does not exist: {candidate}")
        readme = git("cat-file", "-e", f"{candidate}:README.md", check=False)
        if readme.returncode != 0:
            raise RuntimeError(f"Tag {candidate} does not contain README.md")
        return candidate

    head = git("rev-parse", "HEAD").stdout.strip()
    tags = git("tag", "--merged", "HEAD", "--sort=-version:refname").stdout.splitlines()
    for tag in tags:
        commit = git("rev-list", "-n", "1", tag).stdout.strip()
        if commit != head:
            readme = git("cat-file", "-e", f"{tag}:README.md", check=False)
            if readme.returncode != 0:
                continue
            return tag
    raise RuntimeError(
        "No previous release tag is reachable from HEAD. Pass one explicitly: "
        "just sync-readme-ko v0.1.0"
    )


def repository_snapshot() -> dict[str, str]:
    paths = git("ls-files", "--cached", "--others", "--exclude-standard", "-z").stdout.split("\0")
    snapshot: dict[str, str] = {}
    for relative in filter(None, paths):
        path = ROOT / relative
        if path.is_file():
            snapshot[relative] = hashlib.sha256(path.read_bytes()).hexdigest()
    return snapshot


def changed_paths(before: dict[str, str], after: dict[str, str]) -> set[str]:
    return {
        path
        for path in before.keys() | after.keys()
        if before.get(path) != after.get(path)
    }


def validate_readmes(korean: str) -> None:
    english = (ROOT / "README.md").read_text(encoding="utf-8")
    if english.count("```") != korean.count("```"):
        raise RuntimeError("README code-fence counts differ after translation.")
    if not korean.startswith("# "):
        raise RuntimeError("Translated README does not start with a level-one heading.")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-tag", default="", help="Previous release tag; auto-detected when omitted")
    parser.add_argument("--model", default="gpt-5.6-luna")
    parser.add_argument("--reasoning-effort", default="medium")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    base_tag = resolve_base_tag(args.base_tag)
    readme_diff = git("diff", "--no-ext-diff", "--stat", base_tag, "--", "README.md").stdout.strip()
    if not readme_diff:
        print(f"README.md has no changes since {base_tag}; nothing to translate.")
        return 0

    prompt = f"""Produce the complete updated contents for README.ko.md in this repository.

Compare README.md at release tag {base_tag} with the current README.md, then apply every relevant English README change to the current README.ko.md as natural, technically precise Korean.

Requirements:
- Do not edit any file. Return the complete proposed README.ko.md through the required JSON schema.
- Preserve the current README.md structure, section order, links, code blocks, commands, identifiers, package names, paths, and version strings.
- Translate prose naturally instead of word-for-word, without adding claims absent from README.md.
- Use `git diff {base_tag} -- README.md` to focus on changes, while checking the full current README.md and README.ko.md so the Korean document remains coherent.
- Preserve unrelated existing worktree changes exactly.
- Finish only after checking that README.ko.md reflects the current README.md.
"""

    print(f"Base tag: {base_tag}")
    print(f"README changes: {readme_diff}")
    print(f"Codex model: {args.model}; reasoning effort: {args.reasoning_effort}")
    if args.dry_run:
        print("Dry run: Codex was not invoked.")
        return 0

    before = repository_snapshot()
    environment = os.environ.copy()
    environment.setdefault("PYTHONUTF8", "1")
    schema = {
        "type": "object",
        "properties": {
            "readme_ko": {
                "type": "string",
                "description": "Complete Markdown contents for the updated README.ko.md",
            }
        },
        "required": ["readme_ko"],
        "additionalProperties": False,
    }
    with tempfile.TemporaryDirectory(prefix="typst-readme-ko-") as temporary:
        temporary_path = Path(temporary)
        schema_path = temporary_path / "schema.json"
        output_path = temporary_path / "result.json"
        schema_path.write_text(json.dumps(schema), encoding="utf-8")
        command = [
            codex_executable(),
            "exec",
            "--model",
            args.model,
            "--config",
            f'model_reasoning_effort="{args.reasoning_effort}"',
            "--sandbox",
            "read-only",
            "--ephemeral",
            "--cd",
            str(ROOT),
            "--output-schema",
            str(schema_path),
            "--output-last-message",
            str(output_path),
            "-",
        ]
        result = subprocess.run(
            command,
            cwd=ROOT,
            input=prompt,
            text=True,
            encoding="utf-8",
            env=environment,
        )
        if result.returncode != 0:
            raise RuntimeError(f"codex exec failed with exit code {result.returncode}")
        try:
            translated = json.loads(output_path.read_text(encoding="utf-8"))["readme_ko"]
        except (OSError, KeyError, TypeError, json.JSONDecodeError) as error:
            raise RuntimeError(f"Codex returned invalid structured output: {error}") from error

    after = repository_snapshot()
    unexpected = changed_paths(before, after)
    if unexpected:
        names = ", ".join(sorted(unexpected))
        raise RuntimeError(f"Read-only Codex invocation changed repository files: {names}")
    current = (ROOT / ALLOWED_CHANGE).read_text(encoding="utf-8")
    if translated == current:
        raise RuntimeError("Codex returned README.ko.md without any changes.")
    validate_readmes(translated)
    (ROOT / ALLOWED_CHANGE).write_bytes(translated.encode("utf-8"))
    print("README.ko.md synchronization complete.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except RuntimeError as error:
        print(f"error: {error}", file=sys.stderr)
        raise SystemExit(1)
