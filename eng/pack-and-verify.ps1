param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64')]
    [string]$Rid
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$feed = Join-Path $root 'artifacts/packages'
New-Item -ItemType Directory -Force $feed | Out-Null
dotnet pack (Join-Path $root 'src/Cetz.Renderer/Cetz.Renderer.csproj') -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw 'Managed SDK pack failed.' }
dotnet pack (Join-Path $root 'src/Cetz.Renderer.Core/Cetz.Renderer.Core.csproj') -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw 'Core renderer pack failed.' }
dotnet pack (Join-Path $root 'src/Cetz.Renderer.Avalonia/Cetz.Renderer.Avalonia.csproj') -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw 'Avalonia adapter pack failed.' }
dotnet pack (Join-Path $root 'src/Cetz.Renderer.WinUI/Cetz.Renderer.WinUI.csproj') -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw 'WinUI adapter pack failed.' }
dotnet pack (Join-Path $root "src/Cetz.Renderer.Native.$Rid/Cetz.Renderer.Native.$Rid.csproj") -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw "$Rid runtime pack failed." }

$package = Join-Path $feed "Cetz.Renderer.Native.$Rid.0.1.0.nupkg"
if (-not (Test-Path $package)) { throw "Missing package $package" }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($package)
try {
    $nativeName = if ($Rid -eq 'win-x64') { 'cetz_dotnet_native.dll' } else { 'libcetz_dotnet_native.so' }
    $expected = "runtimes/$Rid/native/$nativeName"
    if (-not ($zip.Entries.FullName -contains $expected)) { throw "Package does not contain $expected" }
} finally { $zip.Dispose() }

$winUiPackage = Join-Path $feed 'Cetz.Renderer.WinUI.0.1.0.nupkg'
if (-not (Test-Path $winUiPackage)) { throw "Missing package $winUiPackage" }
$zip = [System.IO.Compression.ZipFile]::OpenRead($winUiPackage)
try {
    $assembly = 'lib/net8.0-windows10.0.19041/Cetz.Renderer.WinUI.dll'
    if (-not ($zip.Entries.FullName -contains $assembly)) { throw "WinUI package does not contain $assembly" }
    $resources = 'lib/net8.0-windows10.0.19041/Cetz.Renderer.WinUI.pri'
    if (-not ($zip.Entries.FullName -contains $resources)) { throw "WinUI package does not contain $resources" }
    $nuspecEntry = $zip.Entries | Where-Object { $_.FullName -eq 'Cetz.Renderer.WinUI.nuspec' }
    if ($null -eq $nuspecEntry) { throw 'WinUI package does not contain its nuspec.' }
    $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
    try { $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($nuspec -notmatch '<dependency id="Cetz\.Renderer\.Core" version="0\.1\.0"') {
        throw 'WinUI package does not depend on Cetz.Renderer.Core 0.1.0.'
    }
    if ($nuspec -notmatch '<dependency id="Microsoft\.WindowsAppSDK" version="2\.3\.1"') {
        throw 'WinUI package does not depend on Microsoft.WindowsAppSDK 2.3.1.'
    }
} finally { $zip.Dispose() }

$winUiConsumer = Join-Path $root 'artifacts/consumer/winui'
if (Test-Path $winUiConsumer) { Remove-Item -Recurse -Force $winUiConsumer }
Copy-Item -Recurse (Join-Path $root 'eng/winui-consumer') $winUiConsumer
dotnet restore (Join-Path $winUiConsumer 'CleanWinUiConsumer.csproj') --force --no-cache
if ($LASTEXITCODE -ne 0) { throw 'WinUI clean consumer restore failed.' }
dotnet build (Join-Path $winUiConsumer 'CleanWinUiConsumer.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'WinUI clean consumer build failed.' }

$currentRid = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
if ($currentRid -eq $Rid) {
    $consumer = Join-Path $root "artifacts/consumer/$Rid"
    if (Test-Path $consumer) { Remove-Item -Recurse -Force $consumer }
    Copy-Item -Recurse (Join-Path $root 'eng/consumer') $consumer
    $packageId = "Cetz.Renderer.Native.$Rid"
    dotnet restore (Join-Path $consumer 'CleanConsumer.csproj') --source $feed --ignore-failed-sources -p:CetzNativePackage=$packageId
    if ($LASTEXITCODE -ne 0) { throw "$Rid clean consumer restore failed." }
    dotnet run --project (Join-Path $consumer 'CleanConsumer.csproj') -c Release --no-restore -p:CetzNativePackage=$packageId
    if ($LASTEXITCODE -ne 0) { throw "$Rid clean consumer run failed." }
} else {
    Write-Host "Packed and inspected $Rid on $currentRid; execute the consumer on a $Rid host."
}
