<#
.SYNOPSIS
    Packs release assets into artifacts: binary archives of library, source zip, NuGet packages and SHA256SUMS.txt.
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

# 2. NuGet packages
Write-Host "Packing NuGet packages..."
Invoke-DotNet pack (Join-Path $root 'IfcIO.slnx') -c Release --no-build -o $Output

# 3. Source code archive
Write-Host "Creating source code archive..."
$sourceZip = Join-Path $Output "IfcIO-$version-source.zip"
if (Test-Path -LiteralPath $sourceZip) { Remove-Item -LiteralPath $sourceZip }
& git -C $root archive --format=zip --prefix=IfcIO/ --output=$sourceZip HEAD
if ($LASTEXITCODE -ne 0) { throw "git archive failed with exit code $LASTEXITCODE" }

Remove-Item -LiteralPath $stage -Recurse -Force

# 4. Checksums
Write-Host "Computing SHA256..."
$files = @(Get-ChildItem -LiteralPath $Output -File | Where-Object { $_.Extension -in '.zip', '.nupkg', '.snupkg' } | Sort-Object Name)
$sums = $files | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
Set-Content -LiteralPath (Join-Path $Output 'SHA256SUMS.txt') -Value $sums -Encoding Ascii
$sums
