[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Branch,

    [Parameter(Mandatory = $true)]
    [string]$Path,

    [switch]$SkipRustCache,
    [switch]$SkipDotNetCache
)

$ErrorActionPreference = 'Stop'

function Invoke-Git {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Copy-CacheDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
        Write-Host "Cache not present; skipped: $Source"
        return
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $savedErrorActionPreference = $ErrorActionPreference
    try {
        # Robocopy uses 0-7 for success, including 1 when files were copied.
        $ErrorActionPreference = 'Continue'
        & robocopy $Source $Destination /E /COPY:DAT /DCOPY:DAT /R:2 /W:1 /MT:16 /NFL /NDL /NJH /NJS /NP
        $robocopyExitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $savedErrorActionPreference
    }
    if ($robocopyExitCode -gt 7) {
        throw "robocopy failed for '$Source' with exit code $robocopyExitCode."
    }
}

$root = (& git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $root) {
    throw 'Run this script from the main typst-dotnet-sdk worktree.'
}
$root = [System.IO.Path]::GetFullPath($root.Trim())

$currentBranch = (& git -C $root branch --show-current).Trim()
if ($currentBranch -ne 'main') {
    throw "Source worktree must be on main; current branch is '$currentBranch'."
}

$status = @(& git -C $root status --porcelain)
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the main worktree.' }
if ($status.Count -ne 0) {
    Write-Warning 'The main worktree has uncommitted changes. The new branch still starts at committed main; seeded outputs are cache hints only and must be rebuilt or tested in the new worktree.'
}

& git check-ref-format --branch $Branch *> $null
if ($LASTEXITCODE -ne 0) { throw "Invalid branch name: '$Branch'." }

& git -C $root show-ref --verify --quiet "refs/heads/$Branch"
if ($LASTEXITCODE -eq 0) { throw "Local branch '$Branch' already exists." }
if ($LASTEXITCODE -ne 1) { throw "Could not check whether branch '$Branch' exists." }

$destination = [System.IO.Path]::GetFullPath($Path)
if (Test-Path -LiteralPath $destination) {
    throw "Destination already exists: $destination"
}

$rootPrefix = $root.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if ($destination.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Destination must be outside the main worktree.'
}

$parent = Split-Path -Parent $destination
if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
}

$sourceCommit = (& git -C $root rev-parse main).Trim()
Write-Host "Creating '$Branch' at $sourceCommit"
Invoke-Git @('-C', $root, 'worktree', 'add', '-b', $Branch, $destination, 'main')

if (-not $SkipRustCache) {
    Copy-CacheDirectory (Join-Path $root 'target') (Join-Path $destination 'target')
    Copy-CacheDirectory (Join-Path $root 'artifacts/native') (Join-Path $destination 'artifacts/native')
}

if (-not $SkipDotNetCache) {
    foreach ($tree in @('src', 'tests')) {
        $sourceTree = Join-Path $root $tree
        Get-ChildItem -LiteralPath $sourceTree -Directory -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -in @('bin', 'obj') } |
            ForEach-Object {
                $relative = [System.IO.Path]::GetRelativePath($root, $_.FullName)
                Copy-CacheDirectory $_.FullName (Join-Path $destination $relative)
            }
    }

    Get-ChildItem -LiteralPath (Join-Path $root 'samples') -Directory -Recurse -Filter obj -ErrorAction SilentlyContinue |
        ForEach-Object {
            $relative = [System.IO.Path]::GetRelativePath($root, $_.FullName)
            Copy-CacheDirectory $_.FullName (Join-Path $destination $relative)
        }
}

$seedDirectory = Join-Path $destination 'artifacts'
New-Item -ItemType Directory -Force -Path $seedDirectory | Out-Null
$seedRecord = [ordered]@{
    sourceWorktree = $root
    sourceCommit = $sourceCommit
    createdAtUtc = [DateTime]::UtcNow.ToString('o')
    rustCache = -not $SkipRustCache
    dotNetCache = -not $SkipDotNetCache
}
$seedRecord | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $seedDirectory 'worktree-seed.json') -Encoding utf8

Write-Host "Worktree ready: $destination"
Write-Host 'Seeded outputs are build caches only. Run all required validation in this worktree before committing.'
