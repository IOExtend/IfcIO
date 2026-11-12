<#
.SYNOPSIS
    Packs release assets into artifacts: binary archives of library, CLI archives, source zip, NuGet packages and SHA256SUMS.txt.
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$props = [xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)
$versionNode = $props.SelectSingleNode('/Project/PropertyGroup/Version')
if (-not $versionNode) {
    $versionNode = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix')
}
$version = if ($versionNode) { $versionNode.InnerText } else { $null }
if (-not $version) { throw "Version or VersionPrefix property not found in Directory.Build.props" }

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$Output = (Resolve-Path $Output).Path
$stage = Join-Path $Output 'stage'
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

function Assert-AdapterNuspec([string]$NuspecPath, [string]$EngineId, [string]$Version) {
    $dependencies = @(([xml](Get-Content -LiteralPath $NuspecPath -Raw)).SelectNodes("//*[local-name()='dependency']"))
    # (a) Guards the PinProjectReferencesExactly target, which hooks a private NuGet target and would fail silently if an SDK renamed it.
    $engine = @($dependencies | Where-Object { $_.GetAttribute('id') -eq $EngineId })
    if ($engine.Count -eq 0) { throw "$NuspecPath has no dependency on $EngineId." }
    foreach ($dependency in $engine) {
        if ($dependency.GetAttribute('version') -cne "[$Version]") {
            throw "$NuspecPath depends on $EngineId '$($dependency.GetAttribute('version'))'; it must be exactly '[$Version]'."
        }
    }
    # (b) Replaces the NU5104 protection that the IOScene range switches off: no adapter built on a preview contract reaches nuget.org.
    $scene = @($dependencies | Where-Object { $_.GetAttribute('id') -eq 'IOScene' })
    if ($scene.Count -eq 0) { throw "$NuspecPath has no dependency on IOScene." }
    foreach ($dependency in $scene) {
        $range = $dependency.GetAttribute('version')
        if ($range.TrimStart('[', '(').Split(',')[0].Contains('-')) {
            throw "$NuspecPath depends on IOScene '$range', whose lower bound is a prerelease; release IOScene first and require its stable version."
        }
    }
}

function New-Zip([string]$SourceDir, [string]$ZipPath) {
    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath }
    Compress-Archive -Path (Join-Path $SourceDir '*') -DestinationPath $ZipPath
}

# 1. IfcIO Library assemblies (net8.0 and net10.0)
Write-Host "Publishing IfcIO library..."
$libStage = Join-Path $stage 'lib'
$libNet8 = Join-Path $libStage 'net8.0'
$libNet10 = Join-Path $libStage 'net10.0'
Invoke-DotNet publish (Join-Path $root 'src\IfcIO\IfcIO.csproj') -c Release -f net8.0 -o $libNet8
Invoke-DotNet publish (Join-Path $root 'src\IfcIO\IfcIO.csproj') -c Release -f net10.0 -o $libNet10
Copy-Item (Join-Path $root 'README.md') $libStage
Copy-Item (Join-Path $root 'LICENSE') $libStage
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $libStage
New-Zip $libStage (Join-Path $Output "IfcIO-$version.zip")

# 2. CLI (.NET 8 Runtime)
Write-Host "Publishing IfcIO.Cli (net8.0)..."
$cliStage = Join-Path $stage 'cli-net8'
Invoke-DotNet publish (Join-Path $root 'src\IfcIO.Cli\IfcIO.Cli.csproj') -c Release -f net8.0 -o $cliStage
if (Test-Path (Join-Path $cliStage 'IfcIO.Cli.exe')) {
    Copy-Item (Join-Path $cliStage 'IfcIO.Cli.exe') (Join-Path $cliStage 'ifcio.exe')
    Copy-Item (Join-Path $cliStage 'IfcIO.Cli.runtimeconfig.json') (Join-Path $cliStage 'ifcio.runtimeconfig.json')
}
Copy-Item (Join-Path $root 'README.md') $cliStage
Copy-Item (Join-Path $root 'LICENSE') $cliStage
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $cliStage
New-Zip $cliStage (Join-Path $Output "IfcIO-Cli-$version.zip")

# 3. CLI (.NET 10 Runtime)
Write-Host "Publishing IfcIO.Cli (net10.0)..."
$cli10Stage = Join-Path $stage 'cli-net10'
Invoke-DotNet publish (Join-Path $root 'src\IfcIO.Cli\IfcIO.Cli.csproj') -c Release -f net10.0 -o $cli10Stage
if (Test-Path (Join-Path $cli10Stage 'IfcIO.Cli.exe')) {
    Copy-Item (Join-Path $cli10Stage 'IfcIO.Cli.exe') (Join-Path $cli10Stage 'ifcio.exe')
    Copy-Item (Join-Path $cli10Stage 'IfcIO.Cli.runtimeconfig.json') (Join-Path $cli10Stage 'ifcio.runtimeconfig.json')
}
Copy-Item (Join-Path $root 'README.md') $cli10Stage
Copy-Item (Join-Path $root 'LICENSE') $cli10Stage
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $cli10Stage
New-Zip $cli10Stage (Join-Path $Output "IfcIO-Cli-$version-net10.0.zip")

# 4. NuGet packages
Write-Host "Packing NuGet packages..."
Invoke-DotNet pack (Join-Path $root 'IfcIO.slnx') -c Release --no-build -o $Output

Write-Host "Checking IfcIO.Scene dependencies..."
Add-Type -AssemblyName System.IO.Compression.FileSystem
$adapterNuspec = Join-Path $stage 'IfcIO.Scene.nuspec'
$adapterPackage = [System.IO.Compression.ZipFile]::OpenRead((Join-Path $Output "IfcIO.Scene.$version.nupkg"))
try {
    $entry = @($adapterPackage.Entries | Where-Object { $_.FullName -notlike '*/*' -and $_.Name -like '*.nuspec' })[0]
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $adapterNuspec, $true)
}
finally { $adapterPackage.Dispose() }
Assert-AdapterNuspec $adapterNuspec 'IfcIO' $version

# 5. Source code archive
Write-Host "Creating source code archive..."
$sourceZip = Join-Path $Output "IfcIO-$version-source.zip"
if (Test-Path -LiteralPath $sourceZip) { Remove-Item -LiteralPath $sourceZip }
& git -C $root archive --format=zip --prefix=IfcIO/ --output=$sourceZip HEAD
if ($LASTEXITCODE -ne 0) { throw "git archive failed with exit code $LASTEXITCODE" }

Remove-Item -LiteralPath $stage -Recurse -Force

# 6. Checksums
Write-Host "Computing SHA256..."
$files = @(Get-ChildItem -LiteralPath $Output -File | Where-Object { $_.Extension -in '.zip', '.nupkg', '.snupkg' } | Sort-Object Name)
$sums = $files | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
Set-Content -LiteralPath (Join-Path $Output 'SHA256SUMS.txt') -Value $sums -Encoding Ascii
$sums
