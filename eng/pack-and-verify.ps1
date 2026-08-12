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
dotnet pack (Join-Path $root 'src/Cetz.Renderer.WinForms/Cetz.Renderer.WinForms.csproj') -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw 'WinForms adapter pack failed.' }
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

$winFormsPackage = Join-Path $feed 'Cetz.Renderer.WinForms.0.1.0.nupkg'
if (-not (Test-Path $winFormsPackage)) { throw "Missing package $winFormsPackage" }
$winFormsZip = [System.IO.Compression.ZipFile]::OpenRead($winFormsPackage)
try {
    $assembly = $winFormsZip.Entries.FullName | Where-Object {
        $_ -match '^lib/net8\.0-windows[^/]*/Cetz\.Renderer\.WinForms\.dll$'
    }
    if (@($assembly).Count -ne 1) { throw 'WinForms package does not contain exactly one net8.0-windows assembly.' }
    if ($winFormsZip.Entries.FullName | Where-Object { $_ -match '\.(exe|dll\.config)$' }) {
        throw 'WinForms adapter package unexpectedly contains an executable runtime dependency.'
    }

    $nuspecEntry = $winFormsZip.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
    $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
    try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $coreDependency = $nuspec.SelectSingleNode("//*[local-name()='dependency'][@id='Cetz.Renderer.Core']")
    if ($null -eq $coreDependency -or $coreDependency.version -ne '0.1.0') {
        throw 'WinForms package must depend on Cetz.Renderer.Core 0.1.0.'
    }
    $frameworkReference = $nuspec.SelectSingleNode(
        "//*[local-name()='frameworkReference'][@name='Microsoft.WindowsDesktop.App.WindowsForms']")
    if ($null -eq $frameworkReference) {
        throw 'WinForms package is missing its Windows Forms framework reference.'
    }
} finally { $winFormsZip.Dispose() }

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

    $winFormsConsumer = Join-Path $root "artifacts/winforms-consumer/$Rid"
    if (Test-Path $winFormsConsumer) { Remove-Item -Recurse -Force $winFormsConsumer }
    Copy-Item -Recurse (Join-Path $root 'eng/winforms-consumer') $winFormsConsumer
    dotnet restore (Join-Path $winFormsConsumer 'WinFormsConsumer.csproj') --source $feed --ignore-failed-sources -p:CetzNativePackage=$packageId
    if ($LASTEXITCODE -ne 0) { throw "$Rid WinForms consumer restore failed." }
    dotnet run --project (Join-Path $winFormsConsumer 'WinFormsConsumer.csproj') -c Release --no-restore -p:CetzNativePackage=$packageId
    if ($LASTEXITCODE -ne 0) { throw "$Rid WinForms consumer run failed." }
} else {
    Write-Host "Packed and inspected $Rid on $currentRid; execute the consumer on a $Rid host."
}
