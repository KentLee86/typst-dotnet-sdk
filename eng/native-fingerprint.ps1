$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$inputs = @('Cargo.lock', 'Cargo.toml', 'native/typst-dotnet-native')
$lines = foreach ($inputPath in $inputs) {
    $objectId = git -C $root rev-parse "HEAD:$inputPath"
    if ($LASTEXITCODE -ne 0) { throw "Cannot resolve Git object for $inputPath." }
    "$inputPath=$objectId"
}

$content = ($lines -join "`n") + "`n"
$bytes = [System.Text.Encoding]::UTF8.GetBytes($content)
[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
