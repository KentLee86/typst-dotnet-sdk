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
dotnet pack (Join-Path $root 'src/Cetz.Renderer.Uno/Cetz.Renderer.Uno.csproj') -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw 'Uno adapter pack failed.' }
dotnet pack (Join-Path $root "src/Cetz.Renderer.Native.$Rid/Cetz.Renderer.Native.$Rid.csproj") -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw "$Rid runtime pack failed." }

$package = Join-Path $feed "Cetz.Renderer.Native.$Rid.0.1.0.nupkg"
if (-not (Test-Path $package)) { throw "Missing package $package" }
$unoPackage = Join-Path $feed 'Cetz.Renderer.Uno.0.1.0.nupkg'
if (-not (Test-Path $unoPackage)) { throw "Missing package $unoPackage" }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($package)
try {
    $nativeName = if ($Rid -eq 'win-x64') { 'cetz_dotnet_native.dll' } else { 'libcetz_dotnet_native.so' }
    $expected = "runtimes/$Rid/native/$nativeName"
    if (-not ($zip.Entries.FullName -contains $expected)) { throw "Package does not contain $expected" }
} finally { $zip.Dispose() }

$unoZip = [System.IO.Compression.ZipFile]::OpenRead($unoPackage)
try {
    $entries = $unoZip.Entries.FullName
    foreach ($framework in @('net8.0', 'net8.0-desktop1.0', 'net8.0-windows10.0.26100')) {
        $assembly = "lib/$framework/Cetz.Renderer.Uno.dll"
        if (-not ($entries -contains $assembly)) { throw "Uno package does not contain $assembly" }
    }

    $nuspecEntry = $unoZip.Entries | Where-Object FullName -eq 'Cetz.Renderer.Uno.nuspec'
    if ($null -eq $nuspecEntry) { throw 'Uno package does not contain its nuspec.' }
    $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
    try { [xml]$unoNuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $coreDependencies = @($unoNuspec.package.metadata.dependencies.group.dependency |
        Where-Object id -eq 'Cetz.Renderer.Core')
    if ($coreDependencies.Count -ne 3) {
        throw 'Uno package must depend on Cetz.Renderer.Core for all three supported target frameworks.'
    }
} finally { $unoZip.Dispose() }

$unoConsumer = Join-Path $root 'artifacts/consumer/uno'
if (Test-Path $unoConsumer) { Remove-Item -Recurse -Force $unoConsumer }
Copy-Item -Recurse (Join-Path $root 'eng/uno-consumer') $unoConsumer
dotnet restore (Join-Path $unoConsumer 'UnoConsumer.csproj') --source $feed --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw 'Uno clean consumer restore failed.' }
dotnet run --project (Join-Path $unoConsumer 'UnoConsumer.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Uno clean consumer run failed.' }

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
