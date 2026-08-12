param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64')]
    [string]$Rid
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$feed = Join-Path $root 'artifacts/packages'
New-Item -ItemType Directory -Force $feed | Out-Null
dotnet pack (Join-Path $root 'src/Typst.Renderer/Typst.Renderer.csproj') -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw 'Managed SDK pack failed.' }
dotnet pack (Join-Path $root 'src/Typst.Renderer.Core/Typst.Renderer.Core.csproj') -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw 'Core renderer pack failed.' }
dotnet pack (Join-Path $root 'src/Typst.Renderer.Avalonia/Typst.Renderer.Avalonia.csproj') -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw 'Avalonia adapter pack failed.' }
dotnet pack (Join-Path $root 'src/Typst.Renderer.Uno/Typst.Renderer.Uno.csproj') -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw 'Uno adapter pack failed.' }
dotnet pack (Join-Path $root 'src/Typst.Renderer.WinForms/Typst.Renderer.WinForms.csproj') -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw 'WinForms adapter pack failed.' }
dotnet pack (Join-Path $root 'src/Typst.Renderer.Wpf/Typst.Renderer.Wpf.csproj') -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw 'WPF adapter pack failed.' }
dotnet pack (Join-Path $root 'src/Typst.Renderer.WinUI/Typst.Renderer.WinUI.csproj') -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw 'WinUI adapter pack failed.' }
dotnet pack (Join-Path $root "src/Typst.Renderer.Native.$Rid/Typst.Renderer.Native.$Rid.csproj") -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw "$Rid runtime pack failed." }

$package = Join-Path $feed "Typst.Renderer.Native.$Rid.0.1.0.nupkg"
if (-not (Test-Path $package)) { throw "Missing package $package" }
$unoPackage = Join-Path $feed 'Typst.Renderer.Uno.0.1.0.nupkg'
if (-not (Test-Path $unoPackage)) { throw "Missing package $unoPackage" }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($package)
try {
    $nativeName = if ($Rid -eq 'win-x64') { 'typst_dotnet_native.dll' } else { 'libtypst_dotnet_native.so' }
    $expected = "runtimes/$Rid/native/$nativeName"
    if (-not ($zip.Entries.FullName -contains $expected)) { throw "Package does not contain $expected" }
} finally { $zip.Dispose() }

$unoZip = [System.IO.Compression.ZipFile]::OpenRead($unoPackage)
try {
    $entries = $unoZip.Entries.FullName
    foreach ($framework in @('net8.0', 'net8.0-desktop1.0', 'net8.0-windows10.0.26100')) {
        $assembly = "lib/$framework/Typst.Renderer.Uno.dll"
        if (-not ($entries -contains $assembly)) { throw "Uno package does not contain $assembly" }
    }

    $nuspecEntry = $unoZip.Entries | Where-Object FullName -eq 'Typst.Renderer.Uno.nuspec'
    if ($null -eq $nuspecEntry) { throw 'Uno package does not contain its nuspec.' }
    $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
    try { [xml]$unoNuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $coreDependencies = @($unoNuspec.package.metadata.dependencies.group.dependency |
        Where-Object id -eq 'Typst.Renderer.Core')
    if ($coreDependencies.Count -ne 3) {
        throw 'Uno package must depend on Typst.Renderer.Core for all three supported target frameworks.'
    }
} finally { $unoZip.Dispose() }

$unoConsumer = Join-Path $root 'artifacts/consumer/uno'
if (Test-Path $unoConsumer) { Remove-Item -Recurse -Force $unoConsumer }
Copy-Item -Recurse (Join-Path $root 'eng/uno-consumer') $unoConsumer
dotnet restore (Join-Path $unoConsumer 'UnoConsumer.csproj') --source $feed --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw 'Uno clean consumer restore failed.' }
dotnet run --project (Join-Path $unoConsumer 'UnoConsumer.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Uno clean consumer run failed.' }

$winFormsPackage = Join-Path $feed 'Typst.Renderer.WinForms.0.1.0.nupkg'
if (-not (Test-Path $winFormsPackage)) { throw "Missing package $winFormsPackage" }
$winFormsZip = [System.IO.Compression.ZipFile]::OpenRead($winFormsPackage)
try {
    $assembly = $winFormsZip.Entries.FullName | Where-Object {
        $_ -match '^lib/net8\.0-windows[^/]*/Typst\.Renderer\.WinForms\.dll$'
    }
    if (@($assembly).Count -ne 1) { throw 'WinForms package does not contain exactly one net8.0-windows assembly.' }
    if ($winFormsZip.Entries.FullName | Where-Object { $_ -match '\.(exe|dll\.config)$' }) {
        throw 'WinForms adapter package unexpectedly contains an executable runtime dependency.'
    }

    $nuspecEntry = $winFormsZip.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
    $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
    try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $coreDependency = $nuspec.SelectSingleNode("//*[local-name()='dependency'][@id='Typst.Renderer.Core']")
    if ($null -eq $coreDependency -or $coreDependency.version -ne '0.1.0') {
        throw 'WinForms package must depend on Typst.Renderer.Core 0.1.0.'
    }
    $frameworkReference = $nuspec.SelectSingleNode(
        "//*[local-name()='frameworkReference'][@name='Microsoft.WindowsDesktop.App.WindowsForms']")
    if ($null -eq $frameworkReference) {
        throw 'WinForms package is missing its Windows Forms framework reference.'
    }
} finally { $winFormsZip.Dispose() }

$wpfPackage = Join-Path $feed 'Typst.Renderer.Wpf.0.1.0.nupkg'
if (-not (Test-Path $wpfPackage)) { throw "Missing package $wpfPackage" }
$zip = [System.IO.Compression.ZipFile]::OpenRead($wpfPackage)
try {
    $wpfAssembly = $zip.Entries.FullName | Where-Object {
        $_ -like 'lib/net8.0-windows*/Typst.Renderer.Wpf.dll'
    }
    if (-not $wpfAssembly) { throw 'WPF package does not contain its net8.0-windows assembly.' }
} finally { $zip.Dispose() }

$winUiPackage = Join-Path $feed 'Typst.Renderer.WinUI.0.1.0.nupkg'
if (-not (Test-Path $winUiPackage)) { throw "Missing package $winUiPackage" }
$zip = [System.IO.Compression.ZipFile]::OpenRead($winUiPackage)
try {
    $assembly = 'lib/net8.0-windows10.0.19041/Typst.Renderer.WinUI.dll'
    if (-not ($zip.Entries.FullName -contains $assembly)) { throw "WinUI package does not contain $assembly" }
    $resources = 'lib/net8.0-windows10.0.19041/Typst.Renderer.WinUI.pri'
    if (-not ($zip.Entries.FullName -contains $resources)) { throw "WinUI package does not contain $resources" }
    $nuspecEntry = $zip.Entries | Where-Object { $_.FullName -eq 'Typst.Renderer.WinUI.nuspec' }
    if ($null -eq $nuspecEntry) { throw 'WinUI package does not contain its nuspec.' }
    $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
    try { $winUiNuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($winUiNuspec -notmatch '<dependency id="Typst\.Renderer\.Core" version="0\.1\.0"') {
        throw 'WinUI package does not depend on Typst.Renderer.Core 0.1.0.'
    }
    if ($winUiNuspec -notmatch '<dependency id="Microsoft\.WindowsAppSDK" version="2\.3\.1"') {
        throw 'WinUI package does not depend on Microsoft.WindowsAppSDK 2.3.1.'
    }
} finally { $zip.Dispose() }

$winUiConsumer = Join-Path $root 'artifacts/consumer/winui'
if (Test-Path $winUiConsumer) { Remove-Item -Recurse -Force $winUiConsumer }
Copy-Item -Recurse (Join-Path $root 'eng/winui-consumer') $winUiConsumer
$winUiPackages = Join-Path $winUiConsumer '.nuget/packages'
dotnet restore (Join-Path $winUiConsumer 'CleanWinUiConsumer.csproj') --force --no-cache --packages $winUiPackages
if ($LASTEXITCODE -ne 0) { throw 'WinUI clean consumer restore failed.' }
dotnet build (Join-Path $winUiConsumer 'CleanWinUiConsumer.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'WinUI clean consumer build failed.' }

$currentRid = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
if ($currentRid -eq $Rid) {
    $consumer = Join-Path $root "artifacts/consumer/$Rid"
    if (Test-Path $consumer) { Remove-Item -Recurse -Force $consumer }
    Copy-Item -Recurse (Join-Path $root 'eng/consumer') $consumer
    $packageId = "Typst.Renderer.Native.$Rid"
    $consumerPackages = Join-Path $consumer '.nuget/packages'
    dotnet restore (Join-Path $consumer 'CleanConsumer.csproj') --ignore-failed-sources --packages $consumerPackages -p:TypstNativePackage=$packageId
    if ($LASTEXITCODE -ne 0) { throw "$Rid clean consumer restore failed." }
    dotnet run --project (Join-Path $consumer 'CleanConsumer.csproj') -c Release --no-restore -p:TypstNativePackage=$packageId
    if ($LASTEXITCODE -ne 0) { throw "$Rid clean consumer run failed." }

    $winFormsConsumer = Join-Path $root "artifacts/winforms-consumer/$Rid"
    if (Test-Path $winFormsConsumer) { Remove-Item -Recurse -Force $winFormsConsumer }
    Copy-Item -Recurse (Join-Path $root 'eng/winforms-consumer') $winFormsConsumer
    dotnet restore (Join-Path $winFormsConsumer 'WinFormsConsumer.csproj') --source $feed --ignore-failed-sources -p:TypstNativePackage=$packageId
    if ($LASTEXITCODE -ne 0) { throw "$Rid WinForms consumer restore failed." }
    dotnet run --project (Join-Path $winFormsConsumer 'WinFormsConsumer.csproj') -c Release --no-restore -p:TypstNativePackage=$packageId
    if ($LASTEXITCODE -ne 0) { throw "$Rid WinForms consumer run failed." }

    if ($Rid -eq 'win-x64') {
        $wpfConsumer = Join-Path $root "artifacts/consumer-wpf/$Rid"
        if (Test-Path $wpfConsumer) { Remove-Item -Recurse -Force $wpfConsumer }
        Copy-Item -Recurse (Join-Path $root 'eng/consumer-wpf') $wpfConsumer
        dotnet restore (Join-Path $wpfConsumer 'CleanWpfConsumer.csproj') --source $feed --ignore-failed-sources -p:TypstNativePackage=$packageId
        if ($LASTEXITCODE -ne 0) { throw 'WPF clean consumer restore failed.' }
        dotnet run --project (Join-Path $wpfConsumer 'CleanWpfConsumer.csproj') -c Release --no-restore -p:TypstNativePackage=$packageId
        if ($LASTEXITCODE -ne 0) { throw 'WPF clean consumer run failed.' }
    }
} else {
    Write-Host "Packed and inspected $Rid on $currentRid; execute the consumer on a $Rid host."
}
