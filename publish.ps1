<#
.SYNOPSIS
    Publishes MultiClicker as a single self-contained Windows EXE and packages it
    together with tessdata/ and cosmetics/ into dist/MultiClicker-vX.Y.Z.zip.

.PARAMETER Version
    Optional semantic version (default: read from the csproj <Version> property,
    falling back to 1.0.0).

.PARAMETER Configuration
    Build configuration (default: Release).

.PARAMETER Runtime
    Target RID (default: win-x64).

.EXAMPLE
    pwsh ./publish.ps1
    pwsh ./publish.ps1 -Version 1.2.3
#>
[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $repoRoot 'Multi-Clicker\MultiClicker.csproj'
$distDir = Join-Path $repoRoot 'dist'
$publishDir = Join-Path $repoRoot "Multi-Clicker\bin\$Configuration\net8.0-windows\$Runtime\publish"

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

if (-not $Version) {
    [xml]$proj = Get-Content $projectPath
    $Version = ($proj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    if (-not $Version) { $Version = '1.0.0' }
}

Write-Host "==> Publishing MultiClicker $Version ($Configuration | $Runtime)" -ForegroundColor Cyan

# Clean any previous publish output for this RID so stale files don't ship.
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

& dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded `
    -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" `
    -p:FileVersion="$Version.0" `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $publishDir)) {
    throw "Expected publish output not found: $publishDir"
}

# Ensure cosmetics ships alongside the EXE (the AfterBuild target writes them
# to bin\<Cfg>\<TFM>\cosmetics; copy them into the publish output too).
$cosmeticsSrc = Join-Path $repoRoot 'Multi-Clicker\mandatory_assets\cosmetics'
$cosmeticsDst = Join-Path $publishDir 'cosmetics'
if (Test-Path $cosmeticsSrc) {
    New-Item -ItemType Directory -Force -Path $cosmeticsDst | Out-Null
    Copy-Item -Path (Join-Path $cosmeticsSrc '*.png') -Destination $cosmeticsDst -Force
}

# Sanity check: tessdata + cosmetics must be present next to the EXE.
$exePath = Join-Path $publishDir 'MultiClicker.exe'
if (-not (Test-Path $exePath)) {
    throw "Published EXE not found: $exePath"
}
$tessCount = (Get-ChildItem -Path (Join-Path $publishDir 'tessdata') -Filter '*.traineddata' -ErrorAction SilentlyContinue).Count
$cosCount  = (Get-ChildItem -Path $cosmeticsDst -Filter '*.png' -ErrorAction SilentlyContinue).Count
Write-Host "    EXE        : $(Split-Path $exePath -Leaf) ($([math]::Round((Get-Item $exePath).Length / 1MB, 1)) MB)"
Write-Host "    tessdata/  : $tessCount file(s)"
Write-Host "    cosmetics/ : $cosCount file(s)"

# Package as zip in dist/.
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }
$zipPath = Join-Path $distDir "MultiClicker-v$Version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Write-Host "==> Packaging $zipPath" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "==> Done: $zipPath ($zipSize MB)" -ForegroundColor Green
