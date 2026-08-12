set windows-shell := ["powershell.exe", "-NoLogo", "-NoProfile", "-Command"]

python := if os_family() == "windows" { "python" } else { "python3" }
powershell := if os_family() == "windows" { "powershell" } else { "pwsh" }
rid := if os_family() == "windows" { "win-x64" } else if os() == "linux" { "linux-x64" } else { "unsupported" }
sdk_version := if os_family() == "windows" { `python eng/version.py sdk` } else { `python3 eng/version.py sdk` }

default:
    @just --list

# Display and validate the central SDK/native version metadata.
version:
    {{ python }} eng/version.py show

# Build the Rust cdylib and stage it under artifacts/native/{rid}/.
native:
    cargo build --release --locked
    just _stage-native-{{ os() }}

[private]
_stage-native-windows:
    New-Item -ItemType Directory -Force 'artifacts/native/win-x64' | Out-Null
    Copy-Item 'target/release/typst_dotnet_native.dll' 'artifacts/native/win-x64/typst_dotnet_native.dll' -Force
    Write-Host 'Staged artifacts/native/win-x64/typst_dotnet_native.dll'

[private]
_stage-native-linux:
    mkdir -p artifacts/native/linux-x64
    cp target/release/libtypst_dotnet_native.so artifacts/native/linux-x64/libtypst_dotnet_native.so
    @echo "Staged artifacts/native/linux-x64/libtypst_dotnet_native.so"

# Run formatting, linting, Rust tests, native staging, and managed tests.
verify:
    cargo fmt --all -- --check
    cargo clippy --workspace --all-targets --locked -- -D warnings
    cargo test --workspace --locked
    just native
    dotnet test tests/Typst.Renderer.Tests/Typst.Renderer.Tests.csproj -c Release
    dotnet test tests/Typst.Renderer.Avalonia.Tests/Typst.Renderer.Avalonia.Tests.csproj -c Release
    just _verify-managed-{{ os() }}

[private]
_verify-managed-windows:
    dotnet test tests/Typst.Renderer.WinForms.Tests/Typst.Renderer.WinForms.Tests.csproj -c Release
    dotnet test tests/Typst.Renderer.Wpf.Tests/Typst.Renderer.Wpf.Tests.csproj -c Release
    dotnet test tests/Typst.Renderer.WinUI.Tests/Typst.Renderer.WinUI.Tests.csproj -c Release

[private]
_verify-managed-linux:
    @echo "Windows-only adapter tests skipped on linux-x64"

# Pack and validate NuGet packages for the current RID (requires PowerShell).
pack: native
    {{ powershell }} -NoProfile -File eng/pack-and-verify.ps1 -Rid {{ rid }}

# Build every sample against an exact published NuGet package version.
test-published version=sdk_version:
    dotnet run --project samples/Typst.Renderer.Sample/Typst.Renderer.Sample.csproj -c Release -p:UsePublishedPackages=true -p:PublishedPackageVersion={{ version }}
    dotnet build samples/Typst.Renderer.Avalonia.Sample/Typst.Renderer.Avalonia.Sample.csproj -c Release -p:UsePublishedPackages=true -p:PublishedPackageVersion={{ version }}
    dotnet build samples/Typst.Renderer.Uno.Sample/Typst.Renderer.Uno.Sample.csproj -c Release -f {{ if os_family() == "windows" { "net8.0-windows10.0.26100" } else { "net8.0-desktop" } }} -p:UsePublishedPackages=true -p:PublishedPackageVersion={{ version }}
    just _test-published-{{ os() }} {{ version }}

[private]
_test-published-windows version:
    dotnet build samples/Typst.Renderer.WinForms.Sample/Typst.Renderer.WinForms.Sample.csproj -c Release -p:UsePublishedPackages=true -p:PublishedPackageVersion={{ version }}
    dotnet build samples/Typst.Renderer.Wpf.Sample/Typst.Renderer.Wpf.Sample.csproj -c Release -p:UsePublishedPackages=true -p:PublishedPackageVersion={{ version }}
    dotnet build samples/Typst.Renderer.WinUI.Sample/Typst.Renderer.WinUI.Sample.csproj -c Release -p:UsePublishedPackages=true -p:PublishedPackageVersion={{ version }}

[private]
_test-published-linux version:
    @echo "Windows-only samples skipped on linux-x64"

# Update all repository-owned package version references.
bump-version version:
    {{ python }} eng/bump_version.py "{{ version }}"

# Apply README.md changes since the previous release tag to README.ko.md with Codex.
sync-readme-ko base_tag="" model="gpt-5.6-luna" effort="medium":
    {{ python }} eng/sync_readme_ko.py --base-tag="{{ base_tag }}" --model "{{ model }}" --reasoning-effort "{{ effort }}"

# Bump the package version, then synchronize the Korean README.
release version base_tag="":
    {{ python }} eng/sync_readme_ko.py --base-tag="{{ base_tag }}" --dry-run
    just bump-version "{{ version }}"
    just sync-readme-ko "{{ base_tag }}"
